namespace AuditSphereOps.Domain.Engagements;

/// <summary>Immutable evidence of reviewed blocked-shell creation. No professional activation or access grant.</summary>
public sealed class EngagementCreation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = "";
  public string ReviewBasis { get; set; } = "";
  public long ClientGeneration { get; set; }
  public long EngagementGeneration { get; set; }
  public string InputJson { get; set; } = "";
  public DateTimeOffset CreatedAt { get; set; }
}
