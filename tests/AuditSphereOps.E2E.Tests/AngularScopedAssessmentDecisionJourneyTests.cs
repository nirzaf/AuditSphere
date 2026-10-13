using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularScopedAssessmentDecisionJourneyTests
{
  [Fact]
  public async Task ReviewedDecisionRecordsExactEngagementAndPeriodWithoutActivation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-SCOPED-ASSESSMENT"); var f = host.Fixture;
    Guid engagementId;
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "Partner", clientId: f.ClientId));
      await QuestionnaireSeed.SeedTemplatesAndDefinitionsAsync(db); await db.SaveChangesAsync();
      var actor = PbcSeed.Actor(f.Admin, "Partner");
      var checklist = (await AcceptanceChecklistService.GetAsync(db, actor, f.ClientId)).Value!;
      foreach (var item in checklist.Items)
      {
        var answer = item.Question.AdverseAnswer == "YES" ? "No" : "Yes";
        var saved = await AcceptanceChecklistService.RecordAnswerAsync(db, actor, f.ClientId, item.Question.Code,
          answer, item.Question.RequiresEvidence ? "SYNTHETIC-" + item.Question.Code : null);
        Assert.True(saved.Succeeded, saved.Message);
      }
      var created = await EngagementLifecycleService.CreateDraftAsync(db, actor,
        new(f.ClientId, "FinancialStatementAudit", "2027-01-01", "2027-12-31", "Synthetic scoped assessment"));
      Assert.True(created.Succeeded, created.Message); engagementId = created.Value;
    }
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string>
      { ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    var path = "/app/clients/" + f.ClientId + "/assessment";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await page.GetByLabel("Service route", new() { Exact = true }).FillAsync("FinancialStatementAudit");
    await page.GetByLabel("Engagement ID (optional for first-period client acceptance)", new() { Exact = true }).FillAsync(engagementId.ToString());
    await page.GetByLabel("Rationale", new() { Exact = true }).FillAsync("Synthetic exact-period professional decision");
    await page.GetByRole(AriaRole.Button, new() { Name = "Review Partner decision", Exact = true }).ClickAsync();
    var review = page.GetByRole(AriaRole.Region, new() { Name = "Exact assessment action review", Exact = true });
    await Assertions.Expect(review).ToContainTextAsync("2027-01-01 to 2027-12-31");
    await Assertions.Expect(review).ToContainTextAsync(engagementId.ToString());
    var confirm = page.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed assessment action", Exact = true });
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    // Inspection holds this reviewed synthetic preview; the human approval flow is still explicit below.
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path + "?engagementId=" + engagementId);
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this exact assessment action and its effects.", Exact = true }).CheckAsync();
    await confirm.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Retained assessment receipt", Exact = true }))
      .ToContainTextAsync("Synthetic exact-period professional decision");
    Guid decisionId;
    await using (var db = host.CreateDbContext())
    {
      var decision = await db.AcceptanceDecisions.AsNoTracking().SingleAsync(x => x.EngagementId == engagementId);
      Assert.Equal("Accepted", decision.Decision); decisionId = decision.Id;
      Assert.Empty(await db.EngagementActivations.ToListAsync());
      Assert.True((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == engagementId)).ProfessionalWorkBlocked);
    }
    var recordedLink = page.GetByRole(AriaRole.Button, new() { Name = "Acknowledge receipt and open recorded decision", Exact = true });
    await recordedLink.ClickAsync();
    await Assertions.Expect(page).ToHaveURLAsync(origin + path + "?decisionId=" + decisionId);
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Recorded professional decision", Exact = true }))
      .ToContainTextAsync("Synthetic exact-period professional decision");
    Assert.Empty(errors);
  }
}
