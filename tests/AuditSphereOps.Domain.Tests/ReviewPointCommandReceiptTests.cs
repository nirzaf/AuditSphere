using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed class ReviewPointCommandReceiptTests
{
  public static TheoryData<string> Actions=>new(){ReviewPointCommandActions.Clear,ReviewPointCommandActions.Reopen};
  private static string Exact(object? value)=>JsonSerializer.Serialize(value);
  [Theory,MemberData(nameof(Actions)),Trait("Profile","Database")]
  public async Task BothActionsRetainExactBeforeAfterAndRecoverOriginalHistoryBeforeCurrentWriteGates(string action)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    if(action==ReviewPointCommandActions.Reopen)Assert.True((await AuditPlanningService.SetReviewPointDispositionAsync(db,w.Staff,w.ReviewPointId,true,1)).Succeeded);
    var r=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,action);
    var first=await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r);Assert.True(first.Succeeded,first.Message);
    var receipt=first.Value!;Assert.Equal(r.ExpectedDispositionRevision,receipt.DispositionEvent.Before.DispositionRevision);
    Assert.Equal((long.Parse(r.ExpectedDispositionRevision)+1).ToString(),receipt.DispositionEvent.After.DispositionRevision);
    Assert.Equal("9007199254740993",receipt.DispositionEvent.After.TargetRevision);Assert.Equal(action==ReviewPointCommandActions.Clear,receipt.DispositionEvent.After.Cleared);
    // The raiser may dispose of their own point under the existing staff authority; no reviewer-independence gate is added.
    Assert.Equal(w.Staff.UserId,receipt.DispositionEvent.Before.RaisedByUserId);
    var later=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,action==ReviewPointCommandActions.Clear?ReviewPointCommandActions.Reopen:ReviewPointCommandActions.Clear);
    Assert.True((await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,later)).Succeeded);
    await ReviewPointCommandReceiptSeed.FreezeAsync(db,w);
    var replay=await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r);Assert.True(replay.Succeeded,replay.Message);
    var lookup=await ReviewPointCommandWorkspace.LookupAsync(db,w.Staff,w.ReviewPointId,r.RequestId,r.ExpectedRequestHash);Assert.True(lookup.Succeeded,lookup.Message);
    Assert.Equal(Exact(receipt),Exact(replay.Value));Assert.Equal(Exact(receipt),Exact(lookup.Value!.Receipt));Assert.Equal(2,await db.ReviewPointCommandReceipts.CountAsync());
    Assert.Equal(ErrorCodes.IdempotencyConflict,(await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r with{Fields=new(action==ReviewPointCommandActions.Clear?ReviewPointCommandActions.Reopen:ReviewPointCommandActions.Clear)})).ErrorCode);
    Assert.Equal(ErrorCodes.IdempotencyConflict,(await ReviewPointCommandWorkspace.LookupAsync(db,w.Staff,w.ReviewPointId,r.RequestId,new('b',64))).ErrorCode);
  }

  [Fact,Trait("Profile","Database")]
  public async Task AbaRevisionAndExpectedPreviewHashFenceStaleIntentWithoutChangingTargetRevision()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);
    Assert.False((await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r with{Reviewed=false})).Succeeded);
    Assert.Equal(ErrorCodes.IdempotencyConflict,(await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r with{ExpectedRequestHash=new('b',64)})).ErrorCode);
    Assert.Empty(await db.ReviewPointDispositionEvents.ToListAsync());
    Assert.True((await AuditPlanningService.SetReviewPointDispositionAsync(db,w.Staff,w.ReviewPointId,true,1)).Succeeded);
    Assert.True((await AuditPlanningService.SetReviewPointDispositionAsync(db,w.Staff,w.ReviewPointId,false,2)).Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision,(await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r)).ErrorCode);
    Assert.Equal(ErrorCodes.StaleRevision,(await AuditPlanningService.SetReviewPointDispositionAsync(db,w.Staff,w.ReviewPointId,true,1)).ErrorCode);
    var point=await db.ReviewPoints.AsNoTracking().SingleAsync();Assert.False(point.Cleared);Assert.Equal(3,point.DispositionRevision);Assert.Equal(9007199254740993,point.TargetRevision);
    Assert.Empty(await db.ReviewPointCommandReceipts.ToListAsync());Assert.Equal(2,await db.ReviewPointDispositionEvents.CountAsync());
  }
  [Theory,InlineData("Staff"),InlineData("Partner"),InlineData("Manager"),InlineData("SeniorManager"),InlineData("Senior"),InlineData("Auditor"),InlineData("EngagementLeader"),InlineData("Administrator"),Trait("Profile","Database")]
  public async Task ExistingInternalRoleSetKeepsAdministrativeAuthorityThroughHoldsAndProfessionalBlock(string role)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;await using var db=new AuditSphereDbContext(pg.Options);
    if(role!="Staff")db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,role,f.ClientId,f.EngagementId));
    await db.SaveChangesAsync();await db.RoleGrants.Where(x=>x.UserId==f.Staff.Id && x.Role!=role).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
    db.EngagementHolds.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,EngagementId=f.EngagementId,HoldKind="Independence",Reason="SYNTHETIC hold",CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();
    await db.Engagements.Where(x=>x.Id==f.EngagementId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ProfessionalWorkBlocked,true));
    var actor=PbcSeed.Actor(f.Staff,role);var state=await ReviewPointCommandWorkspace.StateAsync(db,actor,w.ReviewPointId);Assert.True(state.Succeeded,state.Message);Assert.True(state.Value!.CanChangeDisposition);
    var r=await ReviewPointCommandReceiptSeed.ReviewAsync(db,actor,w.ReviewPointId,ReviewPointCommandActions.Clear);var saved=await ReviewPointCommandWorkspace.ExecuteAsync(db,actor,w.ReviewPointId,r);Assert.True(saved.Succeeded,saved.Message);
  }

  [Fact,Trait("Profile","Database")]
  public async Task FrozenOriginalRefusesChangedDispositionButAllowsExactRecoveryAndCompatibilityNoOp()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);Assert.True((await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r)).Succeeded);
    var reopen=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Reopen);await ReviewPointCommandReceiptSeed.FreezeAsync(db,w);
    Assert.False((await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,reopen)).Succeeded);
    Assert.False((await AuditPlanningService.SetReviewPointDispositionAsync(db,w.Staff,w.ReviewPointId,false,2)).Succeeded);
    Assert.True((await AuditPlanningService.SetReviewPointDispositionAsync(db,w.Staff,w.ReviewPointId,true)).Succeeded);
    var state=await ReviewPointCommandWorkspace.StateAsync(db,w.Staff,w.ReviewPointId);Assert.True(state.Succeeded,state.Message);Assert.False(state.Value!.CanChangeDisposition);
    var record=await AuditRecordQueries.ReviewPointAsync(db,w.Staff,w.ReviewPointId);Assert.True(record.Succeeded,record.Message);Assert.False(record.Value!.CanChangeDisposition);
    Assert.Single(await db.ReviewPointDispositionEvents.ToListAsync());
  }
  [Fact,Trait("Profile","Database")]
  public async Task LegacySignatureKeepsRetryNoOpAndAmbientTransactionOwnerRollsBackEvidence()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    await using(var tx=await db.Database.BeginTransactionAsync())
    {
      Assert.True((await AuditPlanningService.SetReviewPointDispositionAsync(db,w.Staff,w.ReviewPointId,true)).Succeeded);
      Assert.True((await AuditPlanningService.SetReviewPointDispositionAsync(db,w.Staff,w.ReviewPointId,true)).Succeeded);
      Assert.Same(tx,db.Database.CurrentTransaction);Assert.Single(await db.ReviewPointDispositionEvents.ToListAsync());await tx.RollbackAsync();
    }
    await using var fresh=new AuditSphereDbContext(pg.Options);Assert.Empty(await fresh.ReviewPointDispositionEvents.ToListAsync());Assert.False(await fresh.ReviewPoints.Select(x=>x.Cleared).SingleAsync());
  }

  private sealed class ReceiptInterceptor(bool pause=false,bool refuse=false):SaveChangesInterceptor
  {
    public TaskCompletionSource<bool> Reached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default)
    {
      var rows=e.Context!.ChangeTracker.Entries<ReviewPointCommandReceipt>().Where(x=>x.State==EntityState.Added).ToArray();
      foreach(var row in rows)row.Entity.CreatedAt=new DateTimeOffset(DateTimeOffset.UtcNow.Ticks/10*10+7,TimeSpan.Zero);
      if(pause && rows.Length>0){Reached.TrySetResult(true);await Release.Task.WaitAsync(ct);if(refuse)throw new InvalidOperationException("SYNTHETIC receipt rollback");}
      return result;
    }
  }
  [Theory,InlineData("UTC"),InlineData("Asia/Qatar"),Trait("Profile","Database")]
  public async Task CanonicalUtcAndPersistedMicrosecondsAgreeExactlyWithPreviewReplayAndLookup(string zone)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);
    await using var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(new ReceiptInterceptor()).Options);
    await db.Database.OpenConnectionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('TimeZone',{zone},false)");
    var r=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);var first=await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r);Assert.True(first.Succeeded,first.Message);
    var receipt=first.Value!;Assert.Equal(0,receipt.CreatedAt.Ticks%10);Assert.Equal(TimeSpan.Zero,receipt.DispositionEvent.Before.RaisedAt.Offset);Assert.Equal(Exact(receipt.Preview.Before),Exact(receipt.DispositionEvent.Before));
    await using var fresh=new AuditSphereDbContext(pg.Options);Assert.Equal(Exact(receipt),Exact((await ReviewPointCommandWorkspace.ExecuteAsync(fresh,w.Staff,w.ReviewPointId,r)).Value));
    Assert.Equal(Exact(receipt),Exact((await ReviewPointCommandWorkspace.LookupAsync(fresh,w.Staff,w.ReviewPointId,r.RequestId,r.ExpectedRequestHash)).Value!.Receipt));
  }

  private static async Task WaitForLock(AuditSphereDbContext db,CancellationToken ct)
  {
    var end=DateTimeOffset.UtcNow.AddSeconds(10);
    while(DateTimeOffset.UtcNow<end)
    {
      await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()",ct);
      if(await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND wait_event_type='Lock'").SingleAsync(ct)>0)return;
      await Task.Delay(20,ct);
    }
    Assert.Fail("Expected publication barrier wait was not observed.");
  }
  [Theory,InlineData(false),InlineData(true),Trait("Profile","Database")]
  public async Task LookupWaitsForPendingPublicationOrRollback(bool refuse)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);ReviewPointCommandRequest r;
    await using(var setup=new AuditSphereDbContext(pg.Options))r=await ReviewPointCommandReceiptSeed.ReviewAsync(setup,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);
    var pause=new ReceiptInterceptor(true,refuse);await using var writer=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    await using var reader=new AuditSphereDbContext(pg.Options);await using var observer=new AuditSphereDbContext(pg.Options);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
      var publish=ReviewPointCommandWorkspace.ExecuteAsync(writer,w.Staff,w.ReviewPointId,r,timeout.Token);await pause.Reached.Task.WaitAsync(timeout.Token);
      var lookup=ReviewPointCommandWorkspace.LookupAsync(reader,w.Staff,w.ReviewPointId,r.RequestId,r.ExpectedRequestHash,timeout.Token);await WaitForLock(observer,timeout.Token);Assert.False(lookup.IsCompleted);
      pause.Release.TrySetResult(true);if(refuse)await Assert.ThrowsAsync<InvalidOperationException>(async()=>await publish);else Assert.True((await publish).Succeeded);
      var result=await lookup;Assert.True(result.Succeeded,result.Message);Assert.Equal(!refuse,result.Value!.Found);
      Assert.Equal(refuse?0:1,await observer.ReviewPointDispositionEvents.CountAsync());Assert.Equal(refuse?0:1,await observer.ReviewPointCommandReceipts.CountAsync());
    }
    finally{pause.Release.TrySetResult(true);}
  }
  [Theory,InlineData(false),InlineData(true),Trait("Profile","Database")]
  public async Task ConcurrentSameRequestRecoversOnceAndDifferentRequestCannotOvertakeBeforeState(bool same)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);ReviewPointCommandRequest r,r2;
    await using(var setup=new AuditSphereDbContext(pg.Options)){r=await ReviewPointCommandReceiptSeed.ReviewAsync(setup,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);r2=same?r:await ReviewPointCommandReceiptSeed.ReviewAsync(setup,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Reopen);}
    await using var first=new AuditSphereDbContext(pg.Options);await using var second=new AuditSphereDbContext(pg.Options);
    var results=await Task.WhenAll(ReviewPointCommandWorkspace.ExecuteAsync(first,w.Staff,w.ReviewPointId,r),ReviewPointCommandWorkspace.ExecuteAsync(second,w.Staff,w.ReviewPointId,r2));
    if(same){Assert.All(results,x=>Assert.True(x.Succeeded,x.Message));Assert.Equal(Exact(results[0].Value),Exact(results[1].Value));}
    else{Assert.Single(results,x=>x.Succeeded);Assert.Equal(ErrorCodes.StaleRevision,results.Single(x=>!x.Succeeded).ErrorCode);}
    await using var check=new AuditSphereDbContext(pg.Options);Assert.Single(await check.ReviewPointDispositionEvents.ToListAsync());Assert.Single(await check.ReviewPointCommandReceipts.ToListAsync());
  }

  [Theory,InlineData("epoch"),InlineData("disabled"),InlineData("revoked"),InlineData("expired"),Trait("Profile","Database")]
  public async Task RecoveryRemainsActorOwnedOpaqueAndRequiresCurrentLifetimeAndScope(string change)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);Assert.True((await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r)).Succeeded);
    var other=await ReviewPointCommandWorkspace.LookupAsync(db,w.Admin,w.ReviewPointId,r.RequestId,r.ExpectedRequestHash);Assert.True(other.Succeeded,other.Message);Assert.False(other.Value!.Found);
    var absent=await ReviewPointCommandWorkspace.LookupAsync(db,w.Staff,w.ReviewPointId,Guid.NewGuid(),new('b',64));Assert.True(absent.Succeeded,absent.Message);Assert.False(absent.Value!.Found);
    Assert.Equal(ErrorCodes.ScopeDenied,(await ReviewPointCommandWorkspace.LookupAsync(db,w.Staff,Guid.NewGuid(),r.RequestId,r.ExpectedRequestHash)).ErrorCode);
    if(change=="epoch")await db.Users.Where(x=>x.Id==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));
    if(change=="disabled")await db.Users.Where(x=>x.Id==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Disabled,true));
    if(change=="revoked")await db.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
    if(change=="expired")await db.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.GrantedAt,DateTimeOffset.UtcNow.AddDays(-2)).SetProperty(x=>x.ExpiresAt,DateTimeOffset.UtcNow.AddDays(-1)));
    Assert.False((await ReviewPointCommandWorkspace.LookupAsync(db,w.Staff,w.ReviewPointId,r.RequestId,r.ExpectedRequestHash)).Succeeded);
    Assert.False((await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r)).Succeeded);
    if(change=="epoch")Assert.Equal(ErrorCodes.GenerationStale,(await ReviewPointCommandWorkspace.LookupAsync(db,w.Staff with{SessionEpoch=w.Staff.SessionEpoch+1},w.ReviewPointId,r.RequestId,r.ExpectedRequestHash)).ErrorCode);
    Assert.Single(await db.ReviewPointDispositionEvents.ToListAsync());
  }

  [Theory,InlineData(false),InlineData(true),Trait("Profile","Database")]
  public async Task SiblingEngagementAndOtherClientRoleCannotAuthorizeDisposition(bool otherClient)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;await using var db=new AuditSphereDbContext(pg.Options);
    var clientId=otherClient?Guid.NewGuid():f.ClientId;var engagementId=Guid.NewGuid();
    if(otherClient)db.PracticeClients.Add(new(){Id=clientId,FirmId=f.FirmId,LegalName="SYNTHETIC other client",CreatedAt=DateTimeOffset.UtcNow});
    db.Engagements.Add(new(){Id=engagementId,FirmId=f.FirmId,PracticeClientId=clientId,Status="Active",ProfessionalWorkBlocked=false,CreatedAt=DateTimeOffset.UtcNow});
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Reviewer,"Manager",clientId,engagementId));await db.SaveChangesAsync();var actor=PbcSeed.Actor(f.Reviewer,"Manager");
    Assert.Equal(ErrorCodes.ScopeDenied,(await ReviewPointCommandWorkspace.StateAsync(db,actor,w.ReviewPointId)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied,(await AuditPlanningService.SetReviewPointDispositionAsync(db,actor,w.ReviewPointId,true,1)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied,(await AuditRecordQueries.ReviewPointAsync(db,actor,w.ReviewPointId)).ErrorCode);
    Assert.Empty(await db.ReviewPointDispositionEvents.ToListAsync());
  }
  [Fact,Trait("Profile","Database")]
  public async Task ClientActorIsDeniedEvenIfGivenAnInternalRole()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);var f=w.Fixture;
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Client,"Staff",f.ClientId,f.EngagementId));await db.SaveChangesAsync();var actor=PbcSeed.Actor(f.Client,"Staff");
    Assert.False((await ReviewPointCommandWorkspace.StateAsync(db,actor,w.ReviewPointId)).Succeeded);Assert.False((await AuditPlanningService.SetReviewPointDispositionAsync(db,actor,w.ReviewPointId,true,1)).Succeeded);
    Assert.Empty(await db.ReviewPointDispositionEvents.ToListAsync());
  }

  [Fact,Trait("Profile","Database")]
  public async Task RevocationHoldingFirmGuardWinsBeforeDispositionPublication()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);ReviewPointCommandRequest r;
    await using(var setup=new AuditSphereDbContext(pg.Options))r=await ReviewPointCommandReceiptSeed.ReviewAsync(setup,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);
    await using var revoke=new AuditSphereDbContext(pg.Options);await using var tx=await revoke.Database.BeginTransactionAsync();
    await revoke.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={w.Fixture.FirmId} FOR UPDATE").AsNoTracking().SingleAsync();
    await using var writer=new AuditSphereDbContext(pg.Options);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var pending=ReviewPointCommandWorkspace.ExecuteAsync(writer,w.Staff,w.ReviewPointId,r,timeout.Token);await WaitForLock(revoke,timeout.Token);
    await revoke.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));await tx.CommitAsync();Assert.False((await pending).Succeeded);
    Assert.Empty(await revoke.ReviewPointDispositionEvents.ToListAsync());Assert.Empty(await revoke.ReviewPointCommandReceipts.ToListAsync());
  }

  [Fact,Trait("Profile","Database")]
  public async Task PublicationActorShareLockDoesNotConflictWithSameActorForeignKeyKeyShare()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);ReviewPointCommandRequest r;
    await using(var setup=new AuditSphereDbContext(pg.Options))r=await ReviewPointCommandReceiptSeed.ReviewAsync(setup,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);
    await using var foreignKey=new AuditSphereDbContext(pg.Options);await using var tx=await foreignKey.Database.BeginTransactionAsync();
    await foreignKey.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id={w.Staff.UserId} FOR KEY SHARE").AsNoTracking().SingleAsync();
    await using var writer=new AuditSphereDbContext(pg.Options);await writer.Database.OpenConnectionAsync();await writer.Database.ExecuteSqlRawAsync("SET lock_timeout='2s'");
    var saved=await ReviewPointCommandWorkspace.ExecuteAsync(writer,w.Staff,w.ReviewPointId,r);Assert.True(saved.Succeeded,saved.Message);await tx.RollbackAsync();
  }

  [Fact,Trait("Profile","Database")]
  public async Task DatabaseRejectsDirectDispositionTargetEditsFakeEventsAndEvidenceMutation()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    Assert.Equal("55000",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE review_points SET cleared=true,disposition_revision=disposition_revision+1 WHERE id={w.ReviewPointId}"))).SqlState);
    Assert.Equal("55000",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE review_points SET target_revision=target_revision+1 WHERE id={w.ReviewPointId}"))).SqlState);
    var r=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);Assert.True((await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r)).Succeeded);
    foreach(var commands in new[]{("UPDATE review_point_disposition_events SET created_at=created_at+interval '1 second'","DELETE FROM review_point_disposition_events"),("UPDATE review_point_command_receipts SET created_at=created_at+interval '1 second'","DELETE FROM review_point_command_receipts")})
    {
      Assert.Equal("55000",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(commands.Item1))).SqlState);
      Assert.Equal("55000",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(commands.Item2))).SqlState);
    }
    Assert.Equal("55000",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("INSERT INTO review_point_disposition_events SELECT gen_random_uuid(),firm_id,client_id,engagement_id,review_point_id,actor_id,actor_epoch,action,before_revision,after_revision,before_json,after_json,created_at FROM review_point_disposition_events"))).SqlState);
    Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("INSERT INTO review_point_command_receipts SELECT gen_random_uuid(),firm_id,client_id,engagement_id,review_point_id,disposition_event_id,actor_id,actor_epoch,gen_random_uuid(),request_hash,review_basis,action,input_json,preview_json,created_at FROM review_point_command_receipts"))).SqlState);
  }
  [Theory,InlineData("actor"),InlineData("scope"),InlineData("before"),InlineData("outcome"),Trait("Profile","Database")]
  public async Task ReceiptInsertionBindsActualScopedActorBeforeAndOutcome(string corruption)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);var preview=await ReviewPointCommandWorkspace.PreviewAsync(db,w.Staff,w.ReviewPointId,r);Assert.True(preview.Succeeded,preview.Message);
    await using(var tx=await db.Database.BeginTransactionAsync())
    {
      Assert.True((await AuditPlanningService.SetReviewPointDispositionAsync(db,w.Staff,w.ReviewPointId,true,1)).Succeeded);
      var e=await db.ReviewPointDispositionEvents.AsNoTracking().SingleAsync();var p=preview.Value!;
      if(corruption=="before")p=p with{Before=p.Before with{Comment="Fabricated original point"}};if(corruption=="outcome")p=p with{Cleared=false};
      db.ReviewPointCommandReceipts.Add(new(){Id=Guid.NewGuid(),FirmId=e.FirmId,ClientId=corruption=="scope"?Guid.NewGuid():e.ClientId,EngagementId=e.EngagementId,
        ReviewPointId=e.ReviewPointId,DispositionEventId=e.Id,ActorId=corruption=="actor"?w.Fixture.Admin.Id:e.ActorId,ActorEpoch=e.ActorEpoch,Action=e.Action,RequestId=r.RequestId,
        RequestHash=p.RequestHash,ReviewBasis=p.ReviewBasis,InputJson=JsonSerializer.Serialize(r.Fields),PreviewJson=JsonSerializer.Serialize(p),CreatedAt=DateTimeOffset.UtcNow});
      var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal("23514",Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);await tx.RollbackAsync();
    }
    await using var fresh=new AuditSphereDbContext(pg.Options);Assert.Empty(await fresh.ReviewPointDispositionEvents.ToListAsync());Assert.Empty(await fresh.ReviewPointCommandReceipts.ToListAsync());
  }
  [Theory,InlineData(false),InlineData(true),Trait("Profile","Database")]
  public async Task EmptyMigrationDownPreservesOriginalPointButLegacyOrReviewedEvidenceRefusesDown(bool reviewed)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var migrations=db.Database.GetMigrations().ToArray();var index=Array.FindIndex(migrations,x=>x.EndsWith("_ReviewPointReviewedCommandReceipts",StringComparison.Ordinal));Assert.True(index>0);
    await db.GetService<IMigrator>().MigrateAsync(migrations[index-1]);Assert.Equal(1,await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM review_points").SingleAsync());
    await db.GetService<IMigrator>().MigrateAsync(migrations[index]);
    if(reviewed){var r=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);Assert.True((await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r)).Succeeded);}
    else Assert.True((await AuditPlanningService.SetReviewPointDispositionAsync(db,w.Staff,w.ReviewPointId,true)).Succeeded);
    var refusal=await Assert.ThrowsAnyAsync<Exception>(()=>db.GetService<IMigrator>().MigrateAsync(migrations[index-1]));Assert.Equal("55000",Assert.IsType<PostgresException>(refusal.GetBaseException()).SqlState);
    Assert.Single(await db.ReviewPointDispositionEvents.ToListAsync());Assert.Equal(reviewed?1:0,await db.ReviewPointCommandReceipts.CountAsync());
  }

  private sealed class PausePointRead(int ordinal):DbCommandInterceptor
  {
    private int reads;
    public TaskCompletionSource<bool> Reached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(System.Data.Common.DbCommand command,CommandExecutedEventData e,System.Data.Common.DbDataReader reader,CancellationToken ct=default)
    {
      if(command.CommandText.Contains("review_points",StringComparison.Ordinal) && !command.CommandText.Contains("FOR UPDATE",StringComparison.Ordinal)
        && Interlocked.Increment(ref reads)==ordinal){Reached.TrySetResult(true);await Release.Task.WaitAsync(ct);}
      return reader;
    }
  }
  [Fact,Trait("Profile","Database")]
  public async Task CompatibilityCapturedRevisionCannotSilentlyOverwriteConcurrentDisposition()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);var pause=new PausePointRead(1);
    await using var delayed=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    await using var winner=new AuditSphereDbContext(pg.Options);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
      var pending=AuditPlanningService.SetReviewPointDispositionAsync(delayed,w.Staff,w.ReviewPointId,false,timeout.Token);await pause.Reached.Task.WaitAsync(timeout.Token);
      Assert.True((await AuditPlanningService.SetReviewPointDispositionAsync(winner,w.Staff,w.ReviewPointId,true,1,timeout.Token)).Succeeded);pause.Release.TrySetResult(true);
      Assert.Equal(ErrorCodes.StaleRevision,(await pending).ErrorCode);Assert.True(await winner.ReviewPoints.Select(x=>x.Cleared).SingleAsync());Assert.Single(await winner.ReviewPointDispositionEvents.ToListAsync());
    }
    finally{pause.Release.TrySetResult(true);}
  }
  [Theory,InlineData(false),InlineData(true),Trait("Profile","Database")]
  public async Task QueryAndWorkspaceReauthorizeAfterFinalPointProjection(bool workspace)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;var pause=new PausePointRead(2);
    await using var reader=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    await using var revoke=new AuditSphereDbContext(pg.Options);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
      // Retain a valid sibling grant: a generic any-grant check must not expose the revoked point.
      var sibling=Guid.NewGuid();revoke.Engagements.Add(new(){Id=sibling,FirmId=f.FirmId,PracticeClientId=f.ClientId,Status="Active",ProfessionalWorkBlocked=false,CreatedAt=DateTimeOffset.UtcNow});
      revoke.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"Staff",f.ClientId,sibling));await revoke.SaveChangesAsync();
      if(workspace)
      {
        var pending=ReviewPointCommandWorkspace.StateAsync(reader,w.Staff,w.ReviewPointId,timeout.Token);await pause.Reached.Task.WaitAsync(timeout.Token);
        await revoke.RoleGrants.Where(x=>x.UserId==f.Staff.Id && x.EngagementId==f.EngagementId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));pause.Release.TrySetResult(true);Assert.Equal(ErrorCodes.ScopeDenied,(await pending).ErrorCode);
      }
      else
      {
        var pending=AuditRecordQueries.ReviewPointAsync(reader,w.Staff,w.ReviewPointId,timeout.Token);await pause.Reached.Task.WaitAsync(timeout.Token);
        await revoke.RoleGrants.Where(x=>x.UserId==f.Staff.Id && x.EngagementId==f.EngagementId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));pause.Release.TrySetResult(true);Assert.Equal(ErrorCodes.ScopeDenied,(await pending).ErrorCode);
      }
      Assert.Empty(await revoke.ReviewPointDispositionEvents.ToListAsync());Assert.Empty(await revoke.ReviewPointCommandReceipts.ToListAsync());
    }
    finally{pause.Release.TrySetResult(true);}
  }

}
