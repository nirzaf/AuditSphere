namespace AuditSphereOps.Domain.Practice;

/// <summary>Task applicability decisions never approve a linked professional procedure or complete a dependency.</summary>
public static class WorkflowTaskApplicabilityDecisions
{
  public const string Approve = "APPROVE";
  public const string Return = "RETURN";
  public const string Revoke = "REVOKE";
}

/// <summary>
/// Immutable request to exclude one conditional template task from progress. The original WorkTask and its operational
/// history remain unchanged. A proposal alone has no effect; the current independent decision and exact source basis
/// must still match when progress is read.
/// </summary>
public sealed class WorkflowTaskApplicabilityProposal
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid WorkflowTaskBindingId { get; set; }
  public Guid TemplateRevisionId { get; set; }
  public Guid TemplateStepId { get; set; }
  /// <summary>The retained, provider-verified, independently accepted file version supporting the human statement.</summary>
  public Guid PbcAcceptanceReceiptId { get; set; }
  public long Version { get; set; }
  public long TaskRevision { get; set; }
  public int BindingGeneration { get; set; }
  public long EngagementGeneration { get; set; }
  public string ConditionDigest { get; set; } = string.Empty;
  /// <summary>Bounded canonical context, template, task and source metadata; never document bytes or an executable rule.</summary>
  public string BasisJson { get; set; } = string.Empty;
  public string BasisHash { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public Guid ActorUserId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid IdempotencyKey { get; set; }
  public string RequestDigest { get; set; } = string.Empty;
  public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// Append-only independent review of an exact proposal. A returned or revoked proposal cannot exclude the task.
/// A new basis requires a successor proposal, preserving every earlier actor, source pin and decision.
/// </summary>
public sealed class WorkflowTaskApplicabilityDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid WorkflowTaskBindingId { get; set; }
  public Guid ProposalId { get; set; }
  public Guid ProposerUserId { get; set; }
  public Guid? PreviousDecisionId { get; set; }
  public long Version { get; set; }
  public string Kind { get; set; } = string.Empty;
  public string BasisHash { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public Guid ActorUserId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid IdempotencyKey { get; set; }
  public string RequestDigest { get; set; } = string.Empty;
  public DateTimeOffset OccurredAt { get; set; }
}
