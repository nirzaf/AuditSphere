using System.Text.Json;
using System.Data.Common;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

// Synthetic fixtures only: these tests never establish professional or live release acceptance.
public sealed partial class ClientAccountingTests
{
  private static async Task<PeriodMaintenanceRequest> ReviewedMaintenanceAsync(AuditSphereDbContext db, ActorContext actor,
    Guid clientId, string action, RollForwardPeriodRequest? forward = null, CreatePeriodRestatementRequest? restatement = null, Guid? id = null)
  {
    var state = await PeriodMaintenanceWorkspace.StateAsync(db, actor, clientId, action, forward?.PriorPeriodId ?? restatement?.PeriodId, id);
    Assert.True(state.Succeeded, state.Message);
    var request = new PeriodMaintenanceRequest(Guid.NewGuid(), state.Value!.ReviewBasis, action, forward, restatement, id);
    var preview = await PeriodMaintenanceWorkspace.PreviewAsync(db, actor, clientId, request);
    Assert.True(preview.Succeeded, preview.Message);
    Assert.True(preview.Value!.State.CanProceed, string.Join(';', preview.Value.State.Blockers));
    return preview.Value.CanonicalInput with { Reviewed = true, ExpectedRequestHash = preview.Value.RequestHash };
  }

  private static async Task<Guid> ClosedMaintenancePeriodAsync(PgTestSchema pg, Scope scope)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var result = await ClientAccountingService.CreatePeriodAsync(db, Actor(scope.Preparer, "AccountingPreparer"),
      new(scope.ClientA, "2026", new(2026, 1, 1), new(2026, 12, 31), "STATUTORY", "QAR"));
    Assert.True(result.Succeeded, result.Message);
    Assert.True((await ClientAccountingService.ClosePeriodAsync(db, Actor(scope.Reviewer, "AccountingReviewer"), result.Value, "SYNTHETIC closed fixture")).Succeeded);
    return result.Value;
  }

  [Fact, Trait("Profile", "Database")]
  public async Task Maintenance_AllThreeActionsRecoverOriginalSnapshotsBeforeCurrentGates()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer"); var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var period = await ClosedMaintenancePeriodAsync(pg, scope);
    await using var db = new AuditSphereDbContext(pg.Options);
    var original = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "receipt-original");
    var revised = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 90m, "CASH", "receipt-revised");
    await db.SaveChangesAsync();
    var forward = await ReviewedMaintenanceAsync(db, preparer, scope.ClientA, PeriodMaintenanceActions.RollForward,
      new(scope.ClientA, period, "2027", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR", new('a', 64), 100m, 100m, "SYNTHETIC opening", ExpectedPriorRevision: 2));
    var f = await PeriodMaintenanceWorkspace.ExecuteAsync(db, preparer, scope.ClientA, forward);
    Assert.True(f.Succeeded, f.Message);
    var create = await ReviewedMaintenanceAsync(db, preparer, scope.ClientA, PeriodMaintenanceActions.CreateRestatement, restatement:
      new(scope.ClientA, period, original, revised, "IAS 8", "SYNTHETIC prior-period correction", "SYNTHETIC evidence", PeriodRestatementChangeTypes.RestatedError, "FY2026"));
    var c = await PeriodMaintenanceWorkspace.ExecuteAsync(db, preparer, scope.ClientA, create);
    Assert.True(c.Succeeded, c.Message);
    await db.RoleGrants.Where(x => x.UserId == reviewer.UserId && x.Role != "AccountingReviewer").ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    var approve = await ReviewedMaintenanceAsync(db, reviewer, scope.ClientA, PeriodMaintenanceActions.ApproveRestatement, id: c.Value!.Result.RestatementId);
    var a = await PeriodMaintenanceWorkspace.ExecuteAsync(db, reviewer, scope.ClientA, approve);
    Assert.True(a.Succeeded, a.Message);
    Assert.Equal("APPROVED", a.Value!.Result.Status);
    Assert.True((await ClientAccountingService.ReopenPeriodAsync(db, Actor(scope.Partner, "Partner"), period, "SYNTHETIC later amendment")).Succeeded);
    foreach (var (actor, request, receipt) in new[] { (preparer, forward, f.Value!), (preparer, create, c.Value!), (reviewer, approve, a.Value!) })
    {
      await using var fresh = new AuditSphereDbContext(pg.Options);
      var replay = await PeriodMaintenanceWorkspace.ExecuteAsync(fresh, actor, scope.ClientA, request);
      Assert.True(replay.Succeeded, replay.Message); Assert.Equal(receipt, replay.Value);
      var lookup = await PeriodMaintenanceWorkspace.LookupAsync(fresh, actor, scope.ClientA, request.RequestId, receipt.RequestHash);
      Assert.True(lookup.Succeeded, lookup.Message); Assert.True(lookup.Value!.Found); Assert.Equal(receipt, lookup.Value.Receipt);
    }
    Assert.Equal("SUBMITTED", c.Value!.Result.Status); // immutable creation receipt, although the record is now approved
    Assert.Equal(3, await db.PeriodMaintenanceReceipts.CountAsync());
    Assert.Equal(2, await db.ClientReportingPeriods.CountAsync());
    Assert.Single(await db.ClientPeriodRestatements.ToListAsync());
    Assert.Equal(2, await db.FinancialPackages.CountAsync());
    var rid = a.Value.Id;
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE period_maintenance_receipts SET action='CREATE_RESTATEMENT' WHERE id={rid}"));
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM period_maintenance_receipts WHERE id={rid}"));
  }

  [Fact, Trait("Profile", "Database")]
  public async Task Maintenance_ExactPreviewHashRequiredAndChangedFieldsCannotReuseIdentity()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var scope = await SeedAsync(pg); var actor = Actor(scope.Preparer, "AccountingPreparer");
    var period = await ClosedMaintenancePeriodAsync(pg, scope); await using var db = new AuditSphereDbContext(pg.Options);
    var r = await ReviewedMaintenanceAsync(db, actor, scope.ClientA, PeriodMaintenanceActions.RollForward,
      new(scope.ClientA, period, "2027", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR", new('a', 64), 1m, 1m, "SYNTHETIC opening", ExpectedPriorRevision: 2));
    Assert.False((await PeriodMaintenanceWorkspace.ExecuteAsync(db, actor, scope.ClientA, r with { Reviewed = false })).Succeeded);
    Assert.False((await PeriodMaintenanceWorkspace.ExecuteAsync(db, actor, scope.ClientA, r with { ExpectedRequestHash = null })).Succeeded);
    var changed = r with { RollForward = r.RollForward! with { EvidenceReference = "Changed exact evidence" } };
    Assert.Equal(ErrorCodes.IdempotencyConflict, (await PeriodMaintenanceWorkspace.ExecuteAsync(db, actor, scope.ClientA, changed)).ErrorCode);
    Assert.Empty(await db.PeriodMaintenanceReceipts.ToListAsync());
    var result = await PeriodMaintenanceWorkspace.ExecuteAsync(db, actor, scope.ClientA, r); Assert.True(result.Succeeded, result.Message);
    // A newly calculated preview hash still cannot reuse a previously committed request identity.
    var state = await PeriodMaintenanceWorkspace.StateAsync(db, actor, scope.ClientA, r.Action, period);
    var other = r with { RollForward = r.RollForward! with { PeriodCode = "2027-B" }, ReviewBasis = state.Value!.ReviewBasis, Reviewed = false, ExpectedRequestHash = null };
    var preview = await PeriodMaintenanceWorkspace.PreviewAsync(db, actor, scope.ClientA, other); Assert.True(preview.Succeeded, preview.Message);
    Assert.Equal(ErrorCodes.IdempotencyConflict, (await PeriodMaintenanceWorkspace.ExecuteAsync(db, actor, scope.ClientA,
      other with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash })).ErrorCode);
    Assert.Equal(ErrorCodes.IdempotencyConflict, (await PeriodMaintenanceWorkspace.LookupAsync(db, actor, scope.ClientA, r.RequestId, new('b', 64))).ErrorCode);
  }


  [Theory, InlineData("ROLL_FORWARD"), InlineData("CREATE_RESTATEMENT"), InlineData("APPROVE_RESTATEMENT"), Trait("Profile", "Database")]
  public async Task Maintenance_ConcurrentIdenticalIntentPublishesOneEffectAndOneReceipt(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync(); var scope = await SeedAsync(pg);
    var actor = action == PeriodMaintenanceActions.ApproveRestatement ? Actor(scope.Reviewer, "AccountingReviewer") : Actor(scope.Preparer, "AccountingPreparer");
    var period = await ClosedMaintenancePeriodAsync(pg, scope); PeriodMaintenanceRequest request;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      if (action == PeriodMaintenanceActions.RollForward)
        request = await ReviewedMaintenanceAsync(db, actor, scope.ClientA, action,
          new(scope.ClientA, period, "2027", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR", new('a', 64), 1m, 1m, "SYNTHETIC opening", ExpectedPriorRevision: 2));
      else
      {
        var original = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "concurrent-original");
        var revised = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 90m, "CASH", "concurrent-revised"); await db.SaveChangesAsync();
        var fields = new CreatePeriodRestatementRequest(scope.ClientA, period, original, revised, "IAS 8", "SYNTHETIC correction", "SYNTHETIC evidence", PeriodRestatementChangeTypes.RestatedError, "FY2026");
        if (action == PeriodMaintenanceActions.CreateRestatement) request = await ReviewedMaintenanceAsync(db, actor, scope.ClientA, action, restatement: fields);
        else
        {
          var created = await ClientAccountingService.CreatePeriodRestatementAsync(db, Actor(scope.Preparer, "AccountingPreparer"), fields); Assert.True(created.Succeeded, created.Message);
          request = await ReviewedMaintenanceAsync(db, actor, scope.ClientA, action, id: created.Value);
        }
      }
    }
    async Task<CommandResult<PeriodMaintenanceReceiptView>> Publish()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await PeriodMaintenanceWorkspace.ExecuteAsync(db, actor, scope.ClientA, request);
    }
    var results = await Task.WhenAll(Publish(), Publish());
    Assert.All(results, x => Assert.True(x.Succeeded, x.Message)); Assert.Equal(results[0].Value, results[1].Value);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Single(await verify.PeriodMaintenanceReceipts.ToListAsync());
    Assert.Equal(action == PeriodMaintenanceActions.RollForward ? 1 : 0, await verify.OpeningBalanceBridges.CountAsync());
    Assert.Equal(action == PeriodMaintenanceActions.RollForward ? 2 : 1, await verify.ClientReportingPeriods.CountAsync());
    Assert.Equal(action == PeriodMaintenanceActions.RollForward ? 0 : 1, await verify.ClientPeriodRestatements.CountAsync());
  }

  [Fact, Trait("Profile", "Database")]
  public async Task Maintenance_StaleBasisAndSelfReviewCannotPublish()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var scope = await SeedAsync(pg); var actor = Actor(scope.Preparer, "AccountingPreparer");
    var period = await ClosedMaintenancePeriodAsync(pg, scope); await using var db = new AuditSphereDbContext(pg.Options);
    var r = await ReviewedMaintenanceAsync(db, actor, scope.ClientA, PeriodMaintenanceActions.RollForward,
      new(scope.ClientA, period, "2027", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR", new('a', 64), 1m, 1m, "SYNTHETIC opening", ExpectedPriorRevision: 2));
    await using (var other = new AuditSphereDbContext(pg.Options))
      Assert.True((await ClientAccountingService.ReopenPeriodAsync(other, Actor(scope.Partner, "Partner"), period, "SYNTHETIC amendment")).Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, (await PeriodMaintenanceWorkspace.ExecuteAsync(db, actor, scope.ClientA, r)).ErrorCode);
    Assert.Empty(await db.PeriodMaintenanceReceipts.ToListAsync()); Assert.Single(await db.ClientReportingPeriods.ToListAsync());
  }

  [Fact, Trait("Profile", "Database")]
  public async Task Maintenance_RevocationHoldingFirmGuardWinsBeforePublication()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var scope = await SeedAsync(pg); var actor = Actor(scope.Preparer, "AccountingPreparer");
    var period = await ClosedMaintenancePeriodAsync(pg, scope); PeriodMaintenanceRequest r;
    await using (var db = new AuditSphereDbContext(pg.Options)) r = await ReviewedMaintenanceAsync(db, actor, scope.ClientA, PeriodMaintenanceActions.RollForward,
      new(scope.ClientA, period, "2027", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR", new('a', 64), 1m, 1m, "SYNTHETIC opening", ExpectedPriorRevision: 2));
    await using var revoke = new AuditSphereDbContext(pg.Options); await using var tx = await revoke.Database.BeginTransactionAsync();
    await revoke.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM firm_safety_states WHERE id={scope.FirmId} FOR UPDATE");
    await using var publishDb = new AuditSphereDbContext(pg.Options);
    var pending = PeriodMaintenanceWorkspace.ExecuteAsync(publishDb, actor, scope.ClientA, r);
    await AssertMaintenanceLockWaitAsync(revoke, scope.FirmId);
    await revoke.Database.ExecuteSqlInterpolatedAsync($"UPDATE role_grants SET revoked_at=now() WHERE firm_id={scope.FirmId} AND user_id={actor.UserId}");
    await tx.CommitAsync();
    Assert.False((await pending).Succeeded);
    await using var verify = new AuditSphereDbContext(pg.Options); Assert.Empty(await verify.PeriodMaintenanceReceipts.ToListAsync());
    Assert.Single(await verify.ClientReportingPeriods.ToListAsync());
  }

  // PostgreSQL observation, not a timing assertion: prove the contender reached a database lock wait.
  private static async Task AssertMaintenanceLockWaitAsync(AuditSphereDbContext observer, Guid firmId)
  {
    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
    while (DateTimeOffset.UtcNow < deadline)
    {
      await observer.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
      var count = await observer.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE '%firm_safety_states%'").SingleAsync();
      if (count > 0) return;
      await Task.Delay(20);
    }
    Assert.Fail($"No maintenance lock waiter observed for synthetic firm {firmId}.");
  }
  private sealed class MaintenanceReceiptPause(bool refuse) : SaveChangesInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
    {
      if (e.Context!.ChangeTracker.Entries<PeriodMaintenanceReceipt>().Any(x => x.State == EntityState.Added))
      {
        Reached.TrySetResult(true); await Release.Task.WaitAsync(ct);
        if (refuse) throw new InvalidOperationException("SYNTHETIC receipt refusal");
      }
      return result;
    }
  }

  [Theory, InlineData(false), InlineData(true), Trait("Profile", "Database")]
  public async Task Maintenance_LookupWaitsForPendingPublicationOrRollback(bool refuse)
  {
    await using var pg = await PgTestSchema.CreateAsync(); var scope = await SeedAsync(pg); var actor = Actor(scope.Preparer, "AccountingPreparer");
    var period = await ClosedMaintenancePeriodAsync(pg, scope); PeriodMaintenanceRequest r;
    await using (var db = new AuditSphereDbContext(pg.Options)) r = await ReviewedMaintenanceAsync(db, actor, scope.ClientA, PeriodMaintenanceActions.RollForward,
      new(scope.ClientA, period, "2027", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR", new('a', 64), 1m, 1m, "SYNTHETIC opening", ExpectedPriorRevision: 2));
    var pause = new MaintenanceReceiptPause(refuse);
    await using var writer = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    await using var reader = new AuditSphereDbContext(pg.Options);
    await using var observer = new AuditSphereDbContext(pg.Options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
      var publish = PeriodMaintenanceWorkspace.ExecuteAsync(writer, actor, scope.ClientA, r, timeout.Token);
      await pause.Reached.Task.WaitAsync(timeout.Token);
      var lookup = PeriodMaintenanceWorkspace.LookupAsync(reader, actor, scope.ClientA, r.RequestId, r.ExpectedRequestHash, timeout.Token);
      var deadline = DateTimeOffset.UtcNow.AddSeconds(10); var blocked = false;
      while (DateTimeOffset.UtcNow < deadline)
      {
        var count = await observer.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND (query LIKE '%client_safety_states%' OR query LIKE '%users%')").SingleAsync(timeout.Token);
        if (count > 0) { blocked = true; break; }
        await Task.Delay(20, timeout.Token);
      }
      Assert.True(blocked, "Lookup must reach the publication barrier."); Assert.False(lookup.IsCompleted);
      pause.Release.TrySetResult(true);
      if (refuse) await Assert.ThrowsAsync<InvalidOperationException>(() => publish);
      else Assert.True((await publish).Succeeded);
      var recovered = await lookup; Assert.True(recovered.Succeeded, recovered.Message); Assert.Equal(!refuse, recovered.Value!.Found);
      await using var verify = new AuditSphereDbContext(pg.Options);
      Assert.Equal(refuse ? 0 : 1, await verify.PeriodMaintenanceReceipts.CountAsync());
      Assert.Equal(refuse ? 1 : 2, await verify.ClientReportingPeriods.CountAsync());
      Assert.Equal(refuse ? 0 : 1, await verify.OpeningBalanceBridges.CountAsync());
    }
    finally { pause.Release.TrySetResult(true); }
  }

  [Fact, Trait("Profile", "Database")]
  public async Task Maintenance_ReviewerOnlyRecoverySelfReviewAndConcurrentApprovals()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer"); var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var period = await ClosedMaintenancePeriodAsync(pg, scope);
    PeriodMaintenanceRequest first, second; Guid id;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var original = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "approve-original");
      var revised = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 90m, "CASH", "approve-revised");
      await db.SaveChangesAsync();
      var create = await ReviewedMaintenanceAsync(db, preparer, scope.ClientA, PeriodMaintenanceActions.CreateRestatement, restatement:
        new(scope.ClientA, period, original, revised, "IAS 8", "SYNTHETIC correction", "SYNTHETIC evidence", PeriodRestatementChangeTypes.RestatedError, "FY2026"));
      var saved = await PeriodMaintenanceWorkspace.ExecuteAsync(db, preparer, scope.ClientA, create); Assert.True(saved.Succeeded, saved.Message); id = saved.Value!.Result.RestatementId!.Value;
      db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "AccountingReviewer")); await db.SaveChangesAsync();
      var selfState = await PeriodMaintenanceWorkspace.StateAsync(db, preparer, scope.ClientA, PeriodMaintenanceActions.ApproveRestatement, restatementId: id);
      Assert.False(selfState.Value!.CanProceed);
      var overview = await PeriodMaintenanceQuery.OverviewAsync(db, preparer); Assert.False(overview.Value!.Restatements.Single(x => x.Id == id).CanReview);
      await db.RoleGrants.Where(x => x.UserId == reviewer.UserId && x.Role != "AccountingReviewer").ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      first = await ReviewedMaintenanceAsync(db, reviewer, scope.ClientA, PeriodMaintenanceActions.ApproveRestatement, id: id);
      second = await ReviewedMaintenanceAsync(db, Actor(scope.Partner, "Partner"), scope.ClientA, PeriodMaintenanceActions.ApproveRestatement, id: id);
    }
    await using var a = new AuditSphereDbContext(pg.Options); await using var b = new AuditSphereDbContext(pg.Options);
    var results = await Task.WhenAll(PeriodMaintenanceWorkspace.ExecuteAsync(a, reviewer, scope.ClientA, first),
      PeriodMaintenanceWorkspace.ExecuteAsync(b, Actor(scope.Partner, "Partner"), scope.ClientA, second));
    Assert.Single(results, x => x.Succeeded);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(1, await verify.PeriodMaintenanceReceipts.CountAsync(x => x.Action == PeriodMaintenanceActions.ApproveRestatement));
    var winner = results[0].Succeeded ? reviewer : Actor(scope.Partner, "Partner"); var request = results[0].Succeeded ? first : second;
    var receipt = results.Single(x => x.Succeeded).Value!;
    var lookup = await PeriodMaintenanceWorkspace.LookupAsync(verify, winner, scope.ClientA, request.RequestId, receipt.RequestHash);
    Assert.True(lookup.Succeeded, lookup.Message); Assert.True(lookup.Value!.Found);
    var stranger = await PeriodMaintenanceWorkspace.LookupAsync(verify, preparer, scope.ClientA, request.RequestId, receipt.RequestHash);
    Assert.True(stranger.Succeeded); Assert.False(stranger.Value!.Found);
    // Independently exercise the reviewer-only lifetime even if the Partner won the race.
    var lifetimeActor = winner with { SessionEpoch = winner.SessionEpoch + 1 };
    await verify.Users.Where(x => x.Id == winner.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, lifetimeActor.SessionEpoch));
    Assert.False((await PeriodMaintenanceWorkspace.LookupAsync(verify, winner, scope.ClientA, request.RequestId, receipt.RequestHash)).Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, (await PeriodMaintenanceWorkspace.LookupAsync(verify, lifetimeActor, scope.ClientA, request.RequestId, receipt.RequestHash)).ErrorCode);
  }

  [Theory, InlineData(false, false), InlineData(false, true), InlineData(true, false), InlineData(true, true)]
  public async Task Maintenance_DecimalExtremesReturnValidationFailure(bool maximum, bool opening)
  {
    var client = Guid.NewGuid(); var amount = maximum ? decimal.MaxValue : decimal.MinValue;
    var fields = new RollForwardPeriodRequest(client, Guid.NewGuid(), "2027", new(2027, 1, 1), new(2027, 12, 31),
      "STATUTORY", "QAR", new('a',64), opening ? 0m : amount, opening ? amount : 0m, "SYNTHETIC evidence", ExpectedPriorRevision: 1);
    var request = new PeriodMaintenanceRequest(Guid.NewGuid(), new('b',64), PeriodMaintenanceActions.RollForward, fields, Reviewed: true, ExpectedRequestHash: new('c',64));
    var actor = new ActorContext(Guid.NewGuid(), Guid.NewGuid(), 1, ["AccountingPreparer"]);
    var preview = await PeriodMaintenanceWorkspace.PreviewAsync(null!, actor, client, request);
    Assert.False(preview.Succeeded); Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, preview.ErrorCode);
    var execute = await PeriodMaintenanceWorkspace.ExecuteAsync(null!, actor, client, request);
    Assert.False(execute.Succeeded); Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, execute.ErrorCode);
  }

  private sealed class PauseMaintenanceRead(string fragment) : DbCommandInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int paused;
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
      InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
      if (command.CommandText.Contains(fragment, StringComparison.Ordinal) && Interlocked.Exchange(ref paused, 1) == 0)
      { Reached.TrySetResult(true); await Release.Task.WaitAsync(ct); }
      return result;
    }
  }

  [Theory, InlineData(false), InlineData(true), Trait("Profile", "Database")]
  public async Task MaintenanceQueriesRefuseRevokedClientEvenWhenAnotherGrantRemains(bool closedPeriods)
  {
    await using var pg = await PgTestSchema.CreateAsync(); var scope = await SeedAsync(pg);
    var period = await ClosedMaintenancePeriodAsync(pg, scope); var actor = Actor(scope.Preparer, "AccountingPreparer");
    await using (var grants = new AuditSphereDbContext(pg.Options))
    {
      await grants.RoleGrants.Where(x => x.UserId == actor.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      grants.RoleGrants.AddRange(Grant(scope.FirmId, scope.Preparer, "AccountingPreparer", scope.ClientA),
        Grant(scope.FirmId, scope.Preparer, "AccountingPreparer", scope.ClientB)); await grants.SaveChangesAsync();
    }
    var pause = new PauseMaintenanceRead(closedPeriods ? "FROM financial_packages" : "FROM client_period_restatements");
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    async Task<bool> Read() => closedPeriods
      ? (await PeriodMaintenanceQuery.ClosedPeriodsAsync(reader, actor, scope.ClientA, timeout.Token)).Succeeded
      : (await PeriodMaintenanceQuery.OverviewAsync(reader, actor, timeout.Token)).Succeeded;
    try
    {
      var pending = Read(); await pause.Reached.Task.WaitAsync(timeout.Token);
      await using var revoke = new AuditSphereDbContext(pg.Options); await using var tx = await revoke.Database.BeginTransactionAsync();
      await revoke.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={scope.FirmId} FOR UPDATE").AsNoTracking().SingleAsync();
      await revoke.RoleGrants.Where(x => x.UserId == actor.UserId && x.ClientId == scope.ClientA).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      await tx.CommitAsync(); pause.Release.TrySetResult(true); Assert.False(await pending);
    }
    finally { pause.Release.TrySetResult(true); }
  }

  [Fact, Trait("Profile", "Database")]
  public async Task MaintenanceReceiptScopeRejectsEngagementOnlyExpiredAndForeignActors()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var scope = await SeedAsync(pg); var actor = Actor(scope.Preparer, "AccountingPreparer");
    var period = await ClosedMaintenancePeriodAsync(pg, scope); await using var db = new AuditSphereDbContext(pg.Options);
    var r = await ReviewedMaintenanceAsync(db, actor, scope.ClientA, PeriodMaintenanceActions.RollForward,
      new(scope.ClientA, period, "2027", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR", new('a', 64), 1m, 1m, "SYNTHETIC opening", ExpectedPriorRevision: 2));
    var saved = await PeriodMaintenanceWorkspace.ExecuteAsync(db, actor, scope.ClientA, r); Assert.True(saved.Succeeded, saved.Message);
    var foreign = await SeedAsync(pg);
    var hidden = await PeriodMaintenanceWorkspace.LookupAsync(db, Actor(foreign.Preparer, "AccountingPreparer"), foreign.ClientA, r.RequestId, r.ExpectedRequestHash);
    Assert.True(hidden.Succeeded); Assert.False(hidden.Value!.Found);
    await db.RoleGrants.Where(x => x.UserId == actor.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "AccountingPreparer", scope.ClientA, scope.EngagementA)); await db.SaveChangesAsync();
    Assert.False((await PeriodMaintenanceWorkspace.LookupAsync(db, actor, scope.ClientA, r.RequestId, r.ExpectedRequestHash)).Succeeded);
    Assert.False((await PeriodMaintenanceWorkspace.ExecuteAsync(db, actor, scope.ClientA, r)).Succeeded);
    var expired = Grant(scope.FirmId, scope.Preparer, "AccountingPreparer", scope.ClientA);
    expired.GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-2); expired.ExpiresAt = expired.GrantedAt.AddMinutes(1);
    db.RoleGrants.Add(expired); await db.SaveChangesAsync();
    Assert.False((await PeriodMaintenanceWorkspace.LookupAsync(db, actor, scope.ClientA, r.RequestId, r.ExpectedRequestHash)).Succeeded);
  }

  [Fact, Trait("Profile", "Database")]
  public async Task MaintenanceReceiptDatabaseRejectsInvalidShapeAndCrossClientReference()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var scope = await SeedAsync(pg); var actor = Actor(scope.Preparer, "AccountingPreparer");
    var period = await ClosedMaintenancePeriodAsync(pg, scope); await using var db = new AuditSphereDbContext(pg.Options);
    var r = await ReviewedMaintenanceAsync(db, actor, scope.ClientA, PeriodMaintenanceActions.RollForward,
      new(scope.ClientA, period, "2027", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR", new('a', 64), 1m, 1m, "SYNTHETIC opening", ExpectedPriorRevision: 2));
    var saved = await PeriodMaintenanceWorkspace.ExecuteAsync(db, actor, scope.ClientA, r); Assert.True(saved.Succeeded, saved.Message);
    var id = saved.Value!.Id;
    var shape = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO period_maintenance_receipts (id, firm_id, client_id, actor_id, actor_epoch, request_id, request_hash, action, review_basis, period_id, result_period_id, restatement_id, input_json, before_json, result_json, created_at) SELECT {Guid.NewGuid()}, firm_id, client_id, actor_id, actor_epoch, {Guid.NewGuid()}, request_hash, action, review_basis, period_id, NULL, NULL, input_json, before_json, result_json, created_at FROM period_maintenance_receipts WHERE id={id}"));
    Assert.Equal("23514", shape.SqlState);
    var scopeError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO period_maintenance_receipts (id, firm_id, client_id, actor_id, actor_epoch, request_id, request_hash, action, review_basis, period_id, result_period_id, restatement_id, input_json, before_json, result_json, created_at) SELECT {Guid.NewGuid()}, firm_id, {scope.ClientB}, actor_id, actor_epoch, {Guid.NewGuid()}, request_hash, action, review_basis, period_id, result_period_id, restatement_id, input_json, before_json, result_json, created_at FROM period_maintenance_receipts WHERE id={id}"));
    Assert.Equal("23503", scopeError.SqlState);
  }

  [Fact, Trait("Profile", "Database")]
  public async Task MaintenanceLegacyRestatementCallerRetainsAmbientTransactionOwnership()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var scope = await SeedAsync(pg);
    var period = await ClosedMaintenancePeriodAsync(pg, scope); Guid original, revised;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      original = await AddPackageAsync(setup, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "ambient-original");
      revised = await AddPackageAsync(setup, scope, scope.ClientA, scope.EngagementA, 90m, "CASH", "ambient-revised"); await setup.SaveChangesAsync();
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await using var tx = await db.Database.BeginTransactionAsync();
      var created = await ClientAccountingService.CreatePeriodRestatementAsync(db, Actor(scope.Preparer, "AccountingPreparer"),
        new(scope.ClientA, period, original, revised, "IAS 8", "SYNTHETIC ambient correction", "SYNTHETIC evidence", PeriodRestatementChangeTypes.RestatedError, "FY2026"));
      Assert.True(created.Succeeded, created.Message); Assert.Same(tx, db.Database.CurrentTransaction);
      var approved = await ClientAccountingService.ApprovePeriodRestatementAsync(db, Actor(scope.Reviewer, "AccountingReviewer"), created.Value);
      Assert.True(approved.Succeeded, approved.Message); Assert.Same(tx, db.Database.CurrentTransaction); await tx.RollbackAsync();
    }
    await using var verify = new AuditSphereDbContext(pg.Options); Assert.Empty(await verify.ClientPeriodRestatements.ToListAsync()); Assert.Empty(await verify.PeriodMaintenanceReceipts.ToListAsync());
  }

  [Fact, Trait("Profile", "Database")]
  public async Task MaintenanceMigrationPreservesExistingRecordsAndRefusesRetainedReceiptDowngrade()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var scope = await SeedAsync(pg);
    var period = await ClosedMaintenancePeriodAsync(pg, scope);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var migrations = db.Database.GetMigrations().ToArray();
      var index = Array.IndexOf(migrations, "20261016094656_PeriodMaintenanceReviewedReceipts"); Assert.True(index > 0);
      // Only the disposable test database is migrated by this authored test.
      await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
      Assert.True(await db.ClientReportingPeriods.AnyAsync(x => x.Id == period));
      await db.GetService<IMigrator>().MigrateAsync(migrations[index]);
      Assert.True(await db.ClientReportingPeriods.AnyAsync(x => x.Id == period));
      var actor = Actor(scope.Preparer, "AccountingPreparer");
      var r = await ReviewedMaintenanceAsync(db, actor, scope.ClientA, PeriodMaintenanceActions.RollForward,
        new(scope.ClientA, period, "2027", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR", new('a', 64), 1m, 1m, "SYNTHETIC opening", ExpectedPriorRevision: 2));
      var saved = await PeriodMaintenanceWorkspace.ExecuteAsync(db, actor, scope.ClientA, r); Assert.True(saved.Succeeded, saved.Message);
      var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]));
      Assert.Equal("55000", Assert.IsType<PostgresException>(refusal.GetBaseException()).SqlState);
    }
    await using var verify = new AuditSphereDbContext(pg.Options); Assert.Single(await verify.PeriodMaintenanceReceipts.ToListAsync());
    Assert.Equal(2, await verify.ClientReportingPeriods.CountAsync());
  }

}
