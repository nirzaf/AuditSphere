namespace AuditSphereOps.Domain.Practice;

/// <summary>Append-only evidence of an attributable time revision and its exact retained outcome.</summary>
public sealed class TimeEntryChangeReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public string? ActorName { get; set; }
  public Guid RequestId { get; set; }
  public string Kind { get; set; } = string.Empty;
  public Guid TimeEntryId { get; set; }
  public Guid ResultTimeEntryId { get; set; }
  public long SourceRevision { get; set; }
  public long ResultRevision { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public string BeforeJson { get; set; } = string.Empty;
  public string SourceAfterJson { get; set; } = string.Empty;
  public string ResultJson { get; set; } = string.Empty;
  public string? Reason { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
