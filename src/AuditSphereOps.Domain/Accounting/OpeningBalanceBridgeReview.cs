namespace AuditSphereOps.Domain.Accounting;

/// <summary>Retained review of exact opening evidence against a particular closed prior-period revision.</summary>
public sealed class OpeningBalanceBridgeReview
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid BridgeId { get; set; }
  public Guid PriorPeriodId { get; set; }
  public long PriorPeriodRevision { get; set; }
  public string EvidenceHash { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public Guid ReviewerId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
