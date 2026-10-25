using System.Text.Json;
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

      // Put the reminder's task beyond the ordinary engagement workspace window. Undated tasks
      // do not displace the real overdue reminder; exact selection must precede the page bound.
      var now = DateTimeOffset.UtcNow;
      var earlierTasks = Enumerable.Range(1, 500).Select(i => new WorkTask
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        Title = $"Synthetic earlier reminder task {i}", Status = PracticeTimeStates.TaskOpen,
        Phase = BudgetPhases.Unassigned, CreatedAt = now
      }).ToArray();
      db.WorkTasks.AddRange(earlierTasks);
      db.WorkflowTaskBindings.AddRange(earlierTasks.Select((task, index) => new WorkflowTaskBinding
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        WorkTaskId = task.Id, Phase = WorkflowTaskPhases.ArchiveClose, Ordinal = index + 1,
        SourceKey = $"TEST-REMINDER-WINDOW:{index + 1:D4}", CreationRequestDigest = new string('d', 64),
        Generation = 1, DueDatePolicy = "MANUAL", AssignmentSource = "MANUAL", CreatedAt = now,
        CreatedByUserId = f.Admin.Id
      }));
      await db.SaveChangesAsync();
      var bounded = await EngagementWorkflowTaskService.GetAsync(db, actor, f.EngagementId);
      Assert.True(bounded.Succeeded, bounded.Message);
      Assert.True(bounded.Value!.Truncated);
      Assert.Equal(500, bounded.Value.Tasks.Count);
      Assert.DoesNotContain(bounded.Value.Tasks, task => task.Id == overdueTaskId);
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

    var selectedTaskPath = $"/app/work?engagementId={f.EngagementId:D}&taskId={overdueTaskId:D}";
    var sourceLink = overdueReminder.GetByRole(AriaRole.Link,
      new() { Name = "Open task", Exact = true });
    await Assertions.Expect(sourceLink).ToHaveAttributeAsync("href", selectedTaskPath);
    var exactTaskEndpoint = $"/api/ui/engagements/{f.EngagementId:D}/workflow/tasks?taskId={overdueTaskId:D}";
    var capturedResponses = await PlaywrightResponseCapture.RunMultipleAsync(page,
      [
        request => request.Method == "GET" && request.Url.Contains("/api/ui/work/tasks?", StringComparison.Ordinal) &&
          request.Url.Contains("view=MY_TASKS", StringComparison.Ordinal),
        request => request.Method == "GET" && request.Url.EndsWith(exactTaskEndpoint, StringComparison.Ordinal)
      ],
      async () =>
      {
        await sourceLink.FocusAsync();
        await Assertions.Expect(sourceLink).ToBeFocusedAsync();
        await sourceLink.PressAsync("Enter");
      });
    var (myTasksStatus, myTasksBody) = capturedResponses[0];
    Assert.InRange(myTasksStatus, 200, 299);
    using (var myTasks = JsonDocument.Parse(myTasksBody))
      Assert.DoesNotContain(myTasks.RootElement.GetProperty("tasks").EnumerateArray(), row =>
        row.GetProperty("taskId").GetGuid() == overdueTaskId);

    var (exactTaskStatus, exactTaskBody) = capturedResponses[1];
    Assert.InRange(exactTaskStatus, 200, 299);
    using (var exactTask = JsonDocument.Parse(exactTaskBody))
    {
      Assert.Equal(f.EngagementId, exactTask.RootElement.GetProperty("engagementId").GetGuid());
      var selected = Assert.Single(exactTask.RootElement.GetProperty("tasks").EnumerateArray());
      Assert.Equal(overdueTaskId, selected.GetProperty("id").GetGuid());
      Assert.Equal(overdueTitle, selected.GetProperty("title").GetString());
    }
    Assert.Equal(new Uri(origin + selectedTaskPath).AbsoluteUri, page.Url);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "My work", Exact = true }))
      .ToBeVisibleAsync();
    var selectedTaskSummary = page.GetByRole(AriaRole.Region,
      new() { Name = "Task selected from deadline reminder", Exact = true });
    await Assertions.Expect(selectedTaskSummary)
      .ToContainTextAsync($"{overdueTitle} · {overdueTaskId:D}");
    Assert.True(await page.EvaluateAsync<bool>(
      "() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
      "The exact selected task view must not overflow the 390px document viewport.");

    await using (var db = host.CreateDbContext())
    {
      Assert.Equal(activityCountBeforeBrowser,
        await db.WorkflowTaskActivities.CountAsync(x => taskIds.Contains(x.WorkTaskId)));
    }
    Assert.Empty(mutations);
    Assert.Empty(pageErrors);

    await OwnedBrowserFixtureHandoff.WaitAsync(origin, selectedTaskPath, "deadline-exact-task");

    // The same selected record must disappear when its session is revoked, even though the
    // reminder URL remains known. This is an owned disposable identity, not a live account.
    await using (var db = host.CreateDbContext())
      await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(setters =>
        setters.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    var deniedResponse = await page.EvaluateAsync<JsonElement>("""
      async path => {
        const response = await fetch(path, { cache: 'no-store' });
        return { status: response.status, body: await response.text() };
      }
      """, exactTaskEndpoint);
    Assert.Equal(401, deniedResponse.GetProperty("status").GetInt32());
    Assert.DoesNotContain(overdueTitle, deniedResponse.GetProperty("body").GetString()!);
    Assert.DoesNotContain(overdueTaskId.ToString("D"), deniedResponse.GetProperty("body").GetString()!);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    await Assertions.Expect(selectedTaskSummary).ToHaveCountAsync(0);
    var deniedPage = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(overdueTitle, deniedPage);
    Assert.DoesNotContain(overdueTaskId.ToString("D"), deniedPage);
    Assert.Empty(mutations);
    Assert.Empty(pageErrors);
  }
}
