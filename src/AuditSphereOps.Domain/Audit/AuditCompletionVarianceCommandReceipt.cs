namespace AuditSphereOps.Domain.Audit;

/// <summary>Immutable actor-owned recovery evidence for one exact variance preparation or review command.</summary>
public sealed class AuditCompletionVarianceCommandReceipt
{
  public const int MaximumInputJsonLength = 120000;
  public const int MaximumSnapshotJsonLength = 1000000;

  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid VarianceInvestigationId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public string ReviewBasis { get; set; } = string.Empty;
  public string Action { get; set; } = string.Empty;
  public string InputJson { get; set; } = string.Empty;
  public string PreviewJson { get; set; } = string.Empty;
  public string ResultJson { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
