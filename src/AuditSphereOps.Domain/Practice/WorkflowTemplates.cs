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

/// <summary>Per-step PBC instantiation state shown by a workflow plan preview. None of these states sends a request.</summary>
public static class WorkflowPlanPbcStates
{
  /// <summary>Legacy descriptive text only; it is never interpreted as an executable request definition.</summary>
  public const string UnresolvedReference = "UNRESOLVED_REFERENCE";
  public const string OwnersRequired = "OWNERS_REQUIRED";
  /// <summary>The existing Active-engagement PBC guard applies; no request is created before activation.</summary>
  public const string BlockedActivation = "BLOCKED_ACTIVATION";
  /// <summary>Execution creates one fresh unsent DRAFT request through the existing PBC command.</summary>
  public const string ReadyDraft = "READY_DRAFT";
  /// <summary>The plan step's task is already bound to its own request; replay creates nothing.</summary>
  public const string Linked = "LINKED";
}

public sealed class WorkflowTemplateStep
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid TemplateRevisionId { get; set; }
  public string StableKey { get; set; } = string.Empty;
  public string Phase { get; set; } = WorkflowTaskPhases.ClientOnboarding;
  /// <summary>Optional operational grouping label, pinned by this immutable template revision.</summary>
  public string? TaskGroupName { get; set; }
  public string Title { get; set; } = string.Empty;
  public string Instructions { get; set; } = string.Empty;
  public string? SuggestedStaffingLevel { get; set; }
  public int? EstimatedEffortMinutes { get; set; }
  public int? DueDateOffsetDays { get; set; }
  /// <summary>Retained descriptive text. It is not an executable PBC definition and is never inferred into one.</summary>
  public string? PbcRequestReference { get; set; }
  public bool RequiredTask { get; set; } = true;
  /// <summary>
  /// When set on a required step, the step is conditional: it applies unless an authorized user records it as not
  /// applicable for an engagement. Pinned by this immutable revision and covered by its content digest only when present.
  /// </summary>
  public string? ApplicabilityCondition { get; set; }
  public int SortOrder { get; set; }
  // Optional typed PBC instruction (the reusable CreatePbcRequestRequest fields). It is versioned by the immutable
  // template revision and covered by its content digest; engagement, period, owners and due date are resolved per plan.
  public string? PbcObjective { get; set; }
  public string? PbcEntityScope { get; set; }
  public string? PbcArea { get; set; }
  public string? PbcRequestedFormat { get; set; }
  public string? PbcControlTotals { get; set; }
  public string? PbcConfidentiality { get; set; }
  public string? PbcAcceptanceCriteria { get; set; }
}

/// <summary>Immutable, stable-key declaration of one domain-evidence prerequisite on an exact template step.</summary>
public sealed class WorkflowTemplateEvidenceRequirement
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid TemplateRevisionId { get; set; }
  public Guid TemplateStepId { get; set; }
  public string RequirementKey { get; set; } = string.Empty;
  public string Kind { get; set; } = string.Empty;
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
