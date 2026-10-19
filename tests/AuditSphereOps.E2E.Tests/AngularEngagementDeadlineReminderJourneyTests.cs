using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularEngagementDeadlineReminderJourneyTests
{
  [Fact]
  public async Task EngagementRemindersRetainThirtyDayOverdueAndExcludeClosedTasks()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "ANGULAR-ENGAGEMENT-DEADLINE-REMINDERS");
    var f = host.Fixture;
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var overdueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);
    const string overdueTitle = "AS-DEADLINE-REMINDERS 30-DAY OVERDUE";
    const string completedTitle = "AS-DEADLINE-REMINDERS COMPLETED";
    const string cancelledTitle = "AS-DEADLINE-REMINDERS CANCELLED";
    Guid overdueTaskId, completedTaskId, cancelledTaskId;

    await using (var db = host.CreateDbContext())
    {
      async Task<Guid> CreateTaskAsync(string title, int ordinal) {
        var created = await EngagementWorkflowTaskService.CreateAsync(db, actor, f.EngagementId,
          new WorkflowTaskCreateRequest(title, WorkflowTaskPhases.AuditPlanning, ordinal,
            DueDate: overdueDate, IdempotencyKey: Guid.NewGuid()));
        Assert.True(created.Succeeded, created.Message);
        return created.Value;
      }

      overdueTaskId = await CreateTaskAsync(overdueTitle, 1);
      completedTaskId = await CreateTaskAsync(completedTitle, 2);
      cancelledTaskId = await CreateTaskAsync(cancelledTitle, 3);
      var completed = await EngagementWorkflowTaskService.CompleteAsync(db, actor, completedTaskId, 1);
      Assert.True(completed.Succeeded, completed.Message);
      var cancelled = await EngagementWorkflowTaskService.CancelAsync(db, actor, cancelledTaskId,
        "Synthetic closed task for reminder filtering.", 1);
      Assert.True(cancelled.Succeeded, cancelled.Message);
    }
    var taskIds = new[] { overdueTaskId, completedTaskId, cancelledTaskId };
    int activityCountBeforeBrowser;
    await using (var db = host.CreateDbContext())
      activityCountBeforeBrowser = await db.WorkflowTaskActivities.CountAsync(x => taskIds.Contains(x.WorkTaskId));

    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true",
      ["WorkflowExperience__Enabled"] = "true"
    };
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_UI_BUILD_PATH") is { Length: > 0 } buildPath)
      settings["AngularUi__BuildPath"] = buildPath;
    var origin = await host.StartApiForIdentityAsync(f.Admin, settings);

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var pageErrors = new List<string>();
    var mutations = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);
    page.Request += (_, request) =>
    {
      if ((request.Method is "POST" or "PUT" or "PATCH" or "DELETE") &&
          request.Url.Contains("/api/ui/", StringComparison.Ordinal))
        mutations.Add($"{request.Method} {request.Url}");
    };

    var path = $"/app/engagements/{f.EngagementId:D}";
    var reminderResponse = page.WaitForResponseAsync(response => response.Request.Method == "GET" &&
      response.Url.Contains("/api/ui/engagements/", StringComparison.Ordinal) &&
      response.Url.Contains("/deadline-reminders", StringComparison.Ordinal));
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    Assert.Equal(200, (await reminderResponse).Status);

    var reminderList = page.GetByRole(AriaRole.List, new() { Name = "Deadline reminders", Exact = true });
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Deadline reminders", Exact = true })).ToBeVisibleAsync();
    var overdueReminder = reminderList.Locator("li").Filter(new() { HasText = overdueTitle });
    await Assertions.Expect(overdueReminder).ToHaveCountAsync(1);
    await Assertions.Expect(overdueReminder).ToContainTextAsync(overdueDate.ToString("yyyy-MM-dd"));
    await Assertions.Expect(overdueReminder).ToContainTextAsync("OVERDUE");
    await Assertions.Expect(reminderList).Not.ToContainTextAsync(completedTitle);
    await Assertions.Expect(reminderList).Not.ToContainTextAsync(cancelledTitle);

    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>(
      "() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
      "Engagement reminders must not overflow the 390px document viewport.");

    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(activityCountBeforeBrowser,
        await db.WorkflowTaskActivities.CountAsync(x => taskIds.Contains(x.WorkTaskId)));
    }
    Assert.Empty(mutations);
    Assert.Empty(pageErrors);

    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "overdue-reminders");
  }
}
