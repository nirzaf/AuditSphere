using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AuditSphereOps.Application.Operations;

/// <summary>Exact reviewed operator acknowledgements. Queue state is never substituted for a receipt.</summary>
public static class OperationCommandWorkspace
{
  private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
  private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum &&
    !value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));

  private static OperationCommandInput? Canonical(OperationCommandInput? input) => input?.Action switch
  {
    OperationCommandActions.Retry when input.OperationId is { } id && id != Guid.Empty && input.RecoverySessionId is null && input.Disposition is null && input.Findings is null => input,
    OperationCommandActions.Cancel when input.OperationId is { } id && id != Guid.Empty && input.RecoverySessionId is null && input.Findings is null && Text(input.Disposition, 2000) => input with { Disposition = input.Disposition!.Trim() },
    OperationCommandActions.LiftQuarantine when input.OperationId is null && input.RecoverySessionId != Guid.Empty && input.Disposition is null && input.Findings is null => input,
    OperationCommandActions.ApproveRecoveryRestart when input.OperationId is null && input.RecoverySessionId is { } id && id != Guid.Empty && input.Disposition is null && Text(input.Findings, 10000) => input with { Findings = input.Findings!.Trim() },
    _ => null
  };

  private static Task<string> BasisAsync(IAuditSphereDbContext db, ActorContext actor, OperationCommandSnapshot snapshot, CancellationToken ct) =>
    db.Database.SqlQuery<string>($"""
      SELECT operator_command_basis({actor.FirmId}, {actor.UserId}, {actor.SessionEpoch}, CAST({JsonSerializer.Serialize(snapshot)} AS jsonb)) AS "Value"
      """).SingleAsync(ct);

  private static Task<string> RequestHashAsync(IAuditSphereDbContext db, ActorContext actor, Guid requestId,
    OperationCommandInput input, OperationCommandSnapshot snapshot, CancellationToken ct) =>
    db.Database.SqlQuery<string>($"""
      SELECT operator_command_request_hash({actor.FirmId}, {actor.UserId}, {actor.SessionEpoch}, {requestId},
        CAST({JsonSerializer.Serialize(input)} AS jsonb), CAST({JsonSerializer.Serialize(snapshot)} AS jsonb)) AS "Value"
      """).SingleAsync(ct);

  private static async Task BarrierAsync(IAuditSphereDbContext db, ActorContext actor, Guid requestId, bool write, CancellationToken ct)
  {
    var hash = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Namespace = "operator-command-v1", actor.FirmId, actor.UserId, RequestId = requestId }));
    var key = unchecked((long)Convert.ToUInt64(hash[..16], 16));
    if (write) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);
  }

  private static async Task<OperationCommandSnapshot?> SnapshotAsync(IAuditSphereDbContext db, ActorContext actor, Guid? operationId, bool lockOperation, CancellationToken ct)
  {
    var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.FirmId, ct);
    if (firm is null || firm.DeploymentEpoch < 1 || firm.RecoveryEpoch < 0 || firm.PolicyGeneration < 1) return null;
    DurableOperation? operation = null;
    if (operationId is { } id)
    {
      operation = lockOperation
        ? await db.DurableOperations.FromSqlInterpolated($"SELECT * FROM durable_operations WHERE firm_id = {actor.FirmId} AND id = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct)
        : await db.DurableOperations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == id, ct);
      if (operation is null) return null;
    }
    var session = await OperationRecoveryService.CurrentSessionAsync(db, actor.FirmId, ct);
    return new(firm.OperatingMode, Number(firm.DeploymentEpoch), Number(firm.RecoveryEpoch), Number(firm.PolicyGeneration),
      operation is null ? null : new(operation.Id, operation.ClientId, operation.EngagementId, operation.TargetId, operation.CorrelationId,
        operation.OperationKind, operation.RequestDigest, Number(operation.ExpectedRevision), Number(operation.AttemptToken), Number(operation.AttemptCount), operation.Status.ToString(),
        operation.IsReconciliation, operation.LeaseOwner is not null || operation.LeaseExpiresAt is not null, operation.ErrorCode,
        operation.CancellationDisposition, operation.NextAttemptAt.ToUniversalTime(), operation.CompletedAt?.ToUniversalTime(),
        operation.ResultIdentity is not null && operation.ResultDigest is not null),
      session is null ? null : new(session.Id, Number(session.ExternalEpoch), session.RestorePoint, session.ReconciliationScope, session.Findings,
        session.CreatedAt.ToUniversalTime(), session.ApprovedRestartAt?.ToUniversalTime(), session.ApprovedByUserId));
  }

  private static IReadOnlyList<string> Eligibility(OperationCommandSnapshot before, string action)
  {
    var blockers = new List<string>();
    var op = before.Operation;
    var session = before.RecoverySession;
    if (action is OperationCommandActions.Retry or OperationCommandActions.Cancel)
    {
      if (op is null) blockers.Add("Select an exact firm-owned operation.");
      else
      {
        if (op.HasLease) blockers.Add("The operation holds a lease.");
        if (action == OperationCommandActions.Retry)
        {
          if (before.OperatingMode != "LOCAL_ONLY") blockers.Add("Re-arm requires local-only operating mode.");
          if (op.Status is not ("PROVIDER_BLOCKED" or "DEAD_LETTER" or "AUTHORIZATION_BLOCKED" or "RESULT_UNCERTAIN")) blockers.Add("The current operation is not retryable.");
        }
        else if (op.Status is not ("PENDING" or "RETRY_WAIT") || op.IsReconciliation)
          blockers.Add("Only queued work without an unknown effect can be cancelled.");
      }
    }
    else
    {
      if (before.OperatingMode != "RECOVERY_QUARANTINE") blockers.Add("The firm is not awaiting recovery restart.");
      if (before.DeploymentEpoch == Number(long.MaxValue)) blockers.Add("The deployment epoch limit has been reached.");
      if (action == OperationCommandActions.ApproveRecoveryRestart)
      {
        if (session is null || session.ExternalEpoch != before.RecoveryEpoch || session.ApprovedRestartAt is not null)
          blockers.Add("Only the current unapproved recovery session can receive a restart approval.");
      }
      else if (session is null ? before.RecoveryEpoch != "0" : session.ExternalEpoch != before.RecoveryEpoch ||
          session.ApprovedRestartAt is null || session.ApprovedByUserId is null || string.IsNullOrWhiteSpace(session.Findings))
        blockers.Add("Approve the exact current recovery session and findings before lifting quarantine.");
    }
    return blockers;
  }

  public static async Task<CommandResult<OperationCommandState>> StateAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid? operationId = null, Guid? recoverySessionId = null, CancellationToken ct = default)
  {
    if (operationId == Guid.Empty || recoverySessionId == Guid.Empty) return Fail<OperationCommandState>(ErrorCodes.Operations.Invalid, "Provide exact nonempty identifiers.");
    var auth = await OperationRecoveryService.AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return Fail<OperationCommandState>(auth.ErrorCode!, auth.Message!);
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var authority = await OperationRecoveryService.LockAuthorityAsync(db, actor, false, ct);
    if (!authority.Succeeded) return Fail<OperationCommandState>(authority.ErrorCode!, authority.Message!);
    var before = await SnapshotAsync(db, actor, operationId, false, ct);
    if (before is null) return Fail<OperationCommandState>(ErrorCodes.ScopeDenied, "Access denied.");
    if (recoverySessionId is not null && before.RecoverySession?.Id != recoverySessionId)
      return Fail<OperationCommandState>(ErrorCodes.StaleRevision, "The selected recovery session is no longer current.");
    var end = await SnapshotAsync(db, actor, operationId, false, ct);
    if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(end)) return Fail<OperationCommandState>(ErrorCodes.StaleRevision, "The operation changed during inspection.");
    auth = await OperationRecoveryService.AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return Fail<OperationCommandState>(auth.ErrorCode!, auth.Message!);
    var retry = Eligibility(before, OperationCommandActions.Retry);
    var cancel = Eligibility(before, OperationCommandActions.Cancel);
    var lift = Eligibility(before, OperationCommandActions.LiftQuarantine);
    var approve = Eligibility(before, OperationCommandActions.ApproveRecoveryRestart);
    var basis = await BasisAsync(db, actor, before, ct);
    var state = new OperationCommandState(before, basis, retry.Count == 0, cancel.Count == 0,
      lift.Count == 0, approve.Count == 0, [], retry, cancel, lift, approve);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<OperationCommandState>.Ok(state);
  }

  public static async Task<CommandResult<OperationCommandPreview>> PreviewAsync(IAuditSphereDbContext db, ActorContext actor,
    OperationCommandRequest request, CancellationToken ct = default)
  {
    var input = Canonical(request?.Input);
    if (request is null || request.RequestId == Guid.Empty || input is null || JsonSerializer.Serialize(input).Length > OperatorCommandReceipt.MaximumInputLength)
      return Fail<OperationCommandPreview>(ErrorCodes.Operations.Invalid, "Provide one bounded exact operator action.");
    var state = await StateAsync(db, actor, input.OperationId, input.RecoverySessionId, ct);
    if (!state.Succeeded) return Fail<OperationCommandPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (request.ReviewBasis is not null && request.ReviewBasis != s.ReviewBasis)
      return Fail<OperationCommandPreview>(ErrorCodes.StaleRevision, "The operator review basis changed. Review again.");
    var blockers = Eligibility(s.Before, input.Action).ToList();
    if (input.Action == OperationCommandActions.LiftQuarantine && input.RecoverySessionId != s.Before.RecoverySession?.Id)
      blockers.Add("Bind the exact current recovery session, or the original sessionless quarantine.");
    var requestHash = await RequestHashAsync(db, actor, request.RequestId, input, s.Before, ct);
    var preview = new OperationCommandPreview(request.RequestId, requestHash,
      s.ReviewBasis, input, s.Before, blockers.Count == 0, blockers,
      input.Action switch
      {
        OperationCommandActions.Retry => $"Re-arm {s.Before.Operation!.Kind}; unknown effects remain reconciliation-only.",
        OperationCommandActions.Cancel => "Cancel the exact queued operation and retain its disposition.",
        OperationCommandActions.ApproveRecoveryRestart => "Record the exact current recovery findings and restart once.",
        _ => "Lift the exact approved quarantine once; no provider outcome is asserted."
      });
    if (JsonSerializer.Serialize(preview).Length > OperatorCommandReceipt.MaximumPreviewLength)
      return Fail<OperationCommandPreview>(ErrorCodes.Operations.Invalid, "The retained preview exceeds its bound.");
    return CommandResult<OperationCommandPreview>.Ok(preview);
  }

  private static async Task<OperationCommandReceiptView> ViewAsync(IAuditSphereDbContext db, OperatorCommandReceipt receipt, CancellationToken ct)
  {
    var e = await db.OperatorCommandEvents.AsNoTracking().SingleAsync(x => x.Id == receipt.EventId && x.FirmId == receipt.FirmId && x.ActorId == receipt.ActorId, ct);
    if (e.ActorEpoch != receipt.ActorEpoch || e.RequestId != receipt.RequestId || e.RequestHash != receipt.RequestHash || e.Action != receipt.Action)
      throw new InvalidOperationException("Operator receipt evidence is inconsistent.");
    return new(receipt.Id, receipt.ActorId, Number(receipt.ActorEpoch), receipt.RequestId, receipt.RequestHash, receipt.Action, receipt.CreatedAt.ToUniversalTime(),
      NormalizePreview(JsonSerializer.Deserialize<OperationCommandPreview>(receipt.PreviewJson)!), new(e.Id, e.ActorId, Number(e.ActorEpoch), e.Action, e.OperationEventId,
        e.CreatedAt.ToUniversalTime(), NormalizeSnapshot(JsonSerializer.Deserialize<OperationCommandSnapshot>(e.BeforeJson)!), NormalizeSnapshot(JsonSerializer.Deserialize<OperationCommandSnapshot>(e.AfterJson)!)));
  }

  private static OperationCommandPreview NormalizePreview(OperationCommandPreview preview) => preview with { Before = NormalizeSnapshot(preview.Before) };
  private static OperationCommandSnapshot NormalizeSnapshot(OperationCommandSnapshot snapshot) => snapshot with
  {
    Operation = snapshot.Operation is not { } op ? null : op with { NextAttemptAt = op.NextAttemptAt?.ToUniversalTime(), CompletedAt = op.CompletedAt?.ToUniversalTime() },
    RecoverySession = snapshot.RecoverySession is not { } session ? null : session with { CreatedAt = session.CreatedAt.ToUniversalTime(), ApprovedRestartAt = session.ApprovedRestartAt?.ToUniversalTime() }
  };

  public static async Task<CommandResult<OperationCommandLookup>> LookupAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash)) return Fail<OperationCommandLookup>(ErrorCodes.Operations.Invalid, "Provide the exact request identity and hash.");
    var auth = await OperationRecoveryService.AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return Fail<OperationCommandLookup>(auth.ErrorCode!, auth.Message!);
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var authority = await OperationRecoveryService.LockAuthorityAsync(db, actor, false, ct);
    if (!authority.Succeeded) return Fail<OperationCommandLookup>(authority.ErrorCode!, authority.Message!);
    await BarrierAsync(db, actor, requestId, false, ct);
    auth = await OperationRecoveryService.AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return Fail<OperationCommandLookup>(auth.ErrorCode!, auth.Message!);
    var receipt = await db.OperatorCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (receipt is not null && receipt.ActorEpoch != actor.SessionEpoch) return Fail<OperationCommandLookup>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
    if (receipt is not null && receipt.RequestHash != requestHash) return Fail<OperationCommandLookup>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
    var view = receipt is null ? null : await ViewAsync(db, receipt, ct);
    auth = await OperationRecoveryService.AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return Fail<OperationCommandLookup>(auth.ErrorCode!, auth.Message!);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<OperationCommandLookup>.Ok(new(view is not null, view));
  }

  public static async Task<CommandResult<OperationCommandReceiptView>> ExecuteAsync(IAuditSphereDbContext db, ActorContext actor,
    OperationCommandRequest request, CancellationToken ct = default)
  {
    var input = Canonical(request?.Input);
    if (request is null || request.RequestId == Guid.Empty || input is null || !request.Reviewed || !Hash(request.ReviewBasis) ||
        !Hash(request.ExpectedRequestHash) || JsonSerializer.Serialize(input).Length > OperatorCommandReceipt.MaximumInputLength)
      return Fail<OperationCommandReceiptView>(ErrorCodes.Operations.Invalid, "Review the exact bounded operator intent before executing.");
    var auth = await OperationRecoveryService.AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return Fail<OperationCommandReceiptView>(auth.ErrorCode!, auth.Message!);
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var savepoint = "operator_command_" + Guid.NewGuid().ToString("N");
    var transaction = db.Database.CurrentTransaction!;
    await transaction.CreateSavepointAsync(savepoint, ct);
    var tracking = CaptureTracking(db);
    var active = true;
    try
    {
      var exclusive = input.Action is OperationCommandActions.LiftQuarantine or OperationCommandActions.ApproveRecoveryRestart;
      var authority = await OperationRecoveryService.LockAuthorityAsync(db, actor, exclusive, ct);
      if (!authority.Succeeded) return Fail<OperationCommandReceiptView>(authority.ErrorCode!, authority.Message!);
      await BarrierAsync(db, actor, request.RequestId, true, ct);
      auth = await OperationRecoveryService.AuthorizeAsync(db, actor, ct);
      if (!auth.Succeeded) return Fail<OperationCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      var old = await db.OperatorCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
      if (old is not null)
      {
        if (old.ActorEpoch != actor.SessionEpoch) return Fail<OperationCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
        var originalPreview = JsonSerializer.Deserialize<OperationCommandPreview>(old.PreviewJson)
          ?? throw new InvalidOperationException("The retained operator preview is missing.");
        var replayHash = await RequestHashAsync(db, actor, request.RequestId, input, originalPreview.Before, ct);
        if (old.Action != input.Action || old.RequestHash != replayHash || old.RequestHash != request.ExpectedRequestHash ||
            originalPreview.ReviewBasis != request.ReviewBasis || JsonSerializer.Serialize(input) != old.InputJson)
          return Fail<OperationCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
        var original = await ViewAsync(db, old, ct);
        auth = await OperationRecoveryService.AuthorizeAsync(db, actor, ct);
        if (!auth.Succeeded) return Fail<OperationCommandReceiptView>(auth.ErrorCode!, auth.Message!);
        await transaction.ReleaseSavepointAsync(savepoint, ct);
        active = false;
        if (tx is not null) await tx.CommitAsync(ct);
        return CommandResult<OperationCommandReceiptView>.Ok(original);
      }
      var hash = request.ExpectedRequestHash!;
      if (await SnapshotAsync(db, actor, input.OperationId, true, ct) is null) return Fail<OperationCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
      var preview = await PreviewAsync(db, actor, request with { Input = input }, ct);
      if (!preview.Succeeded) return Fail<OperationCommandReceiptView>(preview.ErrorCode!, preview.Message!);
      if (!preview.Value!.CanProceed) return Fail<OperationCommandReceiptView>(ErrorCodes.Operations.State, preview.Value.Blockers.First());
      // Recompute canonical state and hash under the source lock; syntax alone is never a review fence.
      if (preview.Value.RequestHash != hash) return Fail<OperationCommandReceiptView>(ErrorCodes.StaleRevision, "The reviewed operator state changed.");
      var eventId = Guid.CreateVersion7();
      var sourceId = input.OperationId is null ? (Guid?)null : Guid.CreateVersion7();
      await SetPublicationAsync(db, actor, request, input, eventId, sourceId, hash, ct);
      var result = input.Action switch
      {
        OperationCommandActions.Retry => await OperationRecoveryService.RetryCoreAsync(db, actor, new(input.OperationId!.Value), ct, sourceId),
        OperationCommandActions.Cancel => await OperationRecoveryService.CancelCoreAsync(db, actor, new(input.OperationId!.Value, input.Disposition!), ct, sourceId),
        OperationCommandActions.ApproveRecoveryRestart => await OperationRecoveryService.ApproveRecoveryRestartCoreAsync(db, actor, new(input.RecoverySessionId!.Value, input.Findings!), ct),
        _ => await OperationRecoveryService.LiftQuarantineCoreAsync(db, actor, ct)
      };
      if (!result.Succeeded)
      {
        await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
        RestoreTracking(db, tracking);
        return Fail<OperationCommandReceiptView>(result.ErrorCode!, result.Message!);
      }
      var effect = await db.OperatorCommandEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId && x.FirmId == actor.FirmId, ct)
        ?? throw new InvalidOperationException("A real source transition must create its operator event.");
      var receipt = OperatorCommandReceipt.Create(Guid.CreateVersion7(), effect, JsonSerializer.Serialize(input), JsonSerializer.Serialize(preview.Value), DateTimeOffset.UtcNow);
      db.OperatorCommandReceipts.Add(receipt);
      await db.SaveChangesAsync(ct);
      var stored = await db.OperatorCommandReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id && x.FirmId == actor.FirmId, ct);
      var view = await ViewAsync(db, stored, ct);
      auth = await OperationRecoveryService.AuthorizeAsync(db, actor, ct);
      if (!auth.Succeeded)
      {
        await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
        RestoreTracking(db, tracking);
        return Fail<OperationCommandReceiptView>(auth.ErrorCode!, auth.Message!);
      }
      await ClearPublicationAsync(db, ct);
      await transaction.ReleaseSavepointAsync(savepoint, ct);
      active = false;
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<OperationCommandReceiptView>.Ok(view);
    }
    catch (Exception ex)
    {
      if (active) await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
      RestoreTracking(db, tracking);
      if (ex.GetBaseException() is DbException cause && cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505")
        return Fail<OperationCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Concurrent publication changed the outcome. Recover the retained request before retrying.");
      throw;
    }
    finally
    {
      if (active) await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
    }
  }

  private static Task<int> SetPublicationAsync(IAuditSphereDbContext db, ActorContext actor, OperationCommandRequest request,
    OperationCommandInput input, Guid eventId, Guid? sourceId, string hash, CancellationToken ct)
  {
    var eid = eventId.ToString(); var sid = sourceId?.ToString() ?? ""; var rid = request.RequestId.ToString();
    var aid = actor.UserId.ToString(); var epoch = Number(actor.SessionEpoch); var oid = input.OperationId?.ToString() ?? "";
    var session = input.RecoverySessionId?.ToString() ?? ""; var json = JsonSerializer.Serialize(input);
    return db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('audit.operator_event_id',{eid},true),set_config('audit.operator_source_id',{sid},true),set_config('audit.operator_request_id',{rid},true),set_config('audit.operator_actor_id',{aid},true),set_config('audit.operator_actor_epoch',{epoch},true),set_config('audit.operator_operation_id',{oid},true),set_config('audit.operator_session_id',{session},true),set_config('audit.operator_hash',{hash},true),set_config('audit.operator_action',{input.Action},true),set_config('audit.operator_input',{json},true)", ct);
  }
  private static Task<int> ClearPublicationAsync(IAuditSphereDbContext db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync(
    "SELECT set_config('audit.operator_event_id','',true),set_config('audit.operator_action','',true)", ct);

  internal sealed record TrackedPublication(object Entity, EntityState State, PropertyValues Values);
  internal static IReadOnlyList<TrackedPublication> CaptureTracking(IAuditSphereDbContext db) => db is DbContext context
    ? context.ChangeTracker.Entries().Where(x => x.Entity is RecoverySession or OperationEvent or OperatorCommandEvent or OperatorCommandReceipt)
      .Select(x => new TrackedPublication(x.Entity, x.State, x.CurrentValues.Clone())).ToList() : [];
  internal static void RestoreTracking(IAuditSphereDbContext db, IReadOnlyList<TrackedPublication> prior)
  {
    if (db is not DbContext context) return;
    foreach (var entry in context.ChangeTracker.Entries().Where(x => x.Entity is RecoverySession or OperationEvent or OperatorCommandEvent or OperatorCommandReceipt).ToArray())
    {
      var old = prior.FirstOrDefault(x => ReferenceEquals(x.Entity, entry.Entity));
      if (old is null) entry.State = EntityState.Detached;
      else { entry.CurrentValues.SetValues(old.Values); entry.State = old.State; }
    }
  }
}
