namespace AuditSphereOps.Domain.Practice;

/// <summary>Immutable evidence of a reviewed local staffing change; Microsoft membership is reconciled separately.</summary>
public sealed class StaffingChange
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid AssignmentId { get; set; }
  public Guid TargetUserId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string Action { get; set; } = string.Empty;
  public string RequestHash { get; set; } = string.Empty;
  public string ReviewBasis { get; set; } = string.Empty;
  public string PreviewJson { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
