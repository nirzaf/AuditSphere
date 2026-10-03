using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
namespace AuditSphereOps.Domain.Tests;
public sealed class EngagementCreationReviewTests
{
  [Fact]
  public async Task ConcurrentReviewedCreationRetainsOneBlockedShellAndImmutableExactReceipt()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    await EngagementCreationReviewSeed.PopulateAsync(db,f);var a=PbcSeed.Actor(f.Staff,"Manager");
    var s=(await EngagementCreationWorkspace.StateAsync(db,a,f.ClientId)).Value!;var r=new EngagementCreationRequest(Guid.NewGuid(),s.ReviewBasis,new(" AccountingOnly "," SYNTHETIC ","2027-01-01","2027-12-31"));
    var p=(await EngagementCreationWorkspace.PreviewAsync(db,a,f.ClientId,r)).Value!;Assert.Equal("AccountingOnly",p.Fields.ServiceRoute);
    r=r with{Reviewed=true,ExpectedRequestHash=p.RequestHash};
    async Task<AuditSphereOps.Domain.Shared.CommandResult<EngagementCreationReceipt>> Create(){await using var w=new AuditSphereDbContext(pg.Options);return await EngagementCreationWorkspace.ExecuteAsync(w,a,f.ClientId,r);}
    var results=await Task.WhenAll(Create(),Create());Assert.All(results,x=>Assert.True(x.Succeeded,x.Message));Assert.Equal(results[0].Value,results[1].Value);
    var receipt=results[0].Value!;var e=await db.Engagements.AsNoTracking().SingleAsync(x=>x.Id==receipt.EngagementId);
    Assert.Equal(("Draft",true,1L),(e.Status,e.ProfessionalWorkBlocked,e.Generation));Assert.Single(await db.EngagementCreations.ToListAsync());
    Assert.Empty(await db.EngagementActivations.ToListAsync());Assert.Empty(await db.UserAccessInvitations.ToListAsync());Assert.Equal(5,await db.RoleGrants.CountAsync());
    Assert.Equal(receipt,(await EngagementCreationWorkspace.LookupAsync(db,a,f.ClientId,r.RequestId,p.RequestHash)).Value!.Receipt);
    var duplicateRequest=r with{RequestId=Guid.NewGuid()};
    var duplicatePreview=await EngagementCreationWorkspace.PreviewAsync(db,a,f.ClientId,duplicateRequest);
    Assert.True(duplicatePreview.Succeeded,duplicatePreview.Message);Assert.Equal(receipt.EngagementId,duplicatePreview.Value!.ExistingEngagementId);
    var duplicateWrite=await EngagementCreationWorkspace.ExecuteAsync(db,a,f.ClientId,duplicateRequest with{Reviewed=true,ExpectedRequestHash=duplicatePreview.Value.RequestHash});
    Assert.Equal("engagement.conflict",duplicateWrite.ErrorCode);Assert.Equal(2,await db.Engagements.CountAsync());Assert.Single(await db.EngagementCreations.ToListAsync());
    await db.ClientSafetyStates.Where(x=>x.Id==f.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(y=>y.InputGeneration,y=>y.InputGeneration+1));
    Assert.Equal(receipt,(await EngagementCreationWorkspace.ExecuteAsync(db,a,f.ClientId,r)).Value);
    var fresh=(await EngagementCreationWorkspace.StateAsync(db,a,f.ClientId)).Value!;
    var changed=r with{ReviewBasis=fresh.ReviewBasis,Fields=r.Fields with{ServiceProfile="OTHER",PeriodStart="2028-01-01",PeriodEnd="2028-12-31"}};
    var cp=(await EngagementCreationWorkspace.PreviewAsync(db,a,f.ClientId,changed)).Value!;
    Assert.Equal("idempotency.conflict",(await EngagementCreationWorkspace.ExecuteAsync(db,a,f.ClientId,changed with{ExpectedRequestHash=cp.RequestHash})).ErrorCode);
    Assert.Equal("P0001",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE engagement_creations SET input_json='{{}}' WHERE id={receipt.Id}"))).SqlState);
  }
  [Fact]
  public async Task CurrentClientScopeEpochAndRevisionAreRequiredAndInvalidOrUnreviewedIntentsDoNotWrite()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);var foreign=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var a=PbcSeed.Actor(f.Staff,"Manager");
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"Manager",f.ClientId,f.EngagementId));await db.SaveChangesAsync();
    Assert.False((await EngagementCreationWorkspace.StateAsync(db,a,f.ClientId)).Succeeded);await EngagementCreationReviewSeed.PopulateAsync(db,f);
    var s=(await EngagementCreationWorkspace.StateAsync(db,a,f.ClientId)).Value!;var r=new EngagementCreationRequest(Guid.NewGuid(),s.ReviewBasis,new("AccountingOnly","SYNTHETIC","2027-01-01","2027-12-31"));
    var p=(await EngagementCreationWorkspace.PreviewAsync(db,a,f.ClientId,r)).Value!;
    Assert.False((await EngagementCreationWorkspace.ExecuteAsync(db,a,f.ClientId,r)).Succeeded);
    foreach(var fields in new[]{r.Fields with{PeriodStart="2027-02-30"},r.Fields with{PeriodEnd="2026-12-31"},r.Fields with{ServiceProfile="\n"},r.Fields with{ServiceRoute=new string('a',51)}})
      Assert.False((await EngagementCreationWorkspace.PreviewAsync(db,a,f.ClientId,r with{Fields=fields})).Succeeded);
    Assert.Equal((await EngagementCreationWorkspace.StateAsync(db,a,foreign.ClientId)).ErrorCode,(await EngagementCreationWorkspace.StateAsync(db,a,Guid.NewGuid())).ErrorCode);
    Assert.False((await EngagementCreationWorkspace.StateAsync(db,PbcSeed.Actor(f.Admin,"Administrator"),f.ClientId)).Succeeded);
    Assert.False((await EngagementCreationWorkspace.LookupAsync(db,a,f.ClientId,Guid.NewGuid(),p.RequestHash)).Value!.Found);
    await db.ClientSafetyStates.Where(x=>x.Id==f.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(y=>y.InputGeneration,y=>y.InputGeneration+1));
    Assert.Equal("generation.stale",(await EngagementCreationWorkspace.ExecuteAsync(db,a,f.ClientId,r with{Reviewed=true,ExpectedRequestHash=p.RequestHash})).ErrorCode);
    Assert.Empty(await db.EngagementCreations.ToListAsync());Assert.Equal(2,await db.Engagements.CountAsync());
    await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(y=>y.SessionEpoch,y=>y.SessionEpoch+1));
    Assert.False((await EngagementCreationWorkspace.LookupAsync(db,a,f.ClientId,r.RequestId,p.RequestHash)).Succeeded);
  }
  [Fact]
  public async Task RevocationAfterReceiptWriteRollsBackShellAndReceiptTogether()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);EngagementCreationRequest request;
    await using(var setup=new AuditSphereDbContext(pg.Options)){
      await EngagementCreationReviewSeed.PopulateAsync(setup,f);var a=PbcSeed.Actor(f.Staff,"Manager");var s=(await EngagementCreationWorkspace.StateAsync(setup,a,f.ClientId)).Value!;
      var r=new EngagementCreationRequest(Guid.NewGuid(),s.ReviewBasis,new("AccountingOnly","SYNTHETIC","2027-01-01","2027-12-31"));var p=(await EngagementCreationWorkspace.PreviewAsync(setup,a,f.ClientId,r)).Value!;request=r with{Reviewed=true,ExpectedRequestHash=p.RequestHash};}
    var hook=new RevokeAfterReceipt(async()=>{await using var changed=new AuditSphereDbContext(pg.Options);await changed.RoleGrants.Where(x=>x.UserId==f.Staff.Id&&x.Role=="Manager").ExecuteUpdateAsync(x=>x.SetProperty(y=>y.RevokedAt,DateTimeOffset.UtcNow));});
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(hook).Options))
      Assert.False((await EngagementCreationWorkspace.ExecuteAsync(db,PbcSeed.Actor(f.Staff,"Manager"),f.ClientId,request)).Succeeded);
    Assert.True(hook.Fired);await using var inspect=new AuditSphereDbContext(pg.Options);Assert.Empty(await inspect.EngagementCreations.ToListAsync());Assert.Single(await inspect.Engagements.ToListAsync());
  }

  [Fact]
  public async Task ReceiptLookupWaitsForGuardedPublicationBeforeReturningTheOutcome()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);var a=PbcSeed.Actor(f.Staff,"Manager");EngagementCreationRequest r;
    await using(var setup=new AuditSphereDbContext(pg.Options)){
      await EngagementCreationReviewSeed.PopulateAsync(setup,f);var state=(await EngagementCreationWorkspace.StateAsync(setup,a,f.ClientId)).Value!;
      r=new(Guid.NewGuid(),state.ReviewBasis,new("AccountingOnly","SYNTHETIC","2027-01-01","2027-12-31"));
      r=r with{Reviewed=true,ExpectedRequestHash=(await EngagementCreationWorkspace.PreviewAsync(setup,a,f.ClientId,r)).Value!.RequestHash};}
    var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var hook=new RevokeAfterReceipt(async()=>{entered.TrySetResult();await release.Task.WaitAsync(TimeSpan.FromSeconds(15));});
    var write=Task.Run(async()=>{await using var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(hook).Options);return await EngagementCreationWorkspace.ExecuteAsync(db,a,f.ClientId,r);});
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
    var read=Task.Run(async()=>{await using var db=new AuditSphereDbContext(pg.Options);return await EngagementCreationWorkspace.LookupAsync(db,a,f.ClientId,r.RequestId,r.ExpectedRequestHash);});
    await Task.Delay(150);var waited=!read.IsCompleted;release.TrySetResult();
    var published=await write;var observed=await read;Assert.True(waited);Assert.True(published.Succeeded,published.Message);Assert.Equal(published.Value,observed.Value!.Receipt);
  }
  [Fact]
  public async Task DeferredDatabaseGuardRejectsAReceiptWhoseBlockedShellWasChangedBeforeCommit()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);var a=PbcSeed.Actor(f.Staff,"Manager");EngagementCreationRequest r;
    await using(var setup=new AuditSphereDbContext(pg.Options)){
      await EngagementCreationReviewSeed.PopulateAsync(setup,f);var state=(await EngagementCreationWorkspace.StateAsync(setup,a,f.ClientId)).Value!;
      r=new(Guid.NewGuid(),state.ReviewBasis,new("AccountingOnly","SYNTHETIC","2027-01-01","2027-12-31"));
      r=r with{Reviewed=true,ExpectedRequestHash=(await EngagementCreationWorkspace.PreviewAsync(setup,a,f.ClientId,r)).Value!.RequestHash};}
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(new AlterShellBeforeCommit()).Options))
      Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>EngagementCreationWorkspace.ExecuteAsync(db,a,f.ClientId,r))).SqlState);
    await using var inspect=new AuditSphereDbContext(pg.Options);Assert.Empty(await inspect.EngagementCreations.ToListAsync());Assert.Single(await inspect.Engagements.ToListAsync());
  }
  private sealed class AlterShellBeforeCommit:SaveChangesInterceptor
  {
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData d,int result,CancellationToken ct=default)
    {
      var receipt=d.Context!.ChangeTracker.Entries<AuditSphereOps.Domain.Engagements.EngagementCreation>().Select(x=>x.Entity).SingleOrDefault();
      if(receipt is not null)await d.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE engagements SET service_profile_id='CHANGED' WHERE id={receipt.EngagementId}",ct);
      return result;
    }
  }
  private sealed class RevokeAfterReceipt(Func<Task> revoke):SaveChangesInterceptor
  {
    public bool Fired{get;private set;}
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData d,int result,CancellationToken ct=default)
    {if(!Fired&&d.Context!.ChangeTracker.Entries<AuditSphereOps.Domain.Engagements.EngagementCreation>().Any()){Fired=true;await revoke();}return result;}
  }
}
