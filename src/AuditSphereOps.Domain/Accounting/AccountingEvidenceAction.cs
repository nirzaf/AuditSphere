namespace AuditSphereOps.Domain.Accounting;

/// <summary>Immutable evidence for an explicitly reviewed native accounting action.
/// Earlier decisions are not reconstructed from mutable legacy metadata.</summary>
public sealed class AccountingEvidenceAction
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid EvidenceId { get; set; }
  public string EvidenceKind { get; set; } = "";
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = "";
  public string ReviewBasis { get; set; } = "";
  public string Action { get; set; } = "";
  public Guid? ResultId { get; set; }
  public Guid? LinkId { get; set; }
  public string Decision { get; set; } = "";
  public string Reason { get; set; } = "";
  public string EvidenceReference { get; set; } = "";
  public string BeforeJson { get; set; } = "";
  public string AfterJson { get; set; } = "";
  public DateTimeOffset CreatedAt { get; set; }
}
