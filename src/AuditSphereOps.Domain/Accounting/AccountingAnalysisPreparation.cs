namespace AuditSphereOps.Domain.Accounting;

/// <summary>Immutable actor-owned receipt for reviewed native analytical preparation.</summary>
public sealed class AccountingAnalysisPreparation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public string ReviewBasis { get; set; } = string.Empty;
  public Guid EvidenceId { get; set; }
  public string InputJson { get; set; } = string.Empty;
  public string ContextJson { get; set; } = string.Empty;
  public string ResultJson { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
