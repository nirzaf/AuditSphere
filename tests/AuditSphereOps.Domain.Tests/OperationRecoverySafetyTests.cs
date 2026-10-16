using System.Data.Common;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class OperationRecoverySafetyTests
{
  private sealed class CountEffects(DurableOutboxTests.ProbeHandler inner) : IOperationHandler
  {
    public int Effects;
    public int Reconciliations;
    public bool BlockReconciliation;
    public OperationDefinition Definition => inner.Definition;
    public string NormalizePayload(OperationRequest request) => inner.NormalizePayload(request);
    public Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation operation, CancellationToken ct) => inner.LockTargetAsync(db, operation, ct);
    public Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation operation, OperationResult? remote, CancellationToken ct) => inner.PublishAsync(db, operation, remote, ct);
    public Task<OperationResult> ExecuteEffectAsync(DurableOperation operation, CancellationToken ct)
    {
      Effects++;
      return inner.ExecuteEffectAsync(operation, ct);
    }
    public Task<OperationResult> ReconcileAsync(DurableOperation operation, CancellationToken ct)
    {
      Reconciliations++;
      if (BlockReconciliation) throw new OperationBlockedException("SYNTHETIC reconciliation unavailable");
      return inner.ReconcileAsync(operation, ct);
    }
  }

  [Theory, InlineData(false), InlineData(true)]
  public async Task OperatorRearmAfterRemoteSuccessAndRollbackNeverRepeatsEffectIncludingBlockedReconciliation(bool blocked)
  {
    await using var h = await DurableOutboxTests.Harness.CreateAsync();
    var admin = PbcSeed.User(h.Options.FirmId, "Staff");
    await using (var setup = h.Db())
    {
      setup.Users.Add(admin);
      setup.RoleGrants.Add(PbcSeed.AdminGrant(h.Options.FirmId, admin));
      await setup.SaveChangesAsync();
    }
    var actor = PbcSeed.Actor(admin, "Administrator");
    var counted = new CountEffects(h.Handler);
    var dispatcher = new OperationDispatcher(h.Store, new DurableOperationRegistry([counted], h.Options), h.Options);
    h.Handler.FailPublication = true;
    var id = (await h.EnqueueAsync()).Value;
    Assert.True(await dispatcher.ProcessNextAsync());
    Assert.Equal(OperationState.RESULT_UNCERTAIN, (await h.OperationAsync(id)).Status);
    Assert.Equal(1, counted.Effects);
    h.Handler.FailPublication = false;
    if (blocked)
    {
      counted.BlockReconciliation = true;
      await h.MakeDueAsync();
      Assert.True(await dispatcher.ProcessNextAsync());
      var failed = await h.OperationAsync(id);
      Assert.Equal(OperationState.PROVIDER_BLOCKED, failed.Status);
      Assert.True(failed.IsReconciliation);
      counted.BlockReconciliation = false;
    }
    await using (var db = h.Db())
    {
      Assert.True((await OperationRecoveryService.RetryAsync(db, actor, new(id))).Succeeded);
      Assert.False((await OperationRecoveryService.CancelAsync(db, actor, new(id, "Cannot cancel an unknown effect"))).Succeeded);
    }
    Assert.True((await h.OperationAsync(id)).IsReconciliation);
    await h.MakeDueAsync();
    Assert.Null(await h.Store.ClaimAsync(h.Options, [counted.Definition], "ordinary-worker", false, default));
    Assert.True(await dispatcher.ProcessNextAsync());
    Assert.Equal(OperationState.COMPLETED, (await h.OperationAsync(id)).Status);
    Assert.Equal(1, counted.Effects);
    Assert.Equal(blocked ? 2 : 1, counted.Reconciliations);
    Assert.Equal(1, await h.EffectCountAsync());
  }

  [Fact]
  public async Task CurrentRecoveryEpochAndSessionMustBeApprovedAndStaleApprovalCannotLiftNewQuarantine()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    await using var db = new AuditSphereDbContext(pg.Options);
    var first = await OperationRecoveryService.BeginRecoverySessionAsync(db, actor, new("Synthetic restore A", 5, "Synthetic scope", "Pending reconciliation A"));
    Assert.True(first.Succeeded, first.Message);
    Assert.False((await OperationRecoveryService.LiftQuarantineAsync(db, actor)).Succeeded);
    var second = await OperationRecoveryService.BeginRecoverySessionAsync(db, actor, new("Synthetic restore B", 6, "Synthetic scope", "Pending reconciliation B"));
    Assert.True(second.Succeeded, second.Message);
    Assert.False((await OperationRecoveryService.ApproveRecoveryRestartAsync(db, actor, new(first.Value, "Stale findings"))).Succeeded);
    Assert.False((await OperationRecoveryService.BeginRecoverySessionAsync(db, actor, new("Duplicate epoch", 6, "Synthetic scope", "Findings"))).Succeeded);
    Assert.Equal("RECOVERY_QUARANTINE", await db.FirmSafetyStates.Select(x => x.OperatingMode).SingleAsync());
    Assert.True((await OperationRecoveryService.ApproveRecoveryRestartAsync(db, actor, new(second.Value, "Exact B findings reconciled"))).Succeeded);
    Assert.True((await OperationRecoveryService.LiftQuarantineAsync(db, actor)).Succeeded);
    Assert.Equal(2L, await db.FirmSafetyStates.Select(x => x.DeploymentEpoch).SingleAsync());
    Assert.True((await OperationRecoveryService.ApproveRecoveryRestartAsync(db, actor, new(second.Value, "Exact B findings reconciled"))).Succeeded);
    Assert.False((await OperationRecoveryService.ApproveRecoveryRestartAsync(db, actor, new(second.Value, "Changed findings"))).Succeeded);
    var third = await OperationRecoveryService.BeginRecoverySessionAsync(db, actor, new("Synthetic restore C", 7, "Synthetic scope", "Pending C"));
    Assert.True(third.Succeeded, third.Message);
    Assert.False((await OperationRecoveryService.ApproveRecoveryRestartAsync(db, actor, new(second.Value, "Exact B findings reconciled"))).Succeeded);
    Assert.False((await OperationRecoveryService.LiftQuarantineAsync(db, actor)).Succeeded);
  }

  private sealed class ObserveFirmLock : DbCommandInterceptor
  {
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
      if (command.CommandText.Contains("firm_safety_states", StringComparison.Ordinal) && command.CommandText.Contains("FOR ", StringComparison.Ordinal)) Reached.TrySetResult();
      return ValueTask.FromResult(result);
    }
  }

  [Theory]
  [InlineData("retry")]
  [InlineData("cancel")]
  [InlineData("begin")]
  [InlineData("approve")]
  [InlineData("lift")]
  public async Task RevokedAdministratorWaitingForFirmGuardCannotPublishRecoveryTransition(string command)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var id = Guid.NewGuid();
    Guid sessionId = default;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.DurableOperations.Add(Operation(f.FirmId, id, command == "retry" ? OperationState.DEAD_LETTER : OperationState.PENDING));
      await setup.SaveChangesAsync();
      if (command == "approve") sessionId = (await OperationRecoveryService.BeginRecoverySessionAsync(setup, actor, new("Synthetic restore", 1, "Scope", "Pending findings"))).Value;
      if (command == "lift") await setup.FirmSafetyStates.ExecuteUpdateAsync(s => s.SetProperty(x => x.OperatingMode, "RECOVERY_QUARANTINE"));
    }
    await using var blocker = new AuditSphereDbContext(pg.Options);
    await using var tx = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {f.FirmId} FOR UPDATE").SingleAsync();
    var signal = new ObserveFirmLock();
    await using var writer = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(signal).Options);
    async Task<bool> Attempt() => command switch
    {
      "retry" => (await OperationRecoveryService.RetryAsync(writer, actor, new(id))).Succeeded,
      "cancel" => (await OperationRecoveryService.CancelAsync(writer, actor, new(id, "Synthetic cancellation"))).Succeeded,
      "begin" => (await OperationRecoveryService.BeginRecoverySessionAsync(writer, actor, new("Synthetic restore", 1, "Scope", "Findings"))).Succeeded,
      "approve" => (await OperationRecoveryService.ApproveRecoveryRestartAsync(writer, actor, new(sessionId, "Reconciled findings"))).Succeeded,
      _ => (await OperationRecoveryService.LiftQuarantineAsync(writer, actor)).Succeeded
    };
    var attempt = Attempt();
    await signal.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    await blocker.RoleGrants.Where(x => x.UserId == actor.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    await tx.CommitAsync();
    Assert.False(await attempt.WaitAsync(TimeSpan.FromSeconds(15)));
    await using var check = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await check.OperationEvents.ToListAsync());
    Assert.Equal(command == "retry" ? OperationState.DEAD_LETTER : OperationState.PENDING, await check.DurableOperations.Select(x => x.Status).SingleAsync());
    Assert.Equal(command is "approve" or "lift" ? "RECOVERY_QUARANTINE" : "LOCAL_ONLY", await check.FirmSafetyStates.Select(x => x.OperatingMode).SingleAsync());
    Assert.Equal(command == "approve" ? 1 : 0, await check.RecoverySessions.CountAsync());
    Assert.False(await check.RecoverySessions.AnyAsync(x => x.ApprovedRestartAt != null));
  }

  private sealed class RevokeAfterRead(Func<DbCommand, bool> matches, Func<Task> revoke) : DbCommandInterceptor
  {
    public int Calls;
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken ct = default)
    {
      if (matches(command) && Interlocked.CompareExchange(ref Calls, 1, 0) == 0) await revoke();
      return result;
    }
  }

  [Theory, InlineData("list"), InlineData("mode"), InlineData("workspace")]
  public async Task FinalProjectionRefusesCurrentAdministratorRevokedDuringRead(string query)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var interceptor = new RevokeAfterRead(c => c.CommandText.Contains(query == "mode" ? "firm_safety_states" : "durable_operations", StringComparison.Ordinal), async () =>
    {
      await using var revoke = new AuditSphereDbContext(pg.Options);
      await revoke.RoleGrants.Where(x => x.UserId == actor.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options);
    var succeeded = query switch
    {
      "list" => (await OperationRecoveryService.ListAsync(reader, actor)).Succeeded,
      "mode" => (await OperationRecoveryService.GetFirmOperatingModeAsync(reader, actor)).Succeeded,
      _ => (await OperationRecoveryService.WorkspaceAsync(reader, actor)).Succeeded
    };
    Assert.False(succeeded);
    Assert.Equal(1, interceptor.Calls);
  }

  [Fact]
  public async Task RepeatedCancellationRequiresExactNormalizedOriginalDisposition()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var id = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.DurableOperations.Add(Operation(f.FirmId, id, OperationState.PENDING));
    await db.SaveChangesAsync();
    Assert.True((await OperationRecoveryService.CancelAsync(db, actor, new(id, "  Original disposition  "))).Succeeded);
    Assert.True((await OperationRecoveryService.CancelAsync(db, actor, new(id, "Original disposition"))).Succeeded);
    var changed = await OperationRecoveryService.CancelAsync(db, actor, new(id, "Changed disposition"));
    Assert.Equal(ErrorCodes.IdempotencyConflict, changed.ErrorCode);
    Assert.Equal("Original disposition", await db.DurableOperations.Select(x => x.CancellationDisposition).SingleAsync());
    Assert.Single(await db.OperationEvents.ToListAsync());
  }

  private static DurableOperation Operation(Guid firmId, Guid id, OperationState status) => new()
  {
    Id = id, FirmId = firmId, OperationKind = "SYNTHETIC-RECOVERY", TargetId = Guid.NewGuid(), ExpectedRevision = 1,
    CorrelationId = Guid.NewGuid(), IdempotencyKey = id.ToString("D"), RequestDigest = new string('a', 64),
    RequestBytes = [1], Status = status, NextAttemptAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
  };
}
