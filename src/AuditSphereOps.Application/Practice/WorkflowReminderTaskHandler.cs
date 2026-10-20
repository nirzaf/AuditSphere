using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowReminderIntent(
  Guid RuleRevisionId, string RuleDigest, string SourceKind, Guid SourceId,
  long SourceRevision, long DueRevision, string SourceDigest, DateOnly SourceDueDate,
  Guid RecipientUserId, Guid PublisherUserId, long PublisherEpoch, string OccurrenceKey);

internal sealed record WorkflowReminderSource(
  Guid Id, string Kind, Guid ClientId, Guid EngagementId, long Revision, long DueRevision,
  DateOnly DueDate, string Title, string Phase, Guid? ResponsibleUserId, Guid? ReviewerUserId,
  string Digest);

/// <summary>Creates optional local follow-up tasks, never source approvals or external effects.</summary>
public sealed class WorkflowReminderTaskHandler(WorkflowReminderOptions options, TimeProvider? clock = null)
  : IOperationHandler
{
  public const string Kind = "CreateWorkflowReminderTask.v1";
  public const string SourcePrefix = "wf-reminder:";
  private readonly TimeProvider time = clock ?? TimeProvider.System;
  public OperationDefinition Definition { get; } =
    new(Kind, OperationMode.LOCAL, OperationAuthority.LOCAL_VALIDATION);
  public bool RequiresExclusiveFirmLock => true;

  public string NormalizePayload(OperationRequest request)
  {
    var intent = Parse(request.PayloadJson);
    if (request.TargetId != intent.RuleRevisionId || request.ExpectedRevision < 1 ||
        request.ClientId is null || request.EngagementId is null || request.OriginatorId != intent.PublisherUserId)
      throw new OperationBlockedException("workflow-reminder-intent-scope", authorization: true);
    return JsonSerializer.Serialize(intent);
  }

  internal static WorkflowReminderIntent Parse(string json)
  {
    try
    {
      var value = JsonSerializer.Deserialize<WorkflowReminderIntent>(json);
      if (value is null || value.RuleRevisionId == Guid.Empty || value.SourceId == Guid.Empty ||
          value.RecipientUserId == Guid.Empty || value.PublisherUserId == Guid.Empty ||
          value.PublisherEpoch < 1 || value.SourceRevision < 1 || value.DueRevision < 1 ||
          value.SourceKind is not ("TASK_DUE" or "PBC_DUE") ||
          !IsDigest(value.RuleDigest) || !IsDigest(value.SourceDigest) ||
          string.IsNullOrWhiteSpace(value.OccurrenceKey) || value.OccurrenceKey.Length > 40 ||
          value.SourceDueDate.Year < 1)
        throw new OperationBlockedException("workflow-reminder-intent-invalid");
      return value;
    }
    catch (JsonException) { throw new OperationBlockedException("workflow-reminder-intent-invalid"); }
  }

  private static bool IsDigest(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);

  public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation operation, CancellationToken ct)
  {
    _ = await ValidateAsync(db, operation, ct);
  }

  private async Task<(WorkflowReminderIntent Intent, WorkflowReminderRuleRevision Rule,
    WorkflowReminderSource Source)> ValidateAsync(IAuditSphereDbContext db, DurableOperation operation, CancellationToken ct)
  {
    if (!options.Enabled)
      throw new OperationBlockedException("workflow-reminders-disabled", authorization: true);
    var intent = Parse(operation.PayloadJson);
    var revision = await db.WorkflowReminderRuleRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == operation.TargetId && x.Id == intent.RuleRevisionId && x.FirmId == operation.FirmId, ct);
    var rule = revision is null ? null : await db.WorkflowReminderRules.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == revision.RuleId && x.FirmId == operation.FirmId, ct);
    if (revision is null || rule is null || rule.SuspendedAt is not null ||
        rule.CurrentPublishedRevisionId != revision.Id || revision.Status != "PUBLISHED" ||
        revision.Revision != operation.ExpectedRevision || revision.ContentDigest != intent.RuleDigest ||
        revision.PublishedByUserId != intent.PublisherUserId || revision.PublishedSessionEpoch != intent.PublisherEpoch)
      throw new OperationBlockedException("workflow-reminder-rule-not-current", authorization: true);
    // A pause must remain effective even if an operator resumes the rule before
    // this queued operation is claimed. Only the existing reviewed recovery
    // command can release that retained intent after the last state decision.
    var stateChangedAt = await db.WorkflowReminderRuleDecisions.AsNoTracking().Where(x =>
        x.FirmId == operation.FirmId && x.RuleId == rule.Id &&
        (x.Kind == "SUSPENDED" || x.Kind == "RESUMED") && x.OccurredAt >= operation.CreatedAt)
      .MaxAsync(x => (DateTimeOffset?)x.OccurredAt, ct);
    if (stateChangedAt.HasValue && !await db.OperationEvents.AsNoTracking().AnyAsync(x =>
        x.OperationId == operation.Id && x.Kind == "operation.recovery-retry.v1" &&
        x.OccurredAt >= stateChangedAt.Value, ct))
      throw new OperationBlockedException("workflow-reminder-recovery-review-required", authorization: true);
    var definition = WorkflowReminderRuntime.Definition(revision);
    if (WorkflowReminderRuleService.RequiresRevision(definition))
      throw new OperationBlockedException("workflow-reminder-needs-revision");
    var valid = WorkflowReminderRuleService.NormalizeAndValidate(definition);
    if (!valid.Succeeded || WorkflowReminderRuleService.Digest(definition) != intent.RuleDigest)
      throw new OperationBlockedException("workflow-reminder-definition-invalid");
    await WorkflowReminderRuntime.RequirePublisherAsync(db, operation.FirmId, intent.PublisherUserId,
      intent.PublisherEpoch, ct);
    if (operation.ClientId is not { } clientId || operation.EngagementId is not { } engagementId)
      throw new OperationBlockedException("workflow-reminder-intent-scope", authorization: true);
    var source = await WorkflowReminderRuntime.SourceAsync(db, operation.FirmId, clientId, engagementId,
      intent.SourceKind, intent.SourceId, true, ct);
    if (source is null || source.Revision != intent.SourceRevision || source.DueRevision != intent.DueRevision ||
        source.Digest != intent.SourceDigest || source.DueDate != intent.SourceDueDate || definition.SourceKind != source.Kind)
      throw new OperationBlockedException("workflow-reminder-source-changed");
    var recipients = await WorkflowReminderRuntime.RecipientsAsync(db, operation.FirmId, source,
      definition.RecipientKind, time.GetUtcNow(), ct);
    if (!recipients.Contains(intent.RecipientUserId))
      throw new OperationBlockedException("workflow-reminder-recipient-ineligible", authorization: true);
    var now = time.GetUtcNow();
    if (now < definition.EffectiveFromUtc)
      throw new OperationDeferredException(definition.EffectiveFromUtc - now);
    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,
      TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZoneId)).DateTime);
    var scheduledWindowNumber = WorkflowReminderSchedule.ScheduledWindowNumber(
      intent.OccurrenceKey, definition.RepeatEveryDays);
    if (scheduledWindowNumber is null)
      throw new OperationBlockedException("workflow-reminder-occurrence-invalid");
    if (definition.MaxScheduledWindowCount is { } maximum && scheduledWindowNumber > maximum)
      throw new OperationBlockedException("workflow-reminder-schedule-windows-exhausted");
    var currentOccurrence = WorkflowReminderSchedule.CurrentOccurrenceKey(source.DueDate, today,
      definition.OffsetDays, definition.RepeatEveryDays);
    if (currentOccurrence is null || !WorkflowReminderRuntime.OccurrenceHasStarted(intent.OccurrenceKey, currentOccurrence))
      throw new OperationBlockedException("workflow-reminder-occurrence-not-due");
    WorkflowReminderRuntime.RequireOutsideQuietHours(definition, now);
    return (intent, revision, source);
  }

  public async Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation operation,
    OperationResult? verifiedRemoteResult, CancellationToken ct)
  {
    var (intent, rule, source) = await ValidateAsync(db, operation, ct);
    var sourceKey = WorkflowReminderRuntime.TaskSourceKey(intent);
    var digest = Hashing.Sha256Hex(sourceKey);
    var ordinal = (await db.WorkflowTaskBindings.AsNoTracking().Where(x =>
      x.FirmId == operation.FirmId && x.EngagementId == source.EngagementId && x.Phase == source.Phase)
      .MaxAsync(x => (int?)x.Ordinal, ct) ?? 0) + 1;
    if (ordinal > 100000)
      throw new OperationBlockedException("workflow-reminder-task-order-limit");
    var title = $"Reminder: {source.Title}";
    if (title.Length > 300) title = title[..300];
    var actor = new ActorContext(intent.PublisherUserId, operation.FirmId, intent.PublisherEpoch,
      ["Administrator", "Partner"], source.ClientId, source.EngagementId);
    var created = await PracticeTimeService.CreateWorkflowTaskAsync(db, actor,
      new CreateTaskRequest(title, source.ClientId, source.EngagementId, intent.RecipientUserId,
        DueDate: source.DueDate),
      new WorkflowTaskBinding
      {
        Phase = source.Phase, Ordinal = ordinal, SourceKey = sourceKey,
        CreationRequestDigest = digest, Generation = 1, AssignmentSource = "AUTOMATION",
        AssigneeUserIdSnapshot = intent.RecipientUserId, DueDatePolicy = "FIXED",
        ReminderRuleRevisionId = rule.Id, ReminderOperationId = operation.Id
      }, ct);
    if (!created.Succeeded)
      throw new OperationBlockedException("workflow-reminder-task-gate-blocked",
        created.ErrorCode == ErrorCodes.ScopeDenied);
    db.OperationEvents.Add(new OperationEvent
    {
      Id = Guid.CreateVersion7(), OperationId = operation.Id, Token = operation.AttemptToken,
      Kind = "workflow.reminder-task.created.v1", Executor = operation.LeaseOwner ?? "worker",
      OccurredAt = time.GetUtcNow()
    });
    return new(created.Value.ToString("D"), Hashing.Sha256Hex($"{sourceKey}:{created.Value:D}"));
  }

  public Task<OperationResult> ExecuteEffectAsync(DurableOperation operation, CancellationToken ct) =>
    throw new OperationBlockedException("workflow-reminder-has-no-provider-effect");
  public Task<OperationResult> ReconcileAsync(DurableOperation operation, CancellationToken ct) =>
    throw new OperationBlockedException("workflow-reminder-has-no-provider-effect");
}

