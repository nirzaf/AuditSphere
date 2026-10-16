namespace AuditSphereOps.Domain.Audit;

/// <summary>Database-created original publication evidence for one workpaper command.</summary>
public sealed class WorkpaperCommandEvent
{
  public const int MaximumSnapshotLength = 3000000;
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid WorkpaperId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = "";
  public string Action { get; set; } = "";
  public string BeforeJson { get; set; } = "";
  public string AfterJson { get; set; } = "";
  public DateTimeOffset CreatedAt { get; set; }
}
/// <summary>Immutable actor-owned acknowledgement. Save IDs remain the original request IDs.</summary>
public sealed class WorkpaperCommandReceipt
{
  public const int MaximumInputLength = 800000;
  public const int MaximumPreviewLength = 4000000;
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid WorkpaperId { get; set; }
  public Guid EventId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = "";
  public string Action { get; set; } = "";
  public string InputJson { get; set; } = "";
  public string? PreviewJson { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
