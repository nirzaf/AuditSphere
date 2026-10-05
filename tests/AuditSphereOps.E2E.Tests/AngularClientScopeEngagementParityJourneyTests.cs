using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularClientScopeEngagementParityJourneyTests
{
  private static readonly Dictionary<string, string> Angular = new()
  {
    ["AngularUi__Enabled"] = "true",
    ["AngularUi__CanonicalRoutes"] = "true"
  };

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-ACCEPTANCE-EXACT-01")]
  public async Task AcceptanceDecisionRequiresPartnerGrantForExactClient(bool canonical)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-ACCEPTANCE-EXACT-01");
    var f = host.Fixture;
    var partner = PbcSeed.User(f.FirmId, "Staff");
    var unrelatedManager = PbcSeed.User(f.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateRegistration = "SYN-PAR-002-PRIVATE-ACCEPTANCE-REGISTRATION";
    await using (var db = host.CreateDbContext())
    {
      await AssessmentParitySeed.PopulateAsync(db, f);
      (await db.PracticeClients.SingleAsync(x => x.Id == f.ClientId)).RegistrationNumber = privateRegistration;
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = f.FirmId,
        LegalName = "SYN-PAR-002-UNRELATED-ACCEPTANCE-CLIENT", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.AddRange(partner, unrelatedManager);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, partner, "Partner", clientId: f.ClientId),
        PbcSeed.Grant(f.FirmId, unrelatedManager, "Manager", clientId: unrelatedClientId));
      await db.SaveChangesAsync();
    }

    var settings = new Dictionary<string, string>(Angular)
    {
      ["AngularUi__CanonicalRoutes"] = canonical.ToString()
    };
    var prefix = canonical ? string.Empty : "/ui";
    var partnerOrigin = await host.StartApiForIdentityAsync(partner, settings);
    var managerOrigin = await host.StartApiForIdentityAsync(unrelatedManager, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using (var context = await browser.NewContextAsync())
    {
      var page = await context.NewPageAsync();
      var errors = new List<string>();
      page.PageError += (_, error) => errors.Add(error);
      var path = $"{prefix}/app/assessments/{f.ClientId:D}/decision";
      await page.GotoAsync(partnerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
      var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client assessment profile", Exact = true });
      await Assertions.Expect(profile).ToContainTextAsync("PBC TEST CLIENT");
      await Assertions.Expect(profile).ToContainTextAsync(privateRegistration);
      await Assertions.Expect(page.GetByRole(AriaRole.Button,
        new() { Name = "Review Partner decision", Exact = true })).ToBeVisibleAsync();
      Assert.Empty(errors);
    }

    await using (var context = await browser.NewContextAsync())
    {
      var page = await context.NewPageAsync();
      var errors = new List<string>();
      page.PageError += (_, error) => errors.Add(error);
      var path = $"{prefix}/app/assessments/{f.ClientId:D}/decision";
      await page.GotoAsync(managerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
      await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Access denied");
      await Assertions.Expect(page.GetByRole(AriaRole.Region,
        new() { Name = "Client assessment profile", Exact = true })).ToHaveCountAsync(0);
      await Assertions.Expect(page.GetByRole(AriaRole.Button,
        new() { Name = "Review Partner decision", Exact = true })).ToHaveCountAsync(0);
      var body = await page.Locator("body").InnerTextAsync();
      Assert.DoesNotContain(privateRegistration, body, StringComparison.Ordinal);
      Assert.Empty(errors);
    }
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-ENGAGEMENT-PLAN-SCOPE-01")]
  public async Task EngagementAndAuditPlanClearOnScopeChangeAndRefuseRevokedWrites()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-ENGAGEMENT-PLAN-SCOPE-01");
    var f = host.Fixture;
    var siblingEngagementId = Guid.NewGuid();
    var unrelatedClientId = Guid.NewGuid();
    var unrelatedEngagementId = Guid.NewGuid();
    var unrelatedManager = PbcSeed.User(f.FirmId, "Staff");
    const string privateHold = "SYN-PAR-002-ENGAGEMENT-PRIVATE-HOLD";
    const string privateRisk = "SYN-PAR-002-AUDIT-PLAN-PRIVATE-RISK";
    const string revokedRisk = "SYN-PAR-002-REVOKED-RISK-MUST-NOT-PERSIST";
    await using (var db = host.CreateDbContext())
    {
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = f.FirmId,
        PracticeClientId = f.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = f.FirmId,
        LegalName = "SYN-PAR-002-ENGAGEMENT-PRIVATE-CLIENT", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new Engagement
      {
        Id = unrelatedEngagementId, FirmId = f.FirmId,
        PracticeClientId = unrelatedClientId, ServiceRoute = "SYN-PAR-002-ENGAGEMENT-PRIVATE-SERVICE",
        Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = unrelatedClientId, FirmId = f.FirmId });
      db.EngagementHolds.Add(new EngagementHold
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, EngagementId = f.EngagementId,
        HoldKind = "Synthetic review", Reason = privateHold, CreatedAt = DateTimeOffset.UtcNow
      });
      db.AuditRisks.Add(new AuditRisk
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ActorId = f.Staff.Id, AccountArea = "Synthetic audit area", Assertion = "Completeness",
        Description = privateRisk, Drivers = "Synthetic test driver", Severity = RiskSeverities.Normal,
        SignificanceDecision = SignificanceDecisions.Normal, ResponseDescription = "Synthetic response",
        Status = RiskStatuses.Identified, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(unrelatedManager);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, unrelatedManager, "Manager", clientId: unrelatedClientId));
      await db.SaveChangesAsync();
    }

    var staffOrigin = await host.StartApiForIdentityAsync(f.Staff, Angular);
    var managerOrigin = await host.StartApiForIdentityAsync(unrelatedManager, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var targetEngagement = $"/app/engagements/{f.EngagementId:D}";
    var siblingEngagement = $"/app/engagements/{siblingEngagementId:D}";
    var unrelatedEngagement = $"/app/engagements/{unrelatedEngagementId:D}";
    await page.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(targetEngagement));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Engagement details", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText(f.EngagementId.ToString("D"), new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText(privateHold, new() { Exact = true })).ToBeVisibleAsync();
    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__engagementParityToken = token", documentToken);

    var siblingText = await NavigateAsync(page, siblingEngagement, "Engagement unavailable");
    Assert.DoesNotContain(f.EngagementId.ToString("D"), siblingText, StringComparison.Ordinal);
    Assert.DoesNotContain(siblingEngagementId.ToString("D"), siblingText, StringComparison.Ordinal);
    Assert.DoesNotContain(privateHold, siblingText, StringComparison.Ordinal);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("() => window.__engagementParityToken"));
    var restored = await NavigateAsync(page, targetEngagement, f.EngagementId.ToString("D"));
    Assert.Contains(f.EngagementId.ToString("D"), restored, StringComparison.Ordinal);
    Assert.Contains(privateHold, restored, StringComparison.Ordinal);
    var unrelatedText = await NavigateAsync(page, unrelatedEngagement, "Engagement unavailable");
    Assert.DoesNotContain("PBC TEST CLIENT", unrelatedText, StringComparison.Ordinal);
    Assert.DoesNotContain("SYN-PAR-002-ENGAGEMENT-PRIVATE-CLIENT", unrelatedText, StringComparison.Ordinal);
    Assert.DoesNotContain("SYN-PAR-002-ENGAGEMENT-PRIVATE-SERVICE", unrelatedText, StringComparison.Ordinal);
    Assert.DoesNotContain(privateHold, unrelatedText, StringComparison.Ordinal);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("() => window.__engagementParityToken"));

    var targetPlan = targetEngagement + "/audit-plan";
    await NavigateAsync(page, targetPlan, privateRisk);
    var siblingPlanText = await NavigateAsync(page, siblingEngagement + "/audit-plan",
      "Sign in with an authorized internal staff identity assigned to this engagement to view its audit plan.");
    Assert.DoesNotContain(privateRisk, siblingPlanText, StringComparison.Ordinal);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("() => window.__engagementParityToken"));
    await NavigateAsync(page, targetPlan, privateRisk);

    var riskSection = page.GetByRole(AriaRole.Region,
      new() { Name = "Identified risks (§19.3)", Exact = true });
    await riskSection.GetByText("Record an identified risk", new() { Exact = true }).ClickAsync();
    await riskSection.GetByLabel("Account or disclosure area", new() { Exact = true }).FillAsync("Synthetic area");
    await riskSection.GetByLabel("Assertion", new() { Exact = true }).FillAsync("Completeness");
    await riskSection.GetByLabel("Risk description", new() { Exact = true }).FillAsync(revokedRisk);
    await riskSection.GetByLabel("Drivers", new() { Exact = true }).FillAsync("Synthetic test driver");
    await riskSection.GetByLabel("Planned response", new() { Exact = true }).FillAsync("Synthetic response");
    var staleActor = PbcSeed.Actor(f.Staff, "Staff");
    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == f.FirmId && x.UserId == f.Staff.Id &&
        x.Role == "Staff" && x.EngagementId == f.EngagementId && x.RevokedAt == null);
      var result = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(f.Admin, "Administrator"),
        new RevokeRoleGrantRequest(grant.Id, Reason: "AS-PAR-002 route revocation test"));
      Assert.True(result.Succeeded, result.Message);
      var staleWrite = await AuditPlanningService.CreateAuditRiskAsync(db, staleActor,
        new CreateAuditRiskRequest(f.EngagementId, "Synthetic area", "Completeness", revokedRisk,
          "Synthetic test driver", SignificanceDecisions.Normal, null, "Synthetic response"));
      Assert.False(staleWrite.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleWrite.ErrorCode);
    }
    await riskSection.GetByRole(AriaRole.Button, new() { Name = "Record risk", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync(
      "Sign in with your current AuditSphere identity.");
    var revokedText = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(privateRisk, revokedText, StringComparison.Ordinal);
    Assert.DoesNotContain(revokedRisk, revokedText, StringComparison.Ordinal);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("() => window.__engagementParityToken"));
    await using (var db = host.CreateDbContext())
      Assert.False(await db.AuditRisks.AnyAsync(x => x.Description == revokedRisk));

    await using (var managerContext = await browser.NewContextAsync())
    {
      var managerPage = await managerContext.NewPageAsync();
      var managerErrors = new List<string>();
      managerPage.PageError += (_, error) => managerErrors.Add(error);
      var path = targetEngagement + "/audit-plan";
      await managerPage.GotoAsync(managerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
      await Assertions.Expect(managerPage.GetByRole(AriaRole.Alert)).ToContainTextAsync(
        "Sign in with an authorized internal staff identity assigned to this engagement to view its audit plan.");
      Assert.DoesNotContain(privateRisk, await managerPage.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
      Assert.Empty(managerErrors);
    }
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-COMPLETION-SCOPE-01")]
  public async Task CompletionChecklistDeniesSiblingEngagementAndKeepsRepresentationPrivate()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-COMPLETION-SCOPE-01");
    var f = host.Fixture;
    var viewer = PbcSeed.User(f.FirmId, "Staff");
    var unrelatedClientId = Guid.NewGuid();
    const string privateNarrative = "SYNTHETIC-PAR-002-PRIVATE-REPRESENTATION";
    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = unrelatedClientId, FirmId = f.FirmId,
        LegalName = "SYN-PAR-002-UNRELATED-COMPLETION-CLIENT", CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = unrelatedClientId, FirmId = f.FirmId });
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, viewer, "Manager", clientId: unrelatedClientId));
      db.WrittenRepresentations.Add(new WrittenRepresentation
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, Code = "SYN-R-01", Title = "Synthetic representation",
        Narrative = privateNarrative
      });
      await db.SaveChangesAsync();
    }
    var staffOrigin = await host.StartApiForIdentityAsync(f.Staff, Angular);
    var viewerOrigin = await host.StartApiForIdentityAsync(viewer, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    await using (var staffContext = await browser.NewContextAsync())
    {
      var page = await staffContext.NewPageAsync();
      var errors = new List<string>();
      page.PageError += (_, error) => errors.Add(error);
      var path = $"/app/engagements/{f.EngagementId:D}/completion";
      await page.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
      await Assertions.Expect(page.GetByText(privateNarrative, new() { Exact = false })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByText("SYN-R-01", new() { Exact = true })).ToBeVisibleAsync();
      Assert.Empty(errors);
    }

    await using (var viewerContext = await browser.NewContextAsync())
    {
      var page = await viewerContext.NewPageAsync();
      var errors = new List<string>();
      page.PageError += (_, error) => errors.Add(error);
      var path = $"/app/engagements/{f.EngagementId:D}/completion";
      await page.GotoAsync(viewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
      await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync(
        "Sign in with an authorized internal staff identity assigned to this engagement to view its completion checklist.");
      var body = await page.Locator("main").InnerTextAsync();
      Assert.DoesNotContain(privateNarrative, body, StringComparison.Ordinal);
      Assert.DoesNotContain("SYN-R-01", body, StringComparison.Ordinal);
      Assert.Empty(errors);
    }
  }

  private static async Task<string> NavigateAsync(IPage page, string path, string expectedText)
  {
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }", path);
    await page.WaitForFunctionAsync("path => location.pathname === path", path);
    await Assertions.Expect(page.GetByText(expectedText, new() { Exact = false }).First)
      .ToBeVisibleAsync(new() { Timeout = 15000 });
    return await page.Locator("main").InnerTextAsync();
  }
}
