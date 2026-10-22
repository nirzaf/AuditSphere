using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Api.Tests;

/// <summary>
/// AS-PM8-02A: the time-draft endpoint forwards an optional start minute to the existing <c>SaveTimeDraftAsync</c>
/// command and the time workspace returns the recorded start minute. Legacy callers that omit it keep 09:00 (540);
/// interval validation, overlap, scope, closed-task, rate and independent-approval guards stay with the owning command.
/// </summary>
public sealed class PracticeTimeStartMinuteApiTests
{
  private const string EntriesPath = "/api/ui/practice/time/entries";
  private const string TasksPath = "/api/ui/practice/time/tasks";
  private const string WorkspacePath = "/api/ui/practice/time";
  private const string WorkDate = "2026-10-15";
  private const string IntervalMessage = "Time must fit within one bounded 24-hour work date.";
  private const string OverlapMessage = "The time entry overlaps another active entry.";

  [Fact]
  public async Task TaskPhaseIsForwardedWhenSelectedAndOmittedPhaseRemainsUnassigned()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TIME-TASK-PHASE");
    var f = await PbcSeed.SeedAsync(pg);
    using var factory = Factory(pg, f.Admin);
    using var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(admin);

    using var staffFactory = Factory(pg, f.Staff);
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    var staffCsrf = await SignIn(staff);

