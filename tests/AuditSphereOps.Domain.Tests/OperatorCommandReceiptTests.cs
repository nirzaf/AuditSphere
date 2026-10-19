using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class OperatorCommandReceiptTests
{
  [Theory]
  [InlineData(OperationCommandActions.Retry)]
  [InlineData(OperationCommandActions.Cancel)]
  [InlineData(OperationCommandActions.LiftQuarantine)]
  [InlineData(OperationCommandActions.ApproveRecoveryRestart)]
  public async Task FourActionsRetainOriginalReceiptBeforeCurrentTransitionGates(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await OperatorCommandReceiptSeed.SeedAsync(pg, action);
    await using var db = new AuditSphereDbContext(pg.Options);
    var request = await OperatorCommandReceiptSeed.ReviewAsync(db, world);
    var first = await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, request);
    Assert.True(first.Succeeded, first.Message);
    Assert.NotNull(first.Value);
    Assert.Equal(action, first.Value.Action);
    Assert.Equal(request.ExpectedRequestHash, first.Value.RequestHash);
    Assert.Equal(request.ReviewBasis, first.Value.Preview.ReviewBasis);
    Assert.Equal(world.Actor.UserId, first.Value.ActorId);
    Assert.Equal(world.Actor.SessionEpoch.ToString(CultureInfo.InvariantCulture), first.Value.ActorEpoch);
    Assert.NotEqual(JsonSerializer.Serialize(first.Value.CommandEvent.Before), JsonSerializer.Serialize(first.Value.CommandEvent.After));
    Assert.Equal(TimeSpan.Zero, first.Value.CreatedAt.Offset);
    Assert.Equal(TimeSpan.Zero, first.Value.CommandEvent.CreatedAt.Offset);

    // The completed transition itself closes its gate; additionally change the live firm gate.
    await db.FirmSafetyStates.Where(x => x.Id == world.Actor.FirmId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.OperatingMode, "RECOVERY_QUARANTINE"));
    var replay = await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, request);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(JsonSerializer.Serialize(first.Value), JsonSerializer.Serialize(replay.Value));
    var lookup = await OperationCommandWorkspace.LookupAsync(db, world.Actor, request.RequestId, request.ExpectedRequestHash!);
    Assert.True(lookup.Succeeded, lookup.Message);
    Assert.True(lookup.Value!.Found);
    Assert.Equal(JsonSerializer.Serialize(first.Value), JsonSerializer.Serialize(lookup.Value.Receipt));
    Assert.Single(await db.OperatorCommandReceipts.AsNoTracking().ToListAsync());
    Assert.Single(await db.OperatorCommandEvents.AsNoTracking().ToListAsync());
    Assert.Equal(action is OperationCommandActions.Retry or OperationCommandActions.Cancel ? 1 : 0,
      await db.OperationEvents.CountAsync());
  }

  [Fact]
  public async Task ConcurrentCancelWithSameRequestWaitsAndPublishesOneExactReceipt()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await OperatorCommandReceiptSeed.SeedAsync(pg, OperationCommandActions.Cancel);
    OperationCommandRequest request;
    await using (var setup = new AuditSphereDbContext(pg.Options))
      request = await OperatorCommandReceiptSeed.ReviewAsync(setup, world);

    var pause = new PauseAfterReceipt();
    var firstOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options;
    await using var firstDb = new AuditSphereDbContext(firstOptions);
    await using var secondDb = new AuditSphereDbContext(pg.Options);
    await using var observer = new AuditSphereDbContext(pg.Options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await secondDb.Database.OpenConnectionAsync(timeout.Token);
    await observer.Database.OpenConnectionAsync(timeout.Token);

    Task<CommandResult<OperationCommandReceiptView>>? firstTask = null;
    Task<CommandResult<OperationCommandReceiptView>>? secondTask = null;
    try
    {
      firstTask = OperationCommandWorkspace.ExecuteAsync(firstDb, world.Actor, request, timeout.Token);
      var blockingPid = await pause.Reached.Task.WaitAsync(timeout.Token);
      var waitingPid = ((Npgsql.NpgsqlConnection)secondDb.Database.GetDbConnection()).ProcessID;
      Assert.NotEqual(blockingPid, waitingPid);

      secondTask = OperationCommandWorkspace.ExecuteAsync(secondDb, world.Actor, request, timeout.Token);
      await WaitForBlockingPidAsync(observer, waitingPid, blockingPid, timeout.Token);
      Assert.False(secondTask.IsCompleted);

      pause.Release.TrySetResult(true);
      var first = await firstTask.WaitAsync(timeout.Token);
      var second = await secondTask.WaitAsync(timeout.Token);
      Assert.True(first.Succeeded, first.Message);
      Assert.True(second.Succeeded, second.Message);
      Assert.NotNull(first.Value);
      Assert.Equal(JsonSerializer.Serialize(first.Value), JsonSerializer.Serialize(second.Value));
      Assert.Equal(request.RequestId, first.Value.RequestId);
      Assert.Equal(request.ExpectedRequestHash, first.Value.RequestHash);

      Assert.Equal(1, await observer.OperatorCommandReceipts.AsNoTracking().CountAsync(x =>
        x.FirmId == world.Actor.FirmId && x.ActorId == world.Actor.UserId && x.RequestId == request.RequestId, timeout.Token));
      Assert.Equal(1, await observer.OperatorCommandEvents.AsNoTracking().CountAsync(x =>
        x.FirmId == world.Actor.FirmId && x.ActorId == world.Actor.UserId && x.RequestId == request.RequestId, timeout.Token));
      Assert.Equal(1, await observer.OperationEvents.AsNoTracking().CountAsync(x =>
        x.OperationId == world.OperationId && x.Kind == "operation.cancelled.v1", timeout.Token));
      var operation = await observer.DurableOperations.AsNoTracking().SingleAsync(x =>
        x.FirmId == world.Actor.FirmId && x.Id == world.OperationId, timeout.Token);
      Assert.Equal(OperationState.CANCELLED_WITH_DISPOSITION, operation.Status);
      Assert.Equal(request.Input.Disposition, operation.CancellationDisposition);
    }
    finally
    {
      pause.Release.TrySetResult(true);
      var tasks = new Task?[] { firstTask, secondTask }.Where(x => x is not null).Cast<Task>().ToArray();
      if (tasks.Length > 0)
      {
        var all = Task.WhenAll(tasks);
        try { await all.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException)
        {
          timeout.Cancel();
          try { await all.WaitAsync(TimeSpan.FromSeconds(5)); }
          catch { _ = all.Exception; }
        }
        catch { _ = all.Exception; }
      }
    }
  }

  [Theory]
  [InlineData(OperationCommandActions.Retry)]
  [InlineData(OperationCommandActions.Cancel)]
  [InlineData(OperationCommandActions.LiftQuarantine)]
  [InlineData(OperationCommandActions.ApproveRecoveryRestart)]
  public async Task MissingAssentStaleBasisAndChangedHashCannotPublish(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await OperatorCommandReceiptSeed.SeedAsync(pg, action);
    await using var db = new AuditSphereDbContext(pg.Options);
    var request = await OperatorCommandReceiptSeed.ReviewAsync(db, world);
    Assert.False((await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, request with { Reviewed = false })).Succeeded);
    Assert.False((await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, request with { ExpectedRequestHash = null })).Succeeded);
    Assert.False((await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, request with { ReviewBasis = null })).Succeeded);
    Assert.False((await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, request with { ExpectedRequestHash = new string('f', 64) })).Succeeded);
    await db.FirmSafetyStates.Where(x => x.Id == world.Actor.FirmId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.PolicyGeneration, x => x.PolicyGeneration + 1));
    var stale = await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, request);
    Assert.False(stale.Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    Assert.Empty(await db.OperatorCommandReceipts.ToListAsync());
    Assert.Empty(await db.OperatorCommandEvents.ToListAsync());
    Assert.Empty(await db.OperationEvents.ToListAsync());
    Assert.False(await db.RecoverySessions.AnyAsync(x => x.ApprovedRestartAt != null));
  }

  [Theory]
  [InlineData(OperationCommandActions.Retry)]
  [InlineData(OperationCommandActions.Cancel)]
  [InlineData(OperationCommandActions.LiftQuarantine)]
  [InlineData(OperationCommandActions.ApproveRecoveryRestart)]
  public async Task ChangedIntentConflictsAndFreshRequestCannotClaimExistingEffect(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await OperatorCommandReceiptSeed.SeedAsync(pg, action);
    await using var db = new AuditSphereDbContext(pg.Options);
    var request = await OperatorCommandReceiptSeed.ReviewAsync(db, world);
    Assert.True((await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, request)).Succeeded);
    var changed = action switch
    {
      OperationCommandActions.Cancel => request.Input with { Disposition = "Different exact disposition" },
      OperationCommandActions.ApproveRecoveryRestart => request.Input with { Findings = "Different exact findings" },
      OperationCommandActions.Retry => request.Input with { OperationId = Guid.NewGuid() },
      _ => request.Input with { RecoverySessionId = Guid.NewGuid() }
    };
    var conflict = await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, request with { Input = changed });
    Assert.Equal(ErrorCodes.IdempotencyConflict, conflict.ErrorCode);
    var hashConflict = await OperationCommandWorkspace.ExecuteAsync(db, world.Actor,
      request with { ExpectedRequestHash = new string('f', 64) });
    Assert.Equal(ErrorCodes.IdempotencyConflict, hashConflict.ErrorCode);
    var fresh = await OperationCommandWorkspace.PreviewAsync(db, world.Actor, new(Guid.NewGuid(), request.Input));
    Assert.True(fresh.Succeeded, fresh.Message);
    Assert.False(fresh.Value!.CanProceed);
    Assert.False((await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, request with { RequestId = Guid.NewGuid() })).Succeeded);
    Assert.Single(await db.OperatorCommandEvents.ToListAsync());
    Assert.Single(await db.OperatorCommandReceipts.ToListAsync());
  }

  [Fact]
  public async Task CanonicalCountersRemainExactBeyondJavascriptIntegerRange()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    const long exact = 9007199254740993;
    var world = await OperatorCommandReceiptSeed.SeedAsync(pg, OperationCommandActions.Retry,
      deploymentEpoch: exact, policyGeneration: exact, expectedRevision: exact, attemptToken: exact);
    await using var db = new AuditSphereDbContext(pg.Options);
    var reviewed = await OperatorCommandReceiptSeed.ReviewAsync(db, world);
    var receipt = await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, reviewed);
    Assert.True(receipt.Succeeded, receipt.Message);
    var before = receipt.Value!.Preview.Before;
    Assert.Equal("9007199254740993", before.DeploymentEpoch);
    Assert.Equal("9007199254740993", before.PolicyGeneration);
    Assert.Equal("9007199254740993", before.Operation!.ExpectedRevision);
    Assert.Equal("9007199254740993", before.Operation.AttemptToken);
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(before));
    Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("DeploymentEpoch").ValueKind);
    Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("Operation").GetProperty("AttemptToken").ValueKind);
  }

  [Fact]
  public async Task LookupIsActorPrivateAndCurrentSessionFencedEvenForOriginalReceipt()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await OperatorCommandReceiptSeed.SeedAsync(pg, OperationCommandActions.Cancel);
    await using var db = new AuditSphereDbContext(pg.Options);
    var request = await OperatorCommandReceiptSeed.ReviewAsync(db, world);
    Assert.True((await OperationCommandWorkspace.ExecuteAsync(db, world.Actor, request)).Succeeded);
    var other = PbcSeed.User(world.Actor.FirmId, "Staff");
    db.Users.Add(other);
    db.RoleGrants.Add(PbcSeed.AdminGrant(world.Actor.FirmId, other));
    await db.SaveChangesAsync();
    var foreign = await OperationCommandWorkspace.LookupAsync(db, PbcSeed.Actor(other, "Administrator"), request.RequestId, request.ExpectedRequestHash!);
    Assert.True(foreign.Succeeded, foreign.Message);
    Assert.False(foreign.Value!.Found);
    Assert.Null(foreign.Value.Receipt);
    var unknown = await OperationCommandWorkspace.LookupAsync(db, world.Actor, Guid.NewGuid(), request.ExpectedRequestHash!);
    Assert.True(unknown.Succeeded, unknown.Message);
    Assert.False(unknown.Value!.Found);
    Assert.Equal(ErrorCodes.IdempotencyConflict,
      (await OperationCommandWorkspace.LookupAsync(db, world.Actor, request.RequestId, new string('f', 64))).ErrorCode);
    await db.Users.Where(x => x.Id == world.Actor.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    Assert.False((await OperationCommandWorkspace.LookupAsync(db, world.Actor, request.RequestId, request.ExpectedRequestHash!)).Succeeded);
    var current = world.Actor with { SessionEpoch = world.Actor.SessionEpoch + 1 };
    Assert.Equal(ErrorCodes.GenerationStale,
      (await OperationCommandWorkspace.LookupAsync(db, current, request.RequestId, request.ExpectedRequestHash!)).ErrorCode);
    Assert.Equal(ErrorCodes.GenerationStale, (await OperationCommandWorkspace.ExecuteAsync(db, current, request)).ErrorCode);
  }

  private sealed class RevokeAfterReceipt(Guid actorId) : SaveChangesInterceptor
  {
    public int Calls { get; private set; }
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
      if (Calls == 0 && eventData.Context is AuditSphereDbContext db &&
          db.ChangeTracker.Entries<OperatorCommandReceipt>().Any())
      {
        Calls++;
        // Same isolated transaction: final authorization must roll this and every effect back to its savepoint.
        await db.RoleGrants.Where(x => x.UserId == actorId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
      }
      return result;
    }
  }

  private sealed class PauseAfterReceipt : SaveChangesInterceptor
  {
    private int fired;
    public TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
      CancellationToken ct = default)
    {
      if (eventData.Context is AuditSphereDbContext db && db.ChangeTracker.Entries<OperatorCommandReceipt>().Any() &&
          Interlocked.Exchange(ref fired, 1) == 0)
      {
        var pid = ((Npgsql.NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
        Reached.TrySetResult(pid);
        await Release.Task.WaitAsync(ct);
      }
      return result;
    }
  }

  private static async Task WaitForBlockingPidAsync(AuditSphereDbContext observer, int waitingPid, int blockingPid,
    CancellationToken ct)
  {
    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
    while (DateTimeOffset.UtcNow < deadline)
    {
      await observer.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()", ct);
      if (await observer.Database.SqlQuery<bool>($"SELECT {blockingPid} = ANY(pg_blocking_pids({waitingPid})) AS \"Value\"")
            .SingleAsync(ct)) return;
      await Task.Delay(TimeSpan.FromMilliseconds(20), ct);
    }
    Assert.Fail($"PostgreSQL did not report backend {waitingPid} blocked by {blockingPid}.");
  }

  [Theory]
  [InlineData(OperationCommandActions.Retry)]
  [InlineData(OperationCommandActions.Cancel)]
  [InlineData(OperationCommandActions.LiftQuarantine)]
  [InlineData(OperationCommandActions.ApproveRecoveryRestart)]
  public async Task FinalAuthorizationRevocationRollsBackEffectEventReceiptAndLeavesAmbientOwner(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await OperatorCommandReceiptSeed.SeedAsync(pg, action);
    OperationCommandRequest request;
    string before;
    await using (var read = new AuditSphereDbContext(pg.Options))
    {
      request = await OperatorCommandReceiptSeed.ReviewAsync(read, world);
      before = JsonSerializer.Serialize((await OperationCommandWorkspace.StateAsync(read, world.Actor, request.Input.OperationId, request.Input.RecoverySessionId)).Value!.Before);
    }
    var revoke = new RevokeAfterReceipt(world.Actor.UserId);
    await using (var writer = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(revoke).Options))
    {
      await using var tx = await writer.Database.BeginTransactionAsync();
      var refused = await OperationCommandWorkspace.ExecuteAsync(writer, world.Actor, request);
      Assert.False(refused.Succeeded);
      Assert.Equal(1, revoke.Calls);
      Assert.Same(tx, writer.Database.CurrentTransaction);
      await tx.CommitAsync();
    }
    await using var check = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await check.OperatorCommandReceipts.ToListAsync());
    Assert.Empty(await check.OperatorCommandEvents.ToListAsync());
    Assert.Empty(await check.OperationEvents.ToListAsync());
    Assert.False(await check.RoleGrants.AnyAsync(x => x.UserId == world.Actor.UserId && x.RevokedAt != null));
    var state = await OperationCommandWorkspace.StateAsync(check, world.Actor, request.Input.OperationId, request.Input.RecoverySessionId);
    Assert.True(state.Succeeded, state.Message);
    Assert.Equal(before, JsonSerializer.Serialize(state.Value!.Before));
  }
}
