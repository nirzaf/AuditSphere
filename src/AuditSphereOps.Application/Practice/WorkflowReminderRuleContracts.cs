namespace AuditSphereOps.Application.Practice;

/// <summary>Explicit operational policy; no active rules or professional decisions are inferred.</summary>
public sealed record WorkflowReminderRuleDefinition(
  string Name, string SourceKind, string RecipientKind, int OffsetDays, int RepeatEveryDays,
  string TimeZoneId, string QuietHoursStart, string QuietHoursEnd, DateTimeOffset EffectiveFromUtc);

public sealed record WorkflowReminderRuleDraftRequest(
  Guid? RuleId, WorkflowReminderRuleDefinition Definition, long? ExpectedRevision = null,
  Guid RequestKey = default);

public sealed record WorkflowReminderRuleDecisionRequest(
  long ExpectedRevision, string ContentDigest, string Rationale);

public sealed record WorkflowReminderRuleStateRequest(
  long ExpectedRevision, Guid PublishedRevisionId, string ContentDigest, string Rationale);

public sealed record WorkflowReminderRuleCursor(DateTimeOffset CreatedAt, Guid RuleId);

public sealed record WorkflowReminderRuleCatalogItem(
  Guid RuleId, Guid? RevisionId, int? Version, string? Name, string? Status,
  bool Suspended, long Revision, DateTimeOffset CreatedAt,
  DateTimeOffset? LastEvaluatedAt = null, string? LastEvaluationCode = null);

public sealed record WorkflowReminderRuleCatalog(
  bool ExecutionEnabled, bool CanManage, IReadOnlyList<WorkflowReminderRuleCatalogItem> Items,
  bool HasMore, WorkflowReminderRuleCursor? NextCursor);

public sealed record WorkflowReminderRuleRevisionView(
  Guid RuleId, Guid RevisionId, int Version, WorkflowReminderRuleDefinition Definition,
  string Status, string ContentDigest, long Revision, long RuleRevision,
  Guid CreatedByUserId, Guid LastEditedByUserId, Guid? ReviewedByUserId,
  Guid? PublishedByUserId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
  DateTimeOffset? ReviewedAt, DateTimeOffset? PublishedAt, bool Suspended,
  bool CanEdit, bool CanReview, bool CanPublish, bool CanSuspend, bool CanResume,
  string? Blocker);

public sealed record WorkflowReminderRuleDecisionView(
  Guid Id, Guid RuleRevisionId, string Kind, string ContentDigest,
  string Rationale, Guid ActorUserId, DateTimeOffset OccurredAt);

public sealed record WorkflowReminderRuleHistory(
  IReadOnlyList<WorkflowReminderRuleDecisionView> Items, bool HasMore, Guid? NextAfterId);

public sealed record WorkflowReminderExecutionCursor(DateTimeOffset CreatedAt, Guid OperationId);

/// <summary>Redacted existing-outbox evidence, never a provider receipt or source approval.</summary>
public sealed record WorkflowReminderExecutionView(
  Guid OperationId, Guid RuleRevisionId, int RuleVersion, Guid ClientId, Guid EngagementId,
  string State, string? ErrorCode, DateTimeOffset CreatedAt, DateTimeOffset NextAttemptAt,
  string? SourceKind, Guid? SourceId, long? DueRevision, DateOnly? SourceDueDate,
  Guid? RecipientUserId, string? SourceHref, Guid? CreatedTaskId);

public sealed record WorkflowReminderExecutions(
  IReadOnlyList<WorkflowReminderExecutionView> Items, bool HasMore,
  WorkflowReminderExecutionCursor? NextCursor, bool CanOpenOperations);

public sealed record WorkflowReminderTaskOrigin(
  string SourceKind, Guid SourceId, long SourceRevision, long DueRevision,
  DateOnly SourceDueDate, string SourceHref, Guid RecipientUserId, bool? IsSourceCurrent);

public sealed record WorkflowReminderOptions(bool Enabled = false, int BatchSize = 25)
{
  public void Validate()
  {
    if (BatchSize is < 1 or > 100)
      throw new InvalidOperationException("workflow-reminder-batch-invalid");
  }
}
