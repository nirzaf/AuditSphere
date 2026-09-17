// Completion + records: releases, archives, durable operations outbox (§§24–25, 29).
namespace AuditSphereOps.Domain.Completion;

public sealed class Release
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PackageId { get; set; }
  public long PackageRevision { get; set; }
  public string ManifestDigest { get; set; } = string.Empty;
  public bool ExternalCheckpoint { get; set; }
  public DateTimeOffset ReleasedAt { get; set; }
  public Guid ReleasedByUserId { get; set; }
}

public sealed class Archive
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string ProfileId { get; set; } = string.Empty;
  public string Status { get; set; } = "Pending";
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class DurableOperation
{
  public Guid Id { get; set; }
  public string OperationKind { get; set; } = string.Empty;
  public Guid FirmId { get; set; }
  public Guid? ClientId { get; set; }
  public Guid? EngagementId { get; set; }
  public string PayloadJson { get; set; } = "{}";
  public string IdempotencyKey { get; set; } = string.Empty;
  public string RequestDigest { get; set; } = string.Empty;
  public string Status { get; set; } = "Queued"; // Queued|Leased|Succeeded|Failed|Quarantined
  public int Attempt { get; set; }
  public DateTimeOffset? LeaseExpiresAt { get; set; }
  public string? LeaseOwner { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