internal static class WorkflowReminderRuntime
{
  private static readonly string[] StaffRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff"];
  private static readonly string[] WaitingPbcStates =
    [PbcStates.Sent, PbcStates.Acknowledged, PbcStates.PartiallyReceived, PbcStates.ClarificationRequired];

  internal static bool HasVerifiedEnvelope(DurableOperation operation)
  {
    try { return OperationEncoding.Matches(operation, new WorkflowReminderTaskHandler(new WorkflowReminderOptions())); }
    catch (OperationBlockedException) { return false; }
  }

  internal static WorkflowReminderRuleDefinition Definition(WorkflowReminderRuleRevision revision) =>
    new(revision.Name, revision.SourceKind, revision.RecipientKind, revision.OffsetDays, revision.RepeatEveryDays,
      revision.TimeZoneId, revision.QuietHoursStart, revision.QuietHoursEnd, revision.EffectiveFromUtc,
      revision.MaxScheduledWindowCount);

  internal static async Task RequirePublisherAsync(IAuditSphereDbContext db, Guid firmId, Guid userId,
    long epoch, CancellationToken ct)
  {
    var actor = new ActorContext(userId, firmId, epoch, ["Administrator", "Partner"]);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(firmId, RequiredRoles: ["Administrator", "Partner"], InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded)
      throw new OperationBlockedException("workflow-reminder-standing-authority-revoked", authorization: true);
  }

