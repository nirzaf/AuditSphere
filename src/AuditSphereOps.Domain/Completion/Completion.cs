// Completion + records: releases, archives, durable operations outbox (§§24–25, 29).
namespace AuditSphereOps.Domain.Completion;

public static class ReleaseStates
{
  public const string Ready = "READY";
  public const string Issued = "ISSUED";
}

public static class ReleaseTargetKinds
{
  public const string Workpaper = "WORKPAPER";
  public const string FinancialPackage = "FINANCIAL_PACKAGE";
}

public sealed class ReleaseCandidate
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string TargetKind { get; set; } = "WORKPAPER";
  public Guid TargetId { get; set; }
  public long TargetRevision { get; set; } = 1;
  public long Revision { get; set; } = 1;
  public long InputGeneration { get; set; } = 1;
  public long PolicyGeneration { get; set; } = 1;
  public Guid ApprovalId { get; set; }
  public string ManifestDigest { get; set; } = string.Empty;
  public string Status { get; set; } = ReleaseStates.Ready;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Release
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ReleaseCandidateId { get; set; }
  public Guid PackageId { get; set; }
  public long PackageRevision { get; set; }
  public string ManifestDigest { get; set; } = string.Empty;
  public string AuthorizedReleaseKey { get; set; } = string.Empty;
  public Guid CheckpointId { get; set; }
  public DateTimeOffset ReleasedAt { get; set; }
  public Guid ReleasedByUserId { get; set; }
}

public sealed class ReleaseCheckpoint
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ReleaseCandidateId { get; set; }
  public long CandidateRevision { get; set; }
  public string AuthorizedReleaseKey { get; set; } = string.Empty;
  public string ManifestDigest { get; set; } = string.Empty;
  public string StoredReference { get; set; } = string.Empty;
  public string ReadBackDigest { get; set; } = string.Empty;
  public string VerifiedStatus { get; set; } = "PENDING";
  public DateTimeOffset? VerifiedAt { get; set; }
  public string Verifier { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SignatureLineage
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid CandidateId { get; set; }
  public string PreSignArtifactHash { get; set; } = string.Empty;
  public string SignedArtifactHash { get; set; } = string.Empty;
  public string SigningMethod { get; set; } = string.Empty;
  public string RequestIdentity { get; set; } = string.Empty;
  public string VerificationOutcome { get; set; } = "VERIFIED";
  public string Verifier { get; set; } = string.Empty;
  public DateTimeOffset VerifiedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Archive
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string ProfileId { get; set; } = string.Empty;
  public long ProfileVersion { get; set; } = 1;
  public string? ObservedProtectionState { get; set; }
  public DateTimeOffset? ObservedProtectionAt { get; set; }
  public string Status { get; set; } = AuditSphereOps.Domain.Records.ArchiveStates.Issued;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EqrCase
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid EqrPartnerUserId { get; set; }
  public string Status { get; set; } = "PENDING"; // PENDING|IN_PROGRESS|CONCURRED|CHANGES_REQUESTED
  public DateOnly? ConcurrenceDate { get; set; }
  public bool FindingsDiscussed { get; set; }
  public string? Notes { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class WrittenRepresentation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string Code { get; set; } = string.Empty; // e.g. R-01
  public string Title { get; set; } = string.Empty;
  public string Narrative { get; set; } = string.Empty;
  public bool Obtained { get; set; }
  public DateTimeOffset? ObtainedAt { get; set; }
  public string? SignatoryName { get; set; }
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
  public string? CancellationDisposition { get; set; }
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
public enum OperationAuthority { LOCAL_VALIDATION, SIMULATION, LIVE_PROVIDER }

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
  public long RecoveryEpoch { get; set; }
  public long PolicyGeneration { get; set; } = 1;
}

public sealed class RecoverySession
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string RestorePoint { get; set; } = string.Empty;
  public long ExternalEpoch { get; set; }
  public string ReconciliationScope { get; set; } = string.Empty;
  public string Findings { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ApprovedRestartAt { get; set; }
  public Guid? ApprovedByUserId { get; set; }
}

public sealed class ClientSafetyState
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public long InputGeneration { get; set; } = 1;
}
