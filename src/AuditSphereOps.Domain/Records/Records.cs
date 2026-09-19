// Records ledger: local enforcement state; Purview observed separately (§25).
namespace AuditSphereOps.Domain.Records;

public static class ArchiveStates
{
  public const string Issued = "ISSUED";
  public const string AssemblyInProgress = "ASSEMBLY_IN_PROGRESS";
  public const string ManifestBuilt = "MANIFEST_BUILT";
  public const string AssemblyReviewed = "ASSEMBLY_REVIEWED";
  public const string RecordsActionRequested = "RECORDS_ACTION_REQUESTED";
  public const string ProtectionObserved = "PROTECTION_OBSERVED";
  public const string ArchiveVerified = "ARCHIVE_VERIFIED";

  public static bool CanTransition(string current, string next) =>
    (current, next) switch
    {
      (Issued, AssemblyInProgress) => true,
      (AssemblyInProgress, ManifestBuilt) => true,
      (ManifestBuilt, AssemblyReviewed) => true,
      (AssemblyReviewed, RecordsActionRequested) => true,
      (RecordsActionRequested, ProtectionObserved) => true,
      (ProtectionObserved, ArchiveVerified) => true,
      _ => false
    };
}

public sealed class RecordsProfile
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string ProfileCode { get; set; } = string.Empty;
  public long Version { get; set; } = 1;
  public string RecordClass { get; set; } = string.Empty;
  public string Jurisdiction { get; set; } = string.Empty;
  public string ServiceRoute { get; set; } = string.Empty;
  public string RetentionTrigger { get; set; } = string.Empty;
  public int? RetentionDurationDays { get; set; }
  public string ProtectionMode { get; set; } = string.Empty;
  public string LabelId { get; set; } = string.Empty;
  public string LegalHoldBehavior { get; set; } = string.Empty;
  public string AmendmentRoute { get; set; } = string.Empty;
  public string DispositionOwner { get; set; } = string.Empty;
  public string BackupRequirements { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public bool Approved { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ArchiveManifest
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ArchiveId { get; set; }
  public long Version { get; set; } = 1;
  public Guid? PredecessorManifestId { get; set; }    // null on first build; set on every re-archive
  public Guid? SupersededByManifestId { get; set; }   // set on prior version when a new one is built
  public string Status { get; set; } = "BUILT";
  public string ManifestDigest { get; set; } = string.Empty;
  public int EntryCount { get; set; }
  public string CompletenessStatus { get; set; } = "COMPLETE";
  public string? CompletenessException { get; set; }
  public DateTimeOffset BuiltAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
  public Guid? ReviewedByUserId { get; set; }
}

public sealed class ArchiveManifestEntry
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ArchiveManifestId { get; set; }
  public int Ordinal { get; set; }
  public string EntryKind { get; set; } = string.Empty;
  public string SourceKind { get; set; } = string.Empty;
  public Guid? SourceId { get; set; }
  public string RelativeName { get; set; } = string.Empty;
  public string ContentHash { get; set; } = string.Empty;
  public long ByteCount { get; set; }
  public bool Required { get; set; } = true;
  public string MetadataJson { get; set; } = "{}";
}

public sealed class ArchiveStructuredExport
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ArchiveId { get; set; }
  public Guid ArchiveManifestId { get; set; }
  public long Version { get; set; } = 1;
  public string Schema { get; set; } = "records-export.v1";
  public string PayloadJson { get; set; } = string.Empty;
  public string ContentHash { get; set; } = string.Empty;
  public long ByteCount { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RecordsActionEvidence
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid RecordsActionId { get; set; }
  public Guid ArchiveId { get; set; }
  public Guid ArchiveManifestId { get; set; }
  public long Sequence { get; set; }
  public string EventKind { get; set; } = string.Empty;
  public string DesiredLabel { get; set; } = string.Empty;
  public string DesiredProtection { get; set; } = string.Empty;
  public string? ObservedLabel { get; set; }
  public string? ObservedProtection { get; set; }
  public string? ExternalReference { get; set; }
  public string? ObservedBy { get; set; }
  public string? Exception { get; set; }
  public Guid ActorUserId { get; set; }
  public DateTimeOffset OccurredAt { get; set; }
}

public sealed class RecordsAction
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ArchiveId { get; set; }
  public Guid ArchiveManifestId { get; set; }
  public string DesiredLabel { get; set; } = string.Empty;
  public string DesiredProtection { get; set; } = string.Empty;
  public string? ObservedLabel { get; set; }
  public string? ObservedProtection { get; set; }
  public string State { get; set; } = "REQUESTED";
  public string ExternalSystem { get; set; } = "Microsoft Purview";
  public string? ExternalReference { get; set; }
  public Guid RequestedByUserId { get; set; }
  public DateTimeOffset RequestedAt { get; set; }
  public DateTimeOffset? ObservedAt { get; set; }
  public string? ObservedBy { get; set; }
  public string? Exception { get; set; }
}

public sealed class LegalHold
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ArchiveId { get; set; }
  public string HoldReference { get; set; } = string.Empty;
  public string State { get; set; } = "REQUESTED";
  public string ExternalSystem { get; set; } = "Microsoft Purview";
  public string? ExternalReference { get; set; }
  public Guid RequestedByUserId { get; set; }
  public DateTimeOffset RequestedAt { get; set; }
  public DateTimeOffset? AppliedAt { get; set; }
  public DateTimeOffset? ObservedAt { get; set; }
  public DateTimeOffset? ReleasedAt { get; set; }
  public string? Notes { get; set; }
}

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