    var selected = await CreatedTaskAsync(staff, staffCsrf, new
    {
      title = "Synthetic phase-tagged practice task",
      engagementId = f.EngagementId,
      phase = BudgetPhases.Fieldwork
    });
    var omitted = await CreatedTaskAsync(staff, staffCsrf, new
    {
      title = "Synthetic task with no phase",
      engagementId = f.EngagementId
    });

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(BudgetPhases.Fieldwork, (await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == selected)).Phase);
      Assert.Equal(BudgetPhases.Unassigned, (await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == omitted)).Phase);
      Assert.Equal(f.Staff.Id, (await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == selected)).AssigneeUserId);
      Assert.Equal(f.Staff.Id, (await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == omitted)).AssigneeUserId);
    }

    var adminWorkspace = (await admin.GetFromJsonAsync<PracticeTimeWorkspace>(WorkspacePath))!;
    Assert.Equal(BudgetPhases.Fieldwork, Assert.Single(adminWorkspace.OpenTasks, x => x.Id == selected).Phase);
    Assert.Equal(BudgetPhases.Unassigned, Assert.Single(adminWorkspace.OpenTasks, x => x.Id == omitted).Phase);

    var selectedEntry = await CreatedAsync(staff, staffCsrf, Draft(selected, 600, 30));
    var omittedEntry = await CreatedAsync(staff, staffCsrf, Draft(omitted, 630, 30));
    var staffWorkspace = (await staff.GetFromJsonAsync<PracticeTimeWorkspace>(WorkspacePath))!;
    Assert.Equal(BudgetPhases.Fieldwork, Assert.Single(staffWorkspace.MyEntries, x => x.Id == selectedEntry).Phase);
    Assert.Equal(BudgetPhases.Unassigned, Assert.Single(staffWorkspace.MyEntries, x => x.Id == omittedEntry).Phase);

    await AssertOkAsync(staff, staffCsrf, $"{EntriesPath}/{selectedEntry}/submit");
    var approvalWorkspace = (await admin.GetFromJsonAsync<PracticeTimeWorkspace>(WorkspacePath))!;
    Assert.Equal(BudgetPhases.Fieldwork, Assert.Single(approvalWorkspace.AwaitingApproval, x => x.Id == selectedEntry).Phase);
  }

  [Fact]
  public async Task ApprovalQueueUsesBoundedKeysetPagesAndRejectsInvalidPagingParameters()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TIME-APPROVAL-PAGING");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var siblingClientId = Guid.NewGuid();
    var siblingEngagementId = Guid.NewGuid();
    await SeedSiblingScopeAsync(pg, f.FirmId, siblingClientId, siblingEngagementId);

    Guid taskId;
    Guid foreignTaskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      taskId = await CreateTaskAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId, f.EngagementId,
        "Synthetic approval paging task", f.Staff.Id);
      foreignTaskId = await CreateTaskAsync(db, PbcSeed.Actor(foreign.Admin, "Administrator"), foreign.ClientId,
        foreign.EngagementId, "Synthetic foreign-firm paging task", foreign.Staff.Id);
    }

    using var approverFactory = Factory(pg, f.Admin);
    using var approver = approverFactory.CreateClient(new() { AllowAutoRedirect = false });
    var approverCsrf = await SignIn(approver);
    using var staffFactory = Factory(pg, f.Staff);
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    var staffCsrf = await SignIn(staff);
    using var foreignStaffFactory = Factory(pg, foreign.Staff);
    using var foreignStaff = foreignStaffFactory.CreateClient(new() { AllowAutoRedirect = false });
    var foreignStaffCsrf = await SignIn(foreignStaff);

    var validIds = new List<Guid>();
    for (var index = 0; index < 4; index++)
    {
      var id = await CreatedAsync(staff, staffCsrf, Draft(taskId, 300 + index * 2, 1));
      await AssertOkAsync(staff, staffCsrf, $"{EntriesPath}/{id}/submit");
      validIds.Add(id);
    }

    // This pending entry belongs to the reviewer and must stay out of their own queue.
    var ownId = await CreatedAsync(approver, approverCsrf, Draft(taskId, 500, 1));
    await AssertOkAsync(approver, approverCsrf, $"{EntriesPath}/{ownId}/submit");
    var foreignId = await CreatedAsync(foreignStaff, foreignStaffCsrf, Draft(foreignTaskId, 600, 1));
    await AssertOkAsync(foreignStaff, foreignStaffCsrf, $"{EntriesPath}/{foreignId}/submit");

    // A synthetic legacy mismatch has an in-scope task id but a different valid client/engagement pair.
    // It sorts before the valid rows and must be excluded in SQL before the page boundary is applied.
    var invalidTaskBindingId = Guid.NewGuid();
    var submittedAt = DateTimeOffset.UtcNow;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.TimeEntries.Add(new TimeEntry
      {
        Id = invalidTaskBindingId, FirmId = f.FirmId, ClientId = siblingClientId, EngagementId = siblingEngagementId,
        TaskId = taskId, UserId = f.Staff.Id, WorkDate = new DateOnly(2026, 10, 14), StartMinute = 100,
        DurationMinutes = 1, Role = "Staff", Activity = "Synthetic paging decoy", Phase = BudgetPhases.Unassigned,
        BillableClassification = PracticeTimeStates.NonBillable, Narrative = "Synthetic mismatched task scope",
        NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Status = PracticeTimeStates.TimeSubmitted,
        Revision = 1, SubmittedAt = submittedAt, SubmissionRound = 1, CreatedAt = submittedAt
      });
      db.TimeEntryReviewEvents.Add(new TimeEntryReviewEvent
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, TimeEntryId = invalidTaskBindingId, SubmissionRound = 1,
        ActorUserId = f.Staff.Id, Action = PracticeTimeStates.ReviewSubmitted, CreatedAt = submittedAt
      });
      await db.SaveChangesAsync();
    }

    // The original URL remains valid and both it and the legacy application overload produce bounded metadata.
    var defaultWorkspace = (await approver.GetFromJsonAsync<PracticeTimeWorkspace>(WorkspacePath))!;
    Assert.Equal(50, defaultWorkspace.ApprovalPage.PageSize);
    Assert.False(defaultWorkspace.ApprovalPage.HasMore);
    Assert.Null(defaultWorkspace.ApprovalPage.NextCursor);
    Assert.Equal(validIds.OrderBy(id => id.ToString("D"), StringComparer.Ordinal),
      defaultWorkspace.AwaitingApproval.Select(x => x.Id));
    Assert.DoesNotContain(ownId, defaultWorkspace.AwaitingApproval.Select(x => x.Id));
    Assert.DoesNotContain(foreignId, defaultWorkspace.AwaitingApproval.Select(x => x.Id));
    Assert.DoesNotContain(invalidTaskBindingId, defaultWorkspace.AwaitingApproval.Select(x => x.Id));
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var legacyCall = await PracticeTimeWorkspaceQuery.GetAsync(db, PbcSeed.Actor(f.Admin, "Administrator"));
      Assert.True(legacyCall.Succeeded, legacyCall.Message);
      Assert.Equal(50, legacyCall.Value!.ApprovalPage.PageSize);
      Assert.False(legacyCall.Value.ApprovalPage.HasMore);
      Assert.Null(legacyCall.Value.ApprovalPage.NextCursor);
    }
    var staffWorkspace = (await staff.GetFromJsonAsync<PracticeTimeWorkspace>(WorkspacePath))!;
    Assert.False(staffWorkspace.IsApprover);
    Assert.Empty(staffWorkspace.AwaitingApproval);
    Assert.Equal(new TimeApprovalPageInfo(50, false, null), staffWorkspace.ApprovalPage);

    var firstPage = (await approver.GetFromJsonAsync<PracticeTimeWorkspace>($"{WorkspacePath}?approvalPageSize=2"))!;
    var expectedOrder = validIds.OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
    Assert.Equal(expectedOrder.Take(2), firstPage.AwaitingApproval.Select(x => x.Id));
    Assert.All(firstPage.AwaitingApproval, x => Assert.Equal(new DateOnly(2026, 10, 15), x.WorkDate));
    Assert.Equal(2, firstPage.ApprovalPage.PageSize);
    Assert.True(firstPage.ApprovalPage.HasMore);
    Assert.Equal(new TimeApprovalCursor(new DateOnly(2026, 10, 15), firstPage.AwaitingApproval[^1].Id),
      firstPage.ApprovalPage.NextCursor);

    // Removing earlier pending rows cannot move the same-date keyset past later entries.
    foreach (var row in firstPage.AwaitingApproval)
      await AssertOkAsync(approver, approverCsrf, $"{EntriesPath}/{row.Id}/approve");
    var cursor = firstPage.ApprovalPage.NextCursor!;
    var secondPage = (await approver.GetFromJsonAsync<PracticeTimeWorkspace>(
      $"{WorkspacePath}?approvalAfterWorkDate={cursor.WorkDate:yyyy-MM-dd}&approvalAfterEntryId={cursor.EntryId:D}&approvalPageSize=2"))!;
    Assert.Equal(expectedOrder.Skip(2), secondPage.AwaitingApproval.Select(x => x.Id));
    Assert.All(secondPage.AwaitingApproval, x => Assert.Equal(cursor.WorkDate, x.WorkDate));
    Assert.Equal(2, secondPage.ApprovalPage.PageSize);
    Assert.False(secondPage.ApprovalPage.HasMore);
    Assert.Null(secondPage.ApprovalPage.NextCursor);

    var invalidQueries = new[]
    {
      "?approvalAfterWorkDate=2026-10-15",
      $"?approvalAfterEntryId={Guid.NewGuid():D}",
      $"?approvalAfterWorkDate=not-a-date&approvalAfterEntryId={Guid.NewGuid():D}",
      $"?approvalAfterWorkDate=2026-10-15&approvalAfterEntryId={Guid.Empty:D}",
      "?approvalPageSize=not-a-number",
      "?approvalPageSize=0",
      "?approvalPageSize=101",
      "?approvalPageSize=2&approvalPageSize=3"
    };
    foreach (var suffix in invalidQueries)
    {
      using var response = await approver.GetAsync(WorkspacePath + suffix);
      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var invalidCursor = await PracticeTimeWorkspaceQuery.GetAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        new PracticeTimeWorkspaceRequest(new DateOnly(2026, 10, 15), null, 2));
      Assert.False(invalidCursor.Succeeded);
      Assert.Equal("time.invalid", invalidCursor.ErrorCode);
      var invalidPageSize = await PracticeTimeWorkspaceQuery.GetAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        new PracticeTimeWorkspaceRequest(ApprovalPageSize: 101));
      Assert.False(invalidPageSize.Succeeded);
      Assert.Equal("time.invalid", invalidPageSize.ErrorCode);
    }
  }

  [Fact]
  public async Task PersonalEntryHistoryUsesBoundedDateCreationAndIdKeysetWithinCurrentScope()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TIME-ENTRY-HISTORY-PAGING");
    var f = await PbcSeed.SeedAsync(pg);
    var siblingClientId = Guid.NewGuid();
    var siblingEngagementId = Guid.NewGuid();
    await SeedSiblingScopeAsync(pg, f.FirmId, siblingClientId, siblingEngagementId);

    Guid targetTaskId;
    Guid siblingTaskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      targetTaskId = await CreateTaskAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId, f.EngagementId,
        "Synthetic scoped personal history task");
      siblingTaskId = await CreateTaskAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), siblingClientId, siblingEngagementId,
        "Synthetic sibling personal history task");
    }

    static Guid EntryId(long suffix) => Guid.Parse("00000000-0000-0000-0000-" + suffix.ToString("D12", CultureInfo.InvariantCulture));
    var tiedCreatedAt = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero).AddTicks(1_234_560);
    var olderCreatedAt = tiedCreatedAt.AddMinutes(-1);
    var newerCreatedAt = tiedCreatedAt.AddMinutes(1);
    // More than the old 500-row cap: the same CreatedAt instant also exercises the id tie-break.
    var tiedIds = Enumerable.Range(1, 505).Select(index => EntryId(index)).ToArray();
    var olderTimestampId = EntryId(1001);
    var olderWorkDateId = EntryId(1002);
    var otherUserId = EntryId(1010);
    var siblingScopeId = EntryId(1011);
    var mismatchedTaskScopeId = EntryId(1012);
    var firmWideMismatchedTaskIds = Enumerable.Range(1020, 3).Select(index => EntryId(index)).ToArray();
    var newerWorkDate = new DateOnly(2026, 10, 16);
    var workDate = new DateOnly(2026, 10, 15);

    TimeEntry HistoryEntry(Guid id, Guid userId, Guid taskId, Guid clientId, Guid engagementId,
      DateOnly date, DateTimeOffset createdAt, int startMinute = 0) => new()
    {
      Id = id, FirmId = f.FirmId, ClientId = clientId, EngagementId = engagementId, TaskId = taskId, UserId = userId,
      WorkDate = date, StartMinute = startMinute, DurationMinutes = 1, Role = "Staff", Activity = "Synthetic history row",
      BillableClassification = PracticeTimeStates.NonBillable, Narrative = "Synthetic personal history paging fixture",
      NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Status = PracticeTimeStates.TimeDraft,
      Revision = 1, CreatedAt = createdAt
    };

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.TimeEntries.AddRange(
        tiedIds.Select((id, index) => HistoryEntry(id, f.Staff.Id, targetTaskId, f.ClientId, f.EngagementId, workDate, tiedCreatedAt, 600 + index))
          .Append(HistoryEntry(olderTimestampId, f.Staff.Id, targetTaskId, f.ClientId, f.EngagementId, workDate, olderCreatedAt))
          // Despite its later creation instant, the earlier work date sorts after all October 15 rows.
          .Append(HistoryEntry(olderWorkDateId, f.Staff.Id, targetTaskId, f.ClientId, f.EngagementId,
            new DateOnly(2026, 10, 14), newerCreatedAt))
          .Append(HistoryEntry(otherUserId, f.Admin.Id, targetTaskId, f.ClientId, f.EngagementId,
            new DateOnly(2026, 10, 18), newerCreatedAt))
          .Append(HistoryEntry(siblingScopeId, f.Staff.Id, siblingTaskId, siblingClientId, siblingEngagementId,
            new DateOnly(2026, 10, 17), newerCreatedAt))
          // Entry scope matches the staff grant, but its task belongs to a sibling client.
          .Append(HistoryEntry(mismatchedTaskScopeId, f.Staff.Id, siblingTaskId, f.ClientId, f.EngagementId,
            newerWorkDate, newerCreatedAt))
          // A firm-wide reviewer can see both tasks, so these rows exercise the task/scope identity filter itself.
          .Concat(firmWideMismatchedTaskIds.Select(id => HistoryEntry(id, f.Admin.Id, siblingTaskId, f.ClientId,
            f.EngagementId, new DateOnly(2026, 10, 19), newerCreatedAt))));
      await db.SaveChangesAsync();
    }

    using var staffFactory = Factory(pg, f.Staff);
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(staff);

    var complete = (await staff.GetFromJsonAsync<PracticeTimeWorkspace>(WorkspacePath))!;
    Assert.Equal(tiedIds.Reverse().Take(50), complete.MyEntries.Select(x => x.Id));
    Assert.DoesNotContain(otherUserId, complete.MyEntries.Select(x => x.Id));
    Assert.DoesNotContain(siblingScopeId, complete.MyEntries.Select(x => x.Id));
    Assert.DoesNotContain(mismatchedTaskScopeId, complete.MyEntries.Select(x => x.Id));
    Assert.Equal(new TimeEntryHistoryPageInfo(50, true,
      new TimeEntryHistoryCursor(workDate, tiedCreatedAt, tiedIds[^50])), complete.EntriesPage);
    Assert.All(complete.MyEntries, row => Assert.NotNull(row.CreatedAt));
    Assert.All(complete.MyEntries, row => Assert.Equal(TimeSpan.Zero, row.CreatedAt!.Value.Offset));

    var firstPage = (await staff.GetFromJsonAsync<PracticeTimeWorkspace>($"{WorkspacePath}?entriesPageSize=2"))!;
    Assert.Equal(tiedIds.Reverse().Take(2), firstPage.MyEntries.Select(x => x.Id));
    Assert.Equal(2, firstPage.EntriesPage.PageSize);
    Assert.True(firstPage.EntriesPage.HasMore);
    var firstCursor = firstPage.EntriesPage.NextCursor!;
    Assert.Equal(new TimeEntryHistoryCursor(workDate, tiedCreatedAt, tiedIds[^2]), firstCursor);

    // An explicit non-UTC cursor offset is accepted and normalized to the same UTC instant.
    var offsetInstant = Uri.EscapeDataString(firstCursor.CreatedAt.ToOffset(TimeSpan.FromHours(3)).ToString("O"));
    var secondPage = (await staff.GetFromJsonAsync<PracticeTimeWorkspace>(
      $"{WorkspacePath}?entriesBeforeWorkDate={firstCursor.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&entriesBeforeCreatedAt={offsetInstant}&entriesBeforeEntryId={firstCursor.EntryId:D}&entriesPageSize=2"))!;
    Assert.Equal(tiedIds.Reverse().Skip(2).Take(2), secondPage.MyEntries.Select(x => x.Id));
    Assert.True(secondPage.EntriesPage.HasMore);
    var secondCursor = secondPage.EntriesPage.NextCursor!;
    Assert.Equal(new TimeEntryHistoryCursor(workDate, tiedCreatedAt, tiedIds[^4]), secondCursor);

    // The 501st and 502nd descending rows remain addressable after the former Take(500) boundary.
    var beyondLegacyCapInstant = Uri.EscapeDataString(tiedCreatedAt.ToString("O"));
    var beyondLegacyCap = (await staff.GetFromJsonAsync<PracticeTimeWorkspace>(
      $"{WorkspacePath}?entriesBeforeWorkDate={workDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&entriesBeforeCreatedAt={beyondLegacyCapInstant}&entriesBeforeEntryId={EntryId(6):D}&entriesPageSize=2"))!;
    Assert.Equal([EntryId(5), EntryId(4)], beyondLegacyCap.MyEntries.Select(x => x.Id));
    Assert.True(beyondLegacyCap.EntriesPage.HasMore);

    var finalInstant = Uri.EscapeDataString(tiedCreatedAt.ToString("O"));
    var finalPage = (await staff.GetFromJsonAsync<PracticeTimeWorkspace>(
      $"{WorkspacePath}?entriesBeforeWorkDate={workDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&entriesBeforeCreatedAt={finalInstant}&entriesBeforeEntryId={EntryId(1):D}&entriesPageSize=2"))!;
    Assert.Equal([olderTimestampId, olderWorkDateId], finalPage.MyEntries.Select(x => x.Id));
    Assert.False(finalPage.EntriesPage.HasMore);
    Assert.Null(finalPage.EntriesPage.NextCursor);

    // The firm-wide user's task list contains the sibling task, but task/client/engagement mismatches
    // are still removed before Take so they cannot occupy the page or its look-ahead row.
    using var adminFactory = Factory(pg, f.Admin);
    using var admin = adminFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(admin);
    var adminPage = (await admin.GetFromJsonAsync<PracticeTimeWorkspace>($"{WorkspacePath}?entriesPageSize=2"))!;
    Assert.Equal([otherUserId], adminPage.MyEntries.Select(x => x.Id));
    Assert.False(adminPage.EntriesPage.HasMore);
    Assert.Null(adminPage.EntriesPage.NextCursor);
    Assert.DoesNotContain(adminPage.MyEntries, row => firmWideMismatchedTaskIds.Contains(row.Id));

    var invalidQueries = new[]
    {
      "?entriesBeforeWorkDate=2026-10-15",
      $"?entriesBeforeCreatedAt={Uri.EscapeDataString(tiedCreatedAt.ToString("O"))}",
      $"?entriesBeforeEntryId={Guid.NewGuid():D}",
      $"?entriesBeforeWorkDate=2026-10-15&entriesBeforeCreatedAt={Uri.EscapeDataString(tiedCreatedAt.ToString("O"))}",
      $"?entriesBeforeWorkDate=not-a-date&entriesBeforeCreatedAt={Uri.EscapeDataString(tiedCreatedAt.ToString("O"))}&entriesBeforeEntryId={Guid.NewGuid():D}",
      $"?entriesBeforeWorkDate=2026-10-15&entriesBeforeCreatedAt={Uri.EscapeDataString("2026-10-15T12:00:00.123456")}&entriesBeforeEntryId={Guid.NewGuid():D}",
      $"?entriesBeforeWorkDate=2026-10-15&entriesBeforeCreatedAt={Uri.EscapeDataString("2026-13-15T12:00:00Z")}&entriesBeforeEntryId={Guid.NewGuid():D}",
      $"?entriesBeforeWorkDate=2026-10-15&entriesBeforeCreatedAt={Uri.EscapeDataString("2026-10-15T12:00:00.0000001Z")}&entriesBeforeEntryId={Guid.NewGuid():D}",
      $"?entriesBeforeWorkDate=2026-10-15&entriesBeforeCreatedAt={Uri.EscapeDataString(tiedCreatedAt.ToString("O"))}&entriesBeforeEntryId={Guid.Empty:D}",
      $"?entriesBeforeWorkDate=2026-10-15&entriesBeforeCreatedAt={Uri.EscapeDataString(tiedCreatedAt.ToString("O"))}&entriesBeforeEntryId=not-a-guid",
      "?entriesBeforeCreatedAt=",
      "?entriesBeforeEntryId=",
      $"?entriesBeforeWorkDate=2026-10-15&entriesBeforeWorkDate=2026-10-15&entriesBeforeCreatedAt={Uri.EscapeDataString(tiedCreatedAt.ToString("O"))}&entriesBeforeEntryId={Guid.NewGuid():D}",
      "?entriesPageSize=not-a-number",
      "?entriesPageSize=0",
      "?entriesPageSize=101",
      "?entriesPageSize=2&entriesPageSize=3",
      "?entriesPageSize="
    };
    foreach (var suffix in invalidQueries)
    {
      using var response = await staff.GetAsync(WorkspacePath + suffix);
      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var invalidCursor = await PracticeTimeWorkspaceQuery.GetAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
        new PracticeTimeWorkspaceRequest(EntriesBeforeWorkDate: workDate));
      Assert.False(invalidCursor.Succeeded);
      Assert.Equal("time.invalid", invalidCursor.ErrorCode);
      await db.RoleGrants.Where(x => x.FirmId == f.FirmId && x.UserId == f.Staff.Id && x.Role == "Staff" &&
          x.ClientId == f.ClientId && x.EngagementId == f.EngagementId && x.RevokedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var revokedScope = await PracticeTimeWorkspaceQuery.GetAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
        new PracticeTimeWorkspaceRequest(EntriesPageSize: 2));
      Assert.False(revokedScope.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, revokedScope.ErrorCode);
      Assert.Null(revokedScope.Value);
    }
  }

  [Fact]
  public async Task StartMinuteIsForwardedValidatedPersistedAndReturnedWithoutFalseOverlap()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TIME-START-MINUTE");
    var f = await PbcSeed.SeedAsync(pg);
    Guid task;
    await using (var db = new AuditSphereDbContext(pg.Options))
      task = await CreateTaskAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId, f.EngagementId,
        "Synthetic timer task", f.Staff.Id);

    using var factory = Factory(pg, f.Staff);
    using var staff = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(staff);

    // Non-billable 2026-10-15 at 10:00 for 30 minutes: an ordinary DRAFT, never submitted, approved, posted or billed.
    var first = await CreatedAsync(staff, csrf, Draft(task, 600, 30));
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var entry = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == first);
      Assert.Equal(600, entry.StartMinute);
      Assert.Equal(30, entry.DurationMinutes);
      Assert.Equal(PracticeTimeStates.TimeDraft, entry.Status);
      Assert.Equal(PracticeTimeStates.NonBillable, entry.BillableClassification);
      Assert.Equal(f.Staff.Id, entry.UserId);
      Assert.Equal(new DateOnly(2026, 10, 15), entry.WorkDate);
      Assert.Null(entry.SubmittedAt);
      Assert.Null(entry.ApprovedAt);
      Assert.Null(entry.RatePerHour);
    }
    Assert.Equal(600, await WorkspaceStartMinuteAsync(staff, "myEntries", first));

    // A consecutive timer segment (630..660) touches but does not overlap 600..630.
    var consecutive = await CreatedAsync(staff, csrf, Draft(task, 630, 30));
    // 629..660 overlaps 600..630; an exact repeated create cannot silently add a duplicate interval.
    await AssertFailureAsync(staff, csrf, Draft(task, 629, 31), HttpStatusCode.BadRequest, "time.overlap", OverlapMessage);
    await AssertFailureAsync(staff, csrf, Draft(task, 600, 30), HttpStatusCode.BadRequest, "time.overlap", OverlapMessage);

    // Omitted keeps the legacy 09:00 start; explicit zero is midnight, not a missing value; 1439+1 ends exactly at 24:00.
    var legacy = await CreatedAsync(staff, csrf, Draft(task, null, 30));
    var midnight = await CreatedAsync(staff, csrf, Draft(task, 0, 30));
    var lastMinute = await CreatedAsync(staff, csrf, Draft(task, 1439, 1));

    // Invalid intervals are refused by the existing ValidateTime rule with its exact code and message.
    foreach (var (start, duration) in new[] { (1440, 1), (-1, 1), (1439, 2), (600, 0) })
      await AssertFailureAsync(staff, csrf, Draft(task, start, duration), HttpStatusCode.BadRequest, "time.invalid", IntervalMessage);
    // The existing work-date parser and failure are unchanged.
    await AssertFailureAsync(staff, csrf, Draft(task, 600, 30, "15/10/2026"), HttpStatusCode.BadRequest, "request.invalid", "Enter the work date.");

    var expected = new Dictionary<Guid, int>
    {
      [first] = 600, [consecutive] = 630, [legacy] = 540, [midnight] = 0, [lastMinute] = 1439
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var stored = await db.TimeEntries.AsNoTracking().Where(x => x.FirmId == f.FirmId).ToListAsync();
      Assert.Equal(expected.Keys.Order(), stored.Select(x => x.Id).Order());
      Assert.All(stored, x =>
      {
        Assert.Equal(expected[x.Id], x.StartMinute);
        Assert.Equal(PracticeTimeStates.TimeDraft, x.Status);
      });
    }

    // The workspace returns the persisted start minute for every row, as a JSON integer.
    using var workspace = JsonDocument.Parse(await staff.GetStringAsync(WorkspacePath));
    var rows = workspace.RootElement.GetProperty("myEntries").EnumerateArray().ToArray();
    Assert.Equal(expected.Count, rows.Length);
    foreach (var row in rows)
    {
      Assert.Equal(JsonValueKind.Number, row.GetProperty("startMinute").ValueKind);
      Assert.Equal(expected[row.GetProperty("id").GetGuid()], row.GetProperty("startMinute").GetInt32());
    }
    var typed = (await staff.GetFromJsonAsync<PracticeTimeWorkspace>(WorkspacePath))!;
    Assert.Equal(expected.OrderBy(x => x.Key).Select(x => x.Value), typed.MyEntries.OrderBy(x => x.Id).Select(x => x.StartMinute));
  }

  [Fact]
  public async Task RefusalsSessionCsrfAndIndependentApprovalKeepTheirExistingBehavior()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TIME-START-MINUTE-GUARDS");
    var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    var siblingClientId = Guid.NewGuid(); var siblingEngagementId = Guid.NewGuid();
    await SeedSiblingScopeAsync(pg, f.FirmId, siblingClientId, siblingEngagementId);
    var admin = PbcSeed.Actor(f.Admin, "Administrator");
    Guid task, siblingTask, foreignTask, closedTask;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      task = await CreateTaskAsync(db, admin, f.ClientId, f.EngagementId, "Synthetic scoped task", f.Staff.Id);
      siblingTask = await CreateTaskAsync(db, admin, siblingClientId, siblingEngagementId, "Synthetic sibling-client task");
      foreignTask = await CreateTaskAsync(db, PbcSeed.Actor(foreign.Admin, "Administrator"), foreign.ClientId, foreign.EngagementId,
        "Synthetic foreign-firm task");
      closedTask = await CreateTaskAsync(db, admin, f.ClientId, f.EngagementId, "Synthetic closed task");
      Assert.True((await PracticeTimeService.CompleteTaskAsync(db, admin, closedTask)).Succeeded);
    }

    using var staffFactory = Factory(pg, f.Staff);
    // No cookie: the existing authenticated-session challenge refuses the command.
    using (var anonymous = staffFactory.CreateClient(new() { AllowAutoRedirect = false }))
    {
      using var refused = await Post(anonymous, null, EntriesPath, Draft(task, 600, 30));
      Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(staff);

    // Invalid or missing antiforgery token returns 403 before the command runs.
    await AssertFailureAsync(staff, "forged", Draft(task, 600, 30), HttpStatusCode.Forbidden, "csrf.invalid", "Refresh the page and try again.");
    await AssertFailureAsync(staff, null, Draft(task, 600, 30), HttpStatusCode.Forbidden, "csrf.invalid", "Refresh the page and try again.");
    // Another client's task in the same firm, another firm's task and a closed task keep their refusals verbatim.
    await AssertFailureAsync(staff, csrf, Draft(siblingTask, 600, 30), HttpStatusCode.Forbidden, ErrorCodes.ScopeDenied, "Access denied.");
    await AssertFailureAsync(staff, csrf, Draft(foreignTask, 600, 30), HttpStatusCode.Forbidden, ErrorCodes.ScopeDenied, "Access denied.");
    await AssertFailureAsync(staff, csrf, Draft(closedTask, 600, 30), HttpStatusCode.BadRequest, ErrorCodes.GateBlocked,
      "Time cannot be added to a closed task.");
    // Billable time still requires an approved rate card; no rate is calculated by the endpoint.
    await AssertFailureAsync(staff, csrf, Draft(task, 600, 30, billable: true), HttpStatusCode.BadRequest, "time.rate-missing",
      "An approved rate card is required for billable time.");
    // A user without a time-recording role keeps the existing scope refusal.
    using var reviewerFactory = Factory(pg, f.Reviewer);
    using var reviewer = reviewerFactory.CreateClient(new() { AllowAutoRedirect = false });
    var reviewerCsrf = await SignIn(reviewer);
    await AssertFailureAsync(reviewer, reviewerCsrf, Draft(task, 600, 30), HttpStatusCode.Forbidden, ErrorCodes.ScopeDenied, "Access denied.");
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.Empty(await db.TimeEntries.AsNoTracking().ToListAsync());

    // Submission and independent approval still use the existing commands; the draft itself is never submitted.
    var entry = await CreatedAsync(staff, csrf, Draft(task, 600, 30));
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.Equal(PracticeTimeStates.TimeDraft, (await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == entry)).Status);
    await AssertOkAsync(staff, csrf, $"{EntriesPath}/{entry}/submit");
    // The staff preparer holds no approval role.
    using (var selfApproval = await Post(staff, csrf, $"{EntriesPath}/{entry}/approve", new { }))
      Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);

    using var adminFactory = Factory(pg, f.Admin);
    using var approver = adminFactory.CreateClient(new() { AllowAutoRedirect = false });
    var approverCsrf = await SignIn(approver);
    Assert.Equal(600, await WorkspaceStartMinuteAsync(approver, "awaitingApproval", entry));
    // An approver who prepared an entry still cannot approve it.
    var own = await CreatedAsync(approver, approverCsrf, Draft(task, 600, 30));
    await AssertOkAsync(approver, approverCsrf, $"{EntriesPath}/{own}/submit");
    await AssertFailureAsync(approver, approverCsrf, new { }, HttpStatusCode.BadRequest, ErrorCodes.ProtectedState,
      "Time preparers cannot approve their own entry.", $"{EntriesPath}/{own}/approve");
    await AssertOkAsync(approver, approverCsrf, $"{EntriesPath}/{entry}/approve");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var approved = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == entry);
      Assert.Equal(PracticeTimeStates.TimeApproved, approved.Status);
      Assert.Equal(f.Admin.Id, approved.ApprovedByUserId);
      Assert.Equal(600, approved.StartMinute);
      Assert.Equal(30, approved.DurationMinutes);
      var ownEntry = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == own);
      Assert.Equal(PracticeTimeStates.TimeSubmitted, ownEntry.Status);
      Assert.Null(ownEntry.ApprovedByUserId);
      Assert.Equal(2, await db.TimeEntries.AsNoTracking().CountAsync());
    }

    // Expiry must take effect before the expiry worker marks a grant revoked. A live
    // engagement Staff grant must not combine with an expired firm-wide Manager grant.
    var beforeExpiryDecoy = (await staff.GetFromJsonAsync<PracticeTimeWorkspace>(WorkspacePath))!;
    Assert.False(beforeExpiryDecoy.FirmWide);
    Assert.False(beforeExpiryDecoy.IsApprover);
    Assert.Empty(beforeExpiryDecoy.AwaitingApproval);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var expired = PbcSeed.Grant(f.FirmId, f.Staff, "Manager");
      expired.GrantedAt = DateTimeOffset.UtcNow.AddDays(-2);
      expired.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
      db.RoleGrants.Add(expired);
      await db.SaveChangesAsync();
    }
    // The Application query must not depend on the HTTP actor resolver first revoking
    // expired grants. Invoke it before that resolver's independent expiry protection.
    PracticeTimeWorkspace scoped;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var applicationResult = await PracticeTimeWorkspaceQuery.GetAsync(db, PbcSeed.Actor(f.Staff, "Staff"));
      Assert.True(applicationResult.Succeeded, applicationResult.Message);
      scoped = applicationResult.Value!;
    }
    Assert.False(scoped.FirmWide);
    Assert.False(scoped.IsApprover);
    Assert.Empty(scoped.AwaitingApproval);
    Assert.Equal(beforeExpiryDecoy.OpenTasks.Select(x => x.Id).Order(), scoped.OpenTasks.Select(x => x.Id).Order());
    Assert.Equal(beforeExpiryDecoy.Engagements.Select(x => x.Id).Order(), scoped.Engagements.Select(x => x.Id).Order());
    Assert.Equal(beforeExpiryDecoy.MyEntries.Select(x => x.Id).Order(), scoped.MyEntries.Select(x => x.Id).Order());
    Assert.DoesNotContain(scoped.OpenTasks, x => x.Id == siblingTask || x.Id == foreignTask);
    // The HTTP boundary independently revokes the expired grant and rejects the old
    // session. A fresh sign-in then retains the same narrow workspace.
    using (var expiredSession = await staff.GetAsync(WorkspacePath))
      Assert.Equal(HttpStatusCode.Unauthorized, expiredSession.StatusCode);
    await SignIn(staff);
    var freshWorkspace = (await staff.GetFromJsonAsync<PracticeTimeWorkspace>(WorkspacePath))!;
    Assert.False(freshWorkspace.FirmWide);
    Assert.False(freshWorkspace.IsApprover);
    Assert.Empty(freshWorkspace.AwaitingApproval);
  }

  [Theory]
  [InlineData(true, ErrorCodes.ScopeDenied)]
  [InlineData(false, ErrorCodes.GenerationStale)]
  public async Task WorkspaceRead_AuthorityChangedAfterTimeEntriesReadReturnsNoProjection(bool revokeManagerGrant, string expectedError)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TIME-START-MINUTE-READ-FENCE");
    var f = await PbcSeed.SeedAsync(pg);
    var siblingClientId = Guid.NewGuid();
    var siblingEngagementId = Guid.NewGuid();
    await SeedSiblingScopeAsync(pg, f.FirmId, siblingClientId, siblingEngagementId);
    var manager = PbcSeed.Actor(f.Staff, "Manager");
    Guid siblingTask;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // The exact Staff assignment survives the interleaving; only the grant that
      // allowed this actor's firm-wide projection is revoked in that case.
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager"));
      await db.SaveChangesAsync();
      Assert.True((await PracticeTimeService.CreateTaskAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        new CreateTaskRequest("Synthetic sibling timer task", siblingClientId, siblingEngagementId))).Succeeded);
      siblingTask = await db.WorkTasks.Where(x => x.FirmId == f.FirmId && x.ClientId == siblingClientId)
        .Select(x => x.Id).SingleAsync();
    }

    // Establish that the same-scope positive call legitimately sees the firm-wide
    // projection and sibling before the deterministic mid-read authority change.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var baseline = await PracticeTimeWorkspaceQuery.GetAsync(db, manager);
      Assert.True(baseline.Succeeded, baseline.Message);
      Assert.True(baseline.Value!.FirmWide);
      Assert.Contains(siblingEngagementId, baseline.Value.Engagements.Select(x => x.Id));
      Assert.Contains(siblingTask, baseline.Value.OpenTasks.Select(x => x.Id));
    }

    var interceptor = new RevokePracticeTimeReadAuthority(pg, f.FirmId, f.Staff.Id, revokeManagerGrant);
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using (var db = new AuditSphereDbContext(options))
    {
      var result = await PracticeTimeWorkspaceQuery.GetAsync(db, manager);
      Assert.True(interceptor.InterleavedUpdates == 1, "The authority update must occur after the time-entry query executes.");
      Assert.False(result.Succeeded);
      Assert.Equal(expectedError, result.ErrorCode);
      Assert.Null(result.Value);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var survivingStaff = await db.RoleGrants.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId &&
        x.UserId == f.Staff.Id && x.Role == "Staff" && x.ClientId == f.ClientId && x.EngagementId == f.EngagementId);
      Assert.Null(survivingStaff.RevokedAt);
      var firmManager = await db.RoleGrants.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId &&
        x.UserId == f.Staff.Id && x.Role == "Manager" && x.ClientId == null && x.EngagementId == null);
      Assert.Equal(revokeManagerGrant, firmManager.RevokedAt is not null);
      var persistedUser = await db.Users.AsNoTracking().SingleAsync(x => x.Id == f.Staff.Id);
      Assert.Equal(manager.SessionEpoch + (revokeManagerGrant ? 0 : 1), persistedUser.SessionEpoch);
    }
  }

  [Fact]
  public async Task ReturnedEditsAndApprovedCorrectionsRetainActorOwnedRecoverableHistory()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TIME-REVIEW-CHANGES");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    using var adminFactory = Factory(pg, f.Admin);
    using var staffFactory = Factory(pg, f.Staff);
    using var foreignFactory = Factory(pg, foreign.Admin);
    using var admin = adminFactory.CreateClient(new() { AllowAutoRedirect = false });
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    using var stranger = foreignFactory.CreateClient(new() { AllowAutoRedirect = false });
    var adminCsrf = await SignIn(admin);
    var staffCsrf = await SignIn(staff);
    await SignIn(stranger);
    var task = await CreatedTaskAsync(staff, staffCsrf, new { title = "Synthetic reviewed time", engagementId = f.EngagementId });
    var entry = await CreatedAsync(staff, staffCsrf, Draft(task, 600, 30));
    await AssertOkAsync(staff, staffCsrf, $"{EntriesPath}/{entry}/submit");
    using (var returned = await Post(admin, adminCsrf, $"{EntriesPath}/{entry}/return", new { reason = "Explain the retained workpaper effort." }))
      Assert.Equal(HttpStatusCode.OK, returned.StatusCode);

    var edit = new Dictionary<string, object?>
    {
      ["requestId"] = Guid.NewGuid(), ["expectedRevision"] = "1", ["kind"] = "EDIT_UNAPPROVED",
      ["workDate"] = WorkDate, ["startMinute"] = 600, ["durationMinutes"] = 45,
      ["activity"] = "Synthetic fieldwork", ["narrative"] = "Corrected workpaper effort", ["reason"] = "Reconciled the recorded minutes to the workpaper."
    };
    var path = $"{EntriesPath}/{entry}/changes";
    using (var noCsrf = await Post(staff, "invalid", path, edit)) Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
    async Task<Guid> SaveChange(Dictionary<string, object?> body)
    {
      using var response = await Post(staff, staffCsrf, path, body);
      var text = await response.Content.ReadAsStringAsync();
      Assert.True(response.StatusCode == HttpStatusCode.OK, text);
      using var document = JsonDocument.Parse(text);
      return document.RootElement.GetProperty("value").GetGuid();
    }
    Assert.Equal(entry, await SaveChange(edit));
    Assert.Equal(entry, await SaveChange(edit));
    using (var conflict = await Post(staff, staffCsrf, path, new Dictionary<string, object?>(edit) { ["durationMinutes"] = 46 }))
      Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    var own = (await staff.GetFromJsonAsync<PracticeTimeWorkspace>(WorkspacePath))!;
    var revised = Assert.Single(own.MyEntries);
    Assert.Equal(PracticeTimeStates.TimeReturned, revised.Status);
    Assert.Equal("2", revised.Revision);
    Assert.Equal(45, revised.DurationMinutes);
    Assert.Equal("Explain the retained workpaper effort.", revised.LastReturnReason);
    Assert.True(revised.CanEdit);
    Assert.False(revised.CanCorrect);

    using (var recovery = await staff.GetAsync(path + "/requests/" + edit["requestId"]))
    {
      Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
      using var receipt = JsonDocument.Parse(await recovery.Content.ReadAsStringAsync());
      Assert.True(receipt.RootElement.GetProperty("found").GetBoolean());
      var value = receipt.RootElement.GetProperty("receipt");
      Assert.Equal(f.Staff.Id, value.GetProperty("actorId").GetGuid());
      Assert.Equal("1", value.GetProperty("sourceRevision").GetString());
      Assert.Equal("2", value.GetProperty("resultRevision").GetString());
      Assert.Equal(30, value.GetProperty("before").GetProperty("durationMinutes").GetInt32());
      Assert.Equal(45, value.GetProperty("after").GetProperty("durationMinutes").GetInt32());
    }
    using (var otherActorRecovery = await admin.GetAsync(path + "/requests/" + edit["requestId"]))
    {
      Assert.Equal(HttpStatusCode.OK, otherActorRecovery.StatusCode);
      using var lookup = JsonDocument.Parse(await otherActorRecovery.Content.ReadAsStringAsync());
      Assert.False(lookup.RootElement.GetProperty("found").GetBoolean());
      Assert.Equal(JsonValueKind.Null, lookup.RootElement.GetProperty("receipt").ValueKind);
    }
    using (var denied = await stranger.GetAsync(path))
    {
      Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
      Assert.DoesNotContain("Corrected workpaper effort", await denied.Content.ReadAsStringAsync());
    }

    await AssertOkAsync(staff, staffCsrf, $"{EntriesPath}/{entry}/submit");
    await AssertOkAsync(admin, adminCsrf, $"{EntriesPath}/{entry}/approve");
    var approved = Assert.Single((await staff.GetFromJsonAsync<PracticeTimeWorkspace>(WorkspacePath))!.MyEntries);
    Assert.True(approved.CanCorrect);
    var correction = new Dictionary<string, object?>(edit)
    {
      ["requestId"] = Guid.NewGuid(), ["expectedRevision"] = "2", ["kind"] = "CORRECT_APPROVED",
      ["durationMinutes"] = 35, ["narrative"] = "Reviewed approved-time correction", ["reason"] = "Remove duplicated review effort."
    };
    var correctedId = await SaveChange(correction);
    Assert.NotEqual(entry, correctedId);
    Assert.Equal(correctedId, await SaveChange(correction));
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(2, await db.TimeEntries.CountAsync(x => x.FirmId == f.FirmId));
      Assert.Equal(2, await db.TimeEntryChangeReceipts.CountAsync(x => x.FirmId == f.FirmId));
      var original = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == entry);
      var draft = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == correctedId);
      Assert.Equal(PracticeTimeStates.TimeSuperseded, original.Status);
      Assert.Equal(2L, original.Revision);
      Assert.Equal(PracticeTimeStates.TimeDraft, draft.Status);
      Assert.Equal(3L, draft.Revision);
      Assert.Equal(entry, draft.SupersedesId);
      Assert.Equal(original.RateCardVersionId, draft.RateCardVersionId);
      Assert.Equal(original.RatePerHour, draft.RatePerHour);
      Assert.Equal(original.BillableClassification, draft.BillableClassification);
      Assert.Equal(4, await db.TimeEntryReviewEvents.CountAsync(x => x.TimeEntryId == entry));
    }
    using var historyResponse = await staff.GetAsync(path);
    Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
    using var history = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
    Assert.Equal(2, history.RootElement.GetProperty("items").GetArrayLength());
    Assert.False(history.RootElement.GetProperty("hasMore").GetBoolean());
    using var malformedCursor = await staff.GetAsync(path + "?beforeReceiptId=not-a-guid");
    Assert.Equal(HttpStatusCode.BadRequest, malformedCursor.StatusCode);
  }

  private static object Draft(Guid taskId, int? startMinute, int durationMinutes, string workDate = WorkDate, bool billable = false) =>
    startMinute is { } start
      ? new { taskId, workDate, durationMinutes, role = "Staff", activity = "Synthetic fieldwork", billable, narrative = "Synthetic timer segment", startMinute = start }
      : new { taskId, workDate, durationMinutes, role = "Staff", activity = "Synthetic fieldwork", billable, narrative = "Synthetic timer segment" };

  private static async Task<Guid> CreatedAsync(HttpClient c, string csrf, object body)
  {
    using var response = await Post(c, csrf, EntriesPath, body);
    var text = await response.Content.ReadAsStringAsync();
    Assert.True(response.StatusCode == HttpStatusCode.OK, text);
    using var doc = JsonDocument.Parse(text);
    Assert.Equal(["value"], doc.RootElement.EnumerateObject().Select(x => x.Name));
    return doc.RootElement.GetProperty("value").GetGuid();
  }

  private static async Task<Guid> CreatedTaskAsync(HttpClient c, string csrf, object body)
  {
    using var response = await Post(c, csrf, TasksPath, body);
    var text = await response.Content.ReadAsStringAsync();
    Assert.True(response.StatusCode == HttpStatusCode.OK, text);
    using var doc = JsonDocument.Parse(text);
    Assert.Equal(["value"], doc.RootElement.EnumerateObject().Select(x => x.Name));
    return doc.RootElement.GetProperty("value").GetGuid();
  }

  private static async Task AssertOkAsync(HttpClient c, string csrf, string url)
  {
    using var response = await Post(c, csrf, url, new { });
    Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
  }

  private static async Task AssertFailureAsync(HttpClient c, string? csrf, object body, HttpStatusCode status, string code, string message,
    string url = EntriesPath)
  {
    using var response = await Post(c, csrf, url, body);
    Assert.Equal(status, response.StatusCode);
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    Assert.Equal(message, doc.RootElement.GetProperty("message").GetString());
  }

  private static async Task<int> WorkspaceStartMinuteAsync(HttpClient c, string list, Guid entryId)
  {
    using var doc = JsonDocument.Parse(await c.GetStringAsync(WorkspacePath));
    var row = Assert.Single(doc.RootElement.GetProperty(list).EnumerateArray(), x => x.GetProperty("id").GetGuid() == entryId);
    return row.GetProperty("startMinute").GetInt32();
  }

  private static async Task<Guid> CreateTaskAsync(AuditSphereDbContext db, ActorContext actor, Guid clientId,
    Guid engagementId, string title, Guid? assigneeUserId = null)
  {
    var created = await PracticeTimeService.CreateTaskAsync(db, actor,
      new CreateTaskRequest(title, clientId, engagementId, assigneeUserId));
    Assert.True(created.Succeeded, created.Message);
    return created.Value;
  }

  private static async Task SeedSiblingScopeAsync(OwnedPostgresDatabase pg, Guid firmId, Guid clientId, Guid engagementId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "SIBLING TIME CLIENT", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState { Id = clientId, FirmId = firmId });
    await db.SaveChangesAsync();
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser user) => new(new Dictionary<string, string?> {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
    ["DevelopmentIdentity:Subject"] = user.Subject, ["DevelopmentIdentity:TenantId"] = user.TenantId,
    ["ExternalEffects:Enabled"] = "false", ["Application:AllowSimulationAdapters"] = "true"
  });

  private static async Task<string> SignIn(HttpClient c)
  {
    using var signIn = await c.GetAsync("/auth/sign-in");
    using var session = await c.GetAsync("/api/ui/session");
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static Task<HttpResponseMessage> Post(HttpClient c, string? csrf, string url, object body)
  {
    var r = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
    if (csrf is not null) r.Headers.Add("X-XSRF-TOKEN", csrf);
    return c.SendAsync(r);
  }

  private sealed class RevokePracticeTimeReadAuthority(
    ITestPostgresDatabase pg, Guid firmId, Guid userId, bool revokeManagerGrant) : DbCommandInterceptor
  {
    private int interleaved;
    public int InterleavedUpdates => Volatile.Read(ref interleaved);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (!command.CommandText.Contains("time_entries", StringComparison.OrdinalIgnoreCase) ||
          Interlocked.CompareExchange(ref interleaved, 1, 0) != 0)
        return result;

      await using var change = new AuditSphereDbContext(pg.Options);
      var affected = revokeManagerGrant
        ? await change.RoleGrants.Where(x => x.FirmId == firmId && x.UserId == userId && x.Role == "Manager" &&
            x.ClientId == null && x.EngagementId == null && x.RevokedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), cancellationToken)
        : await change.Users.Where(x => x.FirmId == firmId && x.Id == userId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), cancellationToken);
      if (affected != 1) throw new InvalidOperationException("The practice-time read interleaving did not update exactly one authority row.");
      return result;
    }
  }
}
