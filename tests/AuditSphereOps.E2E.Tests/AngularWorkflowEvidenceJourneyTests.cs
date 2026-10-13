using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularWorkflowEvidenceJourneyTests
{
  [Fact]
  public async Task ReviewedPrerequisiteRecoversLostReplyAndKeepsOperationalCompletionSeparateFromProfessionalApproval()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-WORKFLOW-EVIDENCE");
    var f = host.Fixture; Guid taskId;
    const string title = "SYNTHETIC CURRENT MATERIALITY COORDINATION";
    await using (var db = host.CreateDbContext()) {
      var made = await EngagementWorkflowTaskService.CreateAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId,
        new(title, WorkflowTaskPhases.AuditPlanning, 1, AssigneeUserId: f.Admin.Id, IdempotencyKey: Guid.NewGuid()));
      Assert.True(made.Succeeded, made.Message); taskId = made.Value;
    }
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string> {
      ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true", ["WorkflowExperience__Enabled"] = "true"
    });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    const string path = "/app/work";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    var row = page.Locator("tbody tr").Filter(new() { HasText = title });
    await Assertions.Expect(row).ToHaveCountAsync(1);
    await row.GetByRole(AriaRole.Button, new() { Name = "Review prerequisites", Exact = true }).ClickAsync();
    var panel = page.GetByRole(AriaRole.Region, new() { Name = "Evidence prerequisites", Exact = true });
    await Assertions.Expect(panel).ToContainTextAsync("No domain-evidence prerequisites");
    await panel.GetByLabel("Reason for the change", new() { Exact = true }).FillAsync("Require current independently approved materiality for this coordination task.");
    await panel.GetByRole(AriaRole.Button, new() { Name = "Review prerequisite", Exact = true }).ClickAsync();
    await panel.GetByLabel("I reviewed this exact prerequisite change.", new() { Exact = true }).CheckAsync();
    var posts = 0;
    await page.RouteAsync("**/workflow/tasks/*/evidence", async route => {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      posts++; var persisted = await route.FetchAsync(); Assert.True(persisted.Ok);
      await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Synthetic lost response after persistence\"}" });
    });
    await panel.GetByRole(AriaRole.Button, new() { Name = "Save reviewed change", Exact = true }).ClickAsync();
    await Assertions.Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Read saved prerequisite change", Exact = true })).ToBeVisibleAsync();
    await panel.GetByRole(AriaRole.Button, new() { Name = "Read saved prerequisite change", Exact = true }).ClickAsync();
    await Assertions.Expect(row).ToContainTextAsync("1 evidence prerequisite(s) pending");
    Assert.Equal(1, posts);
    await Assertions.Expect(row.GetByRole(AriaRole.Button, new() { Name = "Complete task", Exact = true })).ToHaveCountAsync(0);
    await row.GetByRole(AriaRole.Button, new() { Name = "Review prerequisites", Exact = true }).ClickAsync();
    await Assertions.Expect(panel).ToContainTextAsync("Current approved materiality");
    await Assertions.Expect(panel).ToContainTextAsync("Pending");
    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await using (var db = host.CreateDbContext()) {
      Assert.Single(await db.WorkflowTaskEvidenceRules.ToListAsync());
      Assert.Empty(await db.MaterialityApprovals.ToListAsync());
      Assert.Equal(PracticeTimeStates.TaskInProgress, (await db.WorkTasks.SingleAsync(x => x.Id == taskId)).Status);
    }
    Assert.Empty(errors);
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path);
  }
}
