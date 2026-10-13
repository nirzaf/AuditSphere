using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularWorkflowOrderingJourneyTests
{
  [Fact]
  public async Task ReviewedOrderingRecoversOneSavedChangeAndRefusesAnOppositeEdgeWithoutProfessionalApproval()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-WORKFLOW-ORDERING"); var f = host.Fixture;
    Guid first, second;
    const string firstTitle = "SYNTHETIC FIRST OPERATIONAL TASK", secondTitle = "SYNTHETIC DEPENDENT OPERATIONAL TASK";
    await using (var db = host.CreateDbContext()) {
      async Task<Guid> Make(string title) {
        var made = await EngagementWorkflowTaskService.CreateAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId,
          new(title, WorkflowTaskPhases.AuditPlanning, 1, AssigneeUserId: f.Admin.Id, IdempotencyKey: Guid.NewGuid()));
        Assert.True(made.Succeeded, made.Message); return made.Value;
      }
      first = await Make(firstTitle); second = await Make(secondTitle);
    }
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string> {
      ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true", ["WorkflowExperience__Enabled"] = "true"
    });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Fwork");
    var row = page.Locator("tbody tr").Filter(new() { HasText = secondTitle });
    await row.GetByRole(AriaRole.Button, new() { Name = "Review task ordering", Exact = true }).ClickAsync();
    var panel = page.GetByRole(AriaRole.Region, new() { Name = "Task ordering", Exact = true });
    var inspectBefore = Environment.GetEnvironmentVariable("AUDITSPHERE_ORDERING_INSPECT_BEFORE") == "true";
    if (inspectBefore) await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/app/work");
    await panel.GetByLabel("Predecessor task", new() { Exact = true }).SelectOptionAsync(first.ToString());
    await panel.GetByLabel("Reason for ordering change", new() { Exact = true }).FillAsync("Review the first operational task before this task.");
    await panel.GetByRole(AriaRole.Button, new() { Name = "Review task ordering", Exact = true }).ClickAsync();
    await Assertions.Expect(panel).ToContainTextAsync(firstTitle);
    await panel.GetByLabel("I reviewed this exact task ordering change.", new() { Exact = true }).CheckAsync();
    var posts = 0; const string routePattern = "**/workflow/tasks/*/dependencies";
    await page.RouteAsync(routePattern, async route => {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      posts++; var persisted = await route.FetchAsync(); Assert.True(persisted.Ok);
      await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Synthetic lost saved reply\"}" });
    });
    await panel.GetByRole(AriaRole.Button, new() { Name = "Save reviewed ordering", Exact = true }).ClickAsync();
    await panel.GetByRole(AriaRole.Button, new() { Name = "Read saved ordering change", Exact = true }).ClickAsync();
    await Assertions.Expect(row).ToContainTextAsync("1 task prerequisite(s) incomplete"); Assert.Equal(1, posts);
    await Assertions.Expect(row.GetByRole(AriaRole.Button, new() { Name = "Complete task", Exact = true })).ToHaveCountAsync(0);
    await page.UnrouteAsync(routePattern);
    var firstRow = page.Locator("tbody tr").Filter(new() { HasText = firstTitle });
    await firstRow.GetByRole(AriaRole.Button, new() { Name = "Review task ordering", Exact = true }).ClickAsync();
    await panel.GetByLabel("Predecessor task", new() { Exact = true }).SelectOptionAsync(second.ToString());
    await panel.GetByLabel("Reason for ordering change", new() { Exact = true }).FillAsync("Synthetic opposite edge must fail.");
    await panel.GetByRole(AriaRole.Button, new() { Name = "Review task ordering", Exact = true }).ClickAsync();
    await panel.GetByLabel("I reviewed this exact task ordering change.", new() { Exact = true }).CheckAsync();
    await panel.GetByRole(AriaRole.Button, new() { Name = "Save reviewed ordering", Exact = true }).ClickAsync();
    await Assertions.Expect(panel).ToContainTextAsync("cycle");
    await using (var db = host.CreateDbContext()) {
      Assert.Single(await db.WorkflowTaskDependencyRevisions.ToListAsync()); Assert.Empty(await db.MaterialityApprovals.ToListAsync());
      Assert.Empty(await db.AuditProcedureReviews.ToListAsync()); Assert.False(await db.WorkTasks.AnyAsync(x => x.Status == PracticeTimeStates.TaskCompleted));
    }
    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1")); Assert.Empty(errors);
    if (!inspectBefore) await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/app/work");
  }
}
