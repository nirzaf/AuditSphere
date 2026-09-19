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

public sealed class ProtectionAttestation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ArtifactId { get; set; }
  public string ArtifactHash { get; set; } = string.Empty;
  public string Binding { get; set; } = string.Empty;
  public string ProfileId { get; set; } = string.Empty;
  public long ProfileVersion { get; set; } = 1;
  public string ObservedState { get; set; } = "PROTECTED";
  public DateTimeOffset VerificationTime { get; set; }
  public string Verifier { get; set; } = string.Empty;
  public DateTimeOffset? ExpiryTime { get; set; }
  public string? RecheckRule { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

