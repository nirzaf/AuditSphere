using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class DurableOutboxTests
{
  [Fact]
  public async Task IdenticalConcurrentEnqueues_HaveOneOperationAndAuditEvent_ConflictsDoNotLeak()
  {
    await using var h = await Harness.CreateAsync();
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    async Task<CommandResult<Guid>> Enqueue() { await gate.Task; return await h.EnqueueAsync(); }
    var a = Enqueue(); var b = Enqueue(); gate.SetResult();
    var results = await Task.WhenAll(a, b);
    Assert.All(results, r => Assert.True(r.Succeeded));
    Assert.Equal(results[0].Value, results[1].Value);
    Assert.Equal(results[0].Value, (await h.EnqueueAsync()).Value);
    Assert.False((await h.EnqueueAsync(h.Request with { PayloadJson = "{\"value\":\"changed\"}" })).Succeeded);
    Assert.False((await h.EnqueueAsync(h.Request with { ExpectedRevision = 2 })).Succeeded);
    Assert.False((await h.EnqueueAsync(h.Request with { OriginatorId = Guid.NewGuid() })).Succeeded);
    var other = await h.Pg.SeedScopeAsync();
    // Same firm, different valid client: conflict, never the prior operation ID.
    await using (var seed = h.Db())
    {
      var clientId = Guid.NewGuid();
      seed.PracticeClients.Add(new() { Id = clientId, FirmId = h.Options.FirmId, LegalName = "Another client" });
      seed.ClientSafetyStates.Add(new() { Id = clientId, FirmId = h.Options.FirmId });
      await seed.SaveChangesAsync();
      var conflict = await h.EnqueueAsync(h.Request with { ClientId = clientId, EngagementId = null });
      Assert.False(conflict.Succeeded);
      Assert.Equal(Guid.Empty, conflict.Value);
    }
    // Idempotency is firm-scoped, not globally scoped.
    var independent = await h.EnqueueAsync(h.Request with { FirmId = other.FirmId, ClientId = other.ClientId, EngagementId = other.EngagementId });
    Assert.True(independent.Succeeded);
    Assert.NotEqual(results[0].Value, independent.Value);
    await using var db = h.Db();
    Assert.Equal(2, await db.DurableOperations.CountAsync());
    Assert.Equal(2, await db.OperationEvents.CountAsync());
  }

  [Fact]
  public async Task Rollback_RemovesBusinessChangeOperationAndAuditTogether()
  {
    await using var h = await Harness.CreateAsync();
    await using (var db = h.Db())
    {
      await using var tx = await db.Database.BeginTransactionAsync();
      (await db.PracticeClients.SingleAsync()).LegalName = "Rolled back";
      Assert.True((await h.Store.EnqueueAsync(db, h.Request, h.Handler, default)).Succeeded);
      await db.SaveChangesAsync();
      await tx.RollbackAsync();
    }
    await using var verify = h.Db();
    Assert.Empty(await verify.DurableOperations.ToListAsync());
    Assert.Empty(await verify.OperationEvents.ToListAsync());
    Assert.NotEqual("Rolled back", (await verify.PracticeClients.SingleAsync()).LegalName);
  }

  [Fact]
  public async Task IndependentWorkers_ClaimDistinctRows_AndSkipLockedRows()
  {
    await using var h = await Harness.CreateAsync();
    await h.EnqueueAsync();
    await h.EnqueueAsync(h.Request with { IdempotencyKey = "second" });
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    async Task<DurableOperation?> Claim(string owner) { await gate.Task; return await h.ClaimAsync(owner); }
    var a = Claim("a"); var b = Claim("b"); gate.SetResult();
    var claimed = await Task.WhenAll(a, b);
    Assert.All(claimed, Assert.NotNull);
    Assert.NotEqual(claimed[0]!.Id, claimed[1]!.Id);
    Assert.Null(await h.ClaimAsync("c"));
    var third = (await h.EnqueueAsync(h.Request with { IdempotencyKey = "third" })).Value;
    var fourth = (await h.EnqueueAsync(h.Request with { IdempotencyKey = "fourth" })).Value;
    await using var locked = h.Db();
    await using var tx = await locked.Database.BeginTransactionAsync();
    await locked.DurableOperations.FromSqlInterpolated($"SELECT * FROM durable_operations WHERE id = {third} FOR UPDATE").ToListAsync();
    var next = await h.ClaimAsync("d").WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal(fourth, next!.Id);
    await tx.RollbackAsync();
  }

  [Fact]
  public async Task Eligibility_ExcludesFutureTerminalOtherGroupFirmAndUnsupportedKinds()
  {
    await using var h = await Harness.CreateAsync();
    var id = (await h.EnqueueAsync()).Value;
    await h.SqlAsync("UPDATE durable_operations SET next_attempt_at = statement_timestamp() + interval '1 hour'");
    Assert.Null(await h.ClaimAsync());
    await h.MakeDueAsync();
    Assert.Null(await h.Store.ClaimAsync(h.Options with { Group = "records" }, [h.Handler.Definition], "x", false, default));
    Assert.Null(await h.Store.ClaimAsync(h.Options with { FirmId = Guid.NewGuid() }, [h.Handler.Definition], "x", false, default));
    Assert.Null(await h.Store.ClaimAsync(h.Options, [h.Handler.Definition with { Kind = "unknown" }], "x", false, default));
    Assert.Null(await h.Store.ClaimAsync(h.Options, [h.Handler.Definition with { SchemaVersion = 2 }], "x", false, default));
    Assert.True(await h.Dispatcher().ProcessNextAsync());
    Assert.Equal(OperationState.COMPLETED, (await h.OperationAsync(id)).Status);
    Assert.Null(await h.ClaimAsync());
  }

  [Fact]
  public async Task ExpiredLease_RejectsOldRenewCompleteAndFailure_BeforeAndAfterReplacement()
  {
    await using var h = await Harness.CreateAsync();
    await h.EnqueueAsync();
    var old = (await h.ClaimAsync("old"))!;
    Assert.True(await h.Store.RenewAsync(old, h.Options, default));
    await h.ExpireAsync();
    Assert.False(await h.Store.RenewAsync(old, h.Options, default));
    Assert.False(await h.Store.TransitionAsync(old, h.Options, OperationState.PROVIDER_BLOCKED, "test", null, default));
    Assert.False(await h.Store.ValidateAsync(old, h.Options, h.Handler, default));
    Assert.Equal(1, await h.Store.ReapAsync(h.Options, default));
    await h.MakeDueAsync();
    var replacement = (await h.ClaimAsync("new"))!;
    Assert.Equal(old.AttemptToken + 1, replacement.AttemptToken);
    Assert.False(await h.Store.TransitionAsync(old, h.Options, OperationState.REMOTE_STARTED, null, null, default));
    await h.FinishAsync(replacement);
    // Even a stale caller claiming to be at the completion stage cannot overwrite success.
    old.Status = OperationState.VERIFYING;
    Assert.False(await h.Store.CompleteAsync(old, h.Options, h.Handler, h.Handler.Expected(old), default));
    Assert.False(await h.Store.RenewAsync(old, h.Options, default));
    Assert.False(await h.Store.TransitionAsync(old, h.Options, OperationState.RESULT_UNCERTAIN, "old", null, default));
    var result = await h.OperationAsync(old.Id);
    Assert.Equal(OperationState.COMPLETED, result.Status);
    Assert.Equal(2, result.AttemptToken);
    await using var db = h.Db();
    Assert.Equal(2, await db.OperationAttempts.CountAsync());
    Assert.Equal(1, await db.OperationEvents.CountAsync(e => e.Kind == "operation.completed.v1"));
  }

  [Fact]
  public async Task RemoteSuccessThenLocalRollback_RecreatedWorkerReconcilesOneDurableEffect()
  {
    await using var h = await Harness.CreateAsync();
    h.Handler.FailPublication = true;
    var id = (await h.EnqueueAsync()).Value;
    Assert.True(await h.Dispatcher().ProcessNextAsync());
    Assert.Equal(OperationState.RESULT_UNCERTAIN, (await h.OperationAsync(id)).Status);
    Assert.Equal(1, await h.EffectCountAsync());
    await using (var db = h.Db())
      Assert.DoesNotContain(await db.OperationEvents.ToListAsync(), e => e.Kind == "probe.published.v1");
    h.Handler.FailPublication = false;
    await h.MakeDueAsync();
    Assert.True(await h.Dispatcher(new ProbeHandler(h.Factory)).ProcessNextAsync());
    var result = await h.OperationAsync(id);
    Assert.Equal(OperationState.COMPLETED, result.Status);
    Assert.True(result.IsReconciliation);
    Assert.Equal(2, result.AttemptToken);
    Assert.Equal(1, await h.EffectCountAsync());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task UncertainResult_MissingOrWrongDigestRemainsBlocked(bool mismatch)
  {
    await using var h = await Harness.CreateAsync();
    var id = (await h.EnqueueAsync()).Value;
    var op = (await h.ClaimAsync())!;
    Assert.True(await h.Store.TransitionAsync(op, h.Options, OperationState.REMOTE_STARTED, null, null, default));
    if (mismatch)
    {
      await h.Handler.ExecuteEffectAsync(op, default);
      await h.SqlAsync("UPDATE simulated_effects SET digest = 'wrong'");
    }
    await h.ExpireAsync();
    await h.Store.ReapAsync(h.Options, default);
    Assert.Null(await h.ClaimAsync());
    await h.MakeDueAsync();
    Assert.True(await h.Dispatcher().ProcessNextAsync());
    Assert.Equal(OperationState.PROVIDER_BLOCKED, (await h.OperationAsync(id)).Status);
    Assert.Equal(mismatch ? 1 : 0, await h.EffectCountAsync());
  }

  [Fact]
  public async Task SafeRetry_HonorsDelay_AndDeadLettersAtAttemptLimit()
  {
    await using var h = await Harness.CreateAsync();
    h.Handler.Fault = "retry";
    var id = (await h.EnqueueAsync()).Value;
    for (var i = 1; i <= 5; i++)
    {
      Assert.True(await h.Dispatcher().ProcessNextAsync());
      var op = await h.OperationAsync(id);
      Assert.Equal(i, op.AttemptCount);
      Assert.Equal(i == 5 ? OperationState.DEAD_LETTER : OperationState.RETRY_WAIT, op.Status);
      Assert.True(op.NextAttemptAt >= op.CreatedAt.AddMinutes(10));
      if (i < 5) { Assert.Null(await h.ClaimAsync()); await h.MakeDueAsync(); }
    }
    Assert.Null(await h.ClaimAsync());
    Assert.Equal(0, await h.EffectCountAsync());
  }

  [Theory]
  [InlineData("authorization", OperationState.AUTHORIZATION_BLOCKED)]
  [InlineData("configuration", OperationState.PROVIDER_BLOCKED)]
  public async Task PermanentErrors_AreBlockedNotRetried(string fault, OperationState expected)
  {
    await using var h = await Harness.CreateAsync();
    h.Handler.Fault = fault;
    var id = (await h.EnqueueAsync()).Value;
    Assert.True(await h.Dispatcher().ProcessNextAsync());
    Assert.Equal(expected, (await h.OperationAsync(id)).Status);
    Assert.Null(await h.ClaimAsync());
    Assert.Equal(0, await h.EffectCountAsync());
  }

  [Fact]
  public async Task CancellationAfterEffect_PreservesUncertaintyAndReconciles()
  {
    await using var h = await Harness.CreateAsync();
    h.Handler.Fault = "pause";
    var id = (await h.EnqueueAsync()).Value;
    using var cancel = new CancellationTokenSource();
    var run = h.Dispatcher().ProcessNextAsync(cancel.Token);
    await h.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await cancel.CancelAsync();
    Assert.True(await run);
    Assert.Equal(OperationState.RESULT_UNCERTAIN, (await h.OperationAsync(id)).Status);
    await h.MakeDueAsync();
    Assert.True(await h.Dispatcher(new ProbeHandler(h.Factory)).ProcessNextAsync());
    Assert.Equal(OperationState.COMPLETED, (await h.OperationAsync(id)).Status);
    Assert.Equal(1, await h.EffectCountAsync());
  }

  [Fact]
  public async Task BackgroundRenewer_ExtendsLeaseDuringPausedEffect()
  {
    await using var h = await Harness.CreateAsync();
    h.Options = h.Options with { LeaseSeconds = 4, RenewalSeconds = 1 };
    h.Handler.Fault = "pause";
    var id = (await h.EnqueueAsync()).Value;
    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var run = h.Dispatcher().ProcessNextAsync(cancel.Token);
    await h.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var first = (await h.OperationAsync(id)).LeaseExpiresAt;
    var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
    while ((await h.OperationAsync(id)).LeaseExpiresAt <= first && DateTimeOffset.UtcNow < deadline)
      await Task.Delay(50);
    Assert.True((await h.OperationAsync(id)).LeaseExpiresAt > first);
    h.Handler.Release.TrySetResult();
    Assert.True(await run);
    Assert.Equal(OperationState.COMPLETED, (await h.OperationAsync(id)).Status);
  }

  [Fact]
  public async Task ChangedRevisionAndRecoveryEpoch_BlockPublication()
  {
    await using var h = await Harness.CreateAsync();
    var id = (await h.EnqueueAsync()).Value;
    var op = (await h.ClaimAsync())!;
    await h.SqlAsync("UPDATE simulated_targets SET revision = revision + 1");
    await Assert.ThrowsAsync<OperationBlockedException>(() => h.Store.ValidateAsync(op, h.Options, h.Handler, default));
    await h.SqlAsync("UPDATE firm_safety_states SET deployment_epoch = deployment_epoch + 1");
    Assert.False(await h.Store.RenewAsync(op, h.Options, default));
    Assert.False(await h.Store.TransitionAsync(op, h.Options, OperationState.REMOTE_STARTED, null, null, default));
    Assert.Equal(OperationState.CLAIMED, (await h.OperationAsync(id)).Status);
    await h.SqlAsync("UPDATE firm_safety_states SET operating_mode = 'RECOVERY_QUARANTINE'");
    Assert.Null(await h.ClaimAsync());
    Assert.Equal(0, await h.EffectCountAsync());
  }

  [Fact]
  public async Task ScopeForeignKeysChecksAndAppendOnlyEvidence_AreDatabaseEnforced()
  {
    await using var h = await Harness.CreateAsync();
    var id = (await h.EnqueueAsync()).Value;
    await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync("UPDATE durable_operations SET payload_json = 'tampered'"));
    await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync("UPDATE durable_operations SET attempt_token = -1"));
    await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync("UPDATE durable_operations SET status = 'INVALID'"));
    await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync("UPDATE durable_operations SET status = 'CLAIMED'"));
    await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync("DELETE FROM operation_events"));
    await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync("UPDATE durable_operations SET status = 'COMPLETED', completed_at = statement_timestamp(), result_identity = 'x', result_digest = NULL"));
    var op = (await h.ClaimAsync())!;
    await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync("UPDATE operation_attempts SET owner = 'forged'"));
    await h.FinishAsync(op);
    await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync("UPDATE durable_operations SET result_identity = 'rewritten'"));
    await using var db = h.Db();
    var original = await db.DurableOperations.AsNoTracking().SingleAsync();
    original.Id = Guid.NewGuid();
    db.DurableOperations.Add(original);
    var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    Assert.Equal("ux_operation_firm_key", Assert.IsType<PostgresException>(duplicate.InnerException).ConstraintName);
    db.Entry(original).State = EntityState.Detached;
    original.IdempotencyKey = "bad-link";
    original.EngagementId = Guid.NewGuid();
    db.DurableOperations.Add(original);
    var fk = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(fk.InnerException).SqlState);
    Assert.Equal(OperationState.COMPLETED, (await h.OperationAsync(id)).Status);
  }

  [Fact]
  public async Task InvalidTypedRequest_IsRejectedBeforeEnqueue()
  {
    await using var h = await Harness.CreateAsync();
    foreach (var payload in new[] { "{}", "{\"value\":\"a\",\"value\":\"b\"}", "{\"url\":\"https://example.com\"}" })
      await Assert.ThrowsAsync<OperationBlockedException>(() => h.EnqueueAsync(h.Request with { PayloadJson = payload }));
    await using var db = h.Db();
    Assert.Empty(await db.DurableOperations.ToListAsync());
  }

  [Fact]
  public async Task MissingGuardsAndMismatchedRequest_AreNotExecutable()
  {
    await using var h = await Harness.CreateAsync();
    var id = (await h.EnqueueAsync()).Value;
    var op = (await h.ClaimAsync())!;
    op.PayloadJson = "{\"value\":\"changed\"}";
    await Assert.ThrowsAsync<OperationBlockedException>(() => h.Store.ValidateAsync(op, h.Options, h.Handler, default));
    await h.SqlAsync("DELETE FROM client_safety_states");
    Assert.False((await h.EnqueueAsync(h.Request with { IdempotencyKey = "missing-guard" })).Succeeded);
    op = await h.OperationAsync(id);
    Assert.False(await h.Store.ValidateAsync(op, h.Options, h.Handler, default));
    Assert.Equal(0, await h.EffectCountAsync());
  }

  [Fact]
  public async Task LeaseLostDuringEffect_CancelsWorkWithoutPublishingStaleSuccess()
  {
    await using var h = await Harness.CreateAsync();
    h.Options = h.Options with { LeaseSeconds = 4, RenewalSeconds = 1 };
    h.Handler.Fault = "pause";
    var id = (await h.EnqueueAsync()).Value;
    var run = h.Dispatcher().ProcessNextAsync();
    await h.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await h.ExpireAsync();
    Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(5)));
    Assert.NotEqual(OperationState.COMPLETED, (await h.OperationAsync(id)).Status);
    Assert.Equal(1, await h.Store.ReapAsync(h.Options, default));
    await h.MakeDueAsync();
    Assert.True(await h.Dispatcher(new ProbeHandler(h.Factory)).ProcessNextAsync());
    Assert.Equal(OperationState.COMPLETED, (await h.OperationAsync(id)).Status);
    Assert.Equal(1, await h.EffectCountAsync());
  }

  [Fact]
  public async Task LeaseExpiresWhileRenewalWaitsForLock_CannotBeRevived()
  {
    await using var h = await Harness.CreateAsync();
    await h.EnqueueAsync();
    var op = (await h.ClaimAsync())!;
    await h.SqlAsync("UPDATE durable_operations SET lease_expires_at = statement_timestamp() + interval '1 second'");
    await using var locked = h.Db();
    await using var tx = await locked.Database.BeginTransactionAsync();
    await locked.DurableOperations.FromSqlInterpolated($"SELECT * FROM durable_operations WHERE id = {op.Id} FOR UPDATE").ToListAsync();
    var pid = ((NpgsqlConnection)locked.Database.GetDbConnection()).ProcessID;
    var renew = h.Store.RenewAsync(op, h.Options, default);
    await using var observer = h.Db();
    var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
    var blocked = false;
    while (!blocked && DateTimeOffset.UtcNow < deadline)
    {
      blocked = await observer.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE {pid} = ANY(pg_blocking_pids(pid))) AS \"Value\"").SingleAsync();
      if (!blocked) await Task.Delay(20);
    }
    Assert.True(blocked);
    await locked.Database.ExecuteSqlRawAsync("SELECT pg_sleep(1.1)");
    await tx.CommitAsync();
    Assert.False(await renew);
    Assert.Equal(1, await h.Store.ReapAsync(h.Options, default));
  }

  [Fact]
  public async Task InputChangesDuringEffect_PreventPublicationAfterSuccessfulProviderWrite()
  {
    await using var h = await Harness.CreateAsync();
    h.Handler.Fault = "pause";
    var id = (await h.EnqueueAsync()).Value;
    var run = h.Dispatcher().ProcessNextAsync();
    await h.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await h.SqlAsync("UPDATE simulated_targets SET revision = 2");
    h.Handler.Release.TrySetResult();
    Assert.True(await run);
    Assert.Equal(OperationState.AUTHORIZATION_BLOCKED, (await h.OperationAsync(id)).Status);
    Assert.Equal(1, await h.EffectCountAsync());
    await using var db = h.Db();
    Assert.False(await db.OperationEvents.AnyAsync(e => e.Kind == "operation.completed.v1"));
  }

  internal sealed class Harness : IAsyncDisposable
  {
    public required PgTestSchema Pg { get; init; }
    public required IAuditSphereDbContextFactory Factory { get; init; }
    public required PostgresOperationStore Store { get; init; }
    public required WorkerOptions Options { get; set; }
    public required OperationRequest Request { get; init; }
    public required ProbeHandler Handler { get; init; }
    public static async Task<Harness> CreateAsync()
    {
      var pg = await PgTestSchema.CreateAsync();
      var scope = await pg.SeedScopeAsync();
      var factory = new OperationContextFactory(new TestFactory(pg.Options));
      var target = Guid.NewGuid();
      await using var db = new AuditSphereDbContext(pg.Options);
      await db.Database.ExecuteSqlRawAsync("CREATE TABLE simulated_effects (operation_id uuid PRIMARY KEY, digest text NOT NULL); CREATE TABLE simulated_targets (id uuid PRIMARY KEY, firm_id uuid NOT NULL, revision bigint NOT NULL)");
      await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO simulated_targets VALUES ({target}, {scope.FirmId}, 1)");
      return new() { Pg = pg, Factory = factory, Store = new(factory), Options = new(scope.FirmId, "Test", true),
        Request = new(scope.FirmId, scope.ClientId, scope.EngagementId, "SyntheticEffect.v1", target, 1, "key", "{\"value\":\"fixture\"}"),
        Handler = new(factory) };
    }
    public AuditSphereDbContext Db() => new(Pg.Options);
    public async Task<CommandResult<Guid>> EnqueueAsync(OperationRequest? request = null)
    {
      await using var db = Db(); await using var tx = await db.Database.BeginTransactionAsync();
      var result = await Store.EnqueueAsync(db, request ?? Request, Handler, default);
      await tx.CommitAsync(); return result;
    }
    public Task<DurableOperation?> ClaimAsync(string owner = "worker") => Store.ClaimAsync(Options, [Handler.Definition], owner, false, default);
    public OperationDispatcher Dispatcher(ProbeHandler? handler = null) => new(Store, new([handler ?? Handler], Options), Options);
    public async Task<DurableOperation> OperationAsync(Guid id)
    { await using var db = Db(); return await db.DurableOperations.AsNoTracking().SingleAsync(o => o.Id == id); }
    public async Task SqlAsync(string sql) { await using var db = Db(); await db.Database.ExecuteSqlRawAsync(sql); }
    public Task MakeDueAsync() => SqlAsync("UPDATE durable_operations SET next_attempt_at = statement_timestamp() WHERE status IN ('PENDING','RETRY_WAIT','RESULT_UNCERTAIN')");
    public Task ExpireAsync() => SqlAsync("UPDATE durable_operations SET lease_expires_at = statement_timestamp() - interval '1 second' WHERE lease_owner IS NOT NULL");
    public async Task<int> EffectCountAsync()
    { await using var db = Db(); return await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM simulated_effects").SingleAsync(); }
    public async Task FinishAsync(DurableOperation op)
    {
      Assert.True(await Store.TransitionAsync(op, Options, OperationState.REMOTE_STARTED, null, null, default));
      var remote = await Handler.ExecuteEffectAsync(op, default);
      Assert.True(await Store.TransitionAsync(op, Options, OperationState.VERIFYING, null, null, default));
      Assert.True(await Store.CompleteAsync(op, Options, Handler, remote, default));
    }
    public ValueTask DisposeAsync() => Pg.DisposeAsync();
  }

  internal sealed class TestFactory(DbContextOptions<AuditSphereDbContext> options) : IDbContextFactory<AuditSphereDbContext>
  { public AuditSphereDbContext CreateDbContext() => new(options); }

  internal sealed class ProbeHandler(IAuditSphereDbContextFactory factory) : IOperationHandler
  {
    public OperationDefinition Definition { get; } = new("SyntheticEffect.v1", OperationMode.SIMULATED, OperationAuthority.SIMULATION);
    public string? Fault { get; set; }
    public bool FailPublication { get; set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string NormalizePayload(OperationRequest r)
    {
      using var doc = JsonDocument.Parse(r.PayloadJson);
      var fields = doc.RootElement.EnumerateObject().ToArray();
      if (fields.Length != 1 || fields[0].Name != "value" || fields[0].Value.ValueKind != JsonValueKind.String)
        throw new OperationBlockedException("invalid-probe");
      return JsonSerializer.Serialize(new { value = fields[0].Value.GetString() });
    }
    public OperationResult Expected(DurableOperation op) => new(op.Id.ToString("D"), Hashing.Sha256Hex("synthetic:" + op.TargetId));
    public async Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation op, CancellationToken ct)
    {
      var revisions = await db.Database.SqlQuery<long>($"SELECT revision AS \"Value\" FROM simulated_targets WHERE id = {op.TargetId} AND firm_id = {op.FirmId} FOR UPDATE").ToListAsync(ct);
      if (revisions.Count != 1 || revisions[0] != op.ExpectedRevision) throw new OperationBlockedException("target-changed", true);
    }
    public async Task<OperationResult> ExecuteEffectAsync(DurableOperation op, CancellationToken ct)
    {
      if (Fault == "retry") throw new SafeRetryException(TimeSpan.FromMinutes(10));
      if (Fault == "authorization") throw new OperationBlockedException("denied", true);
      if (Fault == "configuration") throw new OperationBlockedException("missing-configuration");
      var result = Expected(op);
      await using var remote = await factory.CreateAsync(ct);
      // Independent committed provider state survives a later application rollback.
      await remote.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO simulated_effects VALUES ({op.Id}, {result.Digest}) ON CONFLICT DO NOTHING", ct);
      Started.TrySetResult();
      if (Fault == "pause") await Release.Task.WaitAsync(ct);
      return await ReconcileAsync(op, ct);
    }
    public async Task<OperationResult> ReconcileAsync(DurableOperation op, CancellationToken ct)
    {
      await using var remote = await factory.CreateAsync(ct);
      var values = await remote.Database.SqlQuery<string>($"SELECT digest AS \"Value\" FROM simulated_effects WHERE operation_id = {op.Id}").ToListAsync(ct);
      var result = Expected(op);
      if (values.Count != 1 || values[0] != result.Digest) throw new OperationBlockedException("unverified-effect");
      return result;
    }
    public Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation op, OperationResult? remote, CancellationToken ct)
    {
      if (remote != Expected(op)) throw new OperationBlockedException("wrong-effect");
      db.OperationEvents.Add(new() { Id = Guid.NewGuid(), OperationId = op.Id, Token = op.AttemptToken,
        Executor = "probe", Kind = "probe.published.v1", OccurredAt = DateTimeOffset.UtcNow });
      if (FailPublication) throw new InvalidOperationException("Injected local failure after external success");
      return Task.FromResult(remote);
    }
  }
}
