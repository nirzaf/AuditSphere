// Documents: references, snapshots, version-bound approvals (§§11, 27.2, 42.5).
namespace AuditSphereOps.Domain.Documents;

public sealed class RepositoryBinding
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string TenantId { get; set; } = string.Empty;
  public string SiteId { get; set; } = string.Empty;
  public string DriveId { get; set; } = string.Empty;
  public string RootFolderId { get; set; } = string.Empty;
  public string Classification { get; set; } = string.Empty;
  public string DesiredAccess { get; set; } = string.Empty;
  public string ObservedAccess { get; set; } = string.Empty;
  public string CapabilityProfile { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SyncCursor
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid RepositoryBindingId { get; set; }
  public string Cursor { get; set; } = string.Empty;
  public long Generation { get; set; }
  public DateTimeOffset? LastSyncAt { get; set; }
}

public sealed class IntegrationCapability
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid RepositoryBindingId { get; set; }
  public string HealthStatus { get; set; } = string.Empty;
  public string TestedPermissions { get; set; } = string.Empty;
  public DateTimeOffset? TestedAt { get; set; }
}

public sealed class DocumentReference
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid RepositoryBindingId { get; set; }
  public string Provider { get; set; } = "SharePoint";
  public string DriveId { get; set; } = string.Empty;
  public string ItemId { get; set; } = string.Empty;
  public string Path { get; set; } = string.Empty;
  public string Purpose { get; set; } = string.Empty; // Workpaper|Evidence|Report|Package
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class DocumentSnapshot
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid DocumentReferenceId { get; set; }
  public string DriveId { get; set; } = string.Empty;
  public string ItemId { get; set; } = string.Empty;
  public string VersionId { get; set; } = string.Empty;
  public string Sha256Hex { get; set; } = string.Empty;
  public long ByteCount { get; set; }
  public string CapturedBy { get; set; } = string.Empty;
  public DateTimeOffset CapturedAt { get; set; }
}

public sealed class SourceReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string SourceType { get; set; } = "PBC_UPLOAD"; // PBC_UPLOAD|DIRECT_FEED|CSV_IMPORT
  public string ReceiptToken { get; set; } = string.Empty;
  public string Sha256Digest { get; set; } = string.Empty;
  public long ByteCount { get; set; }
  public string OriginalFileName { get; set; } = string.Empty;
  public DateTimeOffset AcquiredAt { get; set; }
  public Guid AcquiredByUserId { get; set; }
}

public sealed class EvidenceLink
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid SourceReceiptId { get; set; }
  public Guid? WorkpaperId { get; set; }
  public string Purpose { get; set; } = string.Empty;
  public string Assertion { get; set; } = string.Empty;
  public string RelevanceReliabilityAssessment { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