  internal static async Task<WorkflowReminderSource?> SourceAsync(IAuditSphereDbContext db, Guid firmId,
    Guid clientId, Guid engagementId, string kind, Guid sourceId, bool locked, CancellationToken ct)
  {
    var engagementQuery = locked
      ? db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE firm_id = {firmId} AND practice_client_id = {clientId} AND id = {engagementId} FOR UPDATE")
      : db.Engagements.Where(x => x.FirmId == firmId && x.PracticeClientId == clientId && x.Id == engagementId);
    var engagement = await engagementQuery.AsNoTracking().SingleOrDefaultAsync(ct);
    if (engagement is null || engagement.Status != "Active" || engagement.ProfessionalWorkBlocked ||
        await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.ClientId == clientId &&
          x.EngagementId == engagementId && x.State == FileFreezeStates.Frozen, ct)) return null;

    if (kind == "TASK_DUE")
    {
      var taskQuery = locked
        ? db.WorkTasks.FromSqlInterpolated($"SELECT * FROM work_tasks WHERE firm_id = {firmId} AND client_id = {clientId} AND engagement_id = {engagementId} AND id = {sourceId} FOR UPDATE")
        : db.WorkTasks.Where(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Id == sourceId);
      var task = await taskQuery.AsNoTracking().SingleOrDefaultAsync(ct);
      var bindingQuery = locked
        ? db.WorkflowTaskBindings.FromSqlInterpolated($"SELECT * FROM workflow_task_bindings WHERE firm_id = {firmId} AND client_id = {clientId} AND engagement_id = {engagementId} AND work_task_id = {sourceId} FOR UPDATE")
        : db.WorkflowTaskBindings.Where(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.WorkTaskId == sourceId);
      var binding = await bindingQuery.AsNoTracking().SingleOrDefaultAsync(ct);
      if (task is null || binding is null || binding.ReminderRuleRevisionId is not null ||
          task.Status is not (PracticeTimeStates.TaskOpen or PracticeTimeStates.TaskInProgress) || task.DueDate is null)
        return null;
      var dueRevision = await db.WorkflowTaskActivities.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == clientId && x.EngagementId == engagementId && x.WorkTaskId == task.Id &&
        x.WorkflowTaskBindingId == binding.Id && x.Kind == WorkflowTaskActivityKinds.DueDateChanged)
        .MaxAsync(x => (long?)x.TaskRevision, ct) ?? 1;
      return new(task.Id, kind, clientId, engagementId, binding.Revision, dueRevision, task.DueDate.Value,
        task.Title, binding.Phase, task.AssigneeUserId, null,
        Hashing.Sha256Hex(JsonSerializer.Serialize(new { task.Id, task.Title, task.Status, task.DueDate,
          task.AssigneeUserId, BindingId = binding.Id, binding.Revision, binding.Generation, binding.Phase })));
    }

    if (kind != "PBC_DUE" || await db.CommercialDeliverableBundles.AsNoTracking().AnyAsync(x =>
        x.FirmId == firmId && x.EngagementId == engagementId, ct)) return null;
    var pbcQuery = locked
      ? db.PbcRequests.FromSqlInterpolated($"SELECT * FROM pbc_requests WHERE firm_id = {firmId} AND client_id = {clientId} AND engagement_id = {engagementId} AND id = {sourceId} FOR UPDATE")
      : db.PbcRequests.Where(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Id == sourceId);
    var pbc = await pbcQuery.AsNoTracking().SingleOrDefaultAsync(ct);
    if (pbc is null || !WaitingPbcStates.Contains(pbc.State, StringComparer.Ordinal) ||
        !DateOnly.TryParseExact(pbc.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
          DateTimeStyles.None, out var dueDate)) return null;
    return new(pbc.Id, kind, clientId, engagementId, pbc.Revision, 1, dueDate,
      $"PBC: {pbc.Area}", WorkflowTaskPhases.AuditFieldwork, pbc.FirmOwnerUserId, pbc.ReviewerUserId,
      Hashing.Sha256Hex(JsonSerializer.Serialize(new { pbc.Id, pbc.Revision, pbc.State, pbc.DueDate,
        pbc.FirmOwnerUserId, pbc.ReviewerUserId, pbc.Area })));
  }

  internal static async Task<IReadOnlyList<Guid>> RecipientsAsync(IAuditSphereDbContext db, Guid firmId,
    WorkflowReminderSource source, string selector, DateTimeOffset now, CancellationToken ct)
  {
    Guid[] candidates;
    if (selector == "RESPONSIBLE_STAFF")
      candidates = source.ResponsibleUserId is { } responsible ? [responsible] : [];
    else if (selector == "ENGAGEMENT_REVIEWER" && source.ReviewerUserId is { } reviewer)
      candidates = [reviewer];
    else if (selector == "ENGAGEMENT_REVIEWER")
    {
      var today = DateOnly.FromDateTime(now.UtcDateTime);
      candidates = await db.EngagementAssignments.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == source.ClientId && x.EngagementId == source.EngagementId && x.Active &&
        (x.Role == "Manager" || x.Role == "SeniorManager" || x.Role == "Partner") &&
        (!x.StartDate.HasValue || x.StartDate <= today) && (!x.EndDate.HasValue || x.EndDate >= today))
        .Select(x => x.UserId).Distinct().OrderBy(x => x).Take(51).ToArrayAsync(ct);
      if (candidates.Length > 50)
        throw new OperationBlockedException("workflow-reminder-recipient-bound");
    }
    else return [];
    var recipients = new List<Guid>();
    foreach (var userId in candidates)
    {
      var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
        x.Id == userId && !x.Disabled && x.UserKind != "Client", ct);
      if (user is null) continue;
      var roles = selector == "ENGAGEMENT_REVIEWER" ? new[] { "Manager", "Partner", "Administrator" } : StaffRoles;
      var actor = new ActorContext(user.Id, firmId, user.SessionEpoch, roles, source.ClientId, source.EngagementId);
      var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new(firmId, source.ClientId, source.EngagementId, roles, InternalOnly: true), ct);
      if (auth.Succeeded) recipients.Add(userId);
    }
    return recipients;
  }

  internal static bool OccurrenceHasStarted(string expected, string current) =>
    expected == "ONCE" && current == "ONCE" ||
    expected.StartsWith("WINDOW:", StringComparison.Ordinal) && current.StartsWith("WINDOW:", StringComparison.Ordinal) &&
    long.TryParse(expected.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var before) &&
    long.TryParse(current.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var now) && before <= now;

  internal static string TaskSourceKey(WorkflowReminderIntent intent) => WorkflowReminderTaskHandler.SourcePrefix +
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { intent.RuleRevisionId, intent.SourceKind, intent.SourceId,
      intent.DueRevision, intent.SourceDueDate, intent.RecipientUserId, intent.OccurrenceKey }));

  internal static void RequireOutsideQuietHours(WorkflowReminderRuleDefinition definition, DateTimeOffset now)
  {
    var zone = TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZoneId);
    var local = TimeZoneInfo.ConvertTime(now, zone);
    var start = TimeOnly.ParseExact(definition.QuietHoursStart, "HH:mm", CultureInfo.InvariantCulture);
    var end = TimeOnly.ParseExact(definition.QuietHoursEnd, "HH:mm", CultureInfo.InvariantCulture);
    if (!PbcReminderDiscovery.IsQuiet(local.TimeOfDay, start, end)) return;
    var day = DateOnly.FromDateTime(local.DateTime);
    if (start > end && TimeOnly.FromDateTime(local.DateTime) >= start) day = day.AddDays(1);
    var localEnd = day.ToDateTime(end, DateTimeKind.Unspecified);
    while (zone.IsInvalidTime(localEnd)) localEnd = localEnd.AddMinutes(1);
    var endUtc = zone.IsAmbiguousTime(localEnd)
      ? new DateTimeOffset(localEnd, zone.GetAmbiguousTimeOffsets(localEnd).Min()).UtcDateTime
      : TimeZoneInfo.ConvertTimeToUtc(localEnd, zone);
    throw new OperationDeferredException(new DateTimeOffset(endUtc, TimeSpan.Zero) - now);
  }
}
