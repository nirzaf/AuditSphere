using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed class FindingCommandReceiptTests
{
  public static TheoryData<string> Actions => new(){FindingCommandActions.RecordResponse,FindingCommandActions.Designate,FindingCommandActions.Undesignate};
  private static string Exact(object? value)=>JsonSerializer.Serialize(value);
  [Theory, MemberData(nameof(Actions)), Trait("Profile", "Database")]
  public async Task ReceiptSnapshotsUseCanonicalUtcUnderNonUtcDatabaseTimezone(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await FindingCommandReceiptSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await db.Database.OpenConnectionAsync();
    await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'Asia/Qatar'");
    if (action == FindingCommandActions.Undesignate)
      Assert.True((await AuditPlanningService.DesignateManagementLetterFindingAsync(db, w.Manager, new(w.FindingId, true, "Prior recommendation"))).Succeeded);
    var request = await FindingCommandReceiptSeed.ReviewAsync(db, w.Manager, w.FindingId, FindingCommandReceiptSeed.Fields(action));
    var saved = await FindingCommandWorkspace.ExecuteAsync(db, w.Manager, w.FindingId, request);
    Assert.True(saved.Succeeded, saved.Message);
    var receipt = saved.Value!;
    Assert.Equal(Exact(receipt.Preview.Before), Exact(receipt.Amendment.Before));
    Assert.Equal(TimeSpan.Zero, receipt.Amendment.Before.CreatedAt.Offset);
    Assert.Equal(TimeSpan.Zero, receipt.Amendment.After.CreatedAt.Offset);
    if (receipt.Amendment.Before.LetterDesignatedAt is { } before) Assert.Equal(TimeSpan.Zero, before.Offset);
    if (receipt.Amendment.After.LetterDesignatedAt is { } after) Assert.Equal(TimeSpan.Zero, after.Offset);
  }

  [Theory,MemberData(nameof(Actions)),Trait("Profile","Database")]
  public async Task EveryActionRetainsExactOriginalEventAfterRepeatedAmendmentsAndCurrentGateChanges(string action)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);var a=w.Manager;
    await using var db=new AuditSphereDbContext(pg.Options);
    if(action==FindingCommandActions.Undesignate) Assert.True((await AuditPlanningService.DesignateManagementLetterFindingAsync(db,a,new(w.FindingId,true,"Prior recommendation"))).Succeeded);
    var r=await FindingCommandReceiptSeed.ReviewAsync(db,a,w.FindingId,FindingCommandReceiptSeed.Fields(action));
    var saved=await FindingCommandWorkspace.ExecuteAsync(db,a,w.FindingId,r);Assert.True(saved.Succeeded,saved.Message);
    Assert.Equal(r.ExpectedRevision,saved.Value!.Amendment.Before.Revision);
    Assert.Equal((long.Parse(r.ExpectedRevision)+1).ToString(),saved.Value.Amendment.After.Revision);
    Assert.True((await AuditPlanningService.RecordFindingResponseAsync(db,a,new(w.FindingId,"Later response",true))).Succeeded);
    var r2=await FindingCommandReceiptSeed.ReviewAsync(db,a,w.FindingId,FindingCommandReceiptSeed.Fields(action));
    Assert.True((await FindingCommandWorkspace.ExecuteAsync(db,a,w.FindingId,r2)).Succeeded);
    Assert.Equal(2,await db.FindingCommandReceipts.CountAsync());
    await db.Engagements.Where(x=>x.Id==w.Fixture.EngagementId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ProfessionalWorkBlocked,true));
    var replay=await FindingCommandWorkspace.ExecuteAsync(db,a,w.FindingId,r);Assert.True(replay.Succeeded,replay.Message);
    var lookup=await FindingCommandWorkspace.LookupAsync(db,a,w.FindingId,r.RequestId,r.ExpectedRequestHash);Assert.True(lookup.Succeeded,lookup.Message);
    Assert.Equal(Exact(saved.Value),Exact(replay.Value));Assert.Equal(Exact(saved.Value),Exact(lookup.Value!.Receipt));
    Assert.Equal(ErrorCodes.IdempotencyConflict,(await FindingCommandWorkspace.ExecuteAsync(db,a,w.FindingId,r with{ReviewBasis=new('a',64)})).ErrorCode);
    Assert.Equal(ErrorCodes.IdempotencyConflict,(await FindingCommandWorkspace.LookupAsync(db,a,w.FindingId,r.RequestId,new('b',64))).ErrorCode);
  }

  [Theory,InlineData(false),InlineData(true),Trait("Profile","Database")]
  public async Task MixedScopeManagerCannotBorrowDesignationAuthorityAndQueriesAgree(bool otherClient)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;
    await using var db=new AuditSphereDbContext(pg.Options);
    var sibling=Guid.NewGuid();var clientId=otherClient?Guid.NewGuid():f.ClientId;
    if(otherClient)db.PracticeClients.Add(new(){Id=clientId,FirmId=f.FirmId,LegalName="SYNTHETIC sibling client",CreatedAt=DateTimeOffset.UtcNow});
    db.Engagements.Add(new(){Id=sibling,FirmId=f.FirmId,PracticeClientId=clientId,Status="Active",ProfessionalWorkBlocked=false,CreatedAt=DateTimeOffset.UtcNow});
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"Manager",clientId,sibling));await db.SaveChangesAsync();
    var mixed=new ActorContext(f.Staff.Id,f.FirmId,f.Staff.SessionEpoch,["Staff","Manager"]);
    var state=await FindingCommandWorkspace.StateAsync(db,mixed,w.FindingId);Assert.True(state.Succeeded,state.Message);Assert.True(state.Value!.CanRecordResponse);Assert.False(state.Value.CanDesignate);
    var record=await AuditRecordQueries.FindingAsync(db,mixed,w.FindingId);Assert.True(record.Succeeded,record.Message);Assert.False(record.Value!.CanDesignate);Assert.True(record.Value.CanRecordResponse);
    foreach(var designated in new[]{true,false}) Assert.Equal(ErrorCodes.ScopeDenied,(await AuditPlanningService.DesignateManagementLetterFindingAsync(db,mixed,new(w.FindingId,designated,"Recommendation"))).ErrorCode);
    var r=new FindingCommandRequest(Guid.NewGuid(),state.Value.Finding.Revision,state.Value.ReviewBasis,FindingCommandReceiptSeed.Fields(FindingCommandActions.Designate));
    var preview=await FindingCommandWorkspace.PreviewAsync(db,mixed,w.FindingId,r);Assert.True(preview.Succeeded,preview.Message);Assert.False(preview.Value!.CanProceed);
    Assert.Equal(ErrorCodes.ScopeDenied,(await FindingCommandWorkspace.ExecuteAsync(db,mixed,w.FindingId,r with{Reviewed=true,ExpectedRequestHash=preview.Value.RequestHash})).ErrorCode);
    Assert.Empty(await db.FindingAmendments.ToListAsync());
    var response=await FindingCommandReceiptSeed.ReviewAsync(db,mixed,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));
    Assert.True((await FindingCommandWorkspace.ExecuteAsync(db,mixed,w.FindingId,response)).Succeeded);
  }

  [Fact,Trait("Profile","Database")]
  public async Task RevisionFencesAbaAndMutableIntentCannotReplaceReviewedHash()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await FindingCommandReceiptSeed.ReviewAsync(db,w.Staff,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));
    Assert.Equal(ErrorCodes.IdempotencyConflict,(await FindingCommandWorkspace.ExecuteAsync(db,w.Staff,w.FindingId,r with{Fields=new(FindingCommandActions.RecordResponse,Response:new("Changed after review",true))})).ErrorCode);
    Assert.Empty(await db.FindingAmendments.ToListAsync());
    Assert.True((await AuditPlanningService.RecordFindingResponseAsync(db,w.Staff,new(w.FindingId,"A",false))).Succeeded);
    r=await FindingCommandReceiptSeed.ReviewAsync(db,w.Staff,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));
    Assert.True((await AuditPlanningService.RecordFindingResponseAsync(db,w.Staff,new(w.FindingId,"B",false))).Succeeded);
    Assert.True((await AuditPlanningService.RecordFindingResponseAsync(db,w.Staff,new(w.FindingId,"A",false))).Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision,(await FindingCommandWorkspace.ExecuteAsync(db,w.Staff,w.FindingId,r)).ErrorCode);
    Assert.Equal(3,await db.FindingAmendments.CountAsync());Assert.Empty(await db.FindingCommandReceipts.ToListAsync());
  }

  [Theory,MemberData(nameof(Actions)),Trait("Profile","Database")]
  public async Task LegacyCoresJoinAmbientTransactionAndRollbackEvidence(string action)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    await using(var tx=await db.Database.BeginTransactionAsync())
    {
      if(action==FindingCommandActions.RecordResponse) Assert.True((await AuditPlanningService.RecordFindingResponseAsync(db,w.Manager,new(w.FindingId,"Response",false))).Succeeded);
      else Assert.True((await AuditPlanningService.DesignateManagementLetterFindingAsync(db,w.Manager,new(w.FindingId,action==FindingCommandActions.Designate,"Recommendation"))).Succeeded);
      Assert.Same(tx,db.Database.CurrentTransaction);Assert.Single(await db.FindingAmendments.ToListAsync());await tx.RollbackAsync();
    }
    await using var check=new AuditSphereDbContext(pg.Options);Assert.Empty(await check.FindingAmendments.ToListAsync());Assert.Equal(1,await check.Findings.Select(x=>x.Revision).SingleAsync());
  }

  private sealed class ReceiptInterceptor(bool pause=false,bool refuse=false):SaveChangesInterceptor
  {
    public TaskCompletionSource<bool> Reached {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default)
    {
      var rows=e.Context!.ChangeTracker.Entries<FindingCommandReceipt>().Where(x=>x.State==EntityState.Added).ToArray();
      foreach(var row in rows) row.Entity.CreatedAt=new DateTimeOffset(DateTimeOffset.UtcNow.Ticks/10*10+7,TimeSpan.Zero);
      if(pause && rows.Length>0){Reached.TrySetResult(true);await Release.Task.WaitAsync(ct);if(refuse)throw new InvalidOperationException("SYNTHETIC publication rollback");}
      return result;
    }
  }
  [Fact,Trait("Profile","Database")]
  public async Task FirstReturnReplayAndLookupUsePersistedMicrosecondTimestampExactly()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);
    await using var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(new ReceiptInterceptor()).Options);
    var r=await FindingCommandReceiptSeed.ReviewAsync(db,w.Staff,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));
    var first=await FindingCommandWorkspace.ExecuteAsync(db,w.Staff,w.FindingId,r);Assert.True(first.Succeeded,first.Message);
    Assert.Equal(0,first.Value!.CreatedAt.Ticks%10);
    await using var fresh=new AuditSphereDbContext(pg.Options);var replay=await FindingCommandWorkspace.ExecuteAsync(fresh,w.Staff,w.FindingId,r);
    var lookup=await FindingCommandWorkspace.LookupAsync(fresh,w.Staff,w.FindingId,r.RequestId,r.ExpectedRequestHash);
    Assert.Equal(Exact(first.Value),Exact(replay.Value));Assert.Equal(Exact(first.Value),Exact(lookup.Value!.Receipt));
  }
  [Theory,InlineData(false),InlineData(true),Trait("Profile","Database")]
  public async Task LookupWaitsForPendingPublicationOrRollback(bool refuse)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);FindingCommandRequest r;
    await using(var setup=new AuditSphereDbContext(pg.Options))r=await FindingCommandReceiptSeed.ReviewAsync(setup,w.Staff,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));
    var pause=new ReceiptInterceptor(true,refuse);await using var writer=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    await using var reader=new AuditSphereDbContext(pg.Options);await using var observer=new AuditSphereDbContext(pg.Options);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
      var publish=FindingCommandWorkspace.ExecuteAsync(writer,w.Staff,w.FindingId,r,timeout.Token);await pause.Reached.Task.WaitAsync(timeout.Token);
      var lookup=FindingCommandWorkspace.LookupAsync(reader,w.Staff,w.FindingId,r.RequestId,r.ExpectedRequestHash,timeout.Token);
      await WaitForLock(observer,timeout.Token);Assert.False(lookup.IsCompleted);pause.Release.TrySetResult(true);
      if(refuse) await Assert.ThrowsAsync<InvalidOperationException>(async()=>await publish);else Assert.True((await publish).Succeeded);
      var recovered=await lookup;Assert.True(recovered.Succeeded,recovered.Message);Assert.Equal(!refuse,recovered.Value!.Found);
      Assert.Equal(refuse?0:1,await observer.FindingAmendments.CountAsync());Assert.Equal(refuse?0:1,await observer.FindingCommandReceipts.CountAsync());
    }
    finally{pause.Release.TrySetResult(true);}
  }
  private static async Task WaitForLock(AuditSphereDbContext observer,CancellationToken ct)
  {
    var end=DateTimeOffset.UtcNow.AddSeconds(10);
    while(DateTimeOffset.UtcNow<end)
    {
      await observer.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()",ct);
      if(await observer.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND wait_event_type='Lock'").SingleAsync(ct)>0)return;
      await Task.Delay(20,ct);
    }
    Assert.Fail("Expected publication lock wait was not observed.");
  }
  [Theory,InlineData(false),InlineData(true),Trait("Profile","Database")]
  public async Task ConcurrentSameAndDifferentRequestsPublishOneTransition(bool sameRequest)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);FindingCommandRequest r,r2;
    await using(var setup=new AuditSphereDbContext(pg.Options)){r=await FindingCommandReceiptSeed.ReviewAsync(setup,w.Staff,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));r2=sameRequest?r:await FindingCommandReceiptSeed.ReviewAsync(setup,w.Staff,w.FindingId,new(FindingCommandActions.RecordResponse,Response:new("Other exact intent",true)));}
    await using var first=new AuditSphereDbContext(pg.Options);await using var second=new AuditSphereDbContext(pg.Options);
    var results=await Task.WhenAll(FindingCommandWorkspace.ExecuteAsync(first,w.Staff,w.FindingId,r),FindingCommandWorkspace.ExecuteAsync(second,w.Staff,w.FindingId,r2));
    if(sameRequest){Assert.All(results,x=>Assert.True(x.Succeeded,x.Message));Assert.Equal(Exact(results[0].Value),Exact(results[1].Value));}
    else{Assert.Single(results,x=>x.Succeeded);Assert.Equal(ErrorCodes.StaleRevision,results.Single(x=>!x.Succeeded).ErrorCode);}
    await using var check=new AuditSphereDbContext(pg.Options);Assert.Single(await check.FindingAmendments.ToListAsync());Assert.Single(await check.FindingCommandReceipts.ToListAsync());
  }

  [Theory,InlineData("epoch"),InlineData("disabled"),InlineData("revoked"),InlineData("expired"),Trait("Profile","Database")]
  public async Task ReceiptRecoveryRequiresCurrentActorLifetimeAndExactScope(string change)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await FindingCommandReceiptSeed.ReviewAsync(db,w.Staff,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));
    Assert.True((await FindingCommandWorkspace.ExecuteAsync(db,w.Staff,w.FindingId,r)).Succeeded);
    var other=await FindingCommandWorkspace.LookupAsync(db,w.Manager,w.FindingId,r.RequestId,r.ExpectedRequestHash);Assert.True(other.Succeeded,other.Message);Assert.False(other.Value!.Found);
    var unknown=await FindingCommandWorkspace.LookupAsync(db,w.Staff,w.FindingId,Guid.NewGuid(),new('a',64));Assert.True(unknown.Succeeded,unknown.Message);Assert.False(unknown.Value!.Found);
    Assert.Equal(ErrorCodes.ScopeDenied,(await FindingCommandWorkspace.LookupAsync(db,w.Staff,Guid.NewGuid(),r.RequestId,r.ExpectedRequestHash)).ErrorCode);
    if(change=="epoch")await db.Users.Where(x=>x.Id==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));
    if(change=="disabled")await db.Users.Where(x=>x.Id==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Disabled,true));
    if(change=="revoked")await db.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
    if(change=="expired")await db.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.GrantedAt,DateTimeOffset.UtcNow.AddDays(-2)).SetProperty(x=>x.ExpiresAt,DateTimeOffset.UtcNow.AddDays(-1)));
    Assert.False((await FindingCommandWorkspace.LookupAsync(db,w.Staff,w.FindingId,r.RequestId,r.ExpectedRequestHash)).Succeeded);
    Assert.False((await FindingCommandWorkspace.ExecuteAsync(db,w.Staff,w.FindingId,r)).Succeeded);
    if(change=="epoch")Assert.Equal(ErrorCodes.GenerationStale,(await FindingCommandWorkspace.LookupAsync(db,w.Staff with{SessionEpoch=w.Staff.SessionEpoch+1},w.FindingId,r.RequestId,r.ExpectedRequestHash)).ErrorCode);
    Assert.Single(await db.FindingAmendments.ToListAsync());
  }

  [Theory,InlineData(false),InlineData(true),Trait("Profile","Database")]
  public async Task FreezeAndHoldBlockNewResponsesIncludingLegacyButRetainRecovery(bool freeze)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;await using var db=new AuditSphereDbContext(pg.Options);
    var r=await FindingCommandReceiptSeed.ReviewAsync(db,w.Staff,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));
    var saved=await FindingCommandWorkspace.ExecuteAsync(db,w.Staff,w.FindingId,r);Assert.True(saved.Succeeded,saved.Message);
    var fresh=await FindingCommandReceiptSeed.ReviewAsync(db,w.Staff,w.FindingId,new(FindingCommandActions.RecordResponse,Response:new("Blocked new response",true)));
    if(freeze)
    {
      var report=Guid.NewGuid();db.AuditDeliverables.Add(new(){Id=report,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,Kind="MANAGEMENT_LETTER",Version=1,TemplateVersion="SYNTHETIC",InputDigest=new('a',64),ContentSha256=new('b',64),Content=[1],CreatedByUserId=f.Admin.Id,CreatedAt=DateTimeOffset.UtcNow});
      var signed=DateTimeOffset.UtcNow.AddDays(-61);db.EngagementFileFreezes.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,ReportDeliverableId=report,ReportSignedAt=signed,DueAt=signed.AddDays(60),State=FileFreezeStates.Frozen,FrozenAt=signed.AddDays(60),UpdatedAt=signed.AddDays(60)});
    }
    else db.EngagementHolds.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,EngagementId=f.EngagementId,HoldKind="Independence",Reason="SYNTHETIC hold",CreatedAt=DateTimeOffset.UtcNow});
    await db.SaveChangesAsync();
    Assert.False((await AuditPlanningService.RecordFindingResponseAsync(db,w.Staff,new(w.FindingId,"Blocked legacy",true))).Succeeded);
    Assert.False((await FindingCommandWorkspace.ExecuteAsync(db,w.Staff,w.FindingId,fresh)).Succeeded);
    var state=await FindingCommandWorkspace.StateAsync(db,w.Staff,w.FindingId);Assert.True(state.Succeeded,state.Message);Assert.False(state.Value!.CanRecordResponse);Assert.False(state.Value.CanDesignate);
    Assert.Equal(Exact(saved.Value),Exact((await FindingCommandWorkspace.ExecuteAsync(db,w.Staff,w.FindingId,r)).Value));
    Assert.Single(await db.FindingAmendments.ToListAsync());Assert.Single(await db.FindingCommandReceipts.ToListAsync());
  }
  [Fact,Trait("Profile","Database")]
  public async Task FirmGuardRevocationWinsBeforePublication()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);FindingCommandRequest r;
    await using(var setup=new AuditSphereDbContext(pg.Options))r=await FindingCommandReceiptSeed.ReviewAsync(setup,w.Staff,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));
    await using var revoke=new AuditSphereDbContext(pg.Options);await using var tx=await revoke.Database.BeginTransactionAsync();
    await revoke.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={w.Fixture.FirmId} FOR UPDATE").AsNoTracking().SingleAsync();
    await using var writer=new AuditSphereDbContext(pg.Options);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var pending=FindingCommandWorkspace.ExecuteAsync(writer,w.Staff,w.FindingId,r,timeout.Token);await WaitForLock(revoke,timeout.Token);
    await revoke.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));await tx.CommitAsync();
    Assert.False((await pending).Succeeded);Assert.Empty(await revoke.FindingAmendments.ToListAsync());Assert.Empty(await revoke.FindingCommandReceipts.ToListAsync());
  }

  [Theory,InlineData(FindingCommandActions.RecordResponse),InlineData(FindingCommandActions.Designate),Trait("Profile","Database")]
  public async Task MaximumArabicMultilineInputIsCanonicalAndRoundTripsExactly(string action)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var max=action==FindingCommandActions.RecordResponse?20000:2000;var text="مراجعة\r\n\t"+new string('ع',max-9);
    var fields=action==FindingCommandActions.RecordResponse?new FindingCommandFields(action,Response:new(text,false)):new(action,Designation:new(text));
    var r=await FindingCommandReceiptSeed.ReviewAsync(db,w.Manager,w.FindingId,fields);var saved=await FindingCommandWorkspace.ExecuteAsync(db,w.Manager,w.FindingId,r);Assert.True(saved.Succeeded,saved.Message);
    Assert.Equal(text,action==FindingCommandActions.RecordResponse?saved.Value!.Amendment.After.ManagementResponse:saved.Value!.Amendment.After.LetterRecommendation);
    var state=await FindingCommandWorkspace.StateAsync(db,w.Manager,w.FindingId);
    foreach(var invalid in new[]{new string('ع',max+1),"bad\u0001control"})
    {
      var bad=action==FindingCommandActions.RecordResponse?new FindingCommandFields(action,Response:new(invalid,false)):new(action,Designation:new(invalid));
      Assert.False((await FindingCommandWorkspace.PreviewAsync(db,w.Manager,w.FindingId,new(Guid.NewGuid(),state.Value!.Finding.Revision,state.Value.ReviewBasis,bad))).Succeeded);
    }
    Assert.Single(await db.FindingAmendments.ToListAsync());
  }

  [Fact,Trait("Profile","Database")]
  public async Task DatabaseRejectsDirectTransitionsAndForgedEventsAndProtectsImmutableEvidence()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    Assert.Equal("55000",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE findings SET management_response='fabricated',revision=revision+1 WHERE id={w.FindingId}"))).SqlState);
    var r=await FindingCommandReceiptSeed.ReviewAsync(db,w.Staff,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));var saved=await FindingCommandWorkspace.ExecuteAsync(db,w.Staff,w.FindingId,r);Assert.True(saved.Succeeded,saved.Message);
    foreach(var commands in new[]{("UPDATE finding_amendments SET created_at=created_at+interval '1 second'","DELETE FROM finding_amendments"),("UPDATE finding_command_receipts SET created_at=created_at+interval '1 second'","DELETE FROM finding_command_receipts")})
    {
      Assert.Equal("55000",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(commands.Item1))).SqlState);
      Assert.Equal("55000",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(commands.Item2))).SqlState);
    }
    Assert.Equal("55000",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("INSERT INTO finding_amendments SELECT gen_random_uuid(),firm_id,client_id,engagement_id,finding_id,actor_id,actor_epoch,action,before_revision,after_revision,before_json,after_json,created_at FROM finding_amendments"))).SqlState);
    Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("INSERT INTO finding_command_receipts SELECT gen_random_uuid(),firm_id,client_id,engagement_id,finding_id,amendment_id,actor_id,actor_epoch,gen_random_uuid(),request_hash,review_basis,action,input_json,preview_json,created_at FROM finding_command_receipts"))).SqlState);
    Assert.Single(await db.FindingAmendments.ToListAsync());Assert.Single(await db.FindingCommandReceipts.ToListAsync());
  }

  [Theory,InlineData(false),InlineData(true),Trait("Profile","Database")]
  public async Task MigrationEmptyDownPreservesFindingsAndRetainedLegacyOrReviewedEvidenceRefusesDown(bool reviewed)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var migrations=db.Database.GetMigrations().ToArray();var index=Array.FindIndex(migrations,x=>x.EndsWith("_FindingReviewedCommandReceipts",StringComparison.Ordinal));Assert.True(index>0);
    await db.GetService<IMigrator>().MigrateAsync(migrations[index-1]);
    Assert.Equal(1,await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM findings").SingleAsync());
    await db.GetService<IMigrator>().MigrateAsync(migrations[index]);
    if(reviewed){var r=await FindingCommandReceiptSeed.ReviewAsync(db,w.Staff,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));Assert.True((await FindingCommandWorkspace.ExecuteAsync(db,w.Staff,w.FindingId,r)).Succeeded);}
    else Assert.True((await AuditPlanningService.RecordFindingResponseAsync(db,w.Staff,new(w.FindingId,"Legacy evidence",false))).Succeeded);
    var refused=await Assert.ThrowsAnyAsync<Exception>(()=>db.GetService<IMigrator>().MigrateAsync(migrations[index-1]));Assert.Equal("55000",Assert.IsType<PostgresException>(refused.GetBaseException()).SqlState);
    Assert.Single(await db.FindingAmendments.ToListAsync());Assert.Equal(reviewed?1:0,await db.FindingCommandReceipts.CountAsync());
  }

  [Theory,InlineData("actor"),InlineData("scope"),InlineData("before"),InlineData("outcome"),Trait("Profile","Database")]
  public async Task PublicationGuardRejectsFabricatedActorScopeBeforeStateAndOutcome(string corruption)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);
    var r=await FindingCommandReceiptSeed.ReviewAsync(db,w.Staff,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));
    var preview=await FindingCommandWorkspace.PreviewAsync(db,w.Staff,w.FindingId,r);Assert.True(preview.Succeeded,preview.Message);
    await using(var tx=await db.Database.BeginTransactionAsync())
    {
      Assert.True((await AuditPlanningService.RecordFindingResponseAsync(db,w.Staff,new(w.FindingId,r.Fields.Response!.ManagementResponse,false))).Succeeded);
      var e=await db.FindingAmendments.AsNoTracking().SingleAsync();
      var p=preview.Value!;
      var fields=r.Fields;
      if(corruption=="before")p=p with{Before=p.Before with{ImpactDescription="Fabricated prior finding"}};
      if(corruption=="outcome") {fields=fields with{Response=new("Different published response",false)};p=p with{CanonicalInput=fields};}
      db.FindingCommandReceipts.Add(new(){Id=Guid.NewGuid(),FirmId=e.FirmId,ClientId=corruption=="scope"?Guid.NewGuid():e.ClientId,
        EngagementId=e.EngagementId,FindingId=e.FindingId,AmendmentId=e.Id,ActorId=corruption=="actor"?w.Fixture.Reviewer.Id:e.ActorId,
        ActorEpoch=e.ActorEpoch,Action=e.Action,RequestId=r.RequestId,RequestHash=p.RequestHash,ReviewBasis=p.ReviewBasis,
        InputJson=JsonSerializer.Serialize(fields),PreviewJson=JsonSerializer.Serialize(p),CreatedAt=DateTimeOffset.UtcNow});
      var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal("23514",Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
      await tx.RollbackAsync();
    }
    await using var fresh=new AuditSphereDbContext(pg.Options);Assert.Empty(await fresh.FindingAmendments.ToListAsync());Assert.Empty(await fresh.FindingCommandReceipts.ToListAsync());Assert.Equal(1,await fresh.Findings.Select(x=>x.Revision).SingleAsync());
  }

  [Theory,InlineData("firm"),InlineData("client"),InlineData("engagement"),Trait("Profile","Database")]
  public async Task ExactScopedManagerDesignationAndUndesignationRemainSupported(string scope)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await FindingCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;await using var db=new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"Manager",scope=="firm"?null:f.ClientId,scope=="engagement"?f.EngagementId:null));await db.SaveChangesAsync();
    var actor=w.Staff with{Roles=["Staff","Manager"]};
    foreach(var action in new[]{FindingCommandActions.Designate,FindingCommandActions.Undesignate})
    {
      var r=await FindingCommandReceiptSeed.ReviewAsync(db,actor,w.FindingId,FindingCommandReceiptSeed.Fields(action));var saved=await FindingCommandWorkspace.ExecuteAsync(db,actor,w.FindingId,r);Assert.True(saved.Succeeded,saved.Message);
    }
    Assert.Equal(2,await db.FindingAmendments.CountAsync());Assert.Equal(2,await db.FindingCommandReceipts.CountAsync());
  }

}
