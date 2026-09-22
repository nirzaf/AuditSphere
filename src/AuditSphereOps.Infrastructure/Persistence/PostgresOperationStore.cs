using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace AuditSphereOps.Infrastructure.Persistence;

public sealed class OperationContextFactory(IDbContextFactory<AuditSphereDbContext> factory) : IAuditSphereDbContextFactory
{
  public async Task<IAuditSphereDbContext> CreateAsync(CancellationToken ct = default) =>
    await factory.CreateDbContextAsync(ct);
}

public sealed class PostgresOperationStore(IAuditSphereDbContextFactory factory) : IOperationStore
{
  private static async Task<DateTimeOffset> NowAsync(IAuditSphereDbContext db, CancellationToken ct)
  {
    await using var cmd = Command(db, "SELECT statement_timestamp()");
    return new DateTimeOffset((DateTime)(await cmd.ExecuteScalarAsync(ct))!, TimeSpan.Zero);
  }

  private static NpgsqlCommand Command(IAuditSphereDbContext db, string sql) => new(sql,
    (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());

  private static async Task<bool> GuardsAsync(IAuditSphereDbContext db, Guid firm, Guid? client,
    long? epoch, CancellationToken ct)
  {
    await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '15s'", ct);
    var firms = await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firm} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    if (firms.Count != 1 || firms[0].OperatingMode != "LOCAL_ONLY" ||
        (epoch.HasValue && firms[0].DeploymentEpoch != epoch.Value)) return false;
    if (client is null) return true;
    return (await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE id = {client.Value} AND firm_id = {firm} FOR UPDATE")
      .AsNoTracking().ToListAsync(ct)).Count == 1;
  }

  private static void Event(IAuditSphereDbContext db, DurableOperation op, string kind, DateTimeOffset now) =>
    db.OperationEvents.Add(new OperationEvent { Id = Guid.CreateVersion7(), OperationId = op.Id,
      Token = op.AttemptToken, Executor = op.LeaseOwner ?? "enqueue", Kind = kind, OccurredAt = now });

