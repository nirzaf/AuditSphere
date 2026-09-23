using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Operations;

public sealed record OperationRecoveryRequest(Guid OperationId);
public sealed record OperationCancellationRequest(Guid OperationId, string Disposition);
public sealed record RecoverySessionRequest(string RestorePoint, long ExternalEpoch,
  string ReconciliationScope, string Findings);
public sealed record RecoveryRestartRequest(Guid SessionId, string Findings);

/// <summary>Operator-visible operation projections for the recovery screen. Payload bytes,
/// request bytes and lease owner identities are never surfaced; only classification codes.</summary>
public sealed record OperationProjection(
  Guid Id, string Kind, string Status, Guid TargetId, long ExpectedRevision, int AttemptCount,
  DateTimeOffset? NextAttemptAt, DateTimeOffset? LeaseExpiresAt, string? ErrorCode,
  string? CancellationDisposition, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, bool HasResultEvidence);

/// <summary>
/// Authorized operator recovery (spec 43.2 operations route). It re-arms retryable terminal
/// or uncertain operations inside the firm guard and records an append-only recovery event.
/// It never rewrites completed results, request identity or attempt history.
/// </summary>
public static class OperationRecoveryService
{
  public static readonly OperationState[] RetryableStates =
  [
    OperationState.PROVIDER_BLOCKED, OperationState.DEAD_LETTER,
    OperationState.AUTHORIZATION_BLOCKED, OperationState.RESULT_UNCERTAIN
  ];

