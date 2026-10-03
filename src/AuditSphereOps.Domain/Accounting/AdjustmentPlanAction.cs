namespace AuditSphereOps.Domain.Accounting;

/// <summary>Retained reviewed intent and result for a native plan command. A request
/// belongs to one firm and actor; corrections create a replacement plan.</summary>
public sealed class AdjustmentPlanAction
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid DatasetId { get; set; }
  public Guid PlanId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public string ReviewBasis { get; set; } = string.Empty;
  public string Action { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public string BeforeJson { get; set; } = string.Empty;
  public string AfterJson { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
