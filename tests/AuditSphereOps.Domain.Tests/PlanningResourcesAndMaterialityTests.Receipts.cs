using System.Data.Common;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;
public sealed partial class PlanningResourcesAndMaterialityTests
{
  private static ResourcePlanningFields PlanningFields(World w,string kind)=>kind switch {
    "PROFILE"=>new(kind,w.Users["associate"].Id,Department:"Audit",Skills:"IFRS,ifrs",WeeklyCapacityMinutes:1200,TargetUtilizationPercent:"75.00"),
    "CERTIFICATION"=>new(kind,w.Users["associate"].Id,Name:"Synthetic certification",ExpiresOn:new DateOnly(2028,12,31)),
    "AVAILABILITY"=>new(kind,w.Users["associate"].Id,StartDate:new DateOnly(2026,10,5),EndDate:new DateOnly(2026,10,6),AvailabilityKind:"LEAVE",MinutesPerDay:480),
    _=>new(kind,w.Users["associate"].Id,EngagementId:w.EngagementId,WeekStart:new DateOnly(2026,10,7),PlannedMinutes:1800) };
  private static async Task<ResourcePlanningCommandRequest> PlanningReview(AuditSphereDbContext db,World w,ResourcePlanningFields fields)
  {
    var result=await ResourcePlanningCommandWorkspace.PreviewAsync(db,w.Actor("partner","Partner"),new(Guid.NewGuid(),fields));
    Assert.True(result.Succeeded,result.Message);
    var p=result.Value!;return new(p.RequestId,p.Fields,p.RequestHash,p.ReviewBasis,true);
  }
  [Theory]
  [InlineData("PROFILE")][InlineData("CERTIFICATION")][InlineData("AVAILABILITY")][InlineData("ALLOCATION")]
  public async Task PlanningReviewedCommandsPublishOneReceiptAndMatchingReplay(string kind)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);
    Assert.True((await StaffingService.AssignAsync(db,w.Actor("partner","Partner"),new(w.EngagementId,w.Users["associate"].Id,StaffingLevels.StaffAssociate))).Succeeded);
    var r=await PlanningReview(db,w,PlanningFields(w,kind));
    var first=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,w.Actor("partner","Partner"),r);Assert.True(first.Succeeded,first.Message);
    var replay=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,w.Actor("partner","Partner"),r);Assert.True(replay.Succeeded,replay.Message);Assert.Equal(first.Value!.Id,replay.Value!.Id);
    var lookup=await ResourcePlanningCommandWorkspace.LookupAsync(db,w.Actor("partner","Partner"),r.RequestId,r.RequestHash);Assert.True(lookup.Value!.Found);Assert.Equal(first.Value.Id,lookup.Value.Receipt!.Id);
    Assert.Single(await db.ResourcePlanningReceipts.ToListAsync());
    var wrongActor=await ResourcePlanningCommandWorkspace.LookupAsync(db,w.Actor("partner2","Partner"),r.RequestId,r.RequestHash);Assert.False(wrongActor.Value!.Found);
    Assert.False((await ResourcePlanningCommandWorkspace.ExecuteAsync(db,w.Actor("partner","Partner"),r with {Reviewed=false})).Succeeded);
    await using var mutate=new AuditSphereDbContext(pg.Options);await Assert.ThrowsAsync<PostgresException>(async()=>await mutate.ResourcePlanningReceipts.ExecuteDeleteAsync());
  }
  [Fact]
  public async Task PlanningStaleAllocationOrCapacityCannotOverwriteAndRemovalHasNullResult()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);var actor=w.Actor("partner","Partner");
    Assert.True((await StaffingService.AssignAsync(db,actor,new(w.EngagementId,w.Users["associate"].Id,StaffingLevels.StaffAssociate))).Succeeded);
    var r=await PlanningReview(db,w,PlanningFields(w,"ALLOCATION"));
    Assert.True((await ResourcePlanningService.SetAllocationAsync(db,actor,new(w.EngagementId,w.Users["associate"].Id,new DateOnly(2026,10,5),1200))).Succeeded);
    Assert.False((await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,r)).Succeeded);
    Assert.Equal(1200,(await db.StaffAllocations.SingleAsync()).PlannedMinutes);
    r=await PlanningReview(db,w,PlanningFields(w,"ALLOCATION"));
    Assert.True((await ResourcePlanningService.SaveProfileAsync(db,actor,new(w.Users["associate"].Id,"Audit","",2400,80))).Succeeded);
    Assert.False((await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,r)).Succeeded);
    var removal=await PlanningReview(db,w,PlanningFields(w,"ALLOCATION") with {PlannedMinutes=0});
    var result=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,removal);Assert.True(result.Succeeded,result.Message);Assert.Null(result.Value!.ResourceId);
    Assert.Empty(await db.StaffAllocations.ToListAsync());Assert.Single(await db.EngagementStaffAssignments.ToListAsync());
    var retry=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,removal);Assert.Equal(result.Value.Id,retry.Value!.Id);
  }
  [Fact]
  public async Task PlanningScopeIdentityAndChangedIntentAreRefused()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);
    var fields=PlanningFields(w,"CERTIFICATION");var r=await PlanningReview(db,w,fields);
    Assert.False((await ResourcePlanningCommandWorkspace.PreviewAsync(db,w.Actor("partner","Partner"),new(Guid.NewGuid(),fields with {Department="Unrelated"}))).Succeeded);
    Assert.False((await ResourcePlanningCommandWorkspace.PreviewAsync(db,w.Actor("outsider","Partner"),new(Guid.NewGuid(),fields))).Succeeded);
    Assert.False((await ResourcePlanningCommandWorkspace.PreviewAsync(db,w.Actor("partner","Partner"),new(Guid.NewGuid(),fields with {UserId=Guid.NewGuid()}))).Succeeded);
    var executed=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,w.Actor("partner","Partner"),r);Assert.True(executed.Succeeded,executed.Message);
    Assert.False((await ResourcePlanningCommandWorkspace.ExecuteAsync(db,w.Actor("partner","Partner"),r with {Fields=r.Fields with {Name="Changed"}})).Succeeded);
    var foreign=await SeedAsync(pg);var hidden=await ResourcePlanningCommandWorkspace.LookupAsync(db,foreign.Actor("partner","Partner"),r.RequestId,r.RequestHash);Assert.False(hidden.Value!.Found);
    await db.Users.Where(x=>x.Id==w.Users["partner"].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));
    Assert.False((await ResourcePlanningCommandWorkspace.LookupAsync(db,w.Actor("partner","Partner"),r.RequestId,r.RequestHash)).Succeeded);
  }
  private sealed class RefusePlanningReceipt:SaveChangesInterceptor
  {
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default)
    {if(e.Context!.ChangeTracker.Entries<ResourcePlanningReceipt>().Any(x=>x.State==EntityState.Added))throw new InvalidOperationException("Synthetic receipt refusal");return ValueTask.FromResult(result);}
  }
  [Fact]
  public async Task PlanningReceiptFailureRollsBackAdditiveMutation()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(new RefusePlanningReceipt()).Options)) {
      var r=await PlanningReview(db,w,PlanningFields(w,"CERTIFICATION"));
      await Assert.ThrowsAsync<InvalidOperationException>(async()=>await ResourcePlanningCommandWorkspace.ExecuteAsync(db,w.Actor("partner","Partner"),r));
    }
    await using var after=new AuditSphereDbContext(pg.Options);Assert.Empty(await after.StaffCertifications.ToListAsync());Assert.Empty(await after.ResourcePlanningReceipts.ToListAsync());
  }
  private sealed class PlanningLockObserver:DbCommandInterceptor
  {
    public TaskCompletionSource<bool> Reached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData e,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
    {if(command.CommandText.Contains("firm_safety_states",StringComparison.Ordinal)&&command.CommandText.Contains("FOR UPDATE",StringComparison.Ordinal))Reached.TrySetResult(true);return ValueTask.FromResult(result);}
  }
  [Fact]
  public async Task PlanningActorRevokedDuringFirmLockWaitPublishesNothing()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);ResourcePlanningCommandRequest r;
    await using(var db=new AuditSphereDbContext(pg.Options))r=await PlanningReview(db,w,PlanningFields(w,"CERTIFICATION"));
    await using var blocker=new AuditSphereDbContext(pg.Options);await using var tx=await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={w.FirmId} FOR UPDATE").SingleAsync();
    var observe=new PlanningLockObserver();await using var writer=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(observe).Options);
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));var attempt=ResourcePlanningCommandWorkspace.ExecuteAsync(writer,w.Actor("partner","Partner"),r,timeout.Token);
    await observe.Reached.Task.WaitAsync(timeout.Token);
    await blocker.RoleGrants.Where(x=>x.UserId==w.Users["partner"].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
    await blocker.Users.Where(x=>x.Id==w.Users["partner"].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));await tx.CommitAsync();
    Assert.False((await attempt).Succeeded);await using var after=new AuditSphereDbContext(pg.Options);Assert.Empty(await after.ResourcePlanningReceipts.ToListAsync());Assert.Empty(await after.StaffCertifications.ToListAsync());
  }
  private sealed class PausePlanningReceipt:SaveChangesInterceptor
  {
    public TaskCompletionSource<bool> Reached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default)
    {
      if(e.Context!.ChangeTracker.Entries<ResourcePlanningReceipt>().Any(x=>x.State==EntityState.Added)){Reached.TrySetResult(true);await Release.Task.WaitAsync(ct);}return result;
    }
  }
  [Fact]
  public async Task PlanningLookupAndConcurrentReplayWaitForPublicationThenReturnOneReceipt()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);ResourcePlanningCommandRequest r;
    await using(var db=new AuditSphereDbContext(pg.Options))r=await PlanningReview(db,w,PlanningFields(w,"CERTIFICATION"));
    var pause=new PausePlanningReceipt();var observe=new PlanningLockObserver();
    await using var writer=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    await using var reader=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(observe).Options);
    await using var replayDb=new AuditSphereDbContext(pg.Options);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
    try {
      var first=ResourcePlanningCommandWorkspace.ExecuteAsync(writer,w.Actor("partner","Partner"),r,timeout.Token);await pause.Reached.Task.WaitAsync(timeout.Token);
      var lookup=ResourcePlanningCommandWorkspace.LookupAsync(reader,w.Actor("partner","Partner"),r.RequestId,r.RequestHash,timeout.Token);await observe.Reached.Task.WaitAsync(timeout.Token);Assert.False(lookup.IsCompleted);
      var replay=ResourcePlanningCommandWorkspace.ExecuteAsync(replayDb,w.Actor("partner","Partner"),r,timeout.Token);pause.Release.TrySetResult(true);
      var result=await first;Assert.True(result.Succeeded,result.Message);var recovered=await lookup;Assert.True(recovered.Value!.Found);Assert.Equal(result.Value!.Id,recovered.Value.Receipt!.Id);Assert.Equal(result.Value.Id,(await replay).Value!.Id);
    }finally{pause.Release.TrySetResult(true);}
    await using var after=new AuditSphereDbContext(pg.Options);Assert.Single(await after.StaffCertifications.ToListAsync());Assert.Single(await after.ResourcePlanningReceipts.ToListAsync());
  }
  [Fact]
  public async Task DisabledTargetCannotReceiveNewPlanningActionsButExistingReceiptRemainsRecoverable()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);var actor=w.Actor("partner","Partner");
    var r=await PlanningReview(db,w,PlanningFields(w,"CERTIFICATION"));var first=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,r);Assert.True(first.Succeeded,first.Message);
    await db.Users.Where(x=>x.Id==w.Users["associate"].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Disabled,true));
    Assert.False((await ResourcePlanningCommandWorkspace.PreviewAsync(db,actor,new(Guid.NewGuid(),r.Fields))).Succeeded);
    Assert.False((await ResourcePlanningService.AddCertificationAsync(db,actor,new(w.Users["associate"].Id,"Refused",null,null))).Succeeded);
    Assert.True((await ResourcePlanningCommandWorkspace.LookupAsync(db,actor,r.RequestId,r.RequestHash)).Value!.Found);
    Assert.Equal(first.Value!.Id,(await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,r)).Value!.Id);
  }

}