  /// <summary>Bounded, redacted operation list for the operations screen.</summary>
  public static async Task<CommandResult<IReadOnlyList<OperationProjection>>> ListAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<OperationProjection>>.Fail(auth.ErrorCode!, auth.Message!);
    var operations = await db.DurableOperations.AsNoTracking()
      .Where(o => o.FirmId == actor.FirmId)
      .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
      .Take(50)
      .Select(o => new OperationProjection(
        o.Id, o.OperationKind, o.Status.ToString(), o.TargetId, o.ExpectedRevision, o.AttemptCount,
        o.NextAttemptAt, o.LeaseExpiresAt, o.ErrorCode, o.CancellationDisposition, o.CreatedAt, o.CompletedAt,
        o.ResultIdentity != null && o.ResultDigest != null))
      .ToListAsync(ct);
    return CommandResult<IReadOnlyList<OperationProjection>>.Ok(operations);
  }

  /// <summary>
  /// Re-arms a retryable operation: the mutable projection returns to RETRY_WAIT with a fresh
  /// attempt budget under the explicit operator authorization, and an append-only event records
  /// the operator identity. Completed results, request bytes and digests are untouched.
  /// </summary>
  public static async Task<CommandResult> RetryAsync(
    IAuditSphereDbContext db, ActorContext actor, OperationRecoveryRequest input,
    CancellationToken ct = default)
  {
    if (input.OperationId == Guid.Empty)
      return CommandResult.Fail(ErrorCodes.Operations.Invalid, "An operation id is required.");
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded)
      return auth;

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.Database.ExecuteSqlRawAsync(
      "SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '15s'", ct);
    var safety = await db.FirmSafetyStates.FromSqlInterpolated($"""
      SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR SHARE
      """).AsNoTracking().ToListAsync(ct);
    if (safety.Count != 1 || safety[0].OperatingMode != "LOCAL_ONLY")
    {
      await tx.RollbackAsync(ct);
      return CommandResult.Fail(ErrorCodes.Operations.Quarantined,
        "Recovery requires the firm to be in the local-only operating mode.");
    }
    var locked = await db.DurableOperations.FromSqlInterpolated($"""
      SELECT * FROM durable_operations WHERE id = {input.OperationId} AND firm_id = {actor.FirmId} FOR UPDATE
      """).ToListAsync(ct);
    if (locked.Count == 0)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var op = locked[0];
    if (!RetryableStates.Contains(op.Status))
      return CommandResult.Fail(ErrorCodes.Operations.State,
        "Only blocked, dead-lettered, authorization-blocked or uncertain operations can be re-armed.");
    if (op.LeaseOwner is not null)
      return CommandResult.Fail(ErrorCodes.Operations.Lease, "The operation still holds an active lease.");

    var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE durable_operations SET status = 'RETRY_WAIT', error_code = NULL,
        next_attempt_at = statement_timestamp() + make_interval(secs => 5),
        attempt_count = 0, lease_owner = NULL, lease_expires_at = NULL
      WHERE id = {op.Id} AND firm_id = {actor.FirmId}
        AND attempt_token = {op.AttemptToken} AND status = {op.Status.ToString()}
      """, ct);
    if (count != 1)
      return CommandResult.Fail(ErrorCodes.Operations.Conflict, "The operation changed while the recovery command ran.");
    db.OperationEvents.Add(new OperationEvent
    {
      Id = Guid.CreateVersion7(), OperationId = op.Id, Token = op.AttemptToken,
      Kind = "operation.recovery-retry.v1", Executor = actor.UserId.ToString("D"),
      OccurredAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>
  /// Cancels only queued work that has not acquired a worker lease. Active work is
  /// deliberately left to lease/reconciliation handling because its effect boundary
  /// cannot be proven safe from an operator request alone.
  /// </summary>
  public static async Task<CommandResult> CancelAsync(
    IAuditSphereDbContext db, ActorContext actor, OperationCancellationRequest input,
    CancellationToken ct = default)
  {
    if (input.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(input.Disposition) || input.Disposition.Trim().Length > 2000)
      return CommandResult.Fail(ErrorCodes.Operations.Invalid, "An operation id and bounded cancellation disposition are required.");
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded)
      return auth;

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.Database.ExecuteSqlRawAsync(
      "SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '15s'", ct);
    var locked = await db.DurableOperations.FromSqlInterpolated($"""
      SELECT * FROM durable_operations WHERE id = {input.OperationId} AND firm_id = {actor.FirmId} FOR UPDATE
      """).ToListAsync(ct);
    if (locked.Count == 0)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var op = locked[0];
    if (op.Status == OperationState.CANCELLED_WITH_DISPOSITION)
      return CommandResult.Ok();
    if (op.Status is not (OperationState.PENDING or OperationState.RETRY_WAIT) || op.LeaseOwner is not null)
      return CommandResult.Fail(ErrorCodes.Operations.State,
        "Only queued work without an active lease can be cancelled safely.");

    var disposition = input.Disposition.Trim();
    var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE durable_operations SET status = 'CANCELLED_WITH_DISPOSITION', error_code = 'operator-cancelled',
        cancellation_disposition = {disposition}, next_attempt_at = statement_timestamp()
      WHERE id = {op.Id} AND firm_id = {actor.FirmId} AND attempt_token = {op.AttemptToken}
        AND status = {op.Status.ToString()} AND lease_owner IS NULL AND lease_expires_at IS NULL
      """, ct);
    if (count != 1)
      return CommandResult.Fail(ErrorCodes.Operations.Conflict, "The operation changed while cancellation was requested.");
    db.OperationEvents.Add(new OperationEvent
    {
      Id = Guid.CreateVersion7(), OperationId = op.Id, Token = op.AttemptToken,
      Kind = "operation.cancelled.v1", Executor = actor.UserId.ToString("D"),
      OccurredAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>Queries current firm operating mode (LOCAL_ONLY vs RECOVERY_QUARANTINE) for UI display.</summary>
  public static async Task<CommandResult<string>> GetFirmOperatingModeAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded)
      return CommandResult<string>.Fail(auth.ErrorCode!, auth.Message!);

    var mode = await db.FirmSafetyStates.AsNoTracking()
      .Where(x => x.Id == actor.FirmId)
      .Select(x => x.OperatingMode)
      .SingleOrDefaultAsync(ct);

    return mode is not null
      ? CommandResult<string>.Ok(mode)
      : CommandResult<string>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  }

  /// <summary>Places a restored firm in quarantine and records the reconciliation scope.</summary>
  public static async Task<CommandResult<Guid>> BeginRecoverySessionAsync(
    IAuditSphereDbContext db, ActorContext actor, RecoverySessionRequest input,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(input.RestorePoint) || input.ExternalEpoch < 1 ||
        string.IsNullOrWhiteSpace(input.ReconciliationScope) || string.IsNullOrWhiteSpace(input.Findings))
      return CommandResult<Guid>.Fail(ErrorCodes.Operations.RecoveryInvalid, "Restore point, epoch, scope and findings are required.");
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var safety = await db.FirmSafetyStates.FromSqlInterpolated($"""
      SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE
      """).ToListAsync(ct);
    if (safety.Count != 1)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var session = new RecoverySession
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      RestorePoint = input.RestorePoint.Trim(), ExternalEpoch = input.ExternalEpoch,
      ReconciliationScope = input.ReconciliationScope.Trim(), Findings = input.Findings.Trim(),
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.RecoverySessions.Add(session);
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE firm_safety_states
      SET operating_mode = 'RECOVERY_QUARANTINE', recovery_epoch = {input.ExternalEpoch}
      WHERE id = {actor.FirmId}
      """, ct);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(session.Id);
  }

  /// <summary>Authorizes restart only after persisted reconciliation findings are updated.</summary>
  public static async Task<CommandResult> ApproveRecoveryRestartAsync(
    IAuditSphereDbContext db, ActorContext actor, RecoveryRestartRequest input,
    CancellationToken ct = default)
  {
    if (input.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(input.Findings))
      return CommandResult.Fail(ErrorCodes.Operations.RecoveryInvalid, "Session and reconciliation findings are required.");
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded)
      return auth;

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var session = await db.RecoverySessions.FromSqlInterpolated($"""
      SELECT * FROM recovery_sessions WHERE firm_id = {actor.FirmId} AND id = {input.SessionId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (session is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (session.ApprovedRestartAt is not null)
      return CommandResult.Ok();

    session.Findings = input.Findings.Trim();
    session.ApprovedRestartAt = DateTimeOffset.UtcNow;
    session.ApprovedByUserId = actor.UserId;
    var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE firm_safety_states
      SET operating_mode = 'LOCAL_ONLY', deployment_epoch = deployment_epoch + 1
      WHERE id = {actor.FirmId} AND operating_mode = 'RECOVERY_QUARANTINE'
      """, ct);
    if (count != 1)
      return CommandResult.Fail(ErrorCodes.Operations.Conflict, "The firm is not awaiting recovery restart approval.");
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>
  /// Lifts recovery quarantine for the firm under explicit administrator authorization.
  /// Transitions operating mode from RECOVERY_QUARANTINE to LOCAL_ONLY and increments deployment epoch.
  /// </summary>
  public static async Task<CommandResult> LiftQuarantineAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded)
      return auth;

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.Database.ExecuteSqlRawAsync(
      "SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '15s'", ct);

    var safety = await db.FirmSafetyStates.FromSqlInterpolated($"""
      SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE
      """).ToListAsync(ct);
    if (safety.Count != 1)
    {
      await tx.RollbackAsync(ct);
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    }

    if (safety[0].OperatingMode == "LOCAL_ONLY")
    {
      await tx.RollbackAsync(ct);
      return CommandResult.Ok();
    }

    var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE firm_safety_states
      SET operating_mode = 'LOCAL_ONLY', deployment_epoch = deployment_epoch + 1
      WHERE id = {actor.FirmId} AND operating_mode = 'RECOVERY_QUARANTINE'
      """, ct);
    if (count != 1)
    {
      await tx.RollbackAsync(ct);
      return CommandResult.Fail(ErrorCodes.Operations.Conflict, "Firm safety state changed while lifting quarantine.");
    }

    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"], RequireFirmWide: true), ct);
}
