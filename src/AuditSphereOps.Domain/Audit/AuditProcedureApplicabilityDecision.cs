namespace AuditSphereOps.Domain.Audit;

/// <summary>Append-only applicability proposal and independent Partner disposition.</summary>
public sealed class AuditProcedureApplicabilityDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid AuditProcedureId { get; set; }
  public int Revision { get; set; }
  public int DefinitionRevision { get; set; }
  public long InputGeneration { get; set; }
  public string Action { get; set; } = string.Empty;
  public string? Rationale { get; set; }
  public Guid? ProposalDecisionId { get; set; }
  public Guid CommandId { get; set; }
  public string RequestDigest { get; set; } = string.Empty;
  public Guid? PreviousDecisionId { get; set; }
  public Guid ActorUserId { get; set; }
  public long ActorEpoch { get; set; }
  public DateTimeOffset OccurredAt { get; set; }
}
