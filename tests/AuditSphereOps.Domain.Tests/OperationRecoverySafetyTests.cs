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
      var retry = await ReviewedCommandAsync(db, actor,
        new(OperationCommandActions.Retry, OperationId: id));
      Assert.True((await OperationCommandWorkspace.ExecuteAsync(db, actor, retry)).Succeeded);
      var cancel = await ReviewedCommandAsync(db, actor,
        new(OperationCommandActions.Cancel, OperationId: id, Disposition: "Cannot cancel an unknown effect"),
        expectedCanProceed: false);
      Assert.False((await OperationCommandWorkspace.ExecuteAsync(db, actor, cancel)).Succeeded);
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

  [Theory]
  [InlineData(OperationCommandActions.Retry)]
  [InlineData(OperationCommandActions.Cancel)]
  [InlineData(OperationCommandActions.LiftQuarantine)]
  [InlineData(OperationCommandActions.ApproveRecoveryRestart)]
  public async Task DirectRecoveryMutatorsFailClosedWithoutPublishing(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await OperatorCommandReceiptSeed.SeedAsync(pg, action);
    await using var db = new AuditSphereDbContext(pg.Options);
    var result = action switch
    {
      OperationCommandActions.Retry => await OperationRecoveryService.RetryAsync(db, world.Actor, new(world.OperationId)),
      OperationCommandActions.Cancel => await OperationRecoveryService.CancelAsync(db, world.Actor,
        new(world.OperationId, "Synthetic exact cancellation\r\nسبب الإلغاء")),
      OperationCommandActions.ApproveRecoveryRestart => await OperationRecoveryService.ApproveRecoveryRestartAsync(db, world.Actor,
        new(world.Input.RecoverySessionId!.Value, world.Input.Findings!)),
      _ => await OperationRecoveryService.LiftQuarantineAsync(db, world.Actor)
    };

    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, result.ErrorCode);
    Assert.Empty(await db.OperationEvents.AsNoTracking().ToListAsync());
    Assert.Empty(await db.OperatorCommandEvents.AsNoTracking().ToListAsync());
    Assert.Empty(await db.OperatorCommandReceipts.AsNoTracking().ToListAsync());
    if (action is OperationCommandActions.Retry or OperationCommandActions.Cancel)
    {
      var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == world.OperationId);
      Assert.Equal(action == OperationCommandActions.Retry ? OperationState.RESULT_UNCERTAIN : OperationState.PENDING, operation.Status);
    }
    if (action == OperationCommandActions.LiftQuarantine)
      Assert.Equal("RECOVERY_QUARANTINE", await db.FirmSafetyStates.Select(x => x.OperatingMode).SingleAsync());
    if (action == OperationCommandActions.ApproveRecoveryRestart)
      Assert.False(await db.RecoverySessions.AnyAsync(x => x.ApprovedRestartAt != null));
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
    var liftBeforeApproval = await ReviewedCommandAsync(db, actor,
      new(OperationCommandActions.LiftQuarantine, RecoverySessionId: first.Value), expectedCanProceed: false);
    Assert.False((await OperationCommandWorkspace.ExecuteAsync(db, actor, liftBeforeApproval)).Succeeded);
    var second = await OperationRecoveryService.BeginRecoverySessionAsync(db, actor, new("Synthetic restore B", 6, "Synthetic scope", "Pending reconciliation B"));
    Assert.True(second.Succeeded, second.Message);
    var staleApproval = await OperationCommandWorkspace.PreviewAsync(db, actor,
      new(Guid.NewGuid(), new(OperationCommandActions.ApproveRecoveryRestart,
        RecoverySessionId: first.Value, Findings: "Stale findings")));
    Assert.False(staleApproval.Succeeded);
    Assert.False((await OperationRecoveryService.BeginRecoverySessionAsync(db, actor, new("Duplicate epoch", 6, "Synthetic scope", "Findings"))).Succeeded);
    Assert.Equal("RECOVERY_QUARANTINE", await db.FirmSafetyStates.Select(x => x.OperatingMode).SingleAsync());
    var approval = await OperatorCommandReceiptSeed.ReviewAsync(db,
      new(f, Guid.Empty, new(OperationCommandActions.ApproveRecoveryRestart,
        RecoverySessionId: second.Value, Findings: "Exact B findings reconciled")));
    Assert.True((await OperationCommandWorkspace.ExecuteAsync(db, actor, approval)).Succeeded);
    Assert.Equal(2L, await db.FirmSafetyStates.Select(x => x.DeploymentEpoch).SingleAsync());
    var liftAfterApproval = await ReviewedCommandAsync(db, actor,
      new(OperationCommandActions.LiftQuarantine, RecoverySessionId: second.Value), expectedCanProceed: false);
    Assert.False((await OperationCommandWorkspace.ExecuteAsync(db, actor, liftAfterApproval)).Succeeded);
    Assert.True((await OperationCommandWorkspace.ExecuteAsync(db, actor, approval)).Succeeded);
    var changedApproval = await OperationCommandWorkspace.ExecuteAsync(db, actor,
      approval with { Input = approval.Input with { Findings = "Changed findings" } });
    Assert.False(changedApproval.Succeeded);
    Assert.Equal(ErrorCodes.IdempotencyConflict, changedApproval.ErrorCode);
    var third = await OperationRecoveryService.BeginRecoverySessionAsync(db, actor, new("Synthetic restore C", 7, "Synthetic scope", "Pending C"));
    Assert.True(third.Succeeded, third.Message);
    var staleApprovalAfterThirdSession = await OperationCommandWorkspace.PreviewAsync(db, actor,
      new(Guid.NewGuid(), approval.Input with { RecoverySessionId = second.Value }));
    Assert.False(staleApprovalAfterThirdSession.Succeeded);
    var liftBeforeThirdApproval = await ReviewedCommandAsync(db, actor,
      new(OperationCommandActions.LiftQuarantine, RecoverySessionId: third.Value), expectedCanProceed: false);
    Assert.False((await OperationCommandWorkspace.ExecuteAsync(db, actor, liftBeforeThirdApproval)).Succeeded);
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
    OperationCommandRequest? reviewed = null;
    if (command != "begin")
    {
      var input = command switch
      {
        "retry" => new OperationCommandInput(OperationCommandActions.Retry, OperationId: id),
        "cancel" => new OperationCommandInput(OperationCommandActions.Cancel, OperationId: id, Disposition: "Synthetic cancellation"),
        "approve" => new OperationCommandInput(OperationCommandActions.ApproveRecoveryRestart,
          RecoverySessionId: sessionId, Findings: "Reconciled findings"),
        _ => new OperationCommandInput(OperationCommandActions.LiftQuarantine)
      };
      await using var review = new AuditSphereDbContext(pg.Options);
      reviewed = await OperatorCommandReceiptSeed.ReviewAsync(review, new(f, id, input));
    }
    await using var blocker = new AuditSphereDbContext(pg.Options);
    await using var tx = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {f.FirmId} FOR UPDATE").SingleAsync();
    var signal = new ObserveFirmLock();
    await using var writer = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(signal).Options);
    async Task<bool> Attempt() => command switch
    {
      "retry" => (await OperationCommandWorkspace.ExecuteAsync(writer, actor, reviewed!)).Succeeded,
      "cancel" => (await OperationCommandWorkspace.ExecuteAsync(writer, actor, reviewed!)).Succeeded,
      "begin" => (await OperationRecoveryService.BeginRecoverySessionAsync(writer, actor, new("Synthetic restore", 1, "Scope", "Findings"))).Succeeded,
      "approve" => (await OperationCommandWorkspace.ExecuteAsync(writer, actor, reviewed!)).Succeeded,
      _ => (await OperationCommandWorkspace.ExecuteAsync(writer, actor, reviewed!)).Succeeded
    };
    var attempt = Attempt();
    await signal.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    await blocker.RoleGrants.Where(x => x.UserId == actor.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    await tx.CommitAsync();
    Assert.False(await attempt.WaitAsync(TimeSpan.FromSeconds(15)));
    await using var check = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await check.OperationEvents.ToListAsync());
    Assert.Empty(await check.OperatorCommandEvents.ToListAsync());
    Assert.Empty(await check.OperatorCommandReceipts.ToListAsync());
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
    var request = await OperatorCommandReceiptSeed.ReviewAsync(db,
      new(f, id, new(OperationCommandActions.Cancel, OperationId: id, Disposition: "  Original disposition  ")));
    Assert.True((await OperationCommandWorkspace.ExecuteAsync(db, actor, request)).Succeeded);
    Assert.True((await OperationCommandWorkspace.ExecuteAsync(db, actor, request)).Succeeded);
    var changed = await OperationCommandWorkspace.ExecuteAsync(db, actor,
      request with { Input = request.Input with { Disposition = "Changed disposition" } });
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

  private static async Task<OperationCommandRequest> ReviewedCommandAsync(
    AuditSphereDbContext db, ActorContext actor, OperationCommandInput input, bool expectedCanProceed = true)
  {
    var request = new OperationCommandRequest(Guid.NewGuid(), input);
    var preview = await OperationCommandWorkspace.PreviewAsync(db, actor, request);
    Assert.True(preview.Succeeded, preview.Message);
    Assert.Equal(expectedCanProceed, preview.Value!.CanProceed);
    return request with
    {
      Input = preview.Value.CanonicalInput,
      ReviewBasis = preview.Value.ReviewBasis,
      ExpectedRequestHash = preview.Value.RequestHash,
      Reviewed = true
    };
  }
}
