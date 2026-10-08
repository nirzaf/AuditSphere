using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularAccountingScopeParityJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-ACCT-WORKSPACE-01")]
  public async Task MismatchedAndOtherRoleGrantsDoNotExpandAngularAccountingWorkspace()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-ACCT-WORKSPACE-01");
    var f = host.Fixture;
    var partner = PbcSeed.User(f.FirmId, "Staff");
    var siblingClientId = Guid.NewGuid();
    var siblingEngagementId = Guid.NewGuid();
    const string siblingClientName = "SYN-PAR-002-UNRELATED-ACCOUNTING-CLIENT";
    const string siblingPeriodCode = "SYN-PAR-002-UNRELATED-ACCOUNTING-PERIOD";
    const string assignedPeriodCode = "SYN-PAR-002-ASSIGNED-ACCOUNTING-PERIOD";
    string assignedClientName;
    Guid assignedPeriodId;
    Guid siblingPeriodId;
    await using (var db = host.CreateDbContext())
    {
      assignedClientName = (await db.PracticeClients.SingleAsync(x => x.Id == f.ClientId)).LegalName;
      db.PracticeClients.Add(new PracticeClient
      {
        Id = siblingClientId, FirmId = f.FirmId, LegalName = siblingClientName, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = f.FirmId, PracticeClientId = siblingClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = siblingClientId, FirmId = f.FirmId });
      db.Users.Add(partner);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, partner, "Partner", clientId: f.ClientId),
        PbcSeed.Grant(f.FirmId, partner, "Staff", clientId: siblingClientId),
        PbcSeed.Grant(f.FirmId, partner, "Partner", clientId: f.ClientId, engagementId: siblingEngagementId));
      var assignedPeriod = Period(f.FirmId, f.ClientId, assignedPeriodCode, f.Staff.Id);
      var siblingPeriod = Period(f.FirmId, siblingClientId, siblingPeriodCode, f.Staff.Id);
      assignedPeriodId = assignedPeriod.Id;
      siblingPeriodId = siblingPeriod.Id;
      db.ClientReportingPeriods.AddRange(assignedPeriod, siblingPeriod);
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(partner,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp%2Faccounting");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Accounting workspace", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Status))
      .ToContainTextAsync("1 clients in your accounting scope");
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = assignedClientName, Exact = true })).ToBeVisibleAsync();
    Assert.DoesNotContain(siblingClientName, await page.Locator("main").InnerTextAsync(), StringComparison.Ordinal);

    await using var list = await context.APIRequest.GetAsync(origin + "/api/ui/accounting/clients?search=&page=0&pageSize=25");
    Assert.Equal(200, list.Status);
    var listBody = await list.TextAsync();
    Assert.Contains(assignedClientName, listBody, StringComparison.Ordinal);
    Assert.DoesNotContain(siblingClientName, listBody, StringComparison.Ordinal);

    var clientSearch = page.GetByLabel("Search client name", new() { Exact = true });
    var clientSearchForm = page.Locator("form").Filter(new() { Has = clientSearch });
    await clientSearch.FillAsync(siblingClientName);
    await clientSearchForm.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("No clients match this search.", new() { Exact = true })).ToBeVisibleAsync();
    Assert.DoesNotContain(siblingClientName, await page.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
    await clientSearch.FillAsync(string.Empty);
    await clientSearchForm.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();

    await page.GetByRole(AriaRole.Button, new() { Name = assignedClientName, Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = assignedClientName, Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText(assignedPeriodCode, new() { Exact = true })).ToBeVisibleAsync();
    var workspaceBody = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(siblingPeriodCode, workspaceBody, StringComparison.Ordinal);
    Assert.DoesNotContain(siblingClientName, workspaceBody, StringComparison.Ordinal);

    await using var denied = await context.APIRequest.GetAsync(origin + "/api/ui/accounting/clients/" + siblingClientId.ToString("D"));
    Assert.Equal(403, denied.Status);
    Assert.DoesNotContain(siblingClientName, await denied.TextAsync(), StringComparison.Ordinal);

    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/ui/app/accounting/periods/{assignedPeriodId:D}");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Accounting period", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("main")).ToContainTextAsync(assignedPeriodCode);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/ui/app/accounting/periods/{siblingPeriodId:D}");
    await Assertions.Expect(page.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("This period is not visible under the current accounting grant.");
    var siblingDenial = await page.GetByRole(AriaRole.Alert).InnerTextAsync();
    Assert.DoesNotContain(assignedPeriodCode, await page.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
    Assert.DoesNotContain(siblingPeriodCode, await page.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/ui/app/accounting/periods/{Guid.NewGuid():D}");
    await Assertions.Expect(page.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("This period is not visible under the current accounting grant.");
    Assert.Equal(siblingDenial, await page.GetByRole(AriaRole.Alert).InnerTextAsync());
    Assert.Empty(errors);
  }

  private static ClientReportingPeriod Period(Guid firmId, Guid clientId, string code, Guid actorId) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, PeriodCode = code,
    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
    Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = actorId, CreatedAt = DateTimeOffset.UtcNow
  };
}
