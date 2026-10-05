using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularFirmScopeRevocationParityJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-FIRM-SCOPE-REVOCATION-01")]
  public async Task ErroneousClientAdministratorGrantIsDeniedAndRevocationClearsNativeWorkspaces()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-FIRM-SCOPE-REVOCATION-01");
    var f = host.Fixture;
    var administrator = PbcSeed.User(f.FirmId, "Staff");
    var relationshipManager = PbcSeed.User(f.FirmId, "Staff");
    var financeReviewer = PbcSeed.User(f.FirmId, "Staff");
    const string operationKind = "SYN-PAR-002-PRIVATE-OPERATION";
    const string leadName = "SYN-PAR-002-PRIVATE-LEAD";
    const string accountName = "SYN-PAR-002-PRIVATE-FIRM-ACCOUNT";
    Guid adminGrantId;
    Guid leadsGrantId;
    Guid financeGrantId;
    Guid invitationGrantId;
    Guid invitationId;

    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(administrator, relationshipManager, financeReviewer);
      var adminGrant = PbcSeed.Grant(f.FirmId, administrator, "Administrator");
      var leadsGrant = PbcSeed.Grant(f.FirmId, relationshipManager, "RelationshipManager");
      var financeGrant = PbcSeed.Grant(f.FirmId, financeReviewer, "FinanceReviewer");
      adminGrantId = adminGrant.Id;
      leadsGrantId = leadsGrant.Id;
      financeGrantId = financeGrant.Id;
      db.RoleGrants.AddRange(adminGrant, leadsGrant, financeGrant);
      db.Leads.Add(new Lead
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, Name = leadName,
        Source = "Synthetic authorization fixture", Status = CrmStates.LeadNew,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.FirmAccounts.Add(new FirmAccount
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, Code = "SYN-PAR-002-FINANCE",
        Name = accountName, AccountType = LedgerStates.AccountAsset,
        NormalSide = LedgerStates.Debit
      });
      db.FirmPeriods.Add(new FirmPeriod
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, PeriodCode = "2026-09",
        Status = LedgerStates.PeriodOpen, Revision = 1
      });
      db.DurableOperations.Add(new DurableOperation
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, OperationKind = operationKind,
        TargetId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), ExpectedRevision = 1,
        IdempotencyKey = Guid.NewGuid().ToString("N"), RequestDigest = new string('a', 64), RequestBytes = [1],
        Status = OperationState.DEAD_LETTER, CreatedAt = DateTimeOffset.UtcNow,
        NextAttemptAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();

      var invited = await RoleAdministrationService.ApplyRoleGrantAndInvitationAsync(db,
        PbcSeed.Actor(f.Admin, "Administrator"),
        new ApplyRoleGrantAndInvitationRequest(f.Staff.Id, "Manager", "CLIENT", f.ClientId));
      Assert.True(invited.Succeeded, invited.Message);
      invitationGrantId = invited.Value!.RoleGrantId;
      invitationId = invited.Value.InvitationId;
    }

    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var adminOrigin = await host.StartApiForIdentityAsync(administrator, settings);
    var leadsOrigin = await host.StartApiForIdentityAsync(relationshipManager, settings);
    var financeOrigin = await host.StartApiForIdentityAsync(financeReviewer, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var errors = new List<string>();

    await using var adminContext = await browser.NewContextAsync();
    var accessPage = await adminContext.NewPageAsync();
    var operationsPage = await adminContext.NewPageAsync();
    accessPage.PageError += (_, error) => errors.Add($"access: {error}");
    operationsPage.PageError += (_, error) => errors.Add($"operations: {error}");
    await accessPage.GotoAsync(adminOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Fadministration%2Fusers");
    await Assertions.Expect(accessPage.GetByRole(AriaRole.Heading, new() { Name = "Users & Access", Exact = true })).ToBeVisibleAsync();
    await accessPage.GetByLabel("Search local users", new() { Exact = true }).FillAsync(f.Staff.Email);
    await accessPage.GetByRole(AriaRole.Button, new() { Name = "Search local access", Exact = true }).ClickAsync();
    var copyInvitation = accessPage.GetByRole(AriaRole.Button, new() { Name = "Copy invitation link", Exact = true });
    await Assertions.Expect(copyInvitation).ToBeVisibleAsync();
    await accessPage.EvaluateAsync("() => { window.__invitationCopyCount = 0; Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: async () => { window.__invitationCopyCount += 1; } } }); }");
    await operationsPage.GotoAsync(adminOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Foperations");
    await Assertions.Expect(operationsPage.GetByRole(AriaRole.Heading, new() { Name = "Operations", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(operationsPage.GetByRole(AriaRole.Region, new() { Name = "Firm operating mode", Exact = true })).ToContainTextAsync("LOCAL_ONLY");
    await Assertions.Expect(operationsPage.Locator("p[role='status']")).ToContainTextAsync("1 need attention");
    await operationsPage.GetByText(operationKind, new() { Exact = true }).WaitForAsync();
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await operationsPage.SetViewportSizeAsync(width, 900);
      await Assertions.Expect(operationsPage.GetByText(operationKind, new() { Exact = true })).ToBeVisibleAsync();
      Assert.True(await operationsPage.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
        $"Operations document overflows at {width}px.");
    }
    await operationsPage.SetViewportSizeAsync(1440, 900);
    await operationsPage.GetByRole(AriaRole.Link, new() { Name = "Back to portfolio" }).FocusAsync();
    await operationsPage.Keyboard.PressAsync("Tab");
    var refreshOperations = operationsPage.GetByRole(AriaRole.Button, new() { Name = "Refresh operations", Exact = true });
    await Assertions.Expect(refreshOperations).ToBeFocusedAsync();
    await Assertions.Expect(refreshOperations).ToHaveCSSAsync("outline-style", "solid");
    var operationsToken = await operationsPage.EvaluateAsync<string>("window.__operationsToken = crypto.randomUUID()");

    await using var leadsContext = await browser.NewContextAsync();
    var leadsPage = await leadsContext.NewPageAsync();
    leadsPage.PageError += (_, error) => errors.Add($"leads: {error}");
    await leadsPage.GotoAsync(leadsOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Fpractice%2Fleads");
    await Assertions.Expect(leadsPage.GetByRole(AriaRole.Heading, new() { Name = "Practice leads", Exact = true })).ToBeVisibleAsync();
    await leadsPage.GetByText(leadName, new() { Exact = true }).WaitForAsync();
    var leadsToken = await leadsPage.EvaluateAsync<string>("window.__leadsToken = crypto.randomUUID()");

    await using var financeContext = await browser.NewContextAsync();
    var financePage = await financeContext.NewPageAsync();
    financePage.PageError += (_, error) => errors.Add($"finance: {error}");
    await financePage.GotoAsync(financeOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Ffinance");
    await Assertions.Expect(financePage.GetByRole(AriaRole.Heading, new() { Name = "Firm ledger & financial operations", Exact = true })).ToBeVisibleAsync();
    await financePage.GetByText(accountName, new() { Exact = true }).WaitForAsync();
    var financeToken = await financePage.EvaluateAsync<string>("window.__financeToken = crypto.randomUUID()");

    await using (var db = host.CreateDbContext())
    {
      var revokedInvitation = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(f.Admin, "Administrator"), new RevokeRoleGrantRequest(invitationGrantId));
      Assert.True(revokedInvitation.Succeeded, revokedInvitation.Message);
    }
    await copyInvitation.ClickAsync();
    await Assertions.Expect(accessPage.GetByRole(AriaRole.Button, new() { Name = "Copy invitation link", Exact = true })).ToHaveCountAsync(0);
    Assert.Equal(0, await accessPage.EvaluateAsync<int>("window.__invitationCopyCount"));
    var staleInvitationStatus = await accessPage.EvaluateAsync<int>($"async () => (await fetch('/api/ui/administration/access/invitations/{invitationId:D}')).status");
    Assert.Equal(403, staleInvitationStatus);

    await using (var db = host.CreateDbContext())
    {
      var revokedAdmin = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(f.Admin, "Administrator"), new RevokeRoleGrantRequest(adminGrantId));
      Assert.True(revokedAdmin.Succeeded, revokedAdmin.Message);
      var revokedLeads = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(f.Admin, "Administrator"), new RevokeRoleGrantRequest(leadsGrantId));
      Assert.True(revokedLeads.Succeeded, revokedLeads.Message);
      var revokedFinance = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(f.Admin, "Administrator"), new RevokeRoleGrantRequest(financeGrantId));
      Assert.True(revokedFinance.Succeeded, revokedFinance.Message);
    }

    foreach (var page in new[] { accessPage, operationsPage })
    {
      var refresh = page.GetByRole(AriaRole.Button, new() { Name = page == accessPage ? "Refresh access" : "Refresh operations", Exact = true });
      await refresh.ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    }
    await leadsPage.GetByRole(AriaRole.Button, new() { Name = "Refresh leads", Exact = true }).ClickAsync();
    await Assertions.Expect(leadsPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    await financePage.GetByRole(AriaRole.Button, new() { Name = "Refresh firm ledger", Exact = true }).ClickAsync();
    await Assertions.Expect(financePage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });

    Assert.DoesNotContain(f.Staff.Email, await accessPage.Locator("body").InnerTextAsync());
    Assert.DoesNotContain("Firm role grants", await accessPage.Locator("body").InnerTextAsync());
    Assert.DoesNotContain(operationKind, await operationsPage.Locator("body").InnerTextAsync());
    Assert.DoesNotContain(leadName, await leadsPage.Locator("body").InnerTextAsync());
    Assert.DoesNotContain(accountName, await financePage.Locator("body").InnerTextAsync());
    Assert.Equal(operationsToken, await operationsPage.EvaluateAsync<string>("window.__operationsToken"));
    Assert.Equal(leadsToken, await leadsPage.EvaluateAsync<string>("window.__leadsToken"));
    Assert.Equal(financeToken, await financePage.EvaluateAsync<string>("window.__financeToken"));

    var erroneousClientAdmin = PbcSeed.User(f.FirmId, "Client");
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(erroneousClientAdmin);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, erroneousClientAdmin, "Administrator"));
      await db.SaveChangesAsync();
    }
    var clientOrigin = await host.StartApiForIdentityAsync(erroneousClientAdmin, settings);
    await using var clientContext = await browser.NewContextAsync();
    var clientPage = await clientContext.NewPageAsync();
    clientPage.PageError += (_, error) => errors.Add($"client: {error}");
    await clientPage.GotoAsync(clientOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Fadministration");
    await Assertions.Expect(clientPage.GetByRole(AriaRole.Heading, new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
    var adminOverviewStatus = await clientPage.EvaluateAsync<int>("async () => (await fetch('/api/ui/administration/overview')).status");
    var accessWorkspaceStatus = await clientPage.EvaluateAsync<int>("async () => (await fetch('/api/ui/administration/access')).status");
    Assert.Equal(403, adminOverviewStatus);
    Assert.Equal(403, accessWorkspaceStatus);
    var deniedBody = await clientPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Users & Access", deniedBody);
    Assert.DoesNotContain("Setup progress", deniedBody);
    Assert.DoesNotContain(erroneousClientAdmin.Email, deniedBody);
    Assert.Empty(errors);
  }
}
