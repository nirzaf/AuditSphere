namespace AuditSphereOps.Domain.Reviews;

/// <summary>Append-only amendment evidence for both reviewed and compatibility review point commands.</summary>
public sealed class ReviewPointDispositionEvent
{
  public const int MaximumSnapshotLength = 1000000;
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ReviewPointId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public string Action { get; set; } = "";
  public long BeforeRevision { get; set; }
  public long AfterRevision { get; set; }
  public string BeforeJson { get; set; } = "";
  public string AfterJson { get; set; } = "";
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One actor's exact reviewed request, tied to a real immutable amendment, never to matching current history.</summary>
public sealed class ReviewPointCommandReceipt
{
  public const int MaximumInputLength = 1000;
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ReviewPointId { get; set; }
  public Guid DispositionEventId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = "";
  public string ReviewBasis { get; set; } = "";
  public string Action { get; set; } = "";
  public string InputJson { get; set; } = "";
  public string PreviewJson { get; set; } = "";
  public DateTimeOffset CreatedAt { get; set; }
}
