using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class EngagementDeadlineReminderTests
{
  [Fact]
  public async Task TaskDatesIncludeOnlyYesterdayTodayAndSevenDaysForActiveTasks()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var ids = new Dictionary<string, Guid>();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      ids["yesterday"] = await CreateTaskAsync(db, actor, f.EngagementId, "Yesterday", today.AddDays(-1), 1);
      ids["today"] = await CreateTaskAsync(db, actor, f.EngagementId, "Today", today, 2);
      ids["seven"] = await CreateTaskAsync(db, actor, f.EngagementId, "Seven days", today.AddDays(7), 3);
      ids["eight"] = await CreateTaskAsync(db, actor, f.EngagementId, "Eight days", today.AddDays(8), 4);
      ids["null"] = await CreateTaskAsync(db, actor, f.EngagementId, "No due date", null, 5);
      ids["completed"] = await CreateTaskAsync(db, actor, f.EngagementId, "Completed", today, 6);
      ids["cancelled"] = await CreateTaskAsync(db, actor, f.EngagementId, "Cancelled", today, 7);
      await db.WorkTasks.Where(x => x.Id == ids["completed"])
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PracticeTimeStates.TaskCompleted));
      await db.WorkTasks.Where(x => x.Id == ids["cancelled"])
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PracticeTimeStates.TaskCancelled));
      await db.WorkTasks.Where(x => x.Id == ids["today"])
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PracticeTimeStates.TaskInProgress));
    }

    await using var queryDb = new AuditSphereDbContext(pg.Options);
    var result = await EngagementDeadlineReminderQuery.GetAsync(queryDb, actor, f.EngagementId);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(today, result.Value!.AsOfDateUtc);
    Assert.Equal(new[] { ids["yesterday"], ids["today"], ids["seven"] }, result.Value.Items.Select(x => x.SourceId));
    Assert.Equal(new[] { "OVERDUE", "DUE_TODAY", "UPCOMING" }, result.Value.Items.Select(x => x.Tone));
    Assert.Equal(new[] { "Task due date has passed.", "Task is due today.", "Task is due within 7 days." },
      result.Value.Items.Select(x => x.Message));
    Assert.All(result.Value.Items, x => Assert.Equal("/app/work", x.SourceHref));
  }

  [Fact]
  public async Task DirectPbcAndRequiredEvidenceHeadDeduplicateAndAcceptedOrClosedPbcDisappearsOnRefresh()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var admin = PbcSeed.Actor(f.Admin, "Administrator");
    var client = PbcSeed.Actor(f.Client, "ClientUser");
    var reviewer = PbcSeed.Actor(f.Reviewer, "Staff");
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, f, admin, client);
    Guid taskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      taskId = await CreateTaskAsync(db, admin, f.EngagementId, "Obtain bank confirmations", null, 1);
      var binding = await db.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == taskId);
      binding.PbcRequestId = requestId;
      binding.Revision++;
      await db.SaveChangesAsync();
      var evidence = await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, admin, f.EngagementId, taskId,
        new(WorkflowEvidenceKinds.AcceptedPbc, requestId, true, "Track the current client response.", binding.Revision, Guid.NewGuid()));
      Assert.True(evidence.Succeeded, evidence.Message);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var result = await EngagementDeadlineReminderQuery.GetAsync(db, admin, f.EngagementId);
      Assert.True(result.Succeeded, result.Message);
      var reminder = Assert.Single(result.Value!.Items);
      Assert.Equal(requestId, reminder.SourceId);
      Assert.Equal("CLIENT_WAIT", reminder.Kind);
      Assert.Equal("Obtain bank confirmations", reminder.Title);
      Assert.Null(reminder.DueDate);
      Assert.Equal("PENDING", reminder.Tone);
      Assert.Equal("A client document response is pending.", reminder.Message);
      Assert.Equal($"/app/engagements/{f.EngagementId}/pbc", reminder.SourceHref);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.PbcRequests.Where(x => x.Id == requestId).ExecuteUpdateAsync(s => s
        .SetProperty(x => x.State, PbcStates.UnderReview)
        .SetProperty(x => x.Revision, 4));
      var accepted = await PbcService.ChangeStateAsync(db, reviewer,
        new(requestId, PbcStates.Accepted, 4));
      Assert.True(accepted.Succeeded, accepted.Message);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var accepted = await EngagementDeadlineReminderQuery.GetAsync(db, admin, f.EngagementId);
      Assert.True(accepted.Succeeded, accepted.Message);
      Assert.Empty(accepted.Value!.Items);
      var closed = await PbcService.ChangeStateAsync(db, reviewer,
        new(requestId, PbcStates.Closed, 5));
      Assert.True(closed.Succeeded, closed.Message);
      var closedResult = await EngagementDeadlineReminderQuery.GetAsync(db, admin, f.EngagementId);
      Assert.True(closedResult.Succeeded, closedResult.Message);
      Assert.Empty(closedResult.Value!.Items);
    }
  }

  [Fact]
  public async Task PastMilestonesUseCautiousWordingAndNeverExposeArchiveDeadline()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var admin = PbcSeed.Actor(f.Admin, "Administrator");
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var periodEnd = today.AddDays(-180);
    var fieldwork = today.AddDays(-160);
    var draft = today.AddDays(-140);
    var final = today.AddDays(-120);
    var cutoff = today.AddDays(-50);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.Engagements.Where(x => x.Id == f.EngagementId).ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.PeriodEnd, periodEnd.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
      var saved = await StatutoryMilestoneService.SaveMilestonesAsync(db, admin,
        new(f.EngagementId, cutoff, fieldwork, draft, final, "Synthetic schedule for reminder coverage."));
      Assert.True(saved.Succeeded, saved.Message);
      Assert.Equal(final.AddDays(60), saved.Value!.ArchiveDeadlineDate);
    }

    await using var queryDb = new AuditSphereDbContext(pg.Options);
    var result = await EngagementDeadlineReminderQuery.GetAsync(queryDb, admin, f.EngagementId);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(new[] { "FIELDWORK_START", "DRAFT_REPORT", "FINAL_REPORT", "STATUTORY_CUTOFF" },
      result.Value!.Items.Select(x => x.Kind));
    Assert.DoesNotContain(result.Value.Items, x => x.Title.Contains("archive", StringComparison.OrdinalIgnoreCase));
    Assert.DoesNotContain(result.Value.Items, x => x.DueDate == final.AddDays(60));
    Assert.Equal("Recorded milestone date has passed; review its current completion status.", result.Value.Items[0].Message);
    Assert.All(result.Value.Items, x => Assert.Equal("OVERDUE", x.Tone));
    Assert.All(result.Value.Items, x => Assert.Equal($"/app/engagements/{f.EngagementId}/audit-plan", x.SourceHref));
  }

  [Fact]
  public async Task FiftyOneEligibleRemindersReturnFiftyAndMarkTruncated()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    await using (var db = new AuditSphereDbContext(pg.Options))
      for (var i = 1; i <= 51; i++)
        await CreateTaskAsync(db, actor, f.EngagementId, $"Reminder {i:D2}", today, i);

    await using var queryDb = new AuditSphereDbContext(pg.Options);
    var result = await EngagementDeadlineReminderQuery.GetAsync(queryDb, actor, f.EngagementId);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(50, result.Value!.Items.Count);
    Assert.True(result.Value.Truncated);
    Assert.Equal(50, result.Value.Items.Select(x => x.SourceId).Distinct().Count());
  }

  [Fact]
  public async Task ForeignClientClientUserAndRevokedGrantAreDenied()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var siblingEngagementId = Guid.NewGuid();
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    var client = PbcSeed.Actor(f.Client, "ClientUser");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var foreignEngagement = await EngagementDeadlineReminderQuery.GetAsync(db, staff, foreign.EngagementId);
      Assert.False(foreignEngagement.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, foreignEngagement.ErrorCode);
      var siblingEngagement = await EngagementDeadlineReminderQuery.GetAsync(db, staff, siblingEngagementId);
      Assert.False(siblingEngagement.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, siblingEngagement.ErrorCode);
      var clientResult = await EngagementDeadlineReminderQuery.GetAsync(db, client, f.EngagementId);
      Assert.False(clientResult.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, clientResult.ErrorCode);
      await db.RoleGrants.Where(x => x.FirmId == f.FirmId && x.UserId == f.Staff.Id && x.EngagementId == f.EngagementId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      var revoked = await EngagementDeadlineReminderQuery.GetAsync(db, staff, f.EngagementId);
      Assert.False(revoked.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, revoked.ErrorCode);
    }
  }

  [Fact]
  public async Task FrozenSuppressesRemindersButPastScheduledFreezeDoesNotAndReadsCreateNoWrites()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    var scheduledEngagementId = Guid.NewGuid();
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var now = DateTimeOffset.UtcNow.AddDays(-61);
    var signedReportIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
    Guid frozenTaskId;
    Guid scheduledTaskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Engagements.Add(new Engagement
      {
        Id = scheduledEngagementId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Staff", f.ClientId, scheduledEngagementId));
      db.AuditDeliverables.AddRange(signedReportIds.Select((id, index) => new AuditDeliverable
      {
        Id = id, FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = index == 0 ? f.EngagementId : scheduledEngagementId,
        Kind = DeliverableKinds.SummaryReviewMemorandum, Version = 1, TemplateVersion = "test",
        InputDigest = new string('a', 64), FileName = "report.pdf", ContentType = "application/pdf",
        InputSummaryJson = "{}", Content = [1], ContentSha256 = new string('b', 64),
        CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
      }));
      await db.SaveChangesAsync();
      frozenTaskId = await CreateTaskAsync(db, staff, f.EngagementId, "Frozen task", today, 1);
      scheduledTaskId = await CreateTaskAsync(db, staff, scheduledEngagementId, "Scheduled task", today, 1);
      var dueAt = now.AddDays(60);
      db.EngagementFileFreezes.AddRange(
        new EngagementFileFreeze
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
          ReportDeliverableId = signedReportIds[0], ReportSignedAt = now, DueAt = dueAt,
          State = FileFreezeStates.Frozen, FrozenAt = dueAt, UpdatedAt = dueAt
        },
        new EngagementFileFreeze
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = scheduledEngagementId,
          ReportDeliverableId = signedReportIds[1], ReportSignedAt = now, DueAt = dueAt,
          State = FileFreezeStates.Scheduled, FrozenAt = null, UpdatedAt = now
        });
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var before = (await db.WorkTasks.CountAsync(), await db.WorkflowTaskActivities.CountAsync(),
        await db.PbcReminderReceipts.CountAsync(), await db.PbcCommunications.CountAsync(),
        await db.DurableOperations.CountAsync(), await db.FrozenAccessAttempts.CountAsync(),
        string.Join(",", await db.Engagements.Where(x => x.Id == f.EngagementId || x.Id == scheduledEngagementId)
          .OrderBy(x => x.Id).Select(x => x.Generation).ToArrayAsync()), await TaskStatusSnapshotAsync(db));
      var frozen = await EngagementDeadlineReminderQuery.GetAsync(db, staff, f.EngagementId);
      Assert.True(frozen.Succeeded, frozen.Message);
      Assert.Empty(frozen.Value!.Items);
      Assert.False(frozen.Value.Truncated);
      var scheduled = await EngagementDeadlineReminderQuery.GetAsync(db, staff, scheduledEngagementId);
      Assert.True(scheduled.Succeeded, scheduled.Message);
      Assert.Equal(scheduledTaskId, Assert.Single(scheduled.Value!.Items).SourceId);
      Assert.NotEqual(frozenTaskId, scheduled.Value.Items[0].SourceId);
      var after = (await db.WorkTasks.CountAsync(), await db.WorkflowTaskActivities.CountAsync(),
        await db.PbcReminderReceipts.CountAsync(), await db.PbcCommunications.CountAsync(),
        await db.DurableOperations.CountAsync(), await db.FrozenAccessAttempts.CountAsync(),
        string.Join(",", await db.Engagements.Where(x => x.Id == f.EngagementId || x.Id == scheduledEngagementId)
          .OrderBy(x => x.Id).Select(x => x.Generation).ToArrayAsync()), await TaskStatusSnapshotAsync(db));
      Assert.Equal(before, after);
      Assert.Equal(0, after.Item6);
    }
  }

  private static async Task<Guid> CreateTaskAsync(AuditSphereDbContext db, ActorContext actor,
    Guid engagementId, string title, DateOnly? dueDate, int ordinal)
  {
    var created = await EngagementWorkflowTaskService.CreateAsync(db, actor, engagementId,
      new(title, WorkflowTaskPhases.AuditPlanning, ordinal, dueDate, IdempotencyKey: Guid.NewGuid()));
    Assert.True(created.Succeeded, created.Message);
    return created.Value;
  }

  private static async Task<string> TaskStatusSnapshotAsync(AuditSphereDbContext db)
  {
    var states = await db.WorkTasks.AsNoTracking().OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.Status }).ToListAsync();
    return string.Join("|", states.Select(x => $"{x.Id:N}:{x.Status}"));
  }
}
