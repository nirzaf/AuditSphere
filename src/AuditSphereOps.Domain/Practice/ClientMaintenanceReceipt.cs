namespace AuditSphereOps.Domain.Practice;

/// <summary>Append-only outcome for an actor-reviewed client profile or contact update.</summary>
public sealed class ClientMaintenanceReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid? ContactId { get; set; }
  public string OperationKind { get; set; } = string.Empty;
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public string ReviewBasis { get; set; } = string.Empty;
  public long PreviousGeneration { get; set; }
  public long ResultGeneration { get; set; }
  public string BeforeJson { get; set; } = string.Empty;
  public string AfterJson { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
