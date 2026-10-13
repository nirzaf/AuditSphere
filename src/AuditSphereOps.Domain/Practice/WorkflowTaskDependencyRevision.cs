namespace AuditSphereOps.Domain.Practice;

/// <summary>Append-only amendments overlay, but never rewrite, the pinned template graph.</summary>
public sealed class WorkflowTaskDependencyRevision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PredecessorBindingId { get; set; }
  public Guid SuccessorBindingId { get; set; }
  public int Version { get; set; }
  public bool Required { get; set; }
  public long TaskRevision { get; set; }
  public long PredecessorRevision { get; set; }
  public string Reason { get; set; } = string.Empty;
  public Guid ActorUserId { get; set; }
  public Guid IdempotencyKey { get; set; }
  public string RequestDigest { get; set; } = string.Empty;
  public DateTimeOffset OccurredAt { get; set; }
}
