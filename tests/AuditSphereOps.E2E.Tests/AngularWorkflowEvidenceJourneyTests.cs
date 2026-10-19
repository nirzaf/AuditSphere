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
    var f = host.Fixture; Guid taskId; Guid secondTaskId;
    const string title = "SYNTHETIC CURRENT MATERIALITY COORDINATION";
    const string secondTitle = "SYNTHETIC INDEPENDENT SOURCE REVIEW";
    await using (var db = host.CreateDbContext()) {
      var made = await EngagementWorkflowTaskService.CreateAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId,
        new(title, WorkflowTaskPhases.AuditPlanning, 1, AssigneeUserId: f.Admin.Id, IdempotencyKey: Guid.NewGuid()));
      Assert.True(made.Succeeded, made.Message); taskId = made.Value;
      var secondMade = await EngagementWorkflowTaskService.CreateAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId,
        new(secondTitle, WorkflowTaskPhases.AuditPlanning, 2, AssigneeUserId: f.Admin.Id, IdempotencyKey: Guid.NewGuid()));
      Assert.True(secondMade.Succeeded, secondMade.Message); secondTaskId = secondMade.Value;
    }
    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string> {
      ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true", ["WorkflowExperience__Enabled"] = "true"
    });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    const string path = "/app/work";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    var row = page.Locator("tbody tr").Filter(new() { HasText = title });
    var secondRow = page.Locator("tbody tr").Filter(new() { HasText = secondTitle });
    await Assertions.Expect(row).ToHaveCountAsync(1);
    await Assertions.Expect(secondRow).ToHaveCountAsync(1);
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
    var pendingStorageKey = $"auditsphere.workflow-evidence:{f.FirmId:D}:{f.Admin.Id:D}:{f.EngagementId:D}:{taskId:D}";
    var pendingKey = await page.EvaluateAsync<string>("key => sessionStorage.getItem(key) ?? ''", pendingStorageKey);
    Assert.NotEmpty(pendingKey);
    var firstReceiptReadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseFirstReceiptRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var receiptReadCount = 0;
    var receiptRoute = $"**/api/ui/engagements/{f.EngagementId:D}/workflow/tasks/{taskId:D}/evidence/receipts/{pendingKey}";
    await page.RouteAsync(receiptRoute, async route => {
      if (route.Request.Method != "GET") { await route.ContinueAsync(); return; }
      if (receiptReadCount++ == 0) {
        firstReceiptReadEntered.TrySetResult();
        await releaseFirstReceiptRead.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Synthetic delayed receipt lookup failure\"}" });
      } else await route.ContinueAsync();
    });
    try {
      await panel.GetByRole(AriaRole.Button, new() { Name = "Read saved prerequisite change", Exact = true }).ClickAsync();
      await firstReceiptReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
      await secondRow.GetByRole(AriaRole.Button, new() { Name = "Review prerequisites", Exact = true }).ClickAsync();
      await Assertions.Expect(panel).ToContainTextAsync(secondTitle);
      await Assertions.Expect(panel).Not.ToContainTextAsync(title);
      var secondReason = panel.GetByLabel("Reason for the change", new() { Exact = true });
      await Assertions.Expect(secondReason).ToBeVisibleAsync();
      const string secondReasonDraft = "Keep the second task's independently reviewed source draft.";
      await secondReason.FillAsync(secondReasonDraft);
      var reviewPrerequisite = panel.GetByRole(AriaRole.Button, new() { Name = "Review prerequisite", Exact = true });
      await Assertions.Expect(reviewPrerequisite).ToBeDisabledAsync();
      releaseFirstReceiptRead.TrySetResult();
      await Assertions.Expect(reviewPrerequisite).ToBeEnabledAsync();
      await Assertions.Expect(secondReason).ToHaveValueAsync(secondReasonDraft);
      await Assertions.Expect(panel).Not.ToContainTextAsync(
        "A saved receipt was not confirmed. Keep the recovery key and review the task; no change was resent.");

      await row.GetByRole(AriaRole.Button, new() { Name = "Review prerequisites", Exact = true }).ClickAsync();
      await Assertions.Expect(panel).ToContainTextAsync(title);
      await Assertions.Expect(panel).Not.ToContainTextAsync(secondTitle);
      var recovery = panel.GetByRole(AriaRole.Button, new() { Name = "Read saved prerequisite change", Exact = true });
      await Assertions.Expect(recovery).ToBeVisibleAsync();
      Assert.Equal(pendingKey, await page.EvaluateAsync<string>("key => sessionStorage.getItem(key) ?? ''", pendingStorageKey));
      await recovery.ClickAsync();
      await Assertions.Expect(recovery).ToHaveCountAsync(0);
      Assert.Null(await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", pendingStorageKey));
      Assert.Equal(2, receiptReadCount);
    }
    finally {
      releaseFirstReceiptRead.TrySetResult();
      await page.UnrouteAsync(receiptRoute);
    }
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
      Assert.Equal(PracticeTimeStates.TaskInProgress, (await db.WorkTasks.SingleAsync(x => x.Id == secondTaskId)).Status);
    }
    Assert.Empty(errors);
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path);
  }
}
