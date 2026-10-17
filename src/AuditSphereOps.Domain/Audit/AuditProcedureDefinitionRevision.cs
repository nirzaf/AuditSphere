namespace AuditSphereOps.Domain.Audit;

/// <summary>
/// Immutable effective instructions for one controlled audit procedure. The original source is
/// resolved through ProgramProcedureId; legacy rows retain only an unattributed effective snapshot.
/// </summary>
public sealed class AuditProcedureDefinitionRevision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid AuditProcedureId { get; set; }
  public int Revision { get; set; }
  public Guid? ProgramProcedureId { get; set; }
  public string EffectiveTitle { get; set; } = string.Empty;
  public string EffectiveWording { get; set; } = string.Empty;
  public string? Rationale { get; set; }
  public Guid CommandId { get; set; }
  public string RequestDigest { get; set; } = string.Empty;
  public Guid? PreviousRevisionId { get; set; }
  public Guid? ActorUserId { get; set; }
  public DateTimeOffset? AuthoredAt { get; set; }
  public bool IsLegacySnapshot { get; set; }
  public DateTimeOffset RecordedAt { get; set; }
}
