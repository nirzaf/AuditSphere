namespace AuditSphereOps.Domain.Practice;

/// <summary>Append-only proof of a reviewed local planning mutation; contains no credentials or external permissions.</summary>
public sealed class ResourcePlanningReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid TargetUserId { get; set; }
  public Guid RequestId { get; set; }
  public string Kind { get; set; } = string.Empty;
  public string RequestHash { get; set; } = string.Empty;
  public string ReviewBasis { get; set; } = string.Empty;
  public string PreviewJson { get; set; } = string.Empty;
  public Guid? ResourceId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
