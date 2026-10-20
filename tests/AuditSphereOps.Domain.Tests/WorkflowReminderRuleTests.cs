using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class WorkflowReminderRuleTests
{
  private static readonly DateTimeOffset ReminderNoonUtc =
    new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public void RecurringSchedulesRequireReviewedBoundsAndKeepLegacyDigestAndWindowIdentity()
  {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    foreach (var definition in new[]
      {
        Definition() with { MaxScheduledWindowCount = null },
        Definition() with { RepeatEveryDays = 0, MaxScheduledWindowCount = null }
      })
    {
      var legacy = new LegacyReminderRuleDefinition(definition.Name, definition.SourceKind,
        definition.RecipientKind, definition.OffsetDays, definition.RepeatEveryDays,
        definition.TimeZoneId, definition.QuietHoursStart, definition.QuietHoursEnd,
        definition.EffectiveFromUtc);
      Assert.Equal(JsonSerializer.Serialize(legacy, options), JsonSerializer.Serialize(definition, options));
      Assert.Equal(WorkflowReminderRuleService.Digest(definition),
        WorkflowReminderRuleService.Digest(WorkflowReminderRuleService.NormalizeAndValidate(
          definition, allowLegacyUnboundedRecurring: true).Value!));
    }

    Assert.False(WorkflowReminderRuleService.NormalizeAndValidate(
      Definition() with { MaxScheduledWindowCount = null }).Succeeded);
    Assert.True(WorkflowReminderRuleService.NormalizeAndValidate(
      Definition() with { RepeatEveryDays = 0, MaxScheduledWindowCount = null }).Succeeded);
    Assert.False(WorkflowReminderRuleService.NormalizeAndValidate(
      Definition() with { RepeatEveryDays = 0 }).Succeeded);

    var boundary = WorkflowReminderSchedule.Evaluate(DateOnly.MinValue, DateOnly.MinValue,
      offsetDays: -30, repeatEveryDays: 7, maxScheduledWindowCount: 4);
    Assert.Equal(WorkflowReminderScheduleDisposition.Exhausted, boundary.Disposition);
    Assert.Equal(5, boundary.WindowNumber);
    Assert.Equal("WINDOW:4", WorkflowReminderSchedule.CurrentOccurrenceKey(
      DateOnly.MinValue, DateOnly.MinValue, offsetDays: -30, repeatEveryDays: 7));
    Assert.Null(WorkflowReminderSchedule.ScheduledWindowNumber("WINDOW:00", 7));
    Assert.Null(WorkflowReminderSchedule.ScheduledWindowNumber("WINDOW:01", 7));
  }

  [Fact]
  public async Task RuleReviewAndPublicationRequireIndependentExactDigestAndSealVersions()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (author, reviewer) = await SeedRuleActorsAsync(pg);
    var definition = Definition();
    await using (var missingKeyDb = new AuditSphereDbContext(pg.Options))
    {
      var missingKey = await WorkflowReminderRuleService.SaveDraftAsync(missingKeyDb, author,
        new(null, definition));
      Assert.False(missingKey.Succeeded);
      Assert.Equal("workflow.reminder.invalid", missingKey.ErrorCode);
      Assert.Empty(await missingKeyDb.WorkflowReminderRules.ToListAsync());
    }
    var createKey = Guid.NewGuid();
    var createRequest = new WorkflowReminderRuleDraftRequest(null, definition, RequestKey: createKey);
    async Task<CommandResult<WorkflowReminderRuleRevisionView>> CreateWithFreshContextAsync()
    {
      await using var createDb = new AuditSphereDbContext(pg.Options);
      return await WorkflowReminderRuleService.SaveDraftAsync(createDb, author, createRequest);
    }
    var createAttempts = await Task.WhenAll(CreateWithFreshContextAsync(), CreateWithFreshContextAsync());
    Assert.All(createAttempts, attempt => Assert.True(attempt.Succeeded, attempt.Message));
    var savedDraft = createAttempts[0].Value!;
    var replayedDraft = createAttempts[1].Value!;
    Assert.Equal(savedDraft.RuleId, replayedDraft.RuleId);
    Assert.Equal(savedDraft.RevisionId, replayedDraft.RevisionId);
    Assert.Equal(savedDraft.Revision, replayedDraft.Revision);
    Guid ruleId;
    Guid firstRevisionId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      ruleId = savedDraft.RuleId;
      firstRevisionId = savedDraft.RevisionId;
      Assert.Equal(1, savedDraft.Version);
      Assert.Equal(WorkflowReminderRuleService.Digest(definition), savedDraft.ContentDigest);

      var conflictingDefinition = definition with { Name = definition.Name + " changed" };
      var conflict = await WorkflowReminderRuleService.SaveDraftAsync(db, author,
        new(null, conflictingDefinition, RequestKey: createKey));
      Assert.False(conflict.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, conflict.ErrorCode);
      Assert.Equal(1, await db.WorkflowReminderRules.CountAsync(x => x.FirmId == author.FirmId));
      Assert.Equal(1, await db.WorkflowReminderRuleRevisions.CountAsync(x => x.RuleId == ruleId));
      Assert.Equal(1, await db.WorkflowReminderRuleDecisions.CountAsync(x => x.RuleId == ruleId));
      var createdDecisionId = await db.WorkflowReminderRuleDecisions.Where(x => x.RuleId == ruleId)
        .Select(x => x.Id).SingleAsync();
      await Assert.ThrowsAsync<PostgresException>(async () =>
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE workflow_reminder_rule_decisions SET rationale = 'rewritten' WHERE firm_id = {author.FirmId} AND id = {createdDecisionId}"));
      await Assert.ThrowsAsync<PostgresException>(async () =>
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM workflow_reminder_rule_decisions WHERE firm_id = {author.FirmId} AND id = {createdDecisionId}"));
      Assert.Equal(1, await db.WorkflowReminderRuleDecisions.CountAsync(x => x.RuleId == ruleId));

      var selfReview = await WorkflowReminderRuleService.ReviewAsync(db, author, ruleId, firstRevisionId,
        new(savedDraft.Revision, savedDraft.ContentDigest, "Review my own reminder rule."));
      Assert.False(selfReview.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, selfReview.ErrorCode);

      var wrongDigest = await WorkflowReminderRuleService.ReviewAsync(db, reviewer, ruleId, firstRevisionId,
        new(savedDraft.Revision, TamperDigest(savedDraft.ContentDigest), "Review the exact reminder revision."));
      Assert.False(wrongDigest.Succeeded);
      Assert.Equal("workflow.reminder.invalid", wrongDigest.ErrorCode);
      Assert.Equal(WorkflowReminderRuleStatuses.Draft,
        (await db.WorkflowReminderRuleRevisions.SingleAsync(x => x.Id == firstRevisionId)).Status);
      Assert.Single(await db.WorkflowReminderRuleDecisions.Where(x => x.RuleId == ruleId).ToListAsync());

      var reviewed = await WorkflowReminderRuleService.ReviewAsync(db, reviewer, ruleId, firstRevisionId,
        new(savedDraft.Revision, savedDraft.ContentDigest, "Reviewed exact reminder revision."));
      Assert.True(reviewed.Succeeded, reviewed.Message);

      var reviewedView = await WorkflowReminderRuleService.GetAsync(db, author, ruleId, firstRevisionId);
      Assert.True(reviewedView.Succeeded, reviewedView.Message);
      var reviewedRevision = reviewedView.Value!;
      Assert.Equal(reviewer.UserId, reviewedRevision.ReviewedByUserId);
      Assert.Equal(savedDraft.ContentDigest, reviewedRevision.ContentDigest);
      await Assert.ThrowsAsync<PostgresException>(async () =>
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE workflow_reminder_rule_revisions SET quiet_hours_start = '20:00' WHERE firm_id = {author.FirmId} AND id = {firstRevisionId}"));
      var reviewedBoundMutation = await Assert.ThrowsAsync<PostgresException>(async () =>
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE workflow_reminder_rule_revisions SET status = 'PUBLISHED', published_by_user_id = {author.UserId}, published_session_epoch = {author.SessionEpoch}, published_at = {DateTimeOffset.UtcNow}, max_scheduled_window_count = 2 WHERE firm_id = {author.FirmId} AND id = {firstRevisionId}"));
      Assert.Contains("schedule bound is immutable", reviewedBoundMutation.MessageText, StringComparison.Ordinal);

      var wrongPublicationDigest = await WorkflowReminderRuleService.PublishAsync(db, author, ruleId,
        firstRevisionId, new(reviewedRevision.Revision, TamperDigest(reviewedRevision.ContentDigest), "Publish reviewed rule."));
      Assert.False(wrongPublicationDigest.Succeeded);
      Assert.Equal("workflow.reminder.invalid", wrongPublicationDigest.ErrorCode);
      Assert.Equal(WorkflowReminderRuleStatuses.Reviewed,
        (await db.WorkflowReminderRuleRevisions.SingleAsync(x => x.Id == firstRevisionId)).Status);

      var published = await WorkflowReminderRuleService.PublishAsync(db, author, ruleId, firstRevisionId,
        new(reviewedRevision.Revision, reviewedRevision.ContentDigest, "Publish reviewed rule."));
      Assert.True(published.Succeeded, published.Message);

      var publishedView = await WorkflowReminderRuleService.GetAsync(db, author, ruleId, firstRevisionId);
      Assert.True(publishedView.Succeeded, publishedView.Message);
      var publishedRevision = publishedView.Value!;
      Assert.Equal(WorkflowReminderRuleStatuses.Published, publishedRevision.Status);
      Assert.Equal(author.UserId, publishedRevision.PublishedByUserId);
      Assert.Equal(reviewer.UserId, publishedRevision.ReviewedByUserId);
      var publishedBoundMutation = await Assert.ThrowsAsync<PostgresException>(async () =>
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE workflow_reminder_rule_revisions SET status = 'SUPERSEDED', max_scheduled_window_count = 2 WHERE firm_id = {author.FirmId} AND id = {firstRevisionId}"));
      Assert.Contains("schedule bound is immutable", publishedBoundMutation.MessageText, StringComparison.Ordinal);

      var nextDefinition = definition with { QuietHoursStart = "20:00" };
      var nextDraft = await WorkflowReminderRuleService.SaveDraftAsync(db, author,
        new(ruleId, nextDefinition, publishedRevision.RuleRevision, RequestKey: Guid.NewGuid()));
      Assert.True(nextDraft.Succeeded, nextDraft.Message);
      Assert.Equal(2, nextDraft.Value!.Version);
      Assert.NotEqual(firstRevisionId, nextDraft.Value.RevisionId);
      Assert.Equal(WorkflowReminderRuleStatuses.Published,
        (await db.WorkflowReminderRuleRevisions.SingleAsync(x => x.Id == firstRevisionId)).Status);
      Assert.Equal(definition.QuietHoursStart,
        (await db.WorkflowReminderRuleRevisions.SingleAsync(x => x.Id == firstRevisionId)).QuietHoursStart);
    }
  }

  [Fact]
  public async Task SuspendAndResumeAreFencedAndAppendTheirExactPublishedRevision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (author, reviewer) = await SeedRuleActorsAsync(pg);
    var published = await CreatePublishedRuleAsync(pg, author, reviewer, Definition());

    await using var db = new AuditSphereDbContext(pg.Options);
    var suspendRequest = new WorkflowReminderRuleStateRequest(
      published.RuleRevision, published.RevisionId, published.ContentDigest,
      "Pause this operational reminder rule.");
    var suspended = await WorkflowReminderRuleService.SuspendAsync(db, author, published.RuleId, suspendRequest);
    Assert.True(suspended.Succeeded, suspended.Message);

    var suspendedView = await WorkflowReminderRuleService.GetAsync(db, author,
      published.RuleId, published.RevisionId);
    Assert.True(suspendedView.Succeeded, suspendedView.Message);
    var suspendedRevision = suspendedView.Value!;
    Assert.True(suspendedRevision.Suspended);
    Assert.True(suspendedRevision.CanResume);

    var replay = await WorkflowReminderRuleService.SuspendAsync(db, author, published.RuleId, suspendRequest);
    Assert.False(replay.Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, replay.ErrorCode);
    Assert.Single(await db.WorkflowReminderRuleDecisions.Where(x =>
      x.RuleId == published.RuleId && x.Kind == WorkflowReminderDecisionKinds.Suspended).ToListAsync());

    var resumeRequest = new WorkflowReminderRuleStateRequest(
      suspendedRevision.RuleRevision, published.RevisionId, published.ContentDigest,
      "Resume the same published reminder rule.");
    var resumed = await WorkflowReminderRuleService.ResumeAsync(db, author, published.RuleId, resumeRequest);
    Assert.True(resumed.Succeeded, resumed.Message);

    var resumedView = await WorkflowReminderRuleService.GetAsync(db, author,
      published.RuleId, published.RevisionId);
    Assert.True(resumedView.Succeeded, resumedView.Message);
    var resumedRevision = resumedView.Value!;
    Assert.False(resumedRevision.Suspended);
    Assert.Equal(published.RevisionId, resumedRevision.RevisionId);
    Assert.Equal(published.ContentDigest, resumedRevision.ContentDigest);
    Assert.Equal(1, await db.WorkflowReminderRuleRevisions.CountAsync(x =>
      x.RuleId == published.RuleId && x.Status == WorkflowReminderRuleStatuses.Published));
    Assert.Equal(5, await db.WorkflowReminderRuleDecisions.CountAsync(x => x.RuleId == published.RuleId));
    Assert.Equal(1, await db.WorkflowReminderRuleDecisions.CountAsync(x =>
      x.RuleId == published.RuleId && x.Kind == WorkflowReminderDecisionKinds.Suspended));
    Assert.Equal(1, await db.WorkflowReminderRuleDecisions.CountAsync(x =>
      x.RuleId == published.RuleId && x.Kind == WorkflowReminderDecisionKinds.Resumed));
  }

  [Fact]
  public async Task NoPublishedHumanRuleCreatesNoReminderIntentOrTask()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var context = await SeedReminderContextAsync(pg);
    var sourceTaskId = await CreateSourceTaskAsync(pg, context.Fixture, new DateOnly(2026, 10, 15));
    var harness = CreateHarness(pg, context.Fixture, ReminderNoonUtc, new WorkflowReminderOptions(Enabled: true));

    Assert.Equal(0, await harness.Discovery.EnqueuePendingAsync());
    Assert.Equal(0, await harness.Worker.DrainAsync());

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await verify.WorkflowReminderRules.AsNoTracking().ToListAsync());
    Assert.Empty(await verify.DurableOperations.AsNoTracking()
      .Where(x => x.OperationKind == WorkflowReminderTaskHandler.Kind).ToListAsync());
    Assert.Equal(1, await verify.WorkTasks.AsNoTracking().CountAsync(x => x.Id == sourceTaskId));
    Assert.Empty(await verify.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.ReminderRuleRevisionId != null).ToListAsync());
    Assert.Empty(await verify.Approvals.AsNoTracking().ToListAsync());
    Assert.Empty(await verify.MaterialityApprovals.AsNoTracking().ToListAsync());
  }

  [Theory]
  [InlineData(WorkflowReminderSourceKinds.TaskDue)]
  [InlineData(WorkflowReminderSourceKinds.PbcDue)]
  public async Task PublishedRuleQueuesOneLocalTaskAndRepeatedPollingDoesNotChangeSourceOrApprovalGates(string sourceKind)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var context = await SeedReminderContextAsync(pg);
    var dueDate = new DateOnly(2026, 10, 16);
    var published = await CreatePublishedRuleAsync(pg, context.Author, context.Reviewer,
      Definition() with
      {
        SourceKind = sourceKind,
        RecipientKind = sourceKind == WorkflowReminderSourceKinds.TaskDue
          ? WorkflowReminderRecipientKinds.ResponsibleStaff : WorkflowReminderRecipientKinds.EngagementReviewer
      });
    var sourceId = sourceKind == WorkflowReminderSourceKinds.TaskDue
      ? await CreateSourceTaskAsync(pg, context.Fixture, dueDate)
      : await CreatePbcDueSourceAsync(pg, context.Fixture, dueDate);
    var harness = CreateHarness(pg, context.Fixture, ReminderNoonUtc, new WorkflowReminderOptions(Enabled: true));
    var taskBefore = sourceKind == WorkflowReminderSourceKinds.TaskDue
      ? await CaptureSourceAsync(pg, context.Fixture, sourceId) : null;
    var pbcBefore = sourceKind == WorkflowReminderSourceKinds.PbcDue
      ? await CapturePbcSourceAsync(pg, context.Fixture, sourceId) : null;

    var concurrentPolls = await Task.WhenAll(
      harness.Discovery.EnqueuePendingAsync(), harness.Discovery.EnqueuePendingAsync());
    Assert.Equal(1, concurrentPolls.Sum());
    Assert.Equal(1, await harness.Worker.DrainAsync());
    Assert.Equal(0, await harness.Discovery.EnqueuePendingAsync());

    await using var verify = new AuditSphereDbContext(pg.Options);
    var operation = await verify.DurableOperations.AsNoTracking()
      .SingleAsync(x => x.OperationKind == WorkflowReminderTaskHandler.Kind);
    Assert.Equal(OperationState.COMPLETED, operation.Status);
    Assert.Equal(published.RevisionId, operation.TargetId);
    Assert.Equal(published.Revision, operation.ExpectedRevision);
    Assert.Equal(context.Fixture.ClientId, operation.ClientId);
    Assert.Equal(context.Fixture.EngagementId, operation.EngagementId);
    var intent = JsonSerializer.Deserialize<WorkflowReminderIntent>(operation.PayloadJson)
      ?? throw new InvalidOperationException("Reminder operation has no typed intent payload.");
    Assert.Equal(published.RevisionId, intent.RuleRevisionId);
    Assert.Equal(published.ContentDigest, intent.RuleDigest);
    Assert.Equal(sourceKind, intent.SourceKind);
    Assert.Equal(sourceId, intent.SourceId);
    Assert.Equal(1, intent.DueRevision);
    Assert.Equal(sourceKind == WorkflowReminderSourceKinds.TaskDue
      ? context.Fixture.Staff.Id : context.Fixture.Reviewer.Id, intent.RecipientUserId);
    Assert.Equal(sourceKind == WorkflowReminderSourceKinds.TaskDue ? 1 : 2, intent.SourceRevision);
    Assert.Equal(context.Author.UserId, intent.PublisherUserId);
    Assert.Equal(dueDate, intent.SourceDueDate);

    var reminderBinding = await verify.WorkflowTaskBindings.AsNoTracking()
      .SingleAsync(x => x.ReminderRuleRevisionId == published.RevisionId);
    Assert.Equal(operation.Id, reminderBinding.ReminderOperationId);
    Assert.StartsWith(WorkflowReminderTaskHandler.SourcePrefix, reminderBinding.SourceKey, StringComparison.Ordinal);
    Assert.Equal(sourceKind == WorkflowReminderSourceKinds.TaskDue
      ? context.Fixture.Staff.Id : context.Fixture.Reviewer.Id, reminderBinding.AssigneeUserIdSnapshot);
    Assert.Equal(sourceKind == WorkflowReminderSourceKinds.TaskDue ? WorkflowTaskPhases.AuditPlanning : WorkflowTaskPhases.AuditFieldwork,
      reminderBinding.Phase);
    Assert.Equal(sourceKind == WorkflowReminderSourceKinds.TaskDue ? 2 : 1,
      await verify.WorkTasks.AsNoTracking().CountAsync(x => x.EngagementId == context.Fixture.EngagementId));
    if (taskBefore is not null) Assert.Equal(taskBefore, await CaptureSourceAsync(pg, context.Fixture, sourceId));
    if (pbcBefore is not null) Assert.Equal(pbcBefore, await CapturePbcSourceAsync(pg, context.Fixture, sourceId));
    Assert.Empty(await verify.Approvals.AsNoTracking().ToListAsync());
    Assert.Empty(await verify.MaterialityApprovals.AsNoTracking().ToListAsync());
  }

  [Fact]
  public async Task DiscoveryCapsScheduleWindowsPerDueRevisionAndResetsForANewDueRevision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var context = await SeedReminderContextAsync(pg);
    var published = await CreatePublishedRuleAsync(pg, context.Author, context.Reviewer,
      Definition() with { OffsetDays = 0, RepeatEveryDays = 1, MaxScheduledWindowCount = 2 });
    var sourceTaskId = await CreateSourceTaskAsync(pg, context.Fixture, new DateOnly(2026, 10, 17));
    var harness = CreateHarness(pg, context.Fixture,
      new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero), new WorkflowReminderOptions(Enabled: true));

    Assert.Equal(1, await harness.Discovery.EnqueuePendingAsync());
    Assert.Equal(1, await harness.Worker.DrainAsync());
    Assert.Equal(0, await harness.Discovery.EnqueuePendingAsync());
    harness.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    Assert.Equal(1, await harness.Discovery.EnqueuePendingAsync());
    Assert.Equal(1, await harness.Worker.DrainAsync());
    harness.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    Assert.Equal(0, await harness.Discovery.EnqueuePendingAsync());

    long previousDueRevision;
    await using (var exhausted = new AuditSphereDbContext(pg.Options))
    {
      var head = await exhausted.WorkflowReminderRules.AsNoTracking().SingleAsync(x => x.Id == published.RuleId);
      Assert.Equal("SCHEDULE_WINDOWS_EXHAUSTED", head.LastEvaluationCode);
      var operations = await exhausted.DurableOperations.AsNoTracking()
        .Where(x => x.OperationKind == WorkflowReminderTaskHandler.Kind).ToListAsync();
      Assert.Equal(2, operations.Count);
      var intents = operations.Select(x => WorkflowReminderTaskHandler.Parse(x.PayloadJson))
        .OrderBy(x => x.OccurrenceKey, StringComparer.Ordinal).ToArray();
      Assert.Equal(new[] { "WINDOW:0", "WINDOW:1" }, intents.Select(x => x.OccurrenceKey).ToArray());
      previousDueRevision = intents.Max(x => x.DueRevision);
      Assert.False(await exhausted.WorkflowTaskActivities.AsNoTracking().AnyAsync(x =>
        x.WorkTaskId == sourceTaskId && x.Kind == WorkflowTaskActivityKinds.DueDateChanged));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var binding = await db.WorkflowTaskBindings.AsNoTracking().SingleAsync(x => x.WorkTaskId == sourceTaskId);
      var changed = await EngagementWorkflowTaskService.ChangeDueDateAsync(db, context.Author, sourceTaskId,
        new DateOnly(2026, 10, 19), binding.Revision);
      Assert.True(changed.Succeeded, changed.Message);
      Assert.Equal(1, await db.WorkflowTaskActivities.AsNoTracking().CountAsync(x =>
        x.WorkTaskId == sourceTaskId && x.Kind == WorkflowTaskActivityKinds.DueDateChanged));
    }
    Assert.Equal(1, await harness.Discovery.EnqueuePendingAsync());
    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var operations = await verify.DurableOperations.AsNoTracking()
        .Where(x => x.OperationKind == WorkflowReminderTaskHandler.Kind).ToListAsync();
      Assert.Equal(3, operations.Count);
      var nextDueRevision = Assert.Single(operations.Select(x => WorkflowReminderTaskHandler.Parse(x.PayloadJson)),
        x => x.DueRevision > previousDueRevision);
      Assert.Equal("WINDOW:0", nextDueRevision.OccurrenceKey);
    }
  }

  [Theory]
  [InlineData("WINDOW:2", "workflow-reminder-schedule-windows-exhausted")]
  [InlineData("WINDOW:00", "provider-or-integrity-blocked")]
  public async Task ExecutionBlocksInvalidScheduledWindowsAndProjectsTheirErrors(string occurrenceKey, string expectedError)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var context = await SeedReminderContextAsync(pg);
    var published = await CreatePublishedRuleAsync(pg, context.Author, context.Reviewer,
      Definition() with { OffsetDays = 0, RepeatEveryDays = 1, MaxScheduledWindowCount = 2 });
    var sourceTaskId = await CreateSourceTaskAsync(pg, context.Fixture, new DateOnly(2026, 10, 17));
    var harness = CreateHarness(pg, context.Fixture, ReminderNoonUtc, new WorkflowReminderOptions(Enabled: true));

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await using var tx = await db.Database.BeginTransactionAsync();
      var source = await WorkflowReminderRuntime.SourceAsync(db, context.Fixture.FirmId,
        context.Fixture.ClientId, context.Fixture.EngagementId, WorkflowReminderSourceKinds.TaskDue,
        sourceTaskId, locked: false, CancellationToken.None);
      Assert.NotNull(source);
      var intent = new WorkflowReminderIntent(published.RevisionId, published.ContentDigest, source!.Kind,
        source.Id, source.Revision, source.DueRevision, source.Digest, source.DueDate,
        context.Fixture.Staff.Id, context.Author.UserId, context.Author.SessionEpoch, occurrenceKey);
      var key = "workflow-reminder:" + Hashing.Sha256Hex(JsonSerializer.Serialize(intent));
      var queued = await harness.Store.EnqueueAsync(db, new OperationRequest(context.Fixture.FirmId,
        context.Fixture.ClientId, context.Fixture.EngagementId, WorkflowReminderTaskHandler.Kind,
        published.RevisionId, published.Revision, key, JsonSerializer.Serialize(intent), context.Author.UserId),
        harness.Handler, CancellationToken.None);
      Assert.True(queued.Succeeded, queued.Message);
      await tx.CommitAsync();
    }

    Assert.Equal(1, await harness.Worker.DrainAsync());
    await using var verify = new AuditSphereDbContext(pg.Options);
    var operation = await verify.DurableOperations.AsNoTracking()
      .SingleAsync(x => x.OperationKind == WorkflowReminderTaskHandler.Kind);
    Assert.Equal(OperationState.PROVIDER_BLOCKED, operation.Status);
    Assert.Equal("provider-or-integrity-blocked", operation.ErrorCode);
    var executions = await WorkflowReminderRuleService.ExecutionsAsync(verify, context.Author,
      published.RuleId);
    Assert.True(executions.Succeeded, executions.Message);
    Assert.Equal(expectedError, Assert.Single(executions.Value!.Items).ErrorCode);
    Assert.False(await verify.WorkflowTaskBindings.AsNoTracking().AnyAsync(x => x.ReminderOperationId == operation.Id));
  }

  [Fact]
  public async Task LegacyPublishedRepeatingRuleKeepsItsDigestAndIsVisibleAsNeedsRevision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var context = await SeedReminderContextAsync(pg);
    var definition = Definition() with { MaxScheduledWindowCount = null };
    var now = ReminderNoonUtc;
    var ruleId = Guid.CreateVersion7();
    var revisionId = Guid.CreateVersion7();
    var legacyDraftRuleId = Guid.CreateVersion7();
    var legacyDraftRevisionId = Guid.CreateVersion7();
    var legacyDraftRequestKey = Guid.CreateVersion7();
    var legacyCreationDigest = LegacyCreationRequestDigest(null, context.Author.UserId, definition);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var rule = new WorkflowReminderRule
      {
        Id = ruleId, FirmId = context.Fixture.FirmId, CreatedByUserId = context.Author.UserId,
        CreatedAt = now.AddMinutes(-2), Revision = 1
      };
      var revision = new WorkflowReminderRuleRevision
      {
        Id = revisionId, FirmId = context.Fixture.FirmId, RuleId = ruleId, Version = 1,
        CreationRequestKey = Guid.CreateVersion7(), CreationRequestDigest = legacyCreationDigest,
        Name = definition.Name, SourceKind = definition.SourceKind, RecipientKind = definition.RecipientKind,
        OffsetDays = definition.OffsetDays, RepeatEveryDays = definition.RepeatEveryDays,
        MaxScheduledWindowCount = null, TimeZoneId = definition.TimeZoneId,
        QuietHoursStart = definition.QuietHoursStart, QuietHoursEnd = definition.QuietHoursEnd,
        EffectiveFromUtc = definition.EffectiveFromUtc, Status = WorkflowReminderRuleStatuses.Published,
        ContentDigest = WorkflowReminderRuleService.Digest(definition), CreatedByUserId = context.Author.UserId,
        LastEditedByUserId = context.Author.UserId, ReviewedByUserId = context.Reviewer.UserId,
        PublishedByUserId = context.Author.UserId, PublishedSessionEpoch = context.Author.SessionEpoch,
        CreatedAt = now.AddMinutes(-2), UpdatedAt = now, ReviewedAt = now.AddMinutes(-1), PublishedAt = now,
        Revision = 3
      };
      db.WorkflowReminderRules.Add(rule);
      db.WorkflowReminderRuleRevisions.Add(revision);
      db.WorkflowReminderRuleDecisions.Add(new WorkflowReminderRuleDecision
      {
        Id = Guid.CreateVersion7(), FirmId = context.Fixture.FirmId, RuleId = ruleId, RuleRevisionId = revisionId,
        Kind = WorkflowReminderDecisionKinds.Reviewed, ContentDigest = revision.ContentDigest,
        Rationale = "Independent historical review retained.", ActorUserId = context.Reviewer.UserId,
        OccurredAt = now.AddMinutes(-1)
      });
      db.WorkflowReminderRuleDecisions.Add(new WorkflowReminderRuleDecision
      {
        Id = Guid.CreateVersion7(), FirmId = context.Fixture.FirmId, RuleId = ruleId, RuleRevisionId = revisionId,
        Kind = WorkflowReminderDecisionKinds.Published, ContentDigest = revision.ContentDigest,
        Rationale = "Historical publication retained.", ActorUserId = context.Author.UserId, OccurredAt = now
      });
      await db.SaveChangesAsync();
      rule.CurrentPublishedRevisionId = revisionId;
      await db.SaveChangesAsync();

      var legacyDraftRule = new WorkflowReminderRule
      {
        Id = legacyDraftRuleId, FirmId = context.Fixture.FirmId, CreatedByUserId = context.Author.UserId,
        CreatedAt = now.AddMinutes(-3), Revision = 1
      };
      var legacyDraftRevision = new WorkflowReminderRuleRevision
      {
        Id = legacyDraftRevisionId, FirmId = context.Fixture.FirmId, RuleId = legacyDraftRuleId, Version = 1,
        CreationRequestKey = legacyDraftRequestKey, CreationRequestDigest = legacyCreationDigest,
        Name = definition.Name, SourceKind = definition.SourceKind, RecipientKind = definition.RecipientKind,
        OffsetDays = definition.OffsetDays, RepeatEveryDays = definition.RepeatEveryDays,
        MaxScheduledWindowCount = null, TimeZoneId = definition.TimeZoneId,
        QuietHoursStart = definition.QuietHoursStart, QuietHoursEnd = definition.QuietHoursEnd,
        EffectiveFromUtc = definition.EffectiveFromUtc, Status = WorkflowReminderRuleStatuses.Draft,
        ContentDigest = WorkflowReminderRuleService.Digest(definition), CreatedByUserId = context.Author.UserId,
        LastEditedByUserId = context.Author.UserId, CreatedAt = now.AddMinutes(-3), UpdatedAt = now.AddMinutes(-3),
        Revision = 1
      };
      db.WorkflowReminderRules.Add(legacyDraftRule);
      db.WorkflowReminderRuleRevisions.Add(legacyDraftRevision);
      db.WorkflowReminderRuleDecisions.Add(new WorkflowReminderRuleDecision
      {
        Id = Guid.CreateVersion7(), FirmId = context.Fixture.FirmId, RuleId = legacyDraftRuleId,
        RuleRevisionId = legacyDraftRevisionId, Kind = WorkflowReminderDecisionKinds.DraftCreated,
        ContentDigest = legacyDraftRevision.ContentDigest, Rationale = "Historical draft receipt retained.",
        ActorUserId = context.Author.UserId, OccurredAt = now.AddMinutes(-3)
      });
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var replay = await WorkflowReminderRuleService.SaveDraftAsync(db, context.Author,
        new(null, definition, RequestKey: legacyDraftRequestKey));
      Assert.True(replay.Succeeded, replay.Message);
      Assert.Equal(legacyDraftRevisionId, replay.Value!.RevisionId);
      Assert.Equal(definition, replay.Value.Definition);

      var changedIntent = await WorkflowReminderRuleService.SaveDraftAsync(db, context.Author,
        new(null, definition with { Name = "Changed legacy reminder intent" }, RequestKey: legacyDraftRequestKey));
      Assert.False(changedIntent.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, changedIntent.ErrorCode);

      var receipt = await WorkflowReminderRuleService.ReceiptAsync(db, context.Author, legacyDraftRequestKey);
      Assert.True(receipt.Succeeded, receipt.Message);
      Assert.Equal(legacyDraftRevisionId, receipt.Value!.RevisionId);

      var existingRevisionCount = await db.WorkflowReminderRuleRevisions.CountAsync(x => x.FirmId == context.Fixture.FirmId);
      var unboundedCreate = await WorkflowReminderRuleService.SaveDraftAsync(db, context.Author,
        new(null, definition, RequestKey: Guid.CreateVersion7()));
      Assert.False(unboundedCreate.Succeeded);
      Assert.Equal("workflow.reminder.invalid", unboundedCreate.ErrorCode);
      Assert.Contains("explicit maximum", unboundedCreate.Message, StringComparison.Ordinal);
      Assert.Equal(existingRevisionCount,
        await db.WorkflowReminderRuleRevisions.CountAsync(x => x.FirmId == context.Fixture.FirmId));

      var view = await WorkflowReminderRuleService.GetAsync(db, context.Author, ruleId, revisionId);
      Assert.True(view.Succeeded, view.Message);
      Assert.Contains("needs a new independently reviewed revision", view.Value!.Blocker, StringComparison.Ordinal);
      Assert.True(view.Value.CanSuspend);
      Assert.Equal(definition, view.Value.Definition);
    }
    var harness = CreateHarness(pg, context.Fixture, ReminderNoonUtc, new WorkflowReminderOptions(Enabled: true));
    Assert.Equal(0, await harness.Discovery.EnqueuePendingAsync());
    await using var check = new AuditSphereDbContext(pg.Options);
    Assert.Equal("NEEDS_REVISION", await check.WorkflowReminderRules.AsNoTracking()
      .Where(x => x.Id == ruleId).Select(x => x.LastEvaluationCode).SingleAsync());
    var catalog = await WorkflowReminderRuleService.ListAsync(check, context.Author,
      new WorkflowReminderOptions(Enabled: true));
    Assert.True(catalog.Succeeded, catalog.Message);
    Assert.Equal("NEEDS_REVISION", Assert.Single(catalog.Value!.Items, x => x.RuleId == ruleId).LastEvaluationCode);
    Assert.Empty(await check.DurableOperations.AsNoTracking()
      .Where(x => x.OperationKind == WorkflowReminderTaskHandler.Kind).ToListAsync());
  }

  [Fact]
  public async Task DueDateRevisionCreatesANewOccurrenceButSourceRevisionAloneDoesNotDuplicateIt()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var context = await SeedReminderContextAsync(pg);
    var definition = Definition() with { OffsetDays = 0, RepeatEveryDays = 0, MaxScheduledWindowCount = null };
    var published = await CreatePublishedRuleAsync(pg, context.Author, context.Reviewer, definition);
    var sourceTaskId = await CreateSourceTaskAsync(pg, context.Fixture, new DateOnly(2026, 10, 18));
    var harness = CreateHarness(pg, context.Fixture, ReminderNoonUtc, new WorkflowReminderOptions(Enabled: true));

    Assert.Equal(1, await harness.Worker.DrainAsync());
    var first = await ReadReminderEvidenceAsync(pg, published.RevisionId);
    Assert.Equal(1, first.Intent.DueRevision);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var binding = await db.WorkflowTaskBindings.AsNoTracking().SingleAsync(x => x.WorkTaskId == sourceTaskId);
      var cancelled = await EngagementWorkflowTaskService.CancelAsync(db, context.Author, sourceTaskId,
        "Temporary source cancellation for version-only identity check.", binding.Revision);
      Assert.True(cancelled.Succeeded, cancelled.Message);
      var cancelledRevision = await db.WorkflowTaskBindings.AsNoTracking()
        .Where(x => x.WorkTaskId == sourceTaskId).Select(x => x.Revision).SingleAsync();
      var reopened = await EngagementWorkflowTaskService.ReopenAsync(db, context.Author, sourceTaskId,
        "Restore the same due occurrence for identity check.", cancelledRevision);
      Assert.True(reopened.Succeeded, reopened.Message);
    }

    Assert.Equal(0, await harness.Discovery.EnqueuePendingAsync());
    Assert.Equal(0, await harness.Discovery.EnqueuePendingAsync());
    await using (var afterVersionOnly = new AuditSphereDbContext(pg.Options))
    {
      Assert.Single(await afterVersionOnly.DurableOperations.AsNoTracking()
        .Where(x => x.OperationKind == WorkflowReminderTaskHandler.Kind).ToListAsync());
      Assert.Equal(2, await afterVersionOnly.WorkTasks.AsNoTracking()
        .CountAsync(x => x.EngagementId == context.Fixture.EngagementId));
      Assert.Equal(first.Binding.SourceKey,
        (await afterVersionOnly.WorkflowTaskBindings.AsNoTracking()
          .SingleAsync(x => x.ReminderOperationId == first.OperationId)).SourceKey);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var binding = await db.WorkflowTaskBindings.AsNoTracking().SingleAsync(x => x.WorkTaskId == sourceTaskId);
      var changed = await EngagementWorkflowTaskService.ChangeDueDateAsync(db, context.Author, sourceTaskId,
        new DateOnly(2026, 10, 19), binding.Revision);
      Assert.True(changed.Succeeded, changed.Message);
    }

    Assert.Equal(1, await harness.Worker.DrainAsync());
    var allEvidence = await ReadAllReminderEvidenceAsync(pg);
    Assert.Equal(2, allEvidence.Count);
    var second = Assert.Single(allEvidence, x => x.OperationId != first.OperationId);
    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var dueChange = await verify.WorkflowTaskActivities.AsNoTracking().SingleAsync(x =>
        x.WorkTaskId == sourceTaskId && x.Kind == WorkflowTaskActivityKinds.DueDateChanged);
      Assert.Equal(dueChange.TaskRevision, second.Intent.DueRevision);
      Assert.True(second.Intent.DueRevision > first.Intent.DueRevision);
      Assert.Equal(new DateOnly(2026, 10, 19), second.Intent.SourceDueDate);
      Assert.NotEqual(first.Binding.SourceKey, second.Binding.SourceKey);
      Assert.Equal(3, await verify.WorkTasks.AsNoTracking().CountAsync(x => x.EngagementId == context.Fixture.EngagementId));
      Assert.Equal(2, await verify.WorkflowTaskBindings.AsNoTracking()
        .CountAsync(x => x.ReminderRuleRevisionId == published.RevisionId));
      Assert.Empty(await verify.Approvals.AsNoTracking().ToListAsync());
      Assert.Empty(await verify.MaterialityApprovals.AsNoTracking().ToListAsync());
    }
    Assert.Equal(0, await harness.Discovery.EnqueuePendingAsync());
  }

  [Theory]
  [InlineData("quiet", OperationState.RETRY_WAIT)]
  [InlineData("suspended", OperationState.AUTHORIZATION_BLOCKED)]
  [InlineData("suspend-resume", OperationState.AUTHORIZATION_BLOCKED)]
  [InlineData("recipient-revoked", OperationState.AUTHORIZATION_BLOCKED)]
  [InlineData("closed", OperationState.PROVIDER_BLOCKED)]
  [InlineData("frozen", OperationState.PROVIDER_BLOCKED)]
  public async Task QueuedReminderIsSuppressedWhenItsOperationalGateChanges(string change, OperationState expectedState)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var context = await SeedReminderContextAsync(pg);
    var published = await CreatePublishedRuleAsync(pg, context.Author, context.Reviewer,
      Definition() with { OffsetDays = 0, RepeatEveryDays = 0, MaxScheduledWindowCount = null });
    var sourceTaskId = await CreateSourceTaskAsync(pg, context.Fixture, new DateOnly(2026, 10, 18));
    var harness = CreateHarness(pg, context.Fixture, ReminderNoonUtc, new WorkflowReminderOptions(Enabled: true));
    var sourceBefore = await CaptureSourceAsync(pg, context.Fixture, sourceTaskId);
    Assert.Equal(1, await harness.Discovery.EnqueuePendingAsync());

    switch (change)
    {
      case "quiet":
        harness.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 19, 22, 0, 0, TimeSpan.Zero));
        break;
      case "suspended":
        await using (var db = new AuditSphereDbContext(pg.Options))
        {
          var suspended = await WorkflowReminderRuleService.SuspendAsync(db, context.Author, published.RuleId,
            new(published.RuleRevision, published.RevisionId, published.ContentDigest,
              "Pause the exact rule before task publication."));
          Assert.True(suspended.Succeeded, suspended.Message);
        }
        break;
      case "suspend-resume":
        await using (var suspendDb = new AuditSphereDbContext(pg.Options))
        {
          var suspended = await WorkflowReminderRuleService.SuspendAsync(suspendDb, context.Author, published.RuleId,
            new(published.RuleRevision, published.RevisionId, published.ContentDigest,
              "Pause the exact rule before task publication."));
          Assert.True(suspended.Succeeded, suspended.Message);
        }
        await using (var resumeDb = new AuditSphereDbContext(pg.Options))
        {
          var suspendedView = await WorkflowReminderRuleService.GetAsync(resumeDb, context.Author,
            published.RuleId, published.RevisionId);
          Assert.True(suspendedView.Succeeded, suspendedView.Message);
          var resumed = await WorkflowReminderRuleService.ResumeAsync(resumeDb, context.Author, published.RuleId,
            new(suspendedView.Value!.RuleRevision, published.RevisionId, published.ContentDigest,
              "Resume the exact rule without rearming queued reminders."));
          Assert.True(resumed.Succeeded, resumed.Message);
        }
        break;
      case "recipient-revoked":
        await using (var db = new AuditSphereDbContext(pg.Options))
        {
          var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == context.Fixture.FirmId &&
            x.UserId == context.Fixture.Staff.Id && x.Role == "Staff");
          grant.RevokedAt = ReminderNoonUtc.AddMinutes(1);
          await db.SaveChangesAsync();
        }
        break;
      case "closed":
        await using (var db = new AuditSphereDbContext(pg.Options))
        {
          (await db.Engagements.SingleAsync(x => x.Id == context.Fixture.EngagementId)).Status = "Closed";
          await db.SaveChangesAsync();
        }
        break;
      case "frozen":
        await AddFrozenReportAsync(pg, context.Fixture, ReminderNoonUtc);
        break;
      default:
        throw new InvalidOperationException($"Unsupported gate test case: {change}");
    }

    Assert.Equal(1, await harness.Worker.DrainAsync());
    await using var verify = new AuditSphereDbContext(pg.Options);
    var operation = await verify.DurableOperations.AsNoTracking()
      .SingleAsync(x => x.OperationKind == WorkflowReminderTaskHandler.Kind);
    Assert.Equal(expectedState, operation.Status);
    if (change == "quiet") Assert.Equal(0, operation.AttemptCount);
    if (change == "suspend-resume")
      Assert.Empty(await verify.OperationEvents.AsNoTracking().Where(x =>
        x.OperationId == operation.Id && x.Kind == "operation.recovery-retry.v1").ToListAsync());
    if (change == "closed")
      Assert.Equal("Closed", await verify.Engagements.AsNoTracking()
        .Where(x => x.Id == context.Fixture.EngagementId).Select(x => x.Status).SingleAsync());
    if (change == "frozen")
      Assert.Equal(FileFreezeStates.Frozen, await verify.EngagementFileFreezes.AsNoTracking()
        .Where(x => x.FirmId == context.Fixture.FirmId && x.EngagementId == context.Fixture.EngagementId)
        .Select(x => x.State).SingleAsync());
    Assert.Equal(sourceBefore, await CaptureSourceAsync(pg, context.Fixture, sourceTaskId));
    Assert.Equal(1, await verify.WorkTasks.AsNoTracking().CountAsync(x => x.EngagementId == context.Fixture.EngagementId));
    Assert.Empty(await verify.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.ReminderRuleRevisionId != null).ToListAsync());
    Assert.Empty(await verify.Approvals.AsNoTracking().ToListAsync());
    Assert.Empty(await verify.MaterialityApprovals.AsNoTracking().ToListAsync());
  }

  private static async Task<(ActorContext Author, ActorContext Reviewer)> SeedRuleActorsAsync(ITestPostgresDatabase pg)
  {
    var context = await SeedReminderContextAsync(pg);
    return (context.Author, context.Reviewer);
  }

  private static async Task<(PbcSeed.Fixture Fixture, ActorContext Author, ActorContext Reviewer)> SeedReminderContextAsync(
    ITestPostgresDatabase pg)
  {
    var fixture = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, fixture.Reviewer, "Partner"));
    await db.SaveChangesAsync();
    return (fixture, PbcSeed.Actor(fixture.Admin, "Administrator"), PbcSeed.Actor(fixture.Reviewer, "Partner"));
  }

  private static async Task<Guid> CreateSourceTaskAsync(
    ITestPostgresDatabase pg, PbcSeed.Fixture fixture, DateOnly dueDate)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var result = await EngagementWorkflowTaskService.CreateAsync(db,
      PbcSeed.Actor(fixture.Admin, "Administrator"), fixture.EngagementId,
      new WorkflowTaskCreateRequest("Prepare engagement planning evidence", WorkflowTaskPhases.AuditPlanning, 1,
        DueDate: dueDate, AssigneeUserId: fixture.Staff.Id, IdempotencyKey: Guid.NewGuid()));
    Assert.True(result.Succeeded, result.Message);
    return result.Value;
  }

  private static async Task<Guid> CreatePbcDueSourceAsync(
    ITestPostgresDatabase pg, PbcSeed.Fixture fixture, DateOnly dueDate)
  {
    var actor = PbcSeed.Actor(fixture.Staff, "Staff");
    Guid requestId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PbcService.CreateRequestAsync(db, actor, new CreatePbcRequestRequest(
        fixture.EngagementId, "Bank statements", "TEST ENTITY", "2026-01-01", "2026-12-31",
        "Cash", "PDF or CSV", "12 months", fixture.Client.Id, fixture.Staff.Id, fixture.Reviewer.Id,
        dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "Confidential",
        "Provide the complete period with readable account identity."));
      Assert.True(created.Succeeded, created.Message);
      requestId = created.Value;
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var sent = await PbcService.ChangeStateAsync(db, actor,
        new PbcStateChangeRequest(requestId, PbcStates.Sent, 1));
      Assert.True(sent.Succeeded, sent.Message);
    }
    return requestId;
  }

  private static ReminderHarness CreateHarness(
    ITestPostgresDatabase pg, PbcSeed.Fixture fixture, DateTimeOffset now, WorkflowReminderOptions reminderOptions)
  {
    var clock = new MutableClock(now);
    var workerOptions = new WorkerOptions(fixture.FirmId, "Test", MaxAttempts: 1);
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var handler = new WorkflowReminderTaskHandler(reminderOptions, clock);
    var store = new PostgresOperationStore(factory);
    var registry = new DurableOperationRegistry([handler], workerOptions);
    var dispatcher = new OperationDispatcher(store, registry, workerOptions);
    var discovery = new WorkflowReminderDiscovery(factory, store, handler, workerOptions, reminderOptions, clock);
    var worker = new AuditSphereOps.Worker.Worker(dispatcher, [discovery], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    return new(discovery, worker, clock, store, handler);
  }

  private static async Task<SourceSnapshot> CaptureSourceAsync(
    ITestPostgresDatabase pg, PbcSeed.Fixture fixture, Guid taskId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var task = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.FirmId == fixture.FirmId && x.Id == taskId);
    var binding = await db.WorkflowTaskBindings.AsNoTracking()
      .SingleAsync(x => x.FirmId == fixture.FirmId && x.WorkTaskId == taskId);
    return new(task.Title, task.Status, task.DueDate, task.AssigneeUserId, binding.Id, binding.Revision,
      binding.SourceKey, binding.ReminderRuleRevisionId, binding.ReminderOperationId,
      await db.WorkflowTaskActivities.AsNoTracking().CountAsync(x => x.FirmId == fixture.FirmId && x.WorkTaskId == taskId),
      await db.Approvals.AsNoTracking().CountAsync(), await db.MaterialityApprovals.AsNoTracking().CountAsync());
  }

  private static async Task<PbcSourceSnapshot> CapturePbcSourceAsync(
    ITestPostgresDatabase pg, PbcSeed.Fixture fixture, Guid requestId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var request = await db.PbcRequests.AsNoTracking().SingleAsync(x =>
      x.FirmId == fixture.FirmId && x.ClientId == fixture.ClientId &&
      x.EngagementId == fixture.EngagementId && x.Id == requestId);
    return new(request.State, request.Revision, request.DueDate, request.FirmOwnerUserId,
      request.ReviewerUserId, request.Area,
      await db.Approvals.AsNoTracking().CountAsync(), await db.MaterialityApprovals.AsNoTracking().CountAsync());
  }

  private static async Task<(Guid OperationId, WorkflowReminderIntent Intent, WorkflowTaskBinding Binding)>
    ReadReminderEvidenceAsync(ITestPostgresDatabase pg, Guid ruleRevisionId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x =>
      x.OperationKind == WorkflowReminderTaskHandler.Kind && x.TargetId == ruleRevisionId);
    var intent = JsonSerializer.Deserialize<WorkflowReminderIntent>(operation.PayloadJson)
      ?? throw new InvalidOperationException("Reminder operation has no typed intent payload.");
    var binding = await db.WorkflowTaskBindings.AsNoTracking()
      .SingleAsync(x => x.ReminderOperationId == operation.Id);
    return (operation.Id, intent, binding);
  }

  private static async Task<IReadOnlyList<(Guid OperationId, WorkflowReminderIntent Intent, WorkflowTaskBinding Binding)>>
    ReadAllReminderEvidenceAsync(ITestPostgresDatabase pg)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var operations = await db.DurableOperations.AsNoTracking()
      .Where(x => x.OperationKind == WorkflowReminderTaskHandler.Kind).OrderBy(x => x.CreatedAt).ToListAsync();
    var result = new List<(Guid OperationId, WorkflowReminderIntent Intent, WorkflowTaskBinding Binding)>();
    foreach (var operation in operations)
    {
      var intent = JsonSerializer.Deserialize<WorkflowReminderIntent>(operation.PayloadJson)
        ?? throw new InvalidOperationException("Reminder operation has no typed intent payload.");
      var binding = await db.WorkflowTaskBindings.AsNoTracking()
        .SingleAsync(x => x.ReminderOperationId == operation.Id);
      result.Add((operation.Id, intent, binding));
    }
    return result;
  }

  private static async Task AddFrozenReportAsync(
    ITestPostgresDatabase pg, PbcSeed.Fixture fixture, DateTimeOffset now)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var signedAt = now.AddDays(-61);
    var dueAt = signedAt.AddDays(60);
    var reportId = Guid.NewGuid();
    db.AuditDeliverables.Add(new AuditDeliverable
    {
      Id = reportId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      Kind = DeliverableKinds.SummaryReviewMemorandum, Version = 1, TemplateVersion = "test",
      InputDigest = new string('a', 64), InputSummaryJson = "{}", FileName = "srm.pdf",
      ContentType = "application/pdf", Content = [1], ContentSha256 = new string('b', 64),
      CreatedByUserId = fixture.Admin.Id, CreatedAt = signedAt
    });
    db.EngagementFileFreezes.Add(new EngagementFileFreeze
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      ReportDeliverableId = reportId, ReportSignedAt = signedAt, DueAt = dueAt,
      State = FileFreezeStates.Frozen, FrozenAt = dueAt, UpdatedAt = dueAt
    });
    await db.SaveChangesAsync();
  }

  private static async Task<WorkflowReminderRuleRevisionView> CreatePublishedRuleAsync(
    ITestPostgresDatabase pg,
    ActorContext author,
    ActorContext reviewer,
    WorkflowReminderRuleDefinition definition)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var draft = await WorkflowReminderRuleService.SaveDraftAsync(db, author,
      new(null, definition, RequestKey: Guid.NewGuid()));
    Assert.True(draft.Succeeded, draft.Message);
    var savedDraft = draft.Value!;

    var reviewed = await WorkflowReminderRuleService.ReviewAsync(db, reviewer, savedDraft.RuleId,
      savedDraft.RevisionId, new(savedDraft.Revision, savedDraft.ContentDigest,
        "Reviewed exact reminder definition."));
    Assert.True(reviewed.Succeeded, reviewed.Message);

    var reviewedView = await WorkflowReminderRuleService.GetAsync(db, author,
      savedDraft.RuleId, savedDraft.RevisionId);
    Assert.True(reviewedView.Succeeded, reviewedView.Message);
    var reviewedRevision = reviewedView.Value!;
    var published = await WorkflowReminderRuleService.PublishAsync(db, author, savedDraft.RuleId,
      savedDraft.RevisionId, new(reviewedRevision.Revision, reviewedRevision.ContentDigest,
        "Publish the independently reviewed reminder rule."));
    Assert.True(published.Succeeded, published.Message);

    var result = await WorkflowReminderRuleService.GetAsync(db, author,
      savedDraft.RuleId, savedDraft.RevisionId);
    Assert.True(result.Succeeded, result.Message);
    return result.Value!;
  }

  private static WorkflowReminderRuleDefinition Definition() => new(
    "Planning task due reminder",
    WorkflowReminderSourceKinds.TaskDue,
    WorkflowReminderRecipientKinds.ResponsibleStaff,
    OffsetDays: 3,
    RepeatEveryDays: 7,
    TimeZoneId: "UTC",
    QuietHoursStart: "21:00",
    QuietHoursEnd: "07:00",
    EffectiveFromUtc: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
    MaxScheduledWindowCount: 3);

  private static string TamperDigest(string digest) =>
    digest[..^1] + (digest[^1] == '0' ? "1" : "0");

  private sealed record ReminderHarness(WorkflowReminderDiscovery Discovery,
    AuditSphereOps.Worker.Worker Worker, MutableClock Clock, IOperationStore Store,
    WorkflowReminderTaskHandler Handler);

  private sealed record SourceSnapshot(string Title, string Status, DateOnly? DueDate, Guid? AssigneeUserId,
    Guid BindingId, long BindingRevision, string SourceKey, Guid? ReminderRuleRevisionId,
    Guid? ReminderOperationId, int ActivityCount, int ApprovalCount, int MaterialityApprovalCount);

  private sealed record PbcSourceSnapshot(string State, long Revision, string DueDate, Guid? FirmOwnerUserId,
    Guid? ReviewerUserId, string Area, int ApprovalCount, int MaterialityApprovalCount);

  private sealed record LegacyReminderRuleDefinition(
    string Name, string SourceKind, string RecipientKind, int OffsetDays, int RepeatEveryDays,
    string TimeZoneId, string QuietHoursStart, string QuietHoursEnd, DateTimeOffset EffectiveFromUtc);

  private static string LegacyCreationRequestDigest(
    Guid? requestedRuleId, Guid creatorUserId, WorkflowReminderRuleDefinition definition)
  {
    var legacyDefinition = new LegacyReminderRuleDefinition(definition.Name, definition.SourceKind,
      definition.RecipientKind, definition.OffsetDays, definition.RepeatEveryDays,
      definition.TimeZoneId, definition.QuietHoursStart, definition.QuietHoursEnd,
      definition.EffectiveFromUtc);
    return Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
    {
      requestedRuleId,
      creatorUserId,
      definition = legacyDefinition
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
  }

  private sealed class MutableClock(DateTimeOffset initial) : TimeProvider
  {
    private DateTimeOffset now = initial;
    public override DateTimeOffset GetUtcNow() => now;
    public void SetUtcNow(DateTimeOffset value) => now = value;
  }
}
