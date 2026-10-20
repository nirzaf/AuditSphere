using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class WorkflowReminderRuleService
{
  public static async Task<CommandResult<WorkflowReminderExecutions>> ExecutionsAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid ruleId,
    WorkflowReminderExecutionCursor? after = null, int pageSize = 25, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return Fail<WorkflowReminderExecutions>(auth);
    if (ruleId == Guid.Empty || pageSize is < 1 or > 100 ||
        after is not null && (after.OperationId == Guid.Empty || after.CreatedAt == default))
      return Invalid<WorkflowReminderExecutions>("Provide a rule and a complete bounded execution cursor.");
    if (!await db.WorkflowReminderRules.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == ruleId, ct))
      return Denied<WorkflowReminderExecutions>();
    var query = from operation in db.DurableOperations.AsNoTracking()
                join revision in db.WorkflowReminderRuleRevisions.AsNoTracking()
                  on new { operation.FirmId, Id = operation.TargetId } equals new { revision.FirmId, revision.Id }
                where operation.FirmId == actor.FirmId && revision.RuleId == ruleId &&
                  operation.OperationKind == WorkflowReminderTaskHandler.Kind &&
                  operation.ClientId != null && operation.EngagementId != null
                select new
                {
                  Operation = operation,
                  RuleVersion = revision.Version,
                  revision.RepeatEveryDays,
                  revision.MaxScheduledWindowCount
                };
    if (after is not null)
      query = query.Where(x => x.Operation.CreatedAt < after.CreatedAt ||
        x.Operation.CreatedAt == after.CreatedAt && x.Operation.Id.CompareTo(after.OperationId) < 0);
    var rows = await query.OrderByDescending(x => x.Operation.CreatedAt).ThenByDescending(x => x.Operation.Id)
      .Take(pageSize + 1).ToListAsync(ct);
    var hasMore = rows.Count > pageSize;
    if (hasMore) rows.RemoveAt(rows.Count - 1);
    var completedIds = rows.Where(x => x.Operation.Status == OperationState.COMPLETED &&
        Guid.TryParse(x.Operation.ResultIdentity, out _)).Select(x => Guid.Parse(x.Operation.ResultIdentity!)).ToArray();
    var bindings = await db.WorkflowTaskBindings.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      completedIds.Contains(x.WorkTaskId) && x.ReminderRuleRevisionId != null && x.ReminderOperationId != null)
      .Select(x => new { x.WorkTaskId, x.ClientId, x.EngagementId, x.ReminderRuleRevisionId, x.ReminderOperationId })
      .ToListAsync(ct);
    var items = new List<WorkflowReminderExecutionView>();
    foreach (var row in rows)
    {
      var operation = row.Operation;
      WorkflowReminderIntent? intent;
      try { intent = WorkflowReminderTaskHandler.Parse(operation.PayloadJson); }
      catch (OperationBlockedException) { intent = null; }
      var validIntent = intent is not null && intent.RuleRevisionId == operation.TargetId &&
        WorkflowReminderRuntime.HasVerifiedEnvelope(operation);
      Guid? taskId = operation.Status == OperationState.COMPLETED &&
        Guid.TryParse(operation.ResultIdentity, out var resultId) && bindings.Any(x =>
          x.WorkTaskId == resultId && x.ClientId == operation.ClientId && x.EngagementId == operation.EngagementId &&
          x.ReminderRuleRevisionId == operation.TargetId && x.ReminderOperationId == operation.Id)
        ? resultId : null;
      var missingEvidence = operation.Status == OperationState.COMPLETED && taskId is null;
      var errorCode = missingEvidence ? "workflow-reminder-result-evidence-unavailable" : operation.ErrorCode;
      if (!missingEvidence && operation.Status == OperationState.PROVIDER_BLOCKED && validIntent &&
          operation.ErrorCode == "provider-or-integrity-blocked" &&
          row.MaxScheduledWindowCount is { } maximum &&
          AuditSphereOps.Domain.Practice.WorkflowReminderSchedule.ScheduledWindowNumber(
            intent!.OccurrenceKey, row.RepeatEveryDays) is { } windowNumber && windowNumber > maximum)
        errorCode = "workflow-reminder-schedule-windows-exhausted";
      var sourceHref = !validIntent ? null : intent!.SourceKind == "TASK_DUE"
        ? $"/app/work?engagementId={operation.EngagementId:D}&taskId={intent.SourceId:D}"
        : $"/app/engagements/{operation.EngagementId:D}/pbc";
      items.Add(new(operation.Id, operation.TargetId, row.RuleVersion,
        operation.ClientId!.Value, operation.EngagementId!.Value,
        missingEvidence ? "EVIDENCE_UNAVAILABLE" : operation.Status.ToString(),
        errorCode,
        operation.CreatedAt, operation.NextAttemptAt,
        validIntent ? intent!.SourceKind : null, validIntent ? intent!.SourceId : null,
        validIntent ? intent!.DueRevision : null, validIntent ? intent!.SourceDueDate : null,
        validIntent ? intent!.RecipientUserId : null, sourceHref, taskId));
    }
    auth = await AuthorizeAsync(db, actor, ConfigureRoles, ct);
    if (!auth.Succeeded) return Fail<WorkflowReminderExecutions>(auth);
    var operations = await AuthorizeAsync(db, actor, ["Administrator"], ct);
    var last = rows.LastOrDefault()?.Operation;
    return CommandResult<WorkflowReminderExecutions>.Ok(new(items, hasMore,
      hasMore && last is not null ? new(last.CreatedAt, last.Id) : null, operations.Succeeded));
  }
}
