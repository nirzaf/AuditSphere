using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularAuditRevocationParityJourneyTests
{
  private static readonly Dictionary<string, string> Angular = new()
  {
    ["AngularUi__Enabled"] = "true",
    ["AngularUi__CanonicalRoutes"] = "true"
  };

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-AUDIT-REFRESH-REVOKED-01")]
  public async Task RefreshAfterGrantRevocationClearsWorkpaperFindingAndAuditPlan()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-AUDIT-REFRESH-REVOKED-01");
    var f = host.Fixture;
    var workpaperId = Guid.NewGuid();
    var findingId = Guid.NewGuid();
    const string workpaperTitle = "Synthetic revoked-grant workpaper";
    const string workpaperBody = "Revoked-grant work performed marker";
    const string findingImpact = "Synthetic revocation finding impact";

    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Partner", f.ClientId, f.EngagementId));
      db.Workpapers.Add(new Workpaper
      {
        Id = workpaperId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ActorId = f.Staff.Id, Index = "SYN-REVOKED-WP", Title = workpaperTitle,
        Objective = "Exercise fail-closed refresh after grant revocation", TemplateVersion = "SYNTHETIC-v1",
        Procedure = "Synthetic procedure", WorkPerformed = workpaperBody,
        Status = WorkpaperStatuses.Working, CreatedAt = DateTimeOffset.UtcNow
      });
      db.Findings.Add(new Finding
      {
        Id = findingId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ActorId = f.Staff.Id, FindingType = "Synthetic cut-off exception", ImpactDescription = findingImpact,
        Status = FindingStatuses.Open, CreatedAt = DateTimeOffset.UtcNow
      });
      db.MaterialityAssessments.Add(new MaterialityAssessment
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ActorId = f.Staff.Id, BenchmarkSource = "Total assets", BenchmarkVersion = "AFS-v1",
        Rationale = "Synthetic revocation fixture", BenchmarkAmount = 1_000_000m, RateApplied = 0.05m,
        OverallMateriality = 50_000m, PerformanceMateriality = 37_500m, ClearlyTrivialThreshold = 2_500m,
        Status = MaterialityStatuses.Draft, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var draft = await AuditPlanningService.SaveWorkpaperDraftAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
        new SaveWorkpaperDraftRequest(workpaperId, 0, 1, 1, 1, Guid.NewGuid(), workpaperBody,
          "Synthetic conclusion retained before revocation."));
      Assert.True(draft.Succeeded, draft.Message);
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff, Angular);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var workpaperContext = await browser.NewContextAsync();
    await using var findingContext = await browser.NewContextAsync();
    await using var planContext = await browser.NewContextAsync();
    var errors = new List<string>();

    var workpaperPage = await workpaperContext.NewPageAsync();
    workpaperPage.PageError += (_, error) => errors.Add(error);
    var workpaperPath = $"/app/audit/workpapers/{workpaperId:D}";
    await workpaperPage.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(workpaperPath));
    await Assertions.Expect(workpaperPage.GetByRole(AriaRole.Heading,
      new() { Name = $"Workpaper: {workpaperTitle}", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(workpaperPage.GetByLabel("Work performed", new() { Exact = true }))
      .ToHaveValueAsync(workpaperBody);

    var findingPage = await findingContext.NewPageAsync();
    findingPage.PageError += (_, error) => errors.Add(error);
    var findingPath = $"/app/findings/{findingId:D}";
    await findingPage.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(findingPath));
    await Assertions.Expect(findingPage.GetByText(findingImpact, new() { Exact = true })).ToBeVisibleAsync();

    var planPage = await planContext.NewPageAsync();
    planPage.PageError += (_, error) => errors.Add(error);
    var planPath = $"/app/engagements/{f.EngagementId:D}/audit-plan";
    await planPage.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(planPath));
    await Assertions.Expect(planPage.GetByRole(AriaRole.Region,
      new() { Name = "Materiality (§19.2)", Exact = true })).ToContainTextAsync("Total assets");

    await using (var db = host.CreateDbContext())
    {
      var grants = await db.RoleGrants.Where(x => x.FirmId == f.FirmId && x.UserId == f.Staff.Id &&
        x.RevokedAt == null && (x.Role == "Partner" || x.Role == "Staff")).ToListAsync();
      Assert.Equal(2, grants.Count);
      foreach (var grant in grants)
      {
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
          PbcSeed.Actor(f.Admin, "Administrator"),
          new RevokeRoleGrantRequest(grant.Id, Reason: "AS-PAR-002 Angular refresh revocation parity"));
        Assert.True(revoked.Succeeded, revoked.Message);
      }
    }

    await workpaperPage.GetByRole(AriaRole.Button, new() { Name = "Reload current target", Exact = true }).ClickAsync();
    await Assertions.Expect(workpaperPage.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("current AuditSphere identity");
    await Assertions.Expect(workpaperPage.GetByLabel("Work performed", new() { Exact = true })).ToHaveCountAsync(0);
    var workpaperDenied = await workpaperPage.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(workpaperTitle, workpaperDenied, StringComparison.Ordinal);
    Assert.DoesNotContain("SYN-REVOKED-WP", workpaperDenied, StringComparison.Ordinal);
    Assert.DoesNotContain(workpaperBody, workpaperDenied, StringComparison.Ordinal);

    await findingPage.GetByRole(AriaRole.Button, new() { Name = "Refresh finding", Exact = true }).ClickAsync();
    await Assertions.Expect(findingPage.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("current AuditSphere identity");
    var findingDenied = await findingPage.Locator("main").InnerTextAsync();
    Assert.DoesNotContain("Synthetic cut-off exception", findingDenied, StringComparison.Ordinal);
    Assert.DoesNotContain(findingImpact, findingDenied, StringComparison.Ordinal);

    await planPage.GetByRole(AriaRole.Button, new() { Name = "Refresh plan", Exact = true }).ClickAsync();
    await Assertions.Expect(planPage.GetByRole(AriaRole.Alert))
      .ToContainTextAsync("current AuditSphere identity");
    var planDenied = await planPage.Locator("main").InnerTextAsync();
    Assert.DoesNotContain("Total assets", planDenied, StringComparison.Ordinal);
    Assert.DoesNotContain("1,000,000.00", planDenied, StringComparison.Ordinal);
    Assert.Empty(errors);
  }
}
