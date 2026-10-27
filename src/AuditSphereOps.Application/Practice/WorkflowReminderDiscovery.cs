using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>
/// Evaluates one bounded rule batch per poll. Checkpoints provide fairness only;
/// immutable operation identities and task source keys remain the correctness boundary.
/// </summary>
public sealed class WorkflowReminderDiscovery(
  IAuditSphereDbContextFactory factory, IOperationStore store, WorkflowReminderTaskHandler handler,
  WorkerOptions worker, WorkflowReminderOptions options, TimeProvider clock) : IPendingOperationDiscovery
{
  private sealed record Scan(Guid RuleId, Guid RevisionId, string? DueDate, Guid? SourceId);
  private sealed record Candidate(Guid Id, Guid ClientId, Guid EngagementId, string DueDate);

  public async Task<int> EnqueuePendingAsync(CancellationToken ct = default)
  {
    if (!options.Enabled) return 0;
    options.Validate();
    var scan = await StartBatchAsync(ct);
    if (scan is null) return 0;
    WorkflowReminderRuleRevision? revision;
    await using (var read = await factory.CreateAsync(ct))
      revision = await read.WorkflowReminderRuleRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == worker.FirmId && x.Id == scan.RevisionId && x.RuleId == scan.RuleId && x.Status == "PUBLISHED", ct);
    if (revision is null) return 0;
    var definition = WorkflowReminderRuntime.Definition(revision);
    var now = clock.GetUtcNow();
    if (now < definition.EffectiveFromUtc)
    {
      await FinishBatchAsync(scan, null, "NOT_EFFECTIVE", advance: false, ct);
      return 0;
    }
    if (WorkflowReminderRuleService.RequiresRevision(definition))
    {
      await FinishBatchAsync(scan, null, "NEEDS_REVISION", advance: false, ct);
      return 0;
    }
    var validated = WorkflowReminderRuleService.NormalizeAndValidate(definition);
    if (!validated.Succeeded || WorkflowReminderRuleService.Digest(definition) != revision.ContentDigest)
    {
      await FinishBatchAsync(scan, null, "DEFINITION_INVALID", advance: false, ct);
      return 0;
    }
    try { WorkflowReminderRuntime.RequireOutsideQuietHours(definition, now); }
    catch (OperationDeferredException)
    {
      await FinishBatchAsync(scan, null, "QUIET_HOURS", advance: false, ct);
      return 0;
    }
    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,
      TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZoneId)).DateTime);
    var candidates = await CandidatesAsync(scan, definition, today, ct);
    var queued = 0;
    string? blocked = null;
    foreach (var candidate in candidates)
    {
      await using var db = await factory.CreateAsync(ct);
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      if (await LockFirmAsync(db, ct) is null) return queued;
      var head = await db.WorkflowReminderRules.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == worker.FirmId && x.Id == scan.RuleId, ct);
      var currentRevision = await db.WorkflowReminderRuleRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == worker.FirmId && x.Id == scan.RevisionId, ct);
      if (head is null || head.SuspendedAt is not null || head.CurrentPublishedRevisionId != scan.RevisionId ||
          currentRevision?.Status != "PUBLISHED" || currentRevision.ContentDigest != revision.ContentDigest)
        break;
      if (currentRevision.PublishedByUserId is not { } publisher ||
          currentRevision.PublishedSessionEpoch is not { } epoch)
      {
        blocked = "STANDING_AUTHORITY_UNAVAILABLE";
        continue;
      }
      try
      {
        await WorkflowReminderRuntime.RequirePublisherAsync(db, worker.FirmId, publisher, epoch, ct);
        var source = await WorkflowReminderRuntime.SourceAsync(db, worker.FirmId,
          candidate.ClientId, candidate.EngagementId, definition.SourceKind, candidate.Id, true, ct);
        if (source is null) { blocked = "SOURCE_NOT_ELIGIBLE"; continue; }
        var schedule = WorkflowReminderSchedule.Evaluate(source.DueDate, today,
          definition.OffsetDays, definition.RepeatEveryDays, definition.MaxScheduledWindowCount);
        if (schedule.Disposition == WorkflowReminderScheduleDisposition.NotDue) continue;
        if (schedule.Disposition == WorkflowReminderScheduleDisposition.NeedsRevision)
        {
          blocked = "NEEDS_REVISION";
          continue;
        }
        if (schedule.Disposition == WorkflowReminderScheduleDisposition.Exhausted)
        {
          blocked = "SCHEDULE_WINDOWS_EXHAUSTED";
          continue;
        }
        var recipients = await WorkflowReminderRuntime.RecipientsAsync(db, worker.FirmId, source,
          definition.RecipientKind, clock.GetUtcNow(), ct);
        if (recipients.Count == 0) { blocked = "NO_ELIGIBLE_RECIPIENT"; continue; }
        foreach (var recipient in recipients)
        {
          var intent = new WorkflowReminderIntent(revision.Id, revision.ContentDigest, source.Kind,
            source.Id, source.Revision, source.DueRevision, source.Digest, source.DueDate,
            recipient, publisher, epoch, schedule.OccurrenceKey!);
          var sourceKey = WorkflowReminderRuntime.TaskSourceKey(intent);
          if (await db.WorkflowTaskBindings.AsNoTracking().AnyAsync(x => x.FirmId == worker.FirmId &&
              x.ClientId == source.ClientId && x.EngagementId == source.EngagementId && x.SourceKey == sourceKey, ct))
            continue;
          // Source changes can replace a stale queued operation, but the task key
          // remains stable for the exact due generation, recipient and occurrence.
          var key = "workflow-reminder:" + Hashing.Sha256Hex(JsonSerializer.Serialize(intent));
          if (await db.DurableOperations.AsNoTracking().AnyAsync(x => x.FirmId == worker.FirmId &&
              x.OperationKind == WorkflowReminderTaskHandler.Kind && x.IdempotencyKey == key, ct)) continue;
          var result = await store.EnqueueAsync(db,
            new(worker.FirmId, source.ClientId, source.EngagementId, WorkflowReminderTaskHandler.Kind,
              revision.Id, revision.Revision, key, JsonSerializer.Serialize(intent), publisher), handler, ct);
          if (result.Succeeded) queued++;
          else blocked = "INTENT_SCOPE_BLOCKED";
        }
        await tx.CommitAsync(ct);
      }
      catch (OperationBlockedException)
      {
        blocked = "AUTHORITY_OR_SOURCE_BLOCKED";
      }
    }
    await FinishBatchAsync(scan, candidates.LastOrDefault(),
      blocked ?? (queued > 0 ? "QUEUED" : candidates.Count == 0 ? "NO_DUE_SOURCES" : "NO_NEW_INTENTS"),
      advance: true, ct);
    return queued;
  }

  private async Task<Scan?> StartBatchAsync(CancellationToken ct)
  {
    await using var db = await factory.CreateAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, ct) is null) return null;
    var rule = await db.WorkflowReminderRules.Where(x => x.FirmId == worker.FirmId &&
        x.CurrentPublishedRevisionId != null && x.SuspendedAt == null)
      .OrderBy(x => x.LastEvaluatedAt ?? DateTimeOffset.UnixEpoch).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
    if (rule?.CurrentPublishedRevisionId is not { } revisionId) return null;
    if (rule.ScanRevisionId != revisionId)
    {
      rule.ScanRevisionId = revisionId;
      rule.ScanAfterDueDate = null;
      rule.ScanAfterSourceId = null;
    }
    rule.LastEvaluatedAt = clock.GetUtcNow();
    rule.LastEvaluationCode = "EVALUATING_BATCH";
    var scan = new Scan(rule.Id, revisionId, rule.ScanAfterDueDate, rule.ScanAfterSourceId);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return scan;
  }

  private async Task FinishBatchAsync(Scan scan, Candidate? last, string code, bool advance, CancellationToken ct)
  {
    await using var db = await factory.CreateAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, ct) is null) return;
    var rule = await db.WorkflowReminderRules.SingleOrDefaultAsync(x =>
      x.FirmId == worker.FirmId && x.Id == scan.RuleId, ct);
    if (rule is null || rule.CurrentPublishedRevisionId != scan.RevisionId) return;
    // A slower replica must not rewind a checkpoint already advanced by another.
    if (rule.ScanRevisionId == scan.RevisionId && rule.ScanAfterDueDate == scan.DueDate &&
        rule.ScanAfterSourceId == scan.SourceId)
    {
      if (advance)
      {
        rule.ScanAfterDueDate = last?.DueDate;
        rule.ScanAfterSourceId = last?.Id;
      }
      rule.LastEvaluationCode = code;
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
    }
  }

  private async Task<List<Candidate>> CandidatesAsync(Scan scan, WorkflowReminderRuleDefinition definition,
    DateOnly today, CancellationToken ct)
  {
    var cutoff = DateOnly.FromDayNumber((int)Math.Clamp((long)today.DayNumber - definition.OffsetDays,
      DateOnly.MinValue.DayNumber, DateOnly.MaxValue.DayNumber));
    await using var db = await factory.CreateAsync(ct);
    if (definition.SourceKind == "TASK_DUE")
    {
      var baseQuery = from binding in db.WorkflowTaskBindings.AsNoTracking()
                  join task in db.WorkTasks.AsNoTracking()
                    on new { binding.FirmId, Id = binding.WorkTaskId } equals new { task.FirmId, task.Id }
                  where binding.FirmId == worker.FirmId && binding.ReminderRuleRevisionId == null &&
                    task.ClientId == binding.ClientId && task.EngagementId == binding.EngagementId &&
                    (task.Status == PracticeTimeStates.TaskOpen || task.Status == PracticeTimeStates.TaskInProgress) &&
                    binding.HeldAt == null &&
                    task.DueDate.HasValue && task.DueDate.Value <= cutoff
                  select new { task.Id, binding.ClientId, binding.EngagementId, DueDate = task.DueDate!.Value };
      var query = baseQuery;
      if (scan.SourceId is { } afterId && DateOnly.TryParseExact(scan.DueDate, "yyyy-MM-dd",
          CultureInfo.InvariantCulture, DateTimeStyles.None, out var afterDate))
        query = query.Where(x => x.DueDate > afterDate || x.DueDate == afterDate && x.Id.CompareTo(afterId) > 0);
      var rows = await query.OrderBy(x => x.DueDate).ThenBy(x => x.Id).Take(options.BatchSize).ToListAsync(ct);
      if (rows.Count == 0 && scan.SourceId is not null)
        rows = await baseQuery.OrderBy(x => x.DueDate).ThenBy(x => x.Id).Take(options.BatchSize).ToListAsync(ct);
      return rows.Select(x => new Candidate(x.Id, x.ClientId, x.EngagementId,
        x.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).ToList();
    }
    if (definition.SourceKind != "PBC_DUE") return [];
    var maximumDate = cutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var basePbc = db.PbcRequests.AsNoTracking().Where(x => x.FirmId == worker.FirmId &&
      (x.State == PbcStates.Sent || x.State == PbcStates.Acknowledged || x.State == PbcStates.PartiallyReceived ||
       x.State == PbcStates.ClarificationRequired) && string.Compare(x.DueDate, maximumDate) <= 0);
    var pbc = basePbc;
    if (scan.SourceId is { } pbcAfterId && scan.DueDate is { } pbcAfterDate)
      pbc = pbc.Where(x => string.Compare(x.DueDate, pbcAfterDate) > 0 ||
        x.DueDate == pbcAfterDate && x.Id.CompareTo(pbcAfterId) > 0);
    var pbcRows = await pbc.OrderBy(x => x.DueDate).ThenBy(x => x.Id).Take(options.BatchSize).ToListAsync(ct);
    if (pbcRows.Count == 0 && scan.SourceId is not null)
      pbcRows = await basePbc.OrderBy(x => x.DueDate).ThenBy(x => x.Id).Take(options.BatchSize).ToListAsync(ct);
    return pbcRows.Select(x => new Candidate(x.Id, x.ClientId, x.EngagementId, x.DueDate)).ToList();
  }

  private Task<AuditSphereOps.Domain.Completion.FirmSafetyState?> LockFirmAsync(
    IAuditSphereDbContext db, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {worker.FirmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
}
