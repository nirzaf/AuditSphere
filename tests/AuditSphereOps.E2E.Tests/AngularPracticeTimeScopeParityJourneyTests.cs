using System.Text.Json;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AuthorizationAndScope")]
public sealed class AngularPracticeTimeScopeParityJourneyTests
{
  [Fact]
  [Trait("CaseId", "STE-GAP-009-RATE-FSLI-BROWSER")]
  [Trait("CompletionUnit", "WU-039")]
  public async Task SeparateAdministratorApprovesSteRateAndMappedTaskPinsItToBillableTime()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "STE-GAP-009-RATE-FSLI-BROWSER");
    var f = host.Fixture;
    var manager = PbcSeed.User(f.FirmId, "Staff");
    var now = DateTimeOffset.UtcNow;
    var datasetId = Guid.NewGuid();
    var mappingId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      var safety = await db.ClientSafetyStates.AsNoTracking().SingleAsync(x => x.Id == f.ClientId);
      db.Users.Add(manager);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, manager, "Manager"));
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        SourceKind = "Raw", Currency = "QAR", Balanced = true, ControlTotal = 0m,
        ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Sealed,
        LegalEntityKey = "SYNTHETIC-ENTITY", RawFileSha256Hex = AuditSphereOps.Domain.Shared.Hashing.Sha256Hex("synthetic-raw-tb"),
        NormalizedDatasetDigest = AuditSphereOps.Domain.Shared.Hashing.Sha256Hex("synthetic-normalized-tb"),
        ImportedAt = now, ImportedByUserId = f.Staff.Id
      });
      db.MappingVersions.Add(new MappingVersion
      {
        Id = mappingId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        DatasetId = datasetId, Version = 1, Generation = safety.InputGeneration,
        TaxonomyVersion = "synthetic-fsli-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Status = AccountingPackageStates.MappingApproved, CreatedByUserId = f.Staff.Id,
        ApprovedByUserId = f.Reviewer.Id, ApprovedAt = now, CreatedAt = now
      });
      db.MappingAllocations.Add(new MappingAllocation
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        MappingVersionId = mappingId, SourceAccountCode = "1000", DestinationCode = "CASH",
        StatementSection = "ASSETS", Fraction = 1m, Rationale = "Synthetic approved FSLI mapping", CreatedAt = now
      });
      await db.SaveChangesAsync();
    }

    var managerOrigin = await host.StartApiForIdentityAsync(manager,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var managerContext = await browser.NewContextAsync();
    var managerPage = await managerContext.NewPageAsync();
    var errors = new List<string>();
    managerPage.PageError += (_, error) => errors.Add(error);
    await managerPage.GotoAsync(managerOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Fpractice%2Frate-cards");
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Charge-out rates", Exact = true })).ToBeVisibleAsync();
    await managerPage.GetByRole(AriaRole.Button,
      new() { Name = "Create missing STE baseline drafts", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.Locator("audit-command-message"))
      .ToContainTextAsync("STE baseline drafts were created. Each draft needs approval by a different authorized approver.");
    var draftRow = managerPage.Locator("section[aria-labelledby='slots-heading'] tbody tr")
      .Filter(new() { HasText = "Audit Manager" });
    await Assertions.Expect(draftRow).ToContainTextAsync("draft");

    var adminOrigin = await host.StartApiForIdentityAsync(f.Admin,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    await using var adminContext = await browser.NewContextAsync();
    var adminPage = await adminContext.NewPageAsync();
    adminPage.PageError += (_, error) => errors.Add(error);
    await adminPage.GotoAsync(adminOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Fpractice%2Frate-cards");
    await Assertions.Expect(adminPage.GetByRole(AriaRole.Heading,
      new() { Name = "Charge-out rates", Exact = true })).ToBeVisibleAsync();
    var approvalRow = adminPage.Locator("section[aria-labelledby='slots-heading'] tbody tr")
      .Filter(new() { HasText = "Audit Manager" });
    await approvalRow.GetByRole(AriaRole.Button, new() { Name = "Approve draft", Exact = true }).ClickAsync();
    await Assertions.Expect(approvalRow).ToContainTextAsync("750.00");
    await Assertions.Expect(approvalRow.GetByRole(AriaRole.Button,
      new() { Name = "Approve draft", Exact = true })).ToHaveCountAsync(0);

    await managerPage.GotoAsync(managerOrigin + "/auth/sign-in?returnUrl=%2Fapp%2Fpractice%2Ftime");
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Practice time & task records", Exact = true })).ToBeVisibleAsync();
    const string taskTitle = "SYN-FSLI-MAPPED-TIME-TASK";
    await managerPage.GetByLabel("Task title", new() { Exact = true }).FillAsync(taskTitle);
    await managerPage.GetByLabel("Engagement (optional)").SelectOptionAsync(f.EngagementId.ToString());
    var fsli = managerPage.GetByLabel("FSLI mapping (optional)");
    await Assertions.Expect(fsli.Locator("option[value='CASH']")).ToBeAttachedAsync();
    await fsli.SelectOptionAsync("CASH");
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Create task", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByText("Work task created.", new() { Exact = true })).ToBeVisibleAsync();

    Guid taskId;
    await using (var db = host.CreateDbContext())
    {
      var task = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Title == taskTitle);
      taskId = task.Id;
      Assert.Equal(mappingId, task.MappingVersionId);
      Assert.Equal("CASH", task.FsliCode);
      var rate = await db.RateCardVersions.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId &&
        x.Role == "Audit Manager" && x.Activity == "General" && x.Status == PracticeTimeStates.RateApproved);
      Assert.Equal(750m, rate.RatePerHour);
      Assert.Equal(f.Admin.Id, rate.ApprovedByUserId);
      Assert.NotEqual(manager.Id, rate.ApprovedByUserId);
    }

    await managerPage.Locator("select[name='task']").SelectOptionAsync(taskId.ToString());
    await managerPage.GetByLabel("Work date (UTC)", new() { Exact = true }).FillAsync("2026-10-09");
    await managerPage.GetByLabel("Start time (UTC)", new() { Exact = true }).FillAsync("09:00");
    await managerPage.GetByLabel("Duration (minutes)", new() { Exact = true }).FillAsync("60");
    await managerPage.GetByLabel("Role", new() { Exact = true }).FillAsync("Audit Manager");
    await managerPage.GetByLabel("Activity", new() { Exact = true }).FillAsync("General");
    await managerPage.GetByLabel("Billable to client (optional)", new() { Exact = true }).CheckAsync();
    await managerPage.GetByRole(AriaRole.Button, new() { Name = "Save time draft", Exact = true }).ClickAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Table,
      new() { Name = "My recorded time entries", Exact = true })).ToContainTextAsync("CASH");
    Guid recordedEntryId;
    var olderEntryIds = new List<Guid>();
    await using (var db = host.CreateDbContext())
    {
      var entry = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.TaskId == taskId);
      recordedEntryId = entry.Id;
      Assert.Equal(new DateOnly(2026, 10, 9), entry.WorkDate);
      Assert.Equal(540, entry.StartMinute);
      Assert.Equal(mappingId, entry.MappingVersionId);
      Assert.Equal("CASH", entry.FsliCode);
      Assert.Equal(PracticeTimeStates.Billable, entry.BillableClassification);
      Assert.Equal(750m, entry.RatePerHour);
      Assert.NotNull(entry.RateCardVersionId);
      Assert.Equal(f.Admin.Id, await db.RateCardVersions.Where(x => x.Id == entry.RateCardVersionId)
        .Select(x => x.ApprovedByUserId).SingleAsync());
      var retainedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
      for (var index = 0; index < 50; index++)
      {
        var olderId = Guid.NewGuid(); olderEntryIds.Add(olderId);
        db.TimeEntries.Add(new TimeEntry
        {
          Id = olderId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
          TaskId = taskId, UserId = manager.Id, WorkDate = new DateOnly(2026, 9, 11),
          StartMinute = 600 + index * 2, DurationMinutes = 1, Role = "Staff", Activity = "Retained synthetic history",
          Narrative = $"SYN-OWN-HISTORY-{index}", MappingVersionId = mappingId, FsliCode = "CASH",
          BillableClassification = PracticeTimeStates.NonBillable, Status = PracticeTimeStates.TimeDraft,
          NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Currency = "QAR", CreatedAt = retainedAt
        });
      }
      db.TimeEntries.Add(new TimeEntry
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        TaskId = taskId, UserId = f.Staff.Id, WorkDate = new DateOnly(2026, 10, 10),
        StartMinute = 540, DurationMinutes = 1, Role = "Staff", Activity = "Other staff retained work",
        Narrative = "SYN-OTHER-USER-HISTORY-MUST-NOT-APPEAR", MappingVersionId = mappingId, FsliCode = "CASH",
        BillableClassification = PracticeTimeStates.NonBillable, Status = PracticeTimeStates.TimeDraft,
        NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Currency = "QAR", CreatedAt = retainedAt
      });
      await db.SaveChangesAsync();
    }
    var pagingWrites = new List<string>();
    managerPage.Request += (_, request) =>
    {
      if (request.Method != "GET" && request.Url.Contains("/api/ui/", StringComparison.Ordinal)) pagingWrites.Add(request.Method + " " + request.Url);
    };
    var (historyStatus, historyBody) = await PlaywrightResponseCapture.RunAsync(managerPage,
      request => request.Method == "GET" && request.Url.EndsWith("/api/ui/practice/time", StringComparison.Ordinal),
      () => managerPage.ReloadAsync());
    Assert.Equal(200, historyStatus);
    using var firstHistory = JsonDocument.Parse(historyBody);
    var historyIds = firstHistory.RootElement.GetProperty("myEntries").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToArray();
    Assert.Equal(50, historyIds.Length); Assert.Equal(recordedEntryId, historyIds[0]);
    var historyPages = managerPage.GetByRole(AriaRole.Navigation, new() { Name = "Recorded time pages", Exact = true });
    await Assertions.Expect(historyPages).ToContainTextAsync("Page 1 · 50 shown");
    var (olderStatus, olderBody) = await PlaywrightResponseCapture.RunAsync(managerPage,
      request => request.Method == "GET" && request.Url.Contains("entriesBeforeCreatedAt=", StringComparison.Ordinal),
      () => historyPages.GetByRole(AriaRole.Button, new() { Name = "Older time entries", Exact = true }).PressAsync("Enter"));
    Assert.Equal(200, olderStatus);
    using var olderHistory = JsonDocument.Parse(olderBody);
    var retained = Assert.Single(olderHistory.RootElement.GetProperty("myEntries").EnumerateArray());
    Assert.Contains(retained.GetProperty("id").GetGuid(), olderEntryIds);
    Assert.DoesNotContain(retained.GetProperty("id").GetGuid(), historyIds);
    Assert.Equal(manager.Id, retained.GetProperty("userId").GetGuid());
    await Assertions.Expect(historyPages).ToContainTextAsync("Page 2 · 1 shown");
    await Assertions.Expect(historyPages.GetByRole(AriaRole.Button, new() { Name = "Older time entries", Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(managerPage.GetByRole(AriaRole.Heading, new() { Name = "My recorded time", Exact = true })).ToBeFocusedAsync();
    Assert.DoesNotContain("SYN-OTHER-USER-HISTORY-MUST-NOT-APPEAR", await managerPage.Locator("main").InnerTextAsync());
    await OwnedBrowserFixtureHandoff.WaitAsync(managerOrigin, "/app/practice/time", "own-time-history-paging");
    var (newerStatus, newerBody) = await PlaywrightResponseCapture.RunAsync(managerPage,
      request => request.Method == "GET" && request.Url.EndsWith("/api/ui/practice/time", StringComparison.Ordinal),
      () => historyPages.GetByRole(AriaRole.Button, new() { Name = "Newer time entries", Exact = true }).PressAsync("Space"));
    Assert.Equal(200, newerStatus);
    using var newerHistory = JsonDocument.Parse(newerBody);
    Assert.Equal(historyIds, newerHistory.RootElement.GetProperty("myEntries").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToArray());
    Assert.DoesNotContain("SYN-OTHER-USER-HISTORY-MUST-NOT-APPEAR", await managerPage.Locator("main").InnerTextAsync());
    await using (var proof = host.CreateDbContext())
    {
      var retainedDraft = await proof.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == recordedEntryId);
      Assert.Equal(750m, retainedDraft.RatePerHour); Assert.Equal(mappingId, retainedDraft.MappingVersionId);
      Assert.Equal(PracticeTimeStates.TimeDraft, retainedDraft.Status);
    }
    Assert.Empty(pagingWrites);

    // Continue the same real captured-rate/FSLI entry through review, revision and an approved correction.
    var ownTable = managerPage.GetByRole(AriaRole.Table, new() { Name = "My recorded time entries", Exact = true });
    var sourceRow = ownTable.GetByRole(AriaRole.Row).Filter(new() { HasText = "2026-10-09" });
    var (submitStatus, _) = await PlaywrightResponseCapture.RunAsync(managerPage,
      request => request.Method == "POST" && request.Url.EndsWith($"/api/ui/practice/time/entries/{recordedEntryId}/submit", StringComparison.Ordinal),
      () => sourceRow.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync());
    Assert.Equal(200, submitStatus);
    await adminPage.GotoAsync(adminOrigin + "/app/practice/time");
    var approvalTable = adminPage.GetByRole(AriaRole.Table, new() { Name = "Time entries awaiting my approval", Exact = true });
    var submittedRow = approvalTable.GetByRole(AriaRole.Row).Filter(new() { HasText = taskTitle });
    await submittedRow.GetByRole(AriaRole.Button, new() { Name = "Return with reason", Exact = true }).ClickAsync();
    const string returnedReason = "Explain the source of the captured fieldwork minutes.";
    await submittedRow.GetByLabel("Return reason *", new() { Exact = true }).FillAsync(returnedReason);
    var (returnStatus, _) = await PlaywrightResponseCapture.RunAsync(adminPage,
      request => request.Method == "POST" && request.Url.EndsWith($"/api/ui/practice/time/entries/{recordedEntryId}/return", StringComparison.Ordinal),
      () => submittedRow.GetByRole(AriaRole.Button, new() { Name = "Return time", Exact = true }).ClickAsync());
    Assert.Equal(200, returnStatus);
    await managerPage.ReloadAsync();
    await Assertions.Expect(sourceRow).ToContainTextAsync(returnedReason);
    var editButton = sourceRow.GetByRole(AriaRole.Button, new() { Name = "Edit time", Exact = true });
    await editButton.FocusAsync(); await editButton.PressAsync("Enter");
    var editor = managerPage.Locator("audit-time-entry-change");
    await Assertions.Expect(editor.GetByRole(AriaRole.Heading, new() { Name = "Time entry change · 2026-10-09", Exact = true })).ToBeFocusedAsync();
    await editor.GetByLabel("Duration (minutes)", new() { Exact = true }).FillAsync("45");
    await editor.GetByLabel("Narrative (optional)", new() { Exact = true }).FillAsync("SYN-REVISED-SOURCE-EXPLANATION");
    await editor.GetByLabel("Reason for this change *", new() { Exact = true }).FillAsync("Reconciled the source workpaper effort.");
    await editor.GetByRole(AriaRole.Button, new() { Name = "Review exact change", Exact = true }).ClickAsync();
    await Assertions.Expect(editor.GetByRole(AriaRole.Heading, new() { Name = "Review before saving", Exact = true })).ToBeFocusedAsync();
    // The acknowledged change triggers a workspace reload; await that read before asserting rendered rows.
    var editResponses = await PlaywrightResponseCapture.RunMultipleAsync(managerPage,
    [
      request => request.Method == "POST" && request.Url.EndsWith($"/api/ui/practice/time/entries/{recordedEntryId}/changes", StringComparison.Ordinal),
      request => request.Method == "GET" && new Uri(request.Url).AbsolutePath == "/api/ui/practice/time"
    ],
      () => editor.GetByRole(AriaRole.Button, new() { Name = "Save time change", Exact = true }).ClickAsync());
    Assert.All(editResponses, response => Assert.Equal(200, response.Status));
    using var editedWorkspace = JsonDocument.Parse(editResponses[1].Body);
    var editedEntry = Assert.Single(editedWorkspace.RootElement.GetProperty("myEntries").EnumerateArray(),
      row => row.GetProperty("id").GetGuid() == recordedEntryId);
    Assert.Equal(45, editedEntry.GetProperty("durationMinutes").GetInt32());
    Assert.Equal("RETURNED", editedEntry.GetProperty("status").GetString());
    await Assertions.Expect(sourceRow).ToContainTextAsync("45m");
    await Assertions.Expect(sourceRow.GetByRole(AriaRole.Button, new() { Name = "Resubmit", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(sourceRow).ToContainTextAsync(returnedReason);
    var (resubmitStatus, _) = await PlaywrightResponseCapture.RunAsync(managerPage,
      request => request.Method == "POST" && request.Url.EndsWith($"/api/ui/practice/time/entries/{recordedEntryId}/submit", StringComparison.Ordinal),
      () => sourceRow.GetByRole(AriaRole.Button, new() { Name = "Resubmit", Exact = true }).ClickAsync());
    Assert.Equal(200, resubmitStatus);
    await adminPage.ReloadAsync();
    var (approvedStatus, _) = await PlaywrightResponseCapture.RunAsync(adminPage,
      request => request.Method == "POST" && request.Url.EndsWith($"/api/ui/practice/time/entries/{recordedEntryId}/approve", StringComparison.Ordinal),
      () => submittedRow.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync());
    Assert.Equal(200, approvedStatus);
    await managerPage.ReloadAsync();
    await sourceRow.GetByRole(AriaRole.Button, new() { Name = "Correct approved time", Exact = true }).ClickAsync();
    await editor.GetByLabel("Duration (minutes)", new() { Exact = true }).FillAsync("35");
    await editor.GetByLabel("Narrative (optional)", new() { Exact = true }).FillAsync("SYN-APPROVED-TIME-CORRECTION");
    const string correctionReason = "Remove duplicated review effort while retaining the original rate.";
    await editor.GetByLabel("Reason for this change *", new() { Exact = true }).FillAsync(correctionReason);
    await editor.GetByRole(AriaRole.Button, new() { Name = "Review exact change", Exact = true }).ClickAsync();
    var committedChange = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
    var correctionPosts = 0;
    const string changeRoute = "**/api/ui/practice/time/entries/*/changes";
    async Task LoseCommittedChange(IRoute route)
    {
      if (route.Request.Method != "POST") { await route.ContinueAsync(); return; }
      try
      {
        correctionPosts++;
        var response = await route.FetchAsync();
        Assert.Equal(200, response.Status);
        using var result = JsonDocument.Parse(await response.TextAsync());
        var resultId = result.RootElement.GetProperty("value").GetGuid();
        await route.AbortAsync();
        committedChange.TrySetResult(resultId);
      }
      catch (Exception error) { committedChange.TrySetException(error); await route.AbortAsync(); }
    }
    await managerPage.RouteAsync(changeRoute, LoseCommittedChange);
    await editor.GetByRole(AriaRole.Button, new() { Name = "Create corrected draft", Exact = true }).ClickAsync();
    var correctedId = await committedChange.Task.WaitAsync(TimeSpan.FromSeconds(30));
    await Assertions.Expect(editor.GetByRole(AriaRole.Heading, new() { Name = "Change outcome needs review", Exact = true })).ToBeVisibleAsync();
    await managerPage.UnrouteAsync(changeRoute, LoseCommittedChange);
    await editor.GetByRole(AriaRole.Button, new() { Name = "Check saved change receipt", Exact = true }).ClickAsync();
    var correctedRow = ownTable.GetByRole(AriaRole.Row).Filter(new() { HasText = "35m" });
    await Assertions.Expect(correctedRow.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true })).ToBeVisibleAsync();
    Assert.Equal(1, correctionPosts);
    await Assertions.Expect(correctedRow).ToContainTextAsync(mappingId.ToString());
    await Assertions.Expect(correctedRow).ToContainTextAsync("CASH");
    var retainedRow = ownTable.GetByRole(AriaRole.Row).Filter(new() { HasText = "45m" });
    await retainedRow.GetByRole(AriaRole.Button, new() { Name = "Change history", Exact = true }).ClickAsync();
    await Assertions.Expect(editor.GetByRole(AriaRole.Heading, new() { Name = "Approved time corrected", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(editor.GetByRole(AriaRole.Heading, new() { Name = "Unapproved time revised", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(editor).ToContainTextAsync(correctionReason);
    await Assertions.Expect(editor).ToContainTextAsync(correctedId.ToString());
    await Assertions.Expect(editor.Locator("time[datetime]")).ToHaveCountAsync(2);
    await using (var proof = host.CreateDbContext())
    {
      var source = await proof.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == recordedEntryId);
      var corrected = await proof.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == correctedId);
      Assert.Equal(PracticeTimeStates.TimeSuperseded, source.Status);
      Assert.Equal(2, source.SubmissionRound); Assert.Equal(2L, source.Revision);
      Assert.Equal(PracticeTimeStates.TimeDraft, corrected.Status);
      Assert.Equal(3L, corrected.Revision); Assert.Equal(recordedEntryId, corrected.SupersedesId);
      Assert.Equal(source.RateCardVersionId, corrected.RateCardVersionId);
      Assert.Equal(750m, corrected.RatePerHour); Assert.Equal(mappingId, corrected.MappingVersionId);
      Assert.Equal("CASH", corrected.FsliCode); Assert.Equal(PracticeTimeStates.Billable, corrected.BillableClassification);
      var changes = await proof.TimeEntryChangeReceipts.AsNoTracking().Where(x => x.FirmId == f.FirmId && x.TimeEntryId == recordedEntryId).ToListAsync();
      Assert.Equal(2, changes.Count); Assert.All(changes, x => Assert.Equal(manager.Id, x.ActorId));
      Assert.Equal(1, await proof.TimeEntries.CountAsync(x => x.SupersedesId == recordedEntryId));
    }
    await OwnedBrowserFixtureHandoff.WaitAsync(managerOrigin, "/app/practice/time", "time-change-recovery");
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-TIME-ENG-01")]
  public async Task EngagementScopedTimeQueueHidesSiblingTasksEntriesAndClientPeriods()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-TIME-ENG-01");
    var f = host.Fixture;
    var engagementManager = PbcSeed.User(f.FirmId, "Staff");
    var siblingEngagementId = Guid.NewGuid();
    var assignedTaskId = Guid.NewGuid();
    var siblingTaskId = Guid.NewGuid();
    const string periodCode = "SYN-PAR-002-TIME-CLIENT-PERIOD";
    const string assignedTaskTitle = "SYN-PAR-002-ASSIGNED-ENGAGEMENT-TASK";
    const string siblingTaskTitle = "SYN-PAR-002-SIBLING-ENGAGEMENT-TASK";
    const string assignedNarrative = "SYN-PAR-002-ASSIGNED-TIME-NARRATIVE";
    const string siblingNarrative = "SYN-PAR-002-SIBLING-TIME-NARRATIVE";
    var pagedEntryIds = new List<Guid>();

    await using (var db = host.CreateDbContext())
    {
      var mappingId = Guid.NewGuid();
      var datasetId = Guid.NewGuid();
      var now = DateTimeOffset.UtcNow;
      db.Users.Add(engagementManager);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, engagementManager, "Manager",
        clientId: f.ClientId, engagementId: f.EngagementId));
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        PeriodCode = periodCode, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        SourceKind = "Raw", Currency = "QAR", Balanced = true, ControlTotal = 0m,
        ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Loading,
        NormalizedDatasetDigest = AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(datasetId.ToString()),
        ImportedAt = now, ImportedByUserId = f.Staff.Id
      });
      await db.SaveChangesAsync();
      await db.TrialBalanceDatasets.Where(x => x.Id == datasetId)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
      db.MappingVersions.Add(new MappingVersion
      {
        Id = mappingId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        DatasetId = datasetId, TaxonomyVersion = "time-e2e-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Status = AccountingPackageStates.MappingApproved, CreatedByUserId = f.Staff.Id,
        ApprovedByUserId = f.Reviewer.Id, ApprovedAt = now, CreatedAt = now
      });
      db.MappingAllocations.Add(new MappingAllocation
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        MappingVersionId = mappingId, SourceAccountCode = "1000", DestinationCode = "CASH",
        StatementSection = "ASSETS", Fraction = 1m, Rationale = "Synthetic approved mapping", CreatedAt = now
      });
      db.WorkTasks.AddRange(
        new WorkTask
        {
          Id = assignedTaskId, FirmId = f.FirmId, ClientId = f.ClientId,
          EngagementId = f.EngagementId, Title = assignedTaskTitle, MappingVersionId = mappingId,
          FsliCode = "CASH", CreatedAt = DateTimeOffset.UtcNow
        },
        new WorkTask
        {
          Id = siblingTaskId, FirmId = f.FirmId, ClientId = f.ClientId,
          EngagementId = siblingEngagementId, Title = siblingTaskTitle, CreatedAt = DateTimeOffset.UtcNow
        });
      db.TimeEntries.AddRange(
        new TimeEntry
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
          EngagementId = f.EngagementId, TaskId = assignedTaskId, UserId = f.Staff.Id,
          WorkDate = new DateOnly(2026, 9, 10), StartMinute = 540, DurationMinutes = 60,
          Role = "Staff", Activity = "Fieldwork", Narrative = assignedNarrative,
          MappingVersionId = mappingId, FsliCode = "CASH",
          BillableClassification = PracticeTimeStates.NonBillable,
          Status = PracticeTimeStates.TimeSubmitted, SubmittedAt = DateTimeOffset.UtcNow,
          NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
        },
        new TimeEntry
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
          EngagementId = siblingEngagementId, TaskId = siblingTaskId, UserId = f.Staff.Id,
          WorkDate = new DateOnly(2026, 9, 11), StartMinute = 540, DurationMinutes = 60,
          Role = "Staff", Activity = "Review", Narrative = siblingNarrative,
          BillableClassification = PracticeTimeStates.NonBillable,
          Status = PracticeTimeStates.TimeSubmitted, SubmittedAt = DateTimeOffset.UtcNow,
          NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
        });
      // Retained synthetic pending rows cross the default approval page boundary; no review is fabricated.
      for (var index = 0; index < 50; index++)
      {
        var entryId = Guid.NewGuid();
        pagedEntryIds.Add(entryId);
        db.TimeEntries.Add(new TimeEntry
        {
          Id = entryId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
          TaskId = assignedTaskId, UserId = f.Staff.Id, WorkDate = new DateOnly(2026, 9, 11),
          StartMinute = 600 + index * 2, DurationMinutes = 1, Role = "Staff", Activity = "Fieldwork",
          Narrative = $"SYN-PAGING-TIME-{index}", MappingVersionId = mappingId, FsliCode = "CASH",
          BillableClassification = PracticeTimeStates.NonBillable, Status = PracticeTimeStates.TimeSubmitted,
          SubmittedAt = now, NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Currency = "QAR", CreatedAt = now
        });
      }
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(engagementManager,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    var writes = new List<string>();
    page.Request += (_, request) =>
    {
      if (request.Method != "GET" && request.Url.Contains("/api/ui/", StringComparison.Ordinal)) writes.Add(request.Method + " " + request.Url);
    };
    var (firstStatus, firstBody) = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "GET" && request.Url.EndsWith("/api/ui/practice/time", StringComparison.Ordinal),
      () => page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp%2Fpractice%2Ftime"));
    Assert.Equal(200, firstStatus);
    using var firstWorkspace = JsonDocument.Parse(firstBody);
    var firstIds = firstWorkspace.RootElement.GetProperty("awaitingApproval").EnumerateArray()
      .Select(row => row.GetProperty("id").GetGuid()).ToArray();
    Assert.Equal(50, firstIds.Length);
    Assert.True(firstWorkspace.RootElement.GetProperty("approvalPage").GetProperty("hasMore").GetBoolean());
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Practice time & task records", Exact = true })).ToBeVisibleAsync();
    var approvalTable = page.GetByRole(AriaRole.Table, new() { Name = "Time entries awaiting my approval", Exact = true });
    await Assertions.Expect(approvalTable).ToContainTextAsync(assignedTaskTitle);
    await Assertions.Expect(page.GetByText(assignedNarrative, new() { Exact = true })).ToBeVisibleAsync();
    var body = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(siblingTaskTitle, body, StringComparison.Ordinal);
    Assert.DoesNotContain(siblingNarrative, body, StringComparison.Ordinal);
    Assert.DoesNotContain(periodCode, body, StringComparison.Ordinal);
    var pages = page.GetByRole(AriaRole.Navigation, new() { Name = "Time approval pages", Exact = true });
    await Assertions.Expect(pages).ToContainTextAsync("Page 1 · 50 shown");
    await Assertions.Expect(pages.GetByRole(AriaRole.Button, new() { Name = "Previous approvals", Exact = true })).ToBeDisabledAsync();
    var (nextStatus, nextBody) = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "GET" && request.Url.Contains("/api/ui/practice/time?approvalAfterWorkDate=", StringComparison.Ordinal),
      () => pages.GetByRole(AriaRole.Button, new() { Name = "Next approvals", Exact = true }).PressAsync("Enter"));
    Assert.Equal(200, nextStatus);
    using var nextWorkspace = JsonDocument.Parse(nextBody);
    var remaining = Assert.Single(nextWorkspace.RootElement.GetProperty("awaitingApproval").EnumerateArray());
    Assert.Contains(remaining.GetProperty("id").GetGuid(), pagedEntryIds);
    Assert.DoesNotContain(remaining.GetProperty("id").GetGuid(), firstIds);
    Assert.Equal(assignedTaskId, remaining.GetProperty("taskId").GetGuid());
    Assert.False(nextWorkspace.RootElement.GetProperty("approvalPage").GetProperty("hasMore").GetBoolean());
    await Assertions.Expect(pages).ToContainTextAsync("Page 2 · 1 shown");
    await Assertions.Expect(pages.GetByRole(AriaRole.Button, new() { Name = "Next approvals", Exact = true })).ToBeDisabledAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Time approval queue", Exact = true })).ToBeFocusedAsync();
    var secondPageBody = await page.Locator("main").InnerTextAsync();
    Assert.DoesNotContain(siblingTaskTitle, secondPageBody, StringComparison.Ordinal);
    Assert.DoesNotContain(siblingNarrative, secondPageBody, StringComparison.Ordinal);
    Assert.DoesNotContain(periodCode, secondPageBody, StringComparison.Ordinal);
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/app/practice/time", "time-approval-paging");
    var (previousStatus, previousBody) = await PlaywrightResponseCapture.RunAsync(page,
      request => request.Method == "GET" && request.Url.EndsWith("/api/ui/practice/time", StringComparison.Ordinal),
      () => pages.GetByRole(AriaRole.Button, new() { Name = "Previous approvals", Exact = true }).PressAsync("Space"));
    Assert.Equal(200, previousStatus);
    using var previousWorkspace = JsonDocument.Parse(previousBody);
    Assert.Equal(firstIds, previousWorkspace.RootElement.GetProperty("awaitingApproval").EnumerateArray()
      .Select(row => row.GetProperty("id").GetGuid()).ToArray());
    await Assertions.Expect(pages).ToContainTextAsync("Page 1 · 50 shown");
    Assert.Empty(errors);
    Assert.Empty(writes);
  }
}
