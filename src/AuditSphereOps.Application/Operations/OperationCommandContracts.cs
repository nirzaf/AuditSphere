namespace AuditSphereOps.Application.Operations;

public static class OperationCommandActions
{
  public const string Retry = "RETRY";
  public const string Cancel = "CANCEL";
  public const string LiftQuarantine = "LIFT_QUARANTINE";
  public const string ApproveRecoveryRestart = "APPROVE_RECOVERY_RESTART";
}

/// <summary>Exactly one action's fields; unrelated fields must be null.</summary>
public sealed record OperationCommandInput(
  string Action, Guid? OperationId = null, Guid? RecoverySessionId = null,
  string? Disposition = null, string? Findings = null);

public sealed record OperationCommandRequest(
  Guid RequestId, OperationCommandInput Input, string? ReviewBasis = null,
  string? ExpectedRequestHash = null, bool Reviewed = false);

public sealed record OperatorOperationSnapshot(
  Guid Id, Guid? ClientId, Guid? EngagementId, Guid TargetId, Guid CorrelationId, string Kind, string RequestDigest,
  string ExpectedRevision, string AttemptToken, string AttemptCount, string Status,
  bool IsReconciliation, bool HasLease, string? ErrorCode, string? CancellationDisposition,
  DateTimeOffset? NextAttemptAt, DateTimeOffset? CompletedAt, bool HasResultEvidence);

public sealed record OperatorRecoverySessionSnapshot(
  Guid Id, string ExternalEpoch, string RestorePoint, string ReconciliationScope, string Findings,
  DateTimeOffset CreatedAt, DateTimeOffset? ApprovedRestartAt, Guid? ApprovedByUserId);

public sealed record OperationCommandSnapshot(
  string OperatingMode, string DeploymentEpoch, string RecoveryEpoch, string PolicyGeneration,
  OperatorOperationSnapshot? Operation, OperatorRecoverySessionSnapshot? RecoverySession);

/// <summary>Server-projected eligible current session only; no approval affordance is inferred by the browser.</summary>
public sealed record OperationCommandState(
  OperationCommandSnapshot Before, string ReviewBasis, bool CanRetry, bool CanCancel,
  bool CanLiftQuarantine, bool CanApproveRecoveryRestart, IReadOnlyList<string> Blockers,
  IReadOnlyList<string> RetryBlockers,
  IReadOnlyList<string> CancelBlockers, IReadOnlyList<string> LiftQuarantineBlockers,
  IReadOnlyList<string> ApproveRecoveryRestartBlockers);

public sealed record OperationCommandPreview(
  Guid RequestId, string RequestHash, string ReviewBasis, OperationCommandInput CanonicalInput,
  OperationCommandSnapshot Before, bool CanProceed, IReadOnlyList<string> Blockers, string Summary);

/// <summary>Evidence identifies a local operator transition, never a successful provider effect.</summary>
public sealed record OperationCommandEventView(
  Guid Id, Guid ActorId, string ActorEpoch, string Action, Guid? OperationEventId,
  DateTimeOffset CreatedAt, OperationCommandSnapshot Before, OperationCommandSnapshot After);

public sealed record OperationCommandReceiptView(
  Guid Id, Guid ActorId, string ActorEpoch, Guid RequestId, string RequestHash, string Action,
  DateTimeOffset CreatedAt, OperationCommandPreview Preview, OperationCommandEventView CommandEvent);

public sealed record OperationCommandLookup
{
  public bool Found { get; }
  public OperationCommandReceiptView? Receipt { get; }

  public OperationCommandLookup(bool found, OperationCommandReceiptView? receipt)
  {
    if (found != (receipt is not null)) throw new ArgumentException("Found must exactly match receipt presence.");
    Found = found;
    Receipt = receipt;
  }
}
