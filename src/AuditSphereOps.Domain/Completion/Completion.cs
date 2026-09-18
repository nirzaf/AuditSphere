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
  public OperationState Status { get; set; } = OperationState.PENDING;
  public long AttemptToken { get; set; }
  public int AttemptCount { get; set; }
  public int SchemaVersion { get; set; } = 1;
  public string ExecutionGroup { get; set; } = "general";
  public OperationMode ExecutionMode { get; set; } = OperationMode.LOCAL;
  public OperationAuthority AuthorityMode { get; set; } = OperationAuthority.LOCAL_VALIDATION;
  public Guid TargetId { get; set; }
  public long ExpectedRevision { get; set; }
  public Guid CorrelationId { get; set; }
  public Guid? OriginatorId { get; set; }
  public byte[] RequestBytes { get; set; } = [];
  public DateTimeOffset NextAttemptAt { get; set; }
  public DateTimeOffset? LeaseExpiresAt { get; set; }
  public string? LeaseOwner { get; set; }
  public long ClaimedEpoch { get; set; }
  public bool IsReconciliation { get; set; }
  public string? ResultIdentity { get; set; }
  public string? ResultDigest { get; set; }
  public string? ErrorCode { get; set; }
  public DateTimeOffset? CompletedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public enum OperationState
{
  PENDING, CLAIMED, REMOTE_STARTED, VERIFYING, COMPLETED, RETRY_WAIT,
  AUTHORIZATION_BLOCKED, PROVIDER_BLOCKED, RESULT_UNCERTAIN, DEAD_LETTER,
  CANCEL_REQUESTED, CANCELLED_WITH_DISPOSITION
}

public enum OperationMode { LOCAL, SIMULATED, LIVE }
public enum OperationAuthority { LOCAL_VALIDATION, SIMULATION }

public sealed class OperationAttempt
{
  public Guid Id { get; set; }
  public Guid OperationId { get; set; }
  public long Token { get; set; }
  public string Owner { get; set; } = string.Empty;
  public bool Reconciliation { get; set; }
  public DateTimeOffset ClaimedAt { get; set; }
}

// Append-only stage evidence is distinct from the mutable operation projection.
public sealed class OperationEvent
{
  public Guid Id { get; set; }
  public Guid OperationId { get; set; }
  public long Token { get; set; }
  public string Kind { get; set; } = string.Empty;
  public string Executor { get; set; } = string.Empty;
  public DateTimeOffset OccurredAt { get; set; }
}

public sealed class FirmSafetyState
{
  public Guid Id { get; set; }
  public string OperatingMode { get; set; } = "LOCAL_ONLY";
  public long DeploymentEpoch { get; set; } = 1;
  public long PolicyGeneration { get; set; } = 1;
}

public sealed class ClientSafetyState
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public long InputGeneration { get; set; } = 1;
}
