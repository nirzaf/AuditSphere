using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
namespace AuditSphereOps.Domain.Tests;
public sealed class WorkpaperCommandReceiptTests
{
  private static string Exact(object? v)=>JsonSerializer.Serialize(v);
  [Fact,Trait("Profile","Database")]
  public async Task SaveOriginalAcknowledgementSurvivesLaterSaveDiscardRestartSubmitAndHold()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);var first=await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,r);Assert.True(first.Succeeded,first.Message);
    var second=await WorkpaperCommandReceiptSeed.SaveAsync(db,w,"Later work","Later conclusion");Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,second)).Succeeded);
    var discard=await WorkpaperCommandReceiptSeed.ReviewAsync(db,w,WorkpaperCommandActions.DiscardDraft);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,discard)).Succeeded);
    var restart=await WorkpaperCommandReceiptSeed.SaveAsync(db,w,"Restart work","Restart conclusion");Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,restart)).Succeeded);
    var submit=await WorkpaperCommandReceiptSeed.ReviewAsync(db,w,WorkpaperCommandActions.Submit);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,submit)).Succeeded);
    await db.Engagements.Where(x=>x.Id==w.Fixture.EngagementId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ProfessionalWorkBlocked,true));
    var replay=await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,r);Assert.True(replay.Succeeded,replay.Message);Assert.Equal(Exact(first.Value),Exact(replay.Value));
    var lookup=await WorkpaperCommandWorkspace.LookupAsync(db,w.Staff,w.WorkpaperId,r.RequestId,r.ExpectedRequestHash);Assert.True(lookup.Succeeded,lookup.Message);Assert.Equal(Exact(first.Value),Exact(lookup.Value!.Receipt));
    var recover=await WorkpaperCommandWorkspace.RecoverAsync(db,w.Staff,w.WorkpaperId,r with{ExpectedRequestHash=null});Assert.True(recover.Succeeded,recover.Message);Assert.Equal(Exact(first.Value),Exact(recover.Value!.Receipt));
    Assert.Equal("1",recover.Value.Receipt!.CommandEvent.After.Draft!.DraftRevision);Assert.Equal(5,await db.WorkpaperCommandReceipts.CountAsync());Assert.Single(await db.WorkpaperSubmissions.ToListAsync());
  }
  [Theory,InlineData(WorkpaperCommandActions.DiscardDraft),InlineData(WorkpaperCommandActions.Submit),Trait("Profile","Database")]
  public async Task ReviewedFinalActionsRecoverOriginalReceiptBeforeCurrentGatesAndRejectChangedIntent(string action)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var save=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,save)).Succeeded);
    var r=await WorkpaperCommandReceiptSeed.ReviewAsync(db,w,action);
    Assert.False((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,r with{Reviewed=false})).Succeeded);
    var first=await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,r);Assert.True(first.Succeeded,first.Message);
    if(action==WorkpaperCommandActions.DiscardDraft){var restart=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,restart)).Succeeded);}
    await db.Engagements.Where(x=>x.Id==w.Fixture.EngagementId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ProfessionalWorkBlocked,true));
    Assert.Equal(Exact(first.Value),Exact((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,r)).Value));
    Assert.Equal(ErrorCodes.IdempotencyConflict,(await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,r with{ReviewBasis=new('a',64)})).ErrorCode);
  }
  [Fact,Trait("Profile","Database")]
  public async Task DiscardAdvanceGenerationsLoadCleanBasisRestartSaveAgainAndSubmitNewBasis()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var initial=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,initial)).Succeeded);
    var discard=await WorkpaperCommandReceiptSeed.ReviewAsync(db,w,WorkpaperCommandActions.DiscardDraft);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,discard)).Succeeded);
    await db.ClientSafetyStates.Where(x=>x.Id==w.Fixture.ClientId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.InputGeneration,x=>x.InputGeneration+1));
    await db.FirmSafetyStates.Where(x=>x.Id==w.Fixture.FirmId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.PolicyGeneration,x=>x.PolicyGeneration+1));
    var clean=await AuditPlanningService.LoadWorkpaperDraftAsync(db,w.Staff,w.WorkpaperId);Assert.True(clean.Succeeded,clean.Message);Assert.Equal(0,clean.Value!.DraftRevision);Assert.Equal(2,clean.Value.BaseInputGeneration);Assert.Equal(2,clean.Value.BasePolicyGeneration);
    var restart=await WorkpaperCommandReceiptSeed.SaveAsync(db,w,"New basis work","New basis conclusion");var saved=await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,restart);Assert.True(saved.Succeeded,saved.Message);
    Assert.Equal("2",saved.Value!.CommandEvent.After.Draft!.BaseInputGeneration);Assert.Equal("2",saved.Value.CommandEvent.After.Draft.BasePolicyGeneration);
    var again=await WorkpaperCommandReceiptSeed.SaveAsync(db,w,"New basis saved again","New basis conclusion");Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,again)).Succeeded);
    var submit=await WorkpaperCommandReceiptSeed.ReviewAsync(db,w,WorkpaperCommandActions.Submit);var result=await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,submit);Assert.True(result.Succeeded,result.Message);Assert.Equal("CONSUMED",result.Value!.CommandEvent.After.Draft!.Lifecycle);
    Assert.Equal("New basis saved again",(await db.WorkpaperSubmissions.SingleAsync()).WorkPerformed);
    Assert.Equal(Exact(saved.Value),Exact((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,restart)).Value));
  }
  [Fact,Trait("Profile","Database")]
  public async Task PrepareSaveIsReadOnlyAndExpectedHashBindsExactFieldsIncludingSupersededSave()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);Assert.Empty(await db.WorkpaperDrafts.ToListAsync());Assert.Empty(await db.WorkpaperCommandEvents.ToListAsync());Assert.Empty(await db.WorkpaperCommandReceipts.ToListAsync());
    Assert.False((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,r with{ExpectedRequestHash=null})).Succeeded);
    var changed=r with{Fields=r.Fields with{Save=r.Fields.Save! with{WorkPerformed="Changed after prepare"}}};Assert.Equal(ErrorCodes.IdempotencyConflict,(await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,changed)).ErrorCode);
    Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,r)).Succeeded);
    Assert.Equal(ErrorCodes.IdempotencyConflict,(await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,changed)).ErrorCode);
    var bad=await WorkpaperCommandWorkspace.LookupAsync(db,w.Staff,w.WorkpaperId,r.RequestId,new('a',64));Assert.Equal(ErrorCodes.IdempotencyConflict,bad.ErrorCode);
    Assert.Single(await db.WorkpaperCommandEvents.ToListAsync());
  }
  [Theory,InlineData("epoch"),InlineData("disabled"),InlineData("revoked"),InlineData("expired"),Trait("Profile","Database")]
  public async Task RecoveryRequiresCurrentActorLifetimeAndExactScopeAndUnknownIsOpaque(string change)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,r)).Succeeded);
    var other=await WorkpaperCommandWorkspace.LookupAsync(db,w.Admin,w.WorkpaperId,r.RequestId,r.ExpectedRequestHash);Assert.True(other.Succeeded,other.Message);Assert.False(other.Value!.Found);
    var absent=await WorkpaperCommandWorkspace.LookupAsync(db,w.Staff,w.WorkpaperId,Guid.NewGuid(),new('a',64));Assert.True(absent.Succeeded,absent.Message);Assert.False(absent.Value!.Found);
    if(change=="epoch")await db.Users.Where(x=>x.Id==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));
    if(change=="disabled")await db.Users.Where(x=>x.Id==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Disabled,true));
    if(change=="revoked")await db.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
    if(change=="expired")await db.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.GrantedAt,DateTimeOffset.UtcNow.AddDays(-2)).SetProperty(x=>x.ExpiresAt,DateTimeOffset.UtcNow.AddDays(-1)));
    Assert.False((await WorkpaperCommandWorkspace.LookupAsync(db,w.Staff,w.WorkpaperId,r.RequestId,r.ExpectedRequestHash)).Succeeded);
    if(change=="epoch")Assert.Equal(ErrorCodes.GenerationStale,(await WorkpaperCommandWorkspace.LookupAsync(db,w.Staff with{SessionEpoch=w.Staff.SessionEpoch+1},w.WorkpaperId,r.RequestId,r.ExpectedRequestHash)).ErrorCode);
  }
  private sealed class ReceiptInterceptor(bool pause=false,bool refuse=false):SaveChangesInterceptor
  {
    public TaskCompletionSource<bool> Reached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default)
    {
      var rows=e.Context!.ChangeTracker.Entries<WorkpaperCommandReceipt>().Where(x=>x.State==EntityState.Added).ToArray();foreach(var row in rows)row.Entity.CreatedAt=new DateTimeOffset(DateTimeOffset.UtcNow.Ticks/10*10+7,TimeSpan.Zero);
      if(rows.Length>0 && pause){Reached.TrySetResult(true);await Release.Task.WaitAsync(ct);}if(rows.Length>0 && refuse)throw new InvalidOperationException("SYNTHETIC receipt insert refusal");return result;
    }
  }
  [Theory,InlineData("UTC"),InlineData("Asia/Qatar"),Trait("Profile","Database")]
  public async Task FirstReturnAndOriginalRecoveryUseCanonicalUtcAndPersistedPrecision(string zone)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(new ReceiptInterceptor()).Options);
    await db.Database.OpenConnectionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('TimeZone',{zone},false)");
    var r=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);var first=await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,r);Assert.True(first.Succeeded,first.Message);Assert.Equal(0,first.Value!.CreatedAt.Ticks%10);Assert.Equal(TimeSpan.Zero,first.Value.CommandEvent.After.Draft!.LastSavedAt.Offset);
    await using var fresh=new AuditSphereDbContext(pg.Options);Assert.Equal(Exact(first.Value),Exact((await WorkpaperCommandWorkspace.ExecuteAsync(fresh,w.Staff,w.WorkpaperId,r)).Value));Assert.Equal(Exact(first.Value),Exact((await WorkpaperCommandWorkspace.LookupAsync(fresh,w.Staff,w.WorkpaperId,r.RequestId,r.ExpectedRequestHash)).Value!.Receipt));
  }
  [Theory,InlineData(WorkpaperCommandActions.SaveDraft),InlineData(WorkpaperCommandActions.DiscardDraft),InlineData(WorkpaperCommandActions.Submit),Trait("Profile","Database")]
  public async Task ReceiptFailureRollsBackDraftWorkpaperProcedureSubmissionAndAmbientCallerCanCommitSafely(string action)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);WorkpaperCommandRequest r;long priorEvents;
    await using(var setup=new AuditSphereDbContext(pg.Options))
    {
      if(action!=WorkpaperCommandActions.SaveDraft){var save=await WorkpaperCommandReceiptSeed.SaveAsync(setup,w);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(setup,w.Staff,w.WorkpaperId,save)).Succeeded);r=await WorkpaperCommandReceiptSeed.ReviewAsync(setup,w,action);}else r=await WorkpaperCommandReceiptSeed.SaveAsync(setup,w);
      priorEvents=await setup.WorkpaperCommandEvents.LongCountAsync();
    }
    await using(var writer=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(new ReceiptInterceptor(refuse:true)).Options))
    {await using var tx=await writer.Database.BeginTransactionAsync();await Assert.ThrowsAsync<InvalidOperationException>(()=>WorkpaperCommandWorkspace.ExecuteAsync(writer,w.Staff,w.WorkpaperId,r));Assert.Same(tx,writer.Database.CurrentTransaction);await tx.CommitAsync();}
    await using var check=new AuditSphereDbContext(pg.Options);Assert.Equal(priorEvents,await check.WorkpaperCommandEvents.LongCountAsync());Assert.Equal(priorEvents,await check.WorkpaperCommandReceipts.LongCountAsync());Assert.Empty(await check.WorkpaperSubmissions.ToListAsync());Assert.Equal("WORKING",await check.Workpapers.Select(x=>x.Status).SingleAsync());Assert.Equal("PLANNED",await check.AuditProcedures.Select(x=>x.Status).SingleAsync());
    if(action!=WorkpaperCommandActions.SaveDraft)Assert.Equal("ACTIVE",await check.WorkpaperDrafts.Select(x=>x.Lifecycle).SingleAsync());else Assert.Empty(await check.WorkpaperDrafts.ToListAsync());
  }
  [Fact,Trait("Profile","Database")]
  public async Task MissingSafetyStateFailsClosedWithoutDraftOrReceipt()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);await db.ClientSafetyStates.Where(x=>x.Id==w.Fixture.ClientId).ExecuteDeleteAsync();Assert.False((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,r)).Succeeded);Assert.False((await AuditPlanningService.LoadWorkpaperDraftAsync(db,w.Staff,w.WorkpaperId)).Succeeded);Assert.Empty(await db.WorkpaperDrafts.ToListAsync());
  }
  [Fact,Trait("Profile","Database")]
  public async Task ConcurrentIdenticalSavePublishesOnceAndDifferentRequestCannotOvertakeBasis()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);
    await using var setup=new AuditSphereDbContext(pg.Options);var request=await WorkpaperCommandReceiptSeed.SaveAsync(setup,w);
    await using var one=new AuditSphereDbContext(pg.Options);await using var two=new AuditSphereDbContext(pg.Options);
    var results=await Task.WhenAll(WorkpaperCommandWorkspace.ExecuteAsync(one,w.Staff,w.WorkpaperId,request),WorkpaperCommandWorkspace.ExecuteAsync(two,w.Staff,w.WorkpaperId,request));
    Assert.All(results,r=>Assert.True(r.Succeeded,r.Message));Assert.Equal(Exact(results[0].Value),Exact(results[1].Value));
    var stale=request with{RequestId=Guid.NewGuid(),ExpectedRequestHash=null};
    Assert.False((await WorkpaperCommandWorkspace.PrepareSaveAsync(setup,w.Staff,w.WorkpaperId,stale)).Succeeded);
    Assert.Single(await setup.WorkpaperCommandReceipts.ToListAsync());Assert.Single(await setup.WorkpaperCommandEvents.ToListAsync());
  }
  [Theory,InlineData(false),InlineData(true),Trait("Profile","Database")]
  public async Task LookupWaitsForPendingPublicationAndObservesCommitOrRollback(bool rollback)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);
    await using var setup=new AuditSphereDbContext(pg.Options);var request=await WorkpaperCommandReceiptSeed.SaveAsync(setup,w);
    var interceptor=new ReceiptInterceptor(pause:true,refuse:rollback);
    await using var writer=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options);
    await using var reader=new AuditSphereDbContext(pg.Options);
    var publish=WorkpaperCommandWorkspace.ExecuteAsync(writer,w.Staff,w.WorkpaperId,request);
    await interceptor.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    var lookup=WorkpaperCommandWorkspace.LookupAsync(reader,w.Staff,w.WorkpaperId,request.RequestId,request.ExpectedRequestHash);
    try{await Task.Delay(150);Assert.False(lookup.IsCompleted);}finally{interceptor.Release.TrySetResult(true);}
    if(rollback)await Assert.ThrowsAsync<InvalidOperationException>(async()=>{await publish;});else Assert.True((await publish).Succeeded);
    var result=await lookup.WaitAsync(TimeSpan.FromSeconds(15));Assert.True(result.Succeeded,result.Message);Assert.Equal(!rollback,result.Value!.Found);
    Assert.Equal(rollback?0:1,await setup.WorkpaperCommandEvents.CountAsync());
  }
  [Fact,Trait("Profile","Database")]
  public async Task FirmGuardRevocationWinsBeforePublication()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);
    await using var setup=new AuditSphereDbContext(pg.Options);var request=await WorkpaperCommandReceiptSeed.SaveAsync(setup,w);
    await using var revoker=new AuditSphereDbContext(pg.Options);await using var tx=await revoker.Database.BeginTransactionAsync();
    await revoker.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM firm_safety_states WHERE id={w.Fixture.FirmId} FOR UPDATE");
    await using var writer=new AuditSphereDbContext(pg.Options);var publish=WorkpaperCommandWorkspace.ExecuteAsync(writer,w.Staff,w.WorkpaperId,request);
    await Task.Delay(150);Assert.False(publish.IsCompleted);
    await revoker.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));await tx.CommitAsync();
    Assert.False((await publish.WaitAsync(TimeSpan.FromSeconds(15))).Succeeded);Assert.Empty(await setup.WorkpaperCommandReceipts.ToListAsync());Assert.Empty(await setup.WorkpaperDrafts.ToListAsync());
  }
  [Fact,Trait("Profile","Database")]
  public async Task MaximumArabicMultilineRoundTripsAndOversizedFieldsRefuseBeforeMutation()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var text=new string('ع',99996)+"\r\n\tع";var conclusion=new string('ق',20000);
    var request=await WorkpaperCommandReceiptSeed.SaveAsync(db,w,text,conclusion);var saved=await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,request);Assert.True(saved.Succeeded,saved.Message);
    Assert.Equal(text,saved.Value!.CommandEvent.After.Draft!.WorkPerformed);Assert.Equal(conclusion,saved.Value.CommandEvent.After.Draft.Conclusion);
    Assert.Equal(Exact(saved.Value),Exact((await WorkpaperCommandWorkspace.LookupAsync(db,w.Staff,w.WorkpaperId,request.RequestId,request.ExpectedRequestHash)).Value!.Receipt));
    var invalid=request with{RequestId=Guid.NewGuid(),Fields=request.Fields with{Save=request.Fields.Save! with{WorkPerformed=new string('ع',100001)}}};
    Assert.False((await WorkpaperCommandWorkspace.PrepareSaveAsync(db,w.Staff,w.WorkpaperId,invalid)).Succeeded);Assert.Single(await db.WorkpaperCommandReceipts.ToListAsync());
  }
  [Fact,Trait("Profile","Database")]
  public async Task StaleReviewedDiscardCannotOvertakeLaterSaveOrDraftAba()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var save=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,save)).Succeeded);
    var old=await WorkpaperCommandReceiptSeed.ReviewAsync(db,w,WorkpaperCommandActions.DiscardDraft);
    var later=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,later)).Succeeded);
    Assert.False((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,old)).Succeeded);
    var discard=await WorkpaperCommandReceiptSeed.ReviewAsync(db,w,WorkpaperCommandActions.DiscardDraft);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,discard)).Succeeded);
    var restart=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,restart)).Succeeded);
    Assert.False((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,old)).Succeeded);Assert.Equal("ACTIVE",await db.WorkpaperDrafts.Select(x=>x.Lifecycle).SingleAsync());
  }
  [Fact,Trait("Profile","Database")]
  public async Task SourceTransitionsEventsAndReceiptsCannotBeForgedOrRewritten()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var request=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,request)).Succeeded);
    foreach(var sql in new[]{"UPDATE workpaper_command_events SET action='DISCARD_DRAFT'","DELETE FROM workpaper_command_events","UPDATE workpaper_command_receipts SET request_hash=repeat('a',64)","DELETE FROM workpaper_command_receipts"})
      Assert.Equal("55000",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(sql))).SqlState);
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE workpaper_drafts SET lifecycle='DISCARDED'"));
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("INSERT INTO workpaper_command_events SELECT gen_random_uuid(),firm_id,client_id,engagement_id,workpaper_id,actor_id,actor_epoch,gen_random_uuid(),request_hash,action,before_json,after_json,created_at FROM workpaper_command_events"));
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("INSERT INTO workpaper_command_receipts SELECT gen_random_uuid(),firm_id,client_id,engagement_id,workpaper_id,event_id,actor_id,actor_epoch,gen_random_uuid(),request_hash,action,input_json,preview_json,created_at FROM workpaper_command_receipts"));
    Assert.Single(await db.WorkpaperCommandReceipts.ToListAsync());
  }
  [Fact,Trait("Profile","Database")]
  public async Task EmptyDowngradePreservesWorkpaperButRetainedAcknowledgementsRefuseDown()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();var index=Array.FindIndex(migrations,x=>x.EndsWith("_WorkpaperCommandReceipts",StringComparison.Ordinal));Assert.True(index>0);
    await db.GetService<IMigrator>().MigrateAsync(migrations[index-1]);Assert.Equal(1,await db.Workpapers.CountAsync());await db.GetService<IMigrator>().MigrateAsync(migrations[index]);
    var request=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,request)).Succeeded);
    var error=await Assert.ThrowsAnyAsync<Exception>(()=>db.GetService<IMigrator>().MigrateAsync(migrations[index-1]));Assert.Equal("55000",Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
  }

  private sealed class RevokeAfterSubmissionRead(Func<Task> revoke):DbCommandInterceptor
  {
    public int Calls;
    public override async ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(System.Data.Common.DbCommand command,CommandExecutedEventData data,System.Data.Common.DbDataReader result,CancellationToken ct=default)
    {
      if(command.CommandText.Contains("workpaper_submissions",StringComparison.Ordinal) && Interlocked.CompareExchange(ref Calls,1,0)==0)await revoke();
      return result;
    }
  }
  [Fact,Trait("Profile","Database")]
  public async Task DetailProjectionRefusesRevocationDuringHistoryReads()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);
    var interceptor=new RevokeAfterSubmissionRead(async()=>{await using var db=new AuditSphereDbContext(pg.Options);await db.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));});
    await using var reader=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options);
    var result=await AuditRecordQueries.WorkpaperAsync(reader,w.Staff,w.WorkpaperId);Assert.Equal(1,interceptor.Calls);Assert.False(result.Succeeded);Assert.Equal(ErrorCodes.ScopeDenied,result.ErrorCode);Assert.Null(result.Value);
  }
  [Theory,InlineData("professional"),InlineData("hold"),InlineData("freeze"),Trait("Profile","Database")]
  public async Task HoldOrFreezeRefusesEveryFreshActionButPreservesOriginalAcknowledgement(string gate)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var saved=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);var original=await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,saved);Assert.True(original.Succeeded,original.Message);
    var next=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);var submit=await WorkpaperCommandReceiptSeed.ReviewAsync(db,w,WorkpaperCommandActions.Submit);var discard=await WorkpaperCommandReceiptSeed.ReviewAsync(db,w,WorkpaperCommandActions.DiscardDraft);
    if(gate=="professional")await db.Engagements.Where(x=>x.Id==w.Fixture.EngagementId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ProfessionalWorkBlocked,true));
    else if(gate=="hold") {db.EngagementHolds.Add(new(){Id=Guid.NewGuid(),FirmId=w.Fixture.FirmId,EngagementId=w.Fixture.EngagementId,HoldKind="Independence",Reason="SYNTHETIC hold",CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();}
    else {
      var f=w.Fixture;var report=Guid.NewGuid();db.AuditDeliverables.Add(new(){Id=report,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,Kind="MANAGEMENT_LETTER",Version=1,TemplateVersion="SYNTHETIC",InputDigest=new('a',64),ContentSha256=new('b',64),Content=[1],CreatedByUserId=f.Admin.Id,CreatedAt=DateTimeOffset.UtcNow});
      var signed=DateTimeOffset.UtcNow.AddDays(-61);db.EngagementFileFreezes.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,ReportDeliverableId=report,ReportSignedAt=signed,DueAt=signed.AddDays(60),State="FROZEN",FrozenAt=signed.AddDays(60),UpdatedAt=signed.AddDays(60)});await db.SaveChangesAsync();
    }
    foreach(var request in new[]{next,submit,discard})Assert.False((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,request)).Succeeded);
    var state=await WorkpaperCommandWorkspace.StateAsync(db,w.Staff,w.WorkpaperId);Assert.True(state.Succeeded,state.Message);Assert.False(state.Value!.CanSaveDraft);Assert.False(state.Value.CanDiscardDraft);Assert.False(state.Value.CanSubmit);
    Assert.Equal(Exact(original.Value),Exact((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,saved)).Value));Assert.Single(await db.WorkpaperCommandReceipts.ToListAsync());
  }

  [Theory,InlineData(false),InlineData(true),Trait("Profile","Database")]
  public async Task ProcedureModeAloneCannotPublishArbitrarySubmittedPaper(bool exactPaper)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var id=Guid.NewGuid();var actor=w.Staff.UserId.ToString();var epoch=w.Staff.SessionEpoch.ToString();var contextPaper=(exactPaper?id:Guid.NewGuid()).ToString();
    await using var tx=await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('audit.workpaper_action','PROCEDURE_SUBMIT',true),set_config('audit.workpaper_actor_id',{actor},true),set_config('audit.workpaper_actor_epoch',{epoch},true),set_config('audit.workpaper_procedure_paper',{contextPaper},true)");
    db.Workpapers.Add(new Workpaper{Id=id,FirmId=w.Fixture.FirmId,ClientId=w.Fixture.ClientId,EngagementId=w.Fixture.EngagementId,ProcedureId=w.ProcedureId,ActorId=w.Staff.UserId,Index="SYN-FORGED",Title="Synthetic refusal",Objective="Synthetic refusal",TemplateVersion="synthetic",Procedure="Synthetic",Revision=1,Status=WorkpaperStatuses.SubmittedSnapshot,WorkPerformed="Synthetic",Conclusion="Synthetic",CreatedAt=DateTimeOffset.UtcNow,SubmittedAt=DateTimeOffset.UtcNow});
    if(exactPaper){await db.SaveChangesAsync();Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>tx.CommitAsync())).SqlState);}
    else {var error=await Assert.ThrowsAnyAsync<Exception>(()=>db.SaveChangesAsync());Assert.Equal("55000",Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);}
    if(!exactPaper)await tx.RollbackAsync();await using var check=new AuditSphereDbContext(pg.Options);Assert.False(await check.Workpapers.AnyAsync(x=>x.Id==id));Assert.Empty(await check.WorkpaperSubmissions.ToListAsync());
  }

  [Fact,Trait("Profile","Database")]
  public async Task LegacySubmissionReplaysOriginalBeforeHoldButChangedContentKeepsProtectedState()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var request=new SubmitWorkpaperRequest(w.WorkpaperId,1,"Synthetic legacy work","Synthetic legacy conclusion");
    var original=await AuditPlanningService.SubmitWorkpaperAsync(db,w.Staff,request);Assert.True(original.Succeeded,original.Message);
    await db.Engagements.Where(x=>x.Id==w.Fixture.EngagementId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ProfessionalWorkBlocked,true));
    var replay=await AuditPlanningService.SubmitWorkpaperAsync(db,w.Staff,request);Assert.True(replay.Succeeded,replay.Message);Assert.Equal(original.Value,replay.Value);
    var changed=await AuditPlanningService.SubmitWorkpaperAsync(db,w.Staff,request with{WorkPerformed="Changed legacy content"});Assert.False(changed.Succeeded);Assert.Equal(ErrorCodes.ProtectedState,changed.ErrorCode);
    Assert.Single(await db.WorkpaperCommandReceipts.ToListAsync());Assert.Single(await db.WorkpaperCommandEvents.ToListAsync());Assert.Single(await db.WorkpaperSubmissions.ToListAsync());
  }

}
