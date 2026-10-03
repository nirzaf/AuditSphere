namespace AuditSphereOps.Domain.Accounting;

/// <summary>Immutable native preparation intent and exact source/result lineage.
/// Preparation is separate from independent human review or client-book posting.</summary>
public sealed class ValuationPreparation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ReconciliationId { get; set; }
  public string Kind { get; set; } = "";
  public Guid EvidenceId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = "";
  public string ReviewBasis { get; set; } = "";
  public string InputJson { get; set; } = "";
  public string ContextJson { get; set; } = "";
  public string ResultJson { get; set; } = "";
  public string Reason { get; set; } = "";
  public string EvidenceReference { get; set; } = "";
  public DateTimeOffset CreatedAt { get; set; }
}
