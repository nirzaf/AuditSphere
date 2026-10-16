using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Text.Json;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularPracticeProductivityJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PM8-ACCEPTANCE")]
  public async Task DashboardAndForegroundTimerAreReadOnlyUntilEachDraftIsExplicitlySaved()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "AS-PM8-ACCEPTANCE");
    var f = host.Fixture;
    var dueDate = DateOnly.FromDateTime(DateTime.UtcNow);
    const string title = "AS-PM8 TASK TODAY";
    Guid taskId;
    await using (var db = host.CreateDbContext())
    {
      var created = await EngagementWorkflowTaskService.CreateAsync(db,
        PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId,
        new WorkflowTaskCreateRequest(title, WorkflowTaskPhases.AuditPlanning, 1,
          DueDate: dueDate, AssigneeUserId: f.Admin.Id, IdempotencyKey: Guid.NewGuid()));
      Assert.True(created.Succeeded, created.Message);
      taskId = created.Value;
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"));
      var siblingClientId = Guid.NewGuid();
      var siblingEngagementId = Guid.NewGuid();
      db.PracticeClients.Add(new PracticeClient
      {
        Id = siblingClientId, FirmId = f.FirmId, LegalName = "AS-PM8 SIBLING CLIENT MUST STAY HIDDEN",
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = f.FirmId, PracticeClientId = siblingClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = siblingClientId, FirmId = f.FirmId });
      await db.SaveChangesAsync();
      var siblingTask = await EngagementWorkflowTaskService.CreateAsync(db,
        PbcSeed.Actor(f.Admin, "Administrator"), siblingEngagementId,
        new WorkflowTaskCreateRequest("AS-PM8 SIBLING TASK MUST STAY HIDDEN", WorkflowTaskPhases.AuditPlanning, 1,
          DueDate: dueDate, AssigneeUserId: f.Admin.Id, IdempotencyKey: Guid.NewGuid()));
      Assert.True(siblingTask.Succeeded, siblingTask.Message);
    }

    var origin = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true",
      ["WorkflowExperience__Enabled"] = "true"
    });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    var mutations = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
    page.Request += (_, request) =>
    {
      if (request.Method == "POST" && request.Url.Contains("/api/ui/", StringComparison.Ordinal))
        mutations.Add(request.Url);
    };

    var queueResponseTask = page.WaitForResponseAsync(response => response.Ok &&
      response.Url.Contains("/api/ui/work/tasks?", StringComparison.Ordinal));
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Fwork");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "My work", Exact = true })).ToBeVisibleAsync();
    var queueResponse = await queueResponseTask;
    using var queueBody = JsonDocument.Parse(await queueResponse.TextAsync());
    var apiDueCount = queueBody.RootElement.GetProperty("summary").GetProperty("counts").EnumerateArray()
      .Single(row => row.GetProperty("view").GetString() == "DUE_TODAY")
      .GetProperty("count").GetString();
    var dueToday = page.Locator("button.summary-card").Filter(new() { HasText = "Due today" });
    await Assertions.Expect(dueToday).ToHaveCountAsync(1);
    var dueCount = await dueToday.Locator(".summary-count").InnerTextAsync();
    var displayedCount = new string(dueCount.Where(char.IsDigit).ToArray());
    Assert.True(long.TryParse(apiDueCount, out var parsedDueCount) && parsedDueCount > 0,
      $"Expected a positive exact due-today count, observed '{dueCount}'.");
    Assert.Equal(apiDueCount, displayedCount);

    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
      "The work dashboard must not overflow the 390px document viewport.");
    var summaryCards = page.Locator("button.summary-card");
    var expectedSummaryLabels = new[]
    {
      "My tasks", "Due today", "Overdue", "Waiting for client", "Under review", "Rework required", "Next 7 days"
    };
    await Assertions.Expect(summaryCards).ToHaveCountAsync(expectedSummaryLabels.Length);
    for (var i = 0; i < expectedSummaryLabels.Length; i++)
    {
      var card = summaryCards.Nth(i);
      await Assertions.Expect(card.Locator(".summary-label")).ToHaveTextAsync(expectedSummaryLabels[i]);
      Assert.True(await card.EvaluateAsync<bool>("element => element.tabIndex >= 0"),
        $"The '{expectedSummaryLabels[i]}' summary card must be keyboard reachable.");
      await card.FocusAsync();
      await Assertions.Expect(card).ToBeFocusedAsync();
      await card.PressAsync("Enter");
      await Assertions.Expect(card).ToHaveAttributeAsync("aria-pressed", "true");
    }

    var dueViewResponseTask = page.WaitForResponseAsync(response => response.Request.Method == "GET" && response.Ok &&
      response.Url.Contains("/api/ui/work/tasks?", StringComparison.Ordinal) &&
      response.Url.Contains("view=DUE_TODAY", StringComparison.Ordinal));
    await dueToday.ClickAsync();
    var dueViewResponse = await dueViewResponseTask;
    using (var dueViewBody = JsonDocument.Parse(await dueViewResponse.TextAsync()))
      Assert.Contains(dueViewBody.RootElement.GetProperty("tasks").EnumerateArray(), row =>
        row.GetProperty("taskId").GetGuid() == taskId);
    var adminTaskRow = page.Locator("tbody tr").Filter(new() { HasText = title });
    await Assertions.Expect(adminTaskRow).ToHaveCountAsync(1);
    await Assertions.Expect(page.Locator("tbody tr").Filter(new() { HasText = "AS-PM8 SIBLING TASK MUST STAY HIDDEN" }))
      .ToHaveCountAsync(1);
    var myTasksResponseTask = page.WaitForResponseAsync(response => response.Request.Method == "GET" && response.Ok &&
      response.Url.Contains("/api/ui/work/tasks?", StringComparison.Ordinal) &&
      response.Url.Contains("view=MY_TASKS", StringComparison.Ordinal));
    await page.Locator("button.summary-card").Filter(new() { HasText = "My tasks" }).ClickAsync();
    var myTasksResponse = await myTasksResponseTask;
    using (var myTasksBody = JsonDocument.Parse(await myTasksResponse.TextAsync()))
      Assert.Contains(myTasksBody.RootElement.GetProperty("tasks").EnumerateArray(), row =>
        row.GetProperty("taskId").GetGuid() == taskId);
    await Assertions.Expect(page.Locator("tbody tr").Filter(new() { HasText = title })).ToHaveCountAsync(1);
    Assert.Empty(mutations);

    var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1).AddHours(9), TimeSpan.Zero)
      .ToUnixTimeMilliseconds();
    await page.AddInitScriptAsync($"window.__asPm8Now = {start}; Date.now = () => window.__asPm8Now;");
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Fpractice%2Ftime");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Practice time & task records", Exact = true })).ToBeVisibleAsync();
    await page.SetViewportSizeAsync(390, 844);
    var billable = page.GetByLabel("Billable to client (optional)", new() { Exact = true });
    await billable.UncheckAsync();
    Assert.False(await billable.IsCheckedAsync());
    await page.GetByLabel("Open work task to track").SelectOptionAsync(taskId.ToString());
    var startButton = page.GetByRole(AriaRole.Button, new() { Name = "Start", Exact = true });
    var pauseButton = page.GetByRole(AriaRole.Button, new() { Name = "Pause", Exact = true });
    await Assertions.Expect(startButton).ToBeEnabledAsync();
    await Assertions.Expect(startButton).ToHaveAttributeAsync("type", "button");
    Assert.True(await startButton.EvaluateAsync<bool>("element => element.closest('form') === null"),
      "The foreground timer must not submit a time-draft form.");
    await startButton.FocusAsync();
    await Assertions.Expect(startButton).ToBeFocusedAsync();
    await startButton.PressAsync("Enter");
    await Assertions.Expect(pauseButton).ToBeVisibleAsync();
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await page.EvaluateAsync("() => window.__asPm8Now += 120000");
    await pauseButton.ClickAsync();
    await page.EvaluateAsync("() => window.__asPm8Now += 60000");
    var resumeButton = page.GetByRole(AriaRole.Button, new() { Name = "Resume", Exact = true });
    await Assertions.Expect(resumeButton).ToBeVisibleAsync();
    await resumeButton.ClickAsync();
    await page.EvaluateAsync("() => window.__asPm8Now += 120000");
    var stopButton = page.GetByRole(AriaRole.Button, new() { Name = "Stop", Exact = true });
    await Assertions.Expect(stopButton).ToBeVisibleAsync();
    await stopButton.ClickAsync();
    var review = page.GetByRole(AriaRole.Table, new() { Name = "Stopped timer segments", Exact = true });
    await Assertions.Expect(review.GetByRole(AriaRole.Row)).ToHaveCountAsync(3);
    Assert.Empty(mutations);

    await review.GetByRole(AriaRole.Button, new() { Name = "Use segment 1", Exact = true }).ClickAsync();
    await page.GetByLabel("Role", new() { Exact = true }).FillAsync("Staff");
    await page.GetByLabel("Activity", new() { Exact = true }).FillAsync("Fieldwork");
    var firstSaveResponseTask = page.WaitForResponseAsync(response => response.Request.Method == "POST" &&
      response.Url.EndsWith("/api/ui/practice/time/entries", StringComparison.Ordinal));
    await page.GetByRole(AriaRole.Button, new() { Name = "Save time draft", Exact = true }).ClickAsync();
    var firstSaveResponse = await firstSaveResponseTask;
    Assert.True(firstSaveResponse.Ok, $"Time draft request failed: HTTP {firstSaveResponse.Status}.");
    await Assertions.Expect(page.GetByText("Time draft recorded (2m).", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(review.GetByText("Saved", new() { Exact = true })).ToHaveCountAsync(1);

    await review.GetByRole(AriaRole.Button, new() { Name = "Use segment 2", Exact = true }).ClickAsync();
    var secondSaveResponseTask = page.WaitForResponseAsync(response => response.Request.Method == "POST" &&
      response.Url.EndsWith("/api/ui/practice/time/entries", StringComparison.Ordinal));
    await page.GetByRole(AriaRole.Button, new() { Name = "Save time draft", Exact = true }).ClickAsync();
    var secondSaveResponse = await secondSaveResponseTask;
    Assert.True(secondSaveResponse.Ok, $"Time draft request failed: HTTP {secondSaveResponse.Status}.");
    await Assertions.Expect(page.GetByText("Time draft recorded (2m).", new() { Exact = true })).ToBeVisibleAsync();
    Assert.Equal(2, mutations.Count(x => x.EndsWith("/api/ui/practice/time/entries", StringComparison.Ordinal)));
    Assert.All(mutations, x => Assert.True(x.EndsWith("/api/ui/practice/time/entries", StringComparison.Ordinal),
      $"Unexpected business mutation: {x}"));

    await using (var db = host.CreateDbContext())
    {
      var entries = await db.TimeEntries.AsNoTracking().Where(x => x.TaskId == taskId)
        .OrderBy(x => x.StartMinute).ToListAsync();
      Assert.Equal(2, entries.Count);
      Assert.Equal(new[] { 9 * 60, 9 * 60 + 3 }, entries.Select(x => x.StartMinute));
      Assert.All(entries, entry =>
      {
        Assert.Equal(2, entry.DurationMinutes);
        Assert.Equal(PracticeTimeStates.NonBillable, entry.BillableClassification);
        Assert.Equal(PracticeTimeStates.TimeDraft, entry.Status);
        Assert.Equal(f.Admin.Id, entry.UserId);
        Assert.Null(entry.ApprovedByUserId);
      });
    }

    await page.ReloadAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Table,
      new() { Name = "My recorded time entries", Exact = true })).ToContainTextAsync("AS-PM8 TASK TODAY");

    // The scoped Staff identity sees the owned task counts, but no sibling-client task or title.
    var staffOrigin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true",
      ["WorkflowExperience__Enabled"] = "true"
    });
    var staffPage = await browser.NewPageAsync();
    staffPage.PageError += (_, error) => errors.Add(error);
    staffPage.Request += (_, request) =>
    {
      if (request.Method == "POST" && request.Url.Contains("/api/ui/", StringComparison.Ordinal))
        mutations.Add(request.Url);
    };
    var staffQueueResponseTask = staffPage.WaitForResponseAsync(response => response.Ok &&
      response.Url.Contains("/api/ui/work/tasks?", StringComparison.Ordinal));
    await staffPage.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Fwork");
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading,
      new() { Name = "My work", Exact = true })).ToBeVisibleAsync();
    var staffQueueResponse = await staffQueueResponseTask;
    using var staffQueueBody = JsonDocument.Parse(await staffQueueResponse.TextAsync());
    Assert.DoesNotContain("AS-PM8 SIBLING CLIENT MUST STAY HIDDEN", staffQueueBody.RootElement.ToString(),
      StringComparison.Ordinal);
    Assert.DoesNotContain("AS-PM8 SIBLING TASK MUST STAY HIDDEN", staffQueueBody.RootElement.ToString(),
      StringComparison.Ordinal);
    var staffDueCount = staffQueueBody.RootElement.GetProperty("summary").GetProperty("counts").EnumerateArray()
      .Single(row => row.GetProperty("view").GetString() == "DUE_TODAY")
      .GetProperty("count").GetString();
    Assert.True(long.TryParse(staffDueCount, out var parsedStaffDueCount));
    Assert.Equal(parsedDueCount - 1, parsedStaffDueCount);
    await staffPage.Locator("button.summary-card").Filter(new() { HasText = "Due today" }).ClickAsync();
    await Assertions.Expect(staffPage.Locator("tbody tr").Filter(new() { HasText = title })).ToHaveCountAsync(1);
    await Assertions.Expect(staffPage.Locator("tbody tr").Filter(new() { HasText = "AS-PM8 SIBLING TASK MUST STAY HIDDEN" }))
      .ToHaveCountAsync(0);
    await Assertions.Expect(staffPage.Locator("body")).Not.ToContainTextAsync("AS-PM8 SIBLING CLIENT MUST STAY HIDDEN");

    // Opening engagement reminders, timeline, WIP and portal branding remains read-only.
    mutations.Clear();
    var engagementUrl = $"/app/engagements/{f.EngagementId:D}";
    var remindersRead = page.WaitForResponseAsync(response => response.Request.Method == "GET" &&
      response.Url.Contains("/deadline-reminders", StringComparison.Ordinal));
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(engagementUrl));
    await remindersRead;
    await Assertions.Expect(page.GetByText("Deadline reminders", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.List, new() { Name = "Deadline reminders" })
      .GetByText(title, new() { Exact = true })).ToBeVisibleAsync();
    var activityRead = page.WaitForResponseAsync(response => response.Request.Method == "GET" &&
      response.Url.Contains("/api/ui/engagements/", StringComparison.Ordinal) &&
      response.Url.Contains("/activity?", StringComparison.Ordinal));
    await page.GetByText("Activity", new() { Exact = true }).Last.ClickAsync();
    await activityRead;
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Activity", Exact = true })).ToBeVisibleAsync();

    var financeUrl = await host.StartApiForIdentityAsync(f.Admin, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    var wipRead = page.WaitForResponseAsync(response => response.Request.Method == "GET" &&
      response.Url.Contains("/api/ui/finance/receivables-aging", StringComparison.Ordinal));
    await page.GotoAsync(financeUrl + "/auth/sign-in?returnUrl=%2Fapp%2Ffinance%2Freceivables-aging");
    await wipRead;
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Firm receivables ageing", Exact = true })).ToBeVisibleAsync();
    await page.GetByText("Approved unbilled time (standard value)", new() { Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region,
      new() { Name = "Approved unbilled time report", Exact = true })).ToBeVisibleAsync();
    var unbilledRead = page.WaitForResponseAsync(response => response.Request.Method == "GET" &&
      response.Url.Contains("/api/ui/finance/unbilled-time?", StringComparison.Ordinal));
    await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
    await unbilledRead;
    await Assertions.Expect(page.GetByText("Current observations, not a historical accounting WIP balance.",
      new() { Exact = true }).First).ToBeVisibleAsync();

    var clientOrigin = await host.StartApiForIdentityAsync(f.Client, new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["AngularUi__CanonicalRoutes"] = "true"
    });
    var clientPage = await browser.NewPageAsync();
    clientPage.PageError += (_, error) => errors.Add(error);
    clientPage.Request += (_, request) =>
    {
      if (request.Method == "POST" && request.Url.Contains("/api/ui/", StringComparison.Ordinal))
        mutations.Add(request.Url);
    };
    var brandingRead = clientPage.WaitForResponseAsync(response => response.Request.Method == "GET" &&
      response.Url.EndsWith("/api/ui/portal/branding", StringComparison.Ordinal));
    await clientPage.GotoAsync(clientOrigin + "/auth/sign-in?returnUrl=%2Fportal");
    await brandingRead;
    await Assertions.Expect(clientPage.GetByRole(AriaRole.Heading,
      new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(clientPage.GetByRole(AriaRole.Heading,
      new() { Name = "AuditSphere", Exact = true })).ToBeVisibleAsync();
    Assert.Empty(mutations);
    Assert.Empty(errors);
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/app/practice/time");
  }
}
