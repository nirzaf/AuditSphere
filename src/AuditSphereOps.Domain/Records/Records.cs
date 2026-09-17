// Records ledger: local enforcement state; Purview observed separately (§25).
namespace AuditSphereOps.Domain.Records;

public sealed class RecordState
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ArtifactId { get; set; }
  public string ArtifactKind { get; set; } = string.Empty;
  public string LocalState { get; set; } = "Active"; // Active|Locked|Archived
  public string? ObservedLabel { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}
