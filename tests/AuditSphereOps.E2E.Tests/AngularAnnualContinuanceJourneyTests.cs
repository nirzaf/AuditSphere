using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAnnualContinuanceJourneyTests
{
  [Fact]
  public async Task AnnualTargetOpensFreshReviewedDeltaAndRecordsOnlyTheNewPeriodDecision()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-ANNUAL-CONTINUANCE");
    var f = host.Fixture; var actor = PbcSeed.Actor(f.Admin, "Partner"); Guid targetId; Guid priorId;
    async Task AnswerCurrentAsync()
    {
      await using var db = host.CreateDbContext();
      var checklist = (await AcceptanceChecklistService.GetAsync(db, actor, f.ClientId)).Value!;
      foreach (var item in checklist.Items)
      {
        var saved = await AcceptanceChecklistService.RecordAnswerAsync(db, actor, f.ClientId, item.Question.Code,
          item.Question.AdverseAnswer == "YES" ? "No" : "Yes", item.Question.RequiresEvidence ? "SYNTHETIC-" + item.Question.Code : null);
        Assert.True(saved.Succeeded, saved.Message);
      }
    }
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "Partner", f.ClientId));
      await QuestionnaireSeed.SeedTemplatesAndDefinitionsAsync(db); await db.SaveChangesAsync();
    }
    await AnswerCurrentAsync();
    await using (var db = host.CreateDbContext())
    {
      var source = await EngagementLifecycleService.CreateDraftAsync(db, actor,
        new(f.ClientId, "AccountingOnly", "2026-01-01", "2026-12-31", "Synthetic prior period"));
      Assert.True(source.Succeeded, source.Message);
      var prior = await AcceptanceDecisionService.RecordAsync(db, actor,
        new(f.ClientId, source.Value, "AccountingOnly", "Accepted", "Synthetic prior-year review", null, 1));
      Assert.True(prior.Succeeded, prior.Message); priorId = prior.Value;
      var target = await EngagementLifecycleService.CreateDraftAsync(db, actor,
        new(f.ClientId, "AccountingOnly", "2027-01-01", "2027-12-31", "Synthetic current period"));
      Assert.True(target.Succeeded, target.Message); targetId = target.Value;
    }
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string>
      { ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    var path = $"/app/clients/{f.ClientId}/assessment?engagementId={targetId}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    var targetPanel = page.GetByRole(AriaRole.Region, new() { Name = "Current-period assessment target", Exact = true });
    await Assertions.Expect(targetPanel).ToContainTextAsync("2027-01-01 to 2027-12-31");
    await Assertions.Expect(targetPanel).ToContainTextAsync("prior professional decision closes");
    await page.GetByRole(AriaRole.Button, new() { Name = "Review continuance", Exact = true }).ClickAsync();
    var confirm = page.GetByRole(AriaRole.Button, new() { Name = "Confirm reviewed assessment action", Exact = true });
    await Assertions.Expect(confirm).ToBeDisabledAsync();
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this exact assessment action and its effects.", Exact = true }).CheckAsync();
    await confirm.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Retained assessment receipt", Exact = true })).ToContainTextAsync("Evaluation 1 → 2");
    await page.GetByRole(AriaRole.Button, new() { Name = "Acknowledge assessment receipt", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Evaluation progress", Exact = true })).ToContainTextAsync("0 of 32 questions answered");
    await Assertions.Expect(page.GetByLabel("Service route", new() { Exact = true })).ToHaveValueAsync("AccountingOnly");
    var targetField = page.GetByLabel("Engagement ID (optional for first-period client acceptance)", new() { Exact = true });
    await Assertions.Expect(targetField).ToHaveValueAsync(targetId.ToString());
    await Assertions.Expect(targetField).ToHaveAttributeAsync("readonly", "");
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path);
    await AnswerCurrentAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh current evaluation", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Evaluation progress", Exact = true })).ToContainTextAsync("32 of 32 questions answered");
    await page.GetByLabel("Rationale", new() { Exact = true }).FillAsync("Synthetic fresh current-period review");
    await page.GetByRole(AriaRole.Button, new() { Name = "Review Partner decision", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Exact assessment action review", Exact = true })).ToContainTextAsync(targetId.ToString());
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this exact assessment action and its effects.", Exact = true }).CheckAsync();
    await confirm.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Retained assessment receipt", Exact = true })).ToContainTextAsync("Synthetic fresh current-period review");
    await using (var db = host.CreateDbContext())
    {
      var current = await db.AcceptanceDecisions.AsNoTracking().SingleAsync(x => x.EngagementId == targetId);
      Assert.Equal((2L, "CONTINUANCE", (Guid?)priorId), (current.Generation, current.Path, current.PriorDecisionId));
      Assert.Equal(1L, (await db.AcceptanceDecisions.AsNoTracking().SingleAsync(x => x.Id == priorId)).Generation);
      Assert.Empty(await db.EngagementActivations.ToListAsync());
      Assert.True((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == targetId)).ProfessionalWorkBlocked);
    }
    Assert.Empty(errors);
  }
}
