using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularPbcScopeParityJourneyTests
{
  private static readonly Dictionary<string, string> Angular = new() { ["AngularUi__Enabled"] = "true" };

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-PBC-CLIENT-SCOPE-01")]
  public async Task ClientPortalHidesUnassignedSiblingRequestsDraftsAndOtherClients()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-PBC-CLIENT-SCOPE-01");
    var f = host.Fixture;
    const string siblingEngagementMarker = "SYN-PAR-002-PBC-SIBLING-ENGAGEMENT-PRIVATE";
    const string otherRecipientMarker = "SYN-PAR-002-PBC-OTHER-RECIPIENT-PRIVATE";
    const string draftMarker = "SYN-PAR-002-PBC-UNSENT-DRAFT-PRIVATE";
    const string unrelatedRequestMarker = "SYN-PAR-002-PBC-OTHER-CLIENT-PRIVATE";
    var siblingEngagementId = Guid.NewGuid();
    var siblingEngagementRequestId = Guid.NewGuid();
    var otherRecipientRequestId = Guid.NewGuid();
    var draftRequestId = Guid.NewGuid();
    var otherRecipient = PbcSeed.User(f.FirmId, "Client");
    var unrelatedClient = PbcSeed.User(f.FirmId, "Client");
    var unrelatedClientId = Guid.NewGuid();
    var unrelatedEngagementId = Guid.NewGuid();
    var unrelatedRequestId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;

    await using (var db = host.CreateDbContext())
    {
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
      });
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = f.FirmId,
        LegalName = "SYN-PAR-002-UNRELATED-PBC-CLIENT", CreatedAt = now
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = unrelatedClientId, FirmId = f.FirmId });
      db.Engagements.Add(new Engagement
      {
        Id = unrelatedEngagementId, FirmId = f.FirmId, PracticeClientId = unrelatedClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
      });
      db.Users.AddRange(otherRecipient, unrelatedClient);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, otherRecipient, "ClientUser", f.ClientId, f.EngagementId),
        PbcSeed.Grant(f.FirmId, unrelatedClient, "ClientUser", unrelatedClientId, unrelatedEngagementId));
      db.ClientPortalFirstSignIns.AddRange(
        PbcSeed.FirstSignIn(otherRecipient), PbcSeed.FirstSignIn(unrelatedClient));
      db.PbcRequests.AddRange(
        Request(siblingEngagementRequestId, f, f.ClientId, siblingEngagementId, f.Client.Id,
          siblingEngagementMarker, PbcStates.Sent, now),
        Request(otherRecipientRequestId, f, f.ClientId, f.EngagementId, otherRecipient.Id,
          otherRecipientMarker, PbcStates.Sent, now),
        Request(draftRequestId, f, f.ClientId, f.EngagementId, f.Client.Id,
          draftMarker, PbcStates.Draft, now),
        Request(unrelatedRequestId, f, unrelatedClientId, unrelatedEngagementId, unrelatedClient.Id,
          unrelatedRequestMarker, PbcStates.Sent, now));
      await db.SaveChangesAsync();
    }

    var clientOrigin = await host.StartApiForIdentityAsync(f.Client, Angular);
    var unrelatedOrigin = await host.StartApiForIdentityAsync(unrelatedClient, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(clientOrigin + "/auth/sign-in?returnUrl=%2Fui%2Fportal");
    await Assertions.Expect(page.GetByText("Cash", new() { Exact = true })).ToBeVisibleAsync();
    var portal = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(siblingEngagementMarker, portal, StringComparison.Ordinal);
    Assert.DoesNotContain(otherRecipientMarker, portal, StringComparison.Ordinal);
    Assert.DoesNotContain(draftMarker, portal, StringComparison.Ordinal);

    await page.EvaluateAsync(
      "path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/ui/portal/requests/{host.RequestId:D}");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "PBC request", Exact = true }))
      .ToBeVisibleAsync();
    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__pbcScopeToken = token", documentToken);

    foreach (var (requestId, marker) in new[]
    {
      (siblingEngagementRequestId, siblingEngagementMarker),
      (otherRecipientRequestId, otherRecipientMarker),
      (draftRequestId, draftMarker)
    })
    {
      await page.EvaluateAsync(
        "path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
        $"/ui/portal/requests/{requestId:D}");
      await Assertions.Expect(page.GetByText(
        "This request is not available in your current access scope.", new() { Exact = true }))
        .ToBeVisibleAsync();
      var denied = await page.Locator("main").InnerTextAsync();
      Assert.DoesNotContain(marker, denied, StringComparison.Ordinal);
      Assert.DoesNotContain("Bank statements", denied, StringComparison.Ordinal);
      Assert.Equal(documentToken, await page.EvaluateAsync<string>("() => window.__pbcScopeToken"));
    }

    await using var otherContext = await browser.NewContextAsync();
    var otherPage = await otherContext.NewPageAsync();
    otherPage.PageError += (_, error) => errors.Add(error);
    await otherPage.GotoAsync(unrelatedOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/ui/portal/requests/{unrelatedRequestId:D}"));
    await Assertions.Expect(otherPage.GetByRole(AriaRole.Heading,
      new() { Name = unrelatedRequestMarker, Exact = true })).ToBeVisibleAsync();
    var otherDocumentToken = Guid.NewGuid().ToString("N");
    await otherPage.EvaluateAsync("token => window.__pbcOtherClientToken = token", otherDocumentToken);
    await otherPage.EvaluateAsync(
      "path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/ui/portal/requests/{host.RequestId:D}");
    await Assertions.Expect(otherPage.GetByText(
      "This request is not available in your current access scope.", new() { Exact = true }))
      .ToBeVisibleAsync();
    var otherBody = await otherPage.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(unrelatedRequestMarker, otherBody, StringComparison.Ordinal);
    Assert.DoesNotContain("Bank statements", otherBody, StringComparison.Ordinal);
    Assert.DoesNotContain("Cash", otherBody, StringComparison.Ordinal);
    Assert.Equal(otherDocumentToken,
      await otherPage.EvaluateAsync<string>("() => window.__pbcOtherClientToken"));
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-PBC-STAFF-ACCESS-01")]
  public async Task PbcInboxRequiresPbcRoleAndClearsAfterStaffGrantRevocation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-PBC-STAFF-ACCESS-01");
    var f = host.Fixture;
    const string privateMarker = "SYN-PAR-002-STAFF-PBC-PRIVATE";
    const string privateDraft = "SYN-PAR-002-STAFF-PBC-UNSENT-DRAFT";
    await using (var db = host.CreateDbContext())
    {
      await db.PbcRequests.Where(x => x.Id == host.RequestId).ExecuteUpdateAsync(update => update
        .SetProperty(x => x.Objective, privateMarker)
        .SetProperty(x => x.Area, privateMarker));
    }

    var wrongRole = PbcSeed.User(f.FirmId, "Staff");
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(wrongRole);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, wrongRole, "FinanceManager", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
    }

    var wrongRoleOrigin = await host.StartApiForIdentityAsync(wrongRole, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using (var wrongRoleContext = await browser.NewContextAsync())
    {
      var deniedPage = await wrongRoleContext.NewPageAsync();
      var deniedErrors = new List<string>();
      deniedPage.PageError += (_, error) => deniedErrors.Add(error);
      await deniedPage.GotoAsync(wrongRoleOrigin + "/auth/sign-in?returnUrl=" +
        Uri.EscapeDataString($"/ui/app/engagements/{f.EngagementId:D}/pbc"));
      await Assertions.Expect(deniedPage.GetByRole(AriaRole.Alert))
        .ToContainTextAsync("The engagement is not available in your current access scope.");
      Assert.DoesNotContain(privateMarker, await deniedPage.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
      Assert.Empty(deniedErrors);
    }

    var staffOrigin = await host.StartApiForIdentityAsync(f.Staff, Angular);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/ui/app/engagements/{f.EngagementId:D}/pbc"));
    await Assertions.Expect(page.Locator("main")).ToContainTextAsync(privateMarker);
    var requestCard = page.Locator("section.panel").Filter(new() { HasText = privateMarker });
    await requestCard.GetByLabel("Request more files", new() { Exact = true }).FillAsync(privateDraft);

    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == f.FirmId && x.UserId == f.Staff.Id &&
        x.Role == "Staff" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(f.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }

    await requestCard.GetByRole(AriaRole.Button,
      new() { Name = "Request more files", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("Sign in with your current AuditSphere identity.");
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateMarker, body, StringComparison.Ordinal);
    Assert.DoesNotContain(privateDraft, body, StringComparison.Ordinal);
    Assert.False(await page.GetByLabel("Request more files", new() { Exact = true }).IsVisibleAsync());
    await using (var verify = host.CreateDbContext())
      Assert.False(await verify.PbcCommunications.AnyAsync(x => x.FirmId == f.FirmId && x.Body == privateDraft));
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-PBC-CLIENT-REVOCATION-01")]
  public async Task ClientReplyIsRefusedAndClearedAfterGrantRevocation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-PBC-CLIENT-REVOCATION-01");
    var f = host.Fixture;
    const string privateMarker = "SYN-PAR-002-CLIENT-PBC-PRIVATE";
    const string privateReply = "SYN-PAR-002-CLIENT-PBC-REVOKED-REPLY";
    await using (var db = host.CreateDbContext())
    {
      await db.PbcRequests.Where(x => x.Id == host.RequestId).ExecuteUpdateAsync(update => update
        .SetProperty(x => x.Objective, privateMarker)
        .SetProperty(x => x.Area, privateMarker));
    }

    var origin = await host.StartApiForIdentityAsync(f.Client, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" +
      Uri.EscapeDataString($"/ui/portal/requests/{host.RequestId:D}"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = privateMarker, Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Reply to the audit team", new() { Exact = true }).FillAsync(privateReply);

    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == f.FirmId && x.UserId == f.Client.Id &&
        x.Role == "ClientUser" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(f.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }

    await page.GetByRole(AriaRole.Button, new() { Name = "Send reply", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("Sign in with your current AuditSphere identity.");
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(privateMarker, body, StringComparison.Ordinal);
    Assert.DoesNotContain(privateReply, body, StringComparison.Ordinal);
    Assert.False(await page.GetByRole(AriaRole.Button, new() { Name = "Send reply", Exact = true }).IsVisibleAsync());
    await using (var verify = host.CreateDbContext())
      Assert.False(await verify.PbcCommunications.AnyAsync(x => x.FirmId == f.FirmId &&
        x.PbcRequestId == host.RequestId && x.Body == privateReply));
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-PBC-STALE-ROUTE-01")]
  public async Task StaffInboxClearsOnUnauthorizedSiblingEngagementNavigation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-PBC-STALE-ROUTE-01");
    var f = host.Fixture;
    const string privateMarker = "SYN-PAR-002-ANG-PBC-ROUTE-PRIVATE";
    const string siblingMarker = "SYN-PAR-002-ANG-PBC-SIBLING-PRIVATE";
    var siblingEngagementId = Guid.NewGuid();
    var siblingRequestId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    await using (var db = host.CreateDbContext())
    {
      await db.PbcRequests.Where(x => x.Id == host.RequestId).ExecuteUpdateAsync(update => update
        .SetProperty(x => x.Objective, privateMarker)
        .SetProperty(x => x.Area, privateMarker));
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
      });
      db.PbcRequests.Add(Request(siblingRequestId, f, f.ClientId, siblingEngagementId,
        f.Client.Id, siblingMarker, PbcStates.Sent, now));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(
      $"/ui/app/engagements/{f.EngagementId:D}/pbc"));
    await Assertions.Expect(page.Locator("main")).ToContainTextAsync(privateMarker);

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__pbcStaffRouteToken = token", documentToken);
    await page.EvaluateAsync(
      "path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/ui/app/engagements/{siblingEngagementId:D}/pbc");
    await Assertions.Expect(page.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("The engagement is not available in your current access scope.");
    var denied = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(privateMarker, denied, StringComparison.Ordinal);
    Assert.DoesNotContain(siblingMarker, denied, StringComparison.Ordinal);
    Assert.Equal(documentToken,
      await page.EvaluateAsync<string>("() => window.__pbcStaffRouteToken"));

    await page.EvaluateAsync(
      "path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/ui/app/engagements/{f.EngagementId:D}/pbc");
    await Assertions.Expect(page.Locator("main")).ToContainTextAsync(privateMarker);
    Assert.Equal(documentToken,
      await page.EvaluateAsync<string>("() => window.__pbcStaffRouteToken"));
    Assert.Empty(errors);
  }

  private static PbcRequest Request(Guid id, PbcSeed.Fixture f, Guid clientId, Guid engagementId,
    Guid ownerId, string marker, string state, DateTimeOffset now) => new()
  {
    Id = id, FirmId = f.FirmId, ClientId = clientId, EngagementId = engagementId,
    Objective = marker, EntityScope = "Synthetic scope", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
    Area = marker, RequestedFormat = "PDF", ControlTotals = "Synthetic totals", ClientOwnerUserId = ownerId,
    FirmOwnerUserId = f.Staff.Id, ReviewerUserId = f.Reviewer.Id, DueDate = "2027-01-31",
    Confidentiality = "Confidential", AcceptanceCriteria = "Synthetic acceptance only", State = state,
    CreatedAt = now, CreatedByUserId = f.Staff.Id, UpdatedAt = now
  };
}