  public async Task<CommandResult<Guid>> EnqueueAsync(IAuditSphereDbContext db, OperationRequest r,
    IOperationHandler handler, CancellationToken ct)
  {
    if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Enqueue requires the caller's transaction.");
    if (r.FirmId == Guid.Empty || r.TargetId == Guid.Empty || r.ExpectedRevision < 1 ||
        string.IsNullOrWhiteSpace(r.IdempotencyKey) || r.IdempotencyKey.Length > 200 ||
        r.PayloadJson.Length > 16384 || r.Kind != handler.Definition.Kind ||
        (r.EngagementId.HasValue && !r.ClientId.HasValue))
      throw new OperationBlockedException("invalid-operation-request");
    var d = handler.Definition;
    var payload = handler.NormalizePayload(r);
    var bytes = OperationEncoding.Encode(r, d, payload);
    var digest = Hashing.Sha256Hex(bytes);
    if (!await GuardsAsync(db, r.FirmId, r.ClientId, null, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Operation scope is unavailable.");
    if (r.EngagementId.HasValue && !await db.Engagements.AnyAsync(e => e.Id == r.EngagementId &&
        e.FirmId == r.FirmId && e.PracticeClientId == r.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Operation scope is unavailable.");
    var id = Guid.CreateVersion7();
    var correlation = Guid.CreateVersion7();
    var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO durable_operations
      (id, firm_id, client_id, engagement_id, operation_kind, payload_json, idempotency_key,
       request_digest, request_bytes, status, attempt_token, attempt_count, schema_version,
       execution_group, execution_mode, authority_mode, target_id, expected_revision, correlation_id,
       originator_id, next_attempt_at, created_at, claimed_epoch, is_reconciliation)
      VALUES ({id}, {r.FirmId}, {r.ClientId}, {r.EngagementId}, {d.Kind}, {payload}, {r.IdempotencyKey},
       {digest}, {bytes}, 'PENDING', 0, 0, {d.SchemaVersion}, {d.Group}, {d.Mode.ToString()},
       {d.Authority.ToString()}, {r.TargetId}, {r.ExpectedRevision}, {correlation}, {r.OriginatorId},
       statement_timestamp(), statement_timestamp(), 0, false)
      ON CONFLICT (firm_id, idempotency_key) DO NOTHING
      """, ct);
    var op = await db.DurableOperations.AsNoTracking().SingleAsync(o => o.FirmId == r.FirmId && o.IdempotencyKey == r.IdempotencyKey, ct);
    if (op.ClientId != r.ClientId || op.EngagementId != r.EngagementId || op.OriginatorId != r.OriginatorId || op.RequestDigest != digest)
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "Idempotency key conflicts with this request.");
    if (count == 1)
    {
      Event(db, op, "operation.enqueued.v1", await NowAsync(db, ct));
      await db.SaveChangesAsync(ct);
    }
    return CommandResult<Guid>.Ok(op.Id);
  }

  public async Task<DurableOperation?> ClaimAsync(WorkerOptions options, IReadOnlyList<OperationDefinition> definitions,
    string owner, bool reconciliation, CancellationToken ct)
  {
    options.Validate(definitions);
    if (definitions.Count == 0) return null;
    if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Worker owner required.", nameof(owner));
    await using var db = await factory.CreateAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // Queue coordination does not acquire client/aggregate guards. They precede publication.
    if (!await GuardsAsync(db, options.FirmId, null, options.DeploymentEpoch, ct)) return null;
    await using var cmd = Command(db, """
      WITH eligible AS (
        SELECT id FROM durable_operations
        WHERE firm_id = @firm AND execution_group = @group AND next_attempt_at <= statement_timestamp()
          AND ((@reconcile AND status = 'RESULT_UNCERTAIN') OR
               (NOT @reconcile AND status IN ('PENDING','RETRY_WAIT') AND attempt_count < @max))
          AND (operation_kind, schema_version, execution_mode, authority_mode) IN
            (SELECT * FROM unnest(@kinds, @schemas, @modes, @authorities))
        ORDER BY next_attempt_at, created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED
      )
      UPDATE durable_operations op SET status = CASE WHEN @reconcile THEN 'VERIFYING' ELSE 'CLAIMED' END,
        attempt_token = attempt_token + 1, attempt_count = attempt_count + CASE WHEN @reconcile THEN 0 ELSE 1 END,
        lease_owner = @owner, lease_expires_at = statement_timestamp() + make_interval(secs => @lease),
        claimed_epoch = @epoch, is_reconciliation = @reconcile
      FROM eligible WHERE op.id = eligible.id RETURNING op.id
      """);
    cmd.Parameters.AddWithValue("firm", options.FirmId);
    cmd.Parameters.AddWithValue("group", options.Group);
    cmd.Parameters.AddWithValue("reconcile", reconciliation);
    cmd.Parameters.AddWithValue("max", options.MaxAttempts);
    cmd.Parameters.AddWithValue("owner", owner);
    cmd.Parameters.AddWithValue("lease", options.LeaseSeconds);
    cmd.Parameters.AddWithValue("epoch", options.DeploymentEpoch);
    cmd.Parameters.AddWithValue("kinds", definitions.Select(d => d.Kind).ToArray());
    cmd.Parameters.AddWithValue("schemas", definitions.Select(d => d.SchemaVersion).ToArray());
    cmd.Parameters.AddWithValue("modes", definitions.Select(d => d.Mode.ToString()).ToArray());
    cmd.Parameters.AddWithValue("authorities", definitions.Select(d => d.Authority.ToString()).ToArray());
    var id = await cmd.ExecuteScalarAsync(ct);
    if (id is not Guid operationId) return null;
    var op = await db.DurableOperations.AsNoTracking().SingleAsync(o => o.Id == operationId, ct);
    var now = await NowAsync(db, ct);
    db.OperationAttempts.Add(new OperationAttempt { Id = Guid.CreateVersion7(), OperationId = op.Id,
      Token = op.AttemptToken, Owner = owner, Reconciliation = reconciliation, ClaimedAt = now });
    Event(db, op, reconciliation ? "operation.reconciliation-claimed.v1" : "operation.claimed.v1", now);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return op;
  }

  private static async Task<bool> OwnsAsync(IAuditSphereDbContext db, DurableOperation op, WorkerOptions options, CancellationToken ct)
  {
    if (op.FirmId != options.FirmId || op.ExecutionGroup != options.Group || op.ClaimedEpoch != options.DeploymentEpoch) return false;
    var owned = await db.DurableOperations.FromSqlInterpolated($"""
      SELECT * FROM durable_operations WHERE id = {op.Id} AND firm_id = {options.FirmId}
        AND lease_owner = {op.LeaseOwner} AND attempt_token = {op.AttemptToken}
        AND status = {op.Status.ToString()} AND request_digest = {op.RequestDigest}
        AND claimed_epoch = {options.DeploymentEpoch} AND lease_expires_at > statement_timestamp()
      FOR UPDATE
      """).AsNoTracking().ToListAsync(ct);
    return owned.Count == 1 && owned[0].LeaseExpiresAt > await NowAsync(db, ct);
  }

  public async Task<bool> ValidateAsync(DurableOperation op, WorkerOptions options, IOperationHandler handler, CancellationToken ct)
  {
    options.Validate([handler.Definition]);
    if (!OperationEncoding.Matches(op, handler)) throw new OperationBlockedException("request-integrity-conflict");
    await using var db = await factory.CreateAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await GuardsAsync(db, op.FirmId, op.ClientId, options.DeploymentEpoch, ct)) return false;
    await handler.LockTargetAsync(db, op, ct);
    var owned = await OwnsAsync(db, op, options, ct);
    await tx.CommitAsync(ct);
    return owned;
  }

  public async Task<bool> RenewAsync(DurableOperation op, WorkerOptions options, CancellationToken ct)
  {
    options.Validate([]);
    await using var db = await factory.CreateAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await GuardsAsync(db, op.FirmId, null, options.DeploymentEpoch, ct)) return false;
    // Renewal accepts any active stage of this exact attempt; dispatcher stage changes cannot race it out.
    var locked = await db.DurableOperations.FromSqlInterpolated($"""
      SELECT * FROM durable_operations WHERE id = {op.Id} AND firm_id = {options.FirmId}
        AND attempt_token = {op.AttemptToken} AND lease_owner = {op.LeaseOwner} FOR UPDATE
      """).AsNoTracking().ToListAsync(ct);
    if (locked.Count != 1) return false;
    // A fresh statement after acquiring the lock prevents reviving a lease that expired while waiting.
    var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE durable_operations SET lease_expires_at = statement_timestamp() + make_interval(secs => {options.LeaseSeconds})
      WHERE id = {op.Id} AND firm_id = {options.FirmId} AND execution_group = {options.Group}
        AND attempt_token = {op.AttemptToken} AND lease_owner = {op.LeaseOwner}
        AND claimed_epoch = {options.DeploymentEpoch} AND lease_expires_at > statement_timestamp()
        AND status IN ('CLAIMED','REMOTE_STARTED','VERIFYING')
      """, ct);
    await tx.CommitAsync(ct);
    return count == 1;
  }

  public async Task<bool> TransitionAsync(DurableOperation op, WorkerOptions options, OperationState next,
    string? error, TimeSpan? delay, CancellationToken ct)
  {
    options.Validate([]);
    var active = op.Status is OperationState.CLAIMED or OperationState.REMOTE_STARTED or OperationState.VERIFYING or OperationState.CANCEL_REQUESTED;
    var allowed = next switch
    {
      OperationState.REMOTE_STARTED => op.Status == OperationState.CLAIMED &&
        op.ExecutionMode is OperationMode.SIMULATED or OperationMode.LIVE,
      OperationState.VERIFYING => op.Status == OperationState.REMOTE_STARTED,
      OperationState.CANCELLED_WITH_DISPOSITION => op.Status == OperationState.CLAIMED,
      OperationState.RETRY_WAIT or OperationState.RESULT_UNCERTAIN or OperationState.DEAD_LETTER or
        OperationState.AUTHORIZATION_BLOCKED or OperationState.PROVIDER_BLOCKED or OperationState.CANCEL_REQUESTED => active,
      _ => false
    };
    if (!allowed) return false;
    await using var db = await factory.CreateAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await GuardsAsync(db, op.FirmId, op.ClientId, options.DeploymentEpoch, ct) || !await OwnsAsync(db, op, options, ct)) return false;
    var retainLease = next is OperationState.REMOTE_STARTED or OperationState.VERIFYING or OperationState.CANCEL_REQUESTED;
    var seconds = Math.Max(0, delay?.TotalSeconds ?? 0);
    var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE durable_operations SET status = {next.ToString()}, error_code = {error},
        next_attempt_at = statement_timestamp() + make_interval(secs => {seconds}),
        lease_owner = CASE WHEN {retainLease} THEN lease_owner ELSE NULL END,
        lease_expires_at = CASE WHEN {retainLease} THEN lease_expires_at ELSE NULL END
      WHERE id = {op.Id} AND attempt_token = {op.AttemptToken} AND lease_owner = {op.LeaseOwner}
        AND status = {op.Status.ToString()} AND lease_expires_at > statement_timestamp()
      """, ct);
    if (count != 1) return false;
    Event(db, op, "operation." + next.ToString().ToLowerInvariant() + ".v1", await NowAsync(db, ct));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    op.Status = next;
    return true;
  }

  public async Task<bool> CompleteAsync(DurableOperation op, WorkerOptions options, IOperationHandler handler,
    OperationResult? remote, CancellationToken ct)
  {
    options.Validate([handler.Definition]);
    if (op.Status != (op.ExecutionMode == OperationMode.LOCAL ? OperationState.CLAIMED : OperationState.VERIFYING)) return false;
    await using var db = await factory.CreateAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await GuardsAsync(db, op.FirmId, op.ClientId, options.DeploymentEpoch, ct)) return false;
    await handler.LockTargetAsync(db, op, ct);
    if (!await OwnsAsync(db, op, options, ct)) return false;
    if (!OperationEncoding.Matches(op, handler)) throw new OperationBlockedException("request-integrity-conflict");
    var result = await handler.PublishAsync(db, op, remote, ct);
    var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE durable_operations SET status = 'COMPLETED', result_identity = {result.Identity}, result_digest = {result.Digest},
        completed_at = statement_timestamp(), error_code = NULL, lease_owner = NULL, lease_expires_at = NULL
      WHERE id = {op.Id} AND attempt_token = {op.AttemptToken} AND lease_owner = {op.LeaseOwner}
        AND status = {op.Status.ToString()} AND lease_expires_at > statement_timestamp()
      """, ct);
    if (count != 1) return false;
    Event(db, op, "operation.completed.v1", await NowAsync(db, ct));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    op.Status = OperationState.COMPLETED;
    return true;
  }

  public async Task<int> ReapAsync(WorkerOptions options, CancellationToken ct)
  {
    options.Validate([]);
    await using var db = await factory.CreateAsync(ct);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await GuardsAsync(db, options.FirmId, null, options.DeploymentEpoch, ct)) return 0;
    var expired = await db.DurableOperations.FromSqlInterpolated($"""
      SELECT * FROM durable_operations WHERE firm_id = {options.FirmId} AND execution_group = {options.Group}
        AND status IN ('CLAIMED','REMOTE_STARTED','VERIFYING','CANCEL_REQUESTED')
        AND lease_expires_at <= statement_timestamp()
      ORDER BY lease_expires_at, id LIMIT 25 FOR UPDATE SKIP LOCKED
      """).ToListAsync(ct);
    var now = await NowAsync(db, ct);
    foreach (var op in expired)
    {
      var next = op.Status == OperationState.CLAIMED
        ? (op.AttemptCount < options.MaxAttempts ? OperationState.RETRY_WAIT : OperationState.DEAD_LETTER)
        : OperationState.RESULT_UNCERTAIN;
      Event(db, op, "operation.lease-expired.v1", now);
      op.Status = next;
      op.ErrorCode = "lease-expired";
      op.LeaseOwner = null;
      op.LeaseExpiresAt = null;
      op.NextAttemptAt = now.AddSeconds(5);
    }
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return expired.Count;
  }
}
