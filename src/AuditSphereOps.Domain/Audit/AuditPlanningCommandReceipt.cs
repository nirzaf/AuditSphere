namespace AuditSphereOps.Domain.Audit;

/// <summary>Immutable evidence of one actor's exact reviewed planning intent and committed result.</summary>
public sealed class AuditPlanningCommandReceipt
{
  public const int MaximumInputJsonLength = 120000;
  public const int MaximumSnapshotJsonLength = 1000000;
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = "";
  public string ReviewBasis { get; set; } = "";
  public string Action { get; set; } = "";
  public Guid ResultId { get; set; }
  public string InputJson { get; set; } = "";
  public string PreviewJson { get; set; } = "";
  public string ResultJson { get; set; } = "";
  public DateTimeOffset CreatedAt { get; set; }
}
