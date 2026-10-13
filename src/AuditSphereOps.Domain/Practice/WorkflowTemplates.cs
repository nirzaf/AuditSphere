namespace AuditSphereOps.Domain.Practice;

public static class WorkflowTemplateStates
{
  public const string Draft = "DRAFT";
  public const string Reviewed = "REVIEWED";
  public const string Published = "PUBLISHED";
  public const string Superseded = "SUPERSEDED";
}

public static class WorkflowTemplateDecisionKinds
{
  public const string DraftCreated = "DRAFT_CREATED";
  public const string DraftEdited = "DRAFT_EDITED";
  public const string Reviewed = "REVIEWED";
  public const string Published = "PUBLISHED";
  public const string Superseded = "SUPERSEDED";
  public const string Archived = "ARCHIVED";
}

public static class WorkflowDependencyTypes
{
  public const string TaskCompleted = "TASK_COMPLETED";
  public const string DomainEvidenceSatisfied = "DOMAIN_EVIDENCE_SATISFIED";
}

/// <summary>Stable firm-owned catalog identity. Revisions carry all selectable content.</summary>
public sealed class WorkflowTemplate
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ArchivedAt { get; set; }
  public Guid? ArchivedByUserId { get; set; }
}

/// <summary>
/// A versioned template definition. Draft content is editable with revision fencing. Review and publication freeze the
/// definition; later edits require a new revision so engagements can pin the exact version they were given.
/// </summary>
public sealed class WorkflowTemplateRevision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid TemplateId { get; set; }
  public int Version { get; set; }
  public string Name { get; set; } = string.Empty;
  public string ServiceRoute { get; set; } = string.Empty;
  public string? IndustryKey { get; set; }
  public string Status { get; set; } = WorkflowTemplateStates.Draft;
  public string ContentDigest { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public Guid LastEditedByUserId { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
  public Guid? PublishedByUserId { get; set; }
  public DateTimeOffset? PublishedAt { get; set; }
  public long Revision { get; set; } = 1;
}

public sealed class WorkflowTemplateStep
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid TemplateRevisionId { get; set; }
  public string StableKey { get; set; } = string.Empty;
  public string Phase { get; set; } = WorkflowTaskPhases.ClientOnboarding;
  public string Title { get; set; } = string.Empty;
  public string Instructions { get; set; } = string.Empty;
  public string? SuggestedStaffingLevel { get; set; }
  public int? EstimatedEffortMinutes { get; set; }
  public int? DueDateOffsetDays { get; set; }
  public string? PbcRequestReference { get; set; }
  public bool RequiredTask { get; set; } = true;
  public int SortOrder { get; set; }
}

public sealed class WorkflowTemplateDependency
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid TemplateRevisionId { get; set; }
  public Guid PredecessorStepId { get; set; }
  public Guid SuccessorStepId { get; set; }
  public string DependencyType { get; set; } = WorkflowDependencyTypes.TaskCompleted;
}

/// <summary>Append-only review, publication, supersession, and archive decisions on exact template revisions.</summary>
public sealed class WorkflowTemplateDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid TemplateId { get; set; }
  public Guid? TemplateRevisionId { get; set; }
  public string Kind { get; set; } = string.Empty;
  public string ContentDigest { get; set; } = string.Empty;
  public string Rationale { get; set; } = string.Empty;
  public Guid ActorUserId { get; set; }
  public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// Immutable receipt for one engagement's instantiation of an exact published template revision and reviewed schedule.
/// Workflow plan receipts create supporting WorkTask rows only; they never satisfy a protected audit gate.
/// </summary>
public sealed class WorkflowTaskPlanReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid TemplateId { get; set; }
  public Guid TemplateRevisionId { get; set; }
  public int TemplateVersion { get; set; }
  public long EngagementGeneration { get; set; }
  public Guid? MilestonePlanId { get; set; }
  public long? MilestonePlanRevision { get; set; }
  public DateOnly? ScheduleBasisDate { get; set; }
  public Guid IdempotencyKey { get; set; }
  public string RequestDigest { get; set; } = string.Empty;
  public int TaskCount { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
