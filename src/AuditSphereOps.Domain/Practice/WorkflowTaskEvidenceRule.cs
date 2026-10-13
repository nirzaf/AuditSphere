namespace AuditSphereOps.Domain.Practice;

public static class WorkflowEvidenceKinds
{
  public const string CurrentMateriality = "CURRENT_MATERIALITY_APPROVED";
  public const string AcceptedPbc = "PBC_ACCEPTED_OR_CLOSED";
  public const string ReviewedProcedure = "CURRENT_PROCEDURE_REVIEWED";
  public static readonly string[] All = [CurrentMateriality, AcceptedPbc, ReviewedProcedure];
}

public static class WorkflowEvidenceRuleStates
{
  public const string Required = "REQUIRED";
  public const string Withdrawn = "WITHDRAWN";
}

/// <summary>
/// Append-only revisions of an operational task's reference to authoritative evidence. Required/withdrawn is the
/// manager's coordination policy, never an editable evidence-satisfied flag. Professional state stays at its source.
/// </summary>
public sealed class WorkflowTaskEvidenceRule
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid WorkflowTaskBindingId { get; set; }
  public Guid RuleKey { get; set; }
  public string DependencyType { get; set; } = WorkflowDependencyTypes.DomainEvidenceSatisfied;
  public string Kind { get; set; } = string.Empty;
  public Guid SourceId { get; set; }
  public Guid? PbcRequestId { get; set; }
  public Guid? AuditProcedureId { get; set; }
  public int Version { get; set; }
  public string State { get; set; } = WorkflowEvidenceRuleStates.Required;
  public long TaskRevision { get; set; }
  public Guid ActorUserId { get; set; }
  public Guid IdempotencyKey { get; set; }
  public string RequestDigest { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public DateTimeOffset OccurredAt { get; set; }
}
