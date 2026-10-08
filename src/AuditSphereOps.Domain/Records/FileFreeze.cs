// Post-opinion file freeze (STE 4.2-01..03, OV-02): a scheduled freeze 60 days after the signed report, the
// requested versus observed external read-only state, authorized amendments, denied access attempts and a local
// document lock register. Local freezing is enforced by the application; SharePoint enforcement is recorded only
// when observed.
namespace AuditSphereOps.Domain.Records;

public static class FileFreezeStates
{
  public const string Scheduled = "SCHEDULED";
  public const string Frozen = "FROZEN";
  public const string AmendmentOpen = "AMENDMENT_OPEN";
}

public static class ExternalReadOnlyStates
{
  public const string NotRequested = "NOT_REQUESTED";
  public const string Requested = "REQUESTED";
  public const string Observed = "OBSERVED";
  /// <summary>No authorized live provider observed the read-only change; the application freeze still applies.</summary>
  public const string BlockedExternal = "BLOCKED_EXTERNAL";
}

public sealed class EngagementFileFreeze
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ReportDeliverableId { get; set; }
  public DateTimeOffset ReportSignedAt { get; set; }
  public DateTimeOffset DueAt { get; set; }
  public string State { get; set; } = FileFreezeStates.Scheduled;
  public long Revision { get; set; } = 1;
  public DateTimeOffset? FrozenAt { get; set; }
  public string ExternalReadOnly { get; set; } = ExternalReadOnlyStates.NotRequested;
  public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FileFreezeAmendment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid FreezeId { get; set; }
  public Guid EngagementId { get; set; }
  public string Reason { get; set; } = string.Empty;
  public Guid RequestedByUserId { get; set; }
  public DateTimeOffset RequestedAt { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? OpenedAt { get; set; }
  public Guid? ClosedByUserId { get; set; }
  public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>
/// Append-only record of a Partner's early (manual) compliance lock during the 60-day countdown (STE 4.4.3). It binds the
/// actor, the reason, the freeze revision and the exact archive-readiness digest the Partner reviewed.
/// </summary>
public sealed class FileFreezeEarlyLock
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FreezeId { get; set; }
  public Guid ReportDeliverableId { get; set; }
  public long FreezeRevision { get; set; }
  public Guid ReleaseId { get; set; }
  public string ArchiveReadinessDigest { get; set; } = string.Empty;
  public string Rationale { get; set; } = string.Empty;
  public Guid LockedByUserId { get; set; }
  public DateTimeOffset LockedAt { get; set; }
}

/// <summary>A write that the freeze refused, kept as audit-trail evidence.</summary>
public sealed class FrozenAccessAttempt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorUserId { get; set; }
  public string Action { get; set; } = string.Empty;
  public DateTimeOffset AttemptedAt { get; set; }
}

/// <summary>Exclusive edit lock on one engagement document; release is a stamp.</summary>
public sealed class DocumentLock
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid EngagementId { get; set; }
  public string DocumentKey { get; set; } = string.Empty;
  public Guid LockedByUserId { get; set; }
  public DateTimeOffset LockedAt { get; set; }
  public DateTimeOffset? ReleasedAt { get; set; }
  public Guid? ReleasedByUserId { get; set; }
}
