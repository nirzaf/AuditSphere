using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
namespace AuditSphereOps.Domain.Tests;
public sealed class EngagementActivationReviewTests
{
  [Fact]
  public async Task ExactReviewConcurrentRecoveryAndAppendOnlyEvidenceActivateOnce()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);var decision=await EngagementActivationReviewSeed.PopulateAsync(db,f);var a=PbcSeed.Actor(f.Staff,"Partner");
    var s=(await EngagementActivationWorkspace.StateAsync(db,a,f.EngagementId)).Value!;Assert.True(s.Eligible);Assert.Equal("9007199254740993",s.EngagementGeneration);
    var r=new EngagementActivationRequest(Guid.NewGuid(),s.ReviewBasis);var p=(await EngagementActivationWorkspace.PreviewAsync(db,a,f.EngagementId,r)).Value!;
    Assert.False((await EngagementActivationWorkspace.ExecuteAsync(db,a,f.EngagementId,r with{ExpectedRequestHash=p.RequestHash})).Succeeded);
    r=r with{Reviewed=true,ExpectedRequestHash=p.RequestHash};
    async Task<AuditSphereOps.Domain.Shared.CommandResult<EngagementActivationReceipt>> Run(){await using var worker=new AuditSphereDbContext(pg.Options);return await EngagementActivationWorkspace.ExecuteAsync(worker,a,f.EngagementId,r);}
    var results=await Task.WhenAll(Run(),Run());Assert.All(results,x=>Assert.True(x.Succeeded,x.Message));Assert.Equal(results[0].Value,results[1].Value);
    var receipt=results[0].Value!;Assert.Equal(decision,receipt.AcceptanceDecisionId);Assert.Equal("9007199254740994",receipt.ResultGeneration);
    Assert.Single(await db.EngagementActivations.ToListAsync());var e=await db.Engagements.AsNoTracking().SingleAsync(x=>x.Id==f.EngagementId);
    Assert.Equal(("Active",false,9007199254740994L),(e.Status,e.ProfessionalWorkBlocked,e.Generation));
    Assert.Equal(receipt,(await EngagementActivationWorkspace.LookupAsync(db,a,f.EngagementId,r.RequestId,p.RequestHash)).Value!.Receipt);
    Assert.Equal(receipt,(await EngagementActivationWorkspace.ExecuteAsync(db,a,f.EngagementId,r)).Value);
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE engagement_activations SET review_basis={new string('b',64)} WHERE id={receipt.Id}"));
    var migrations=db.Database.GetMigrations().ToArray();var at=Array.FindIndex(migrations,x=>x.EndsWith("_NativeEngagementActivationReview",StringComparison.Ordinal));Assert.True(at>0);
    await Assert.ThrowsAsync<PostgresException>(()=>db.GetService<IMigrator>().MigrateAsync(migrations[at-1]));
    Assert.Single(await db.EngagementActivations.ToListAsync());
  }
  [Fact]
  public async Task ExactScopeWrongRoleStaleDecisionHoldsAndEpochFailClosed()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);var foreign=await PbcSeed.SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);await EngagementActivationReviewSeed.PopulateAsync(db,f);var a=PbcSeed.Actor(f.Staff,"Partner");
    foreach(var denied in new[]{PbcSeed.Actor(f.Admin,"Administrator"),PbcSeed.Actor(f.Client,"ClientUser"),PbcSeed.Actor(foreign.Staff,"Partner")})
      Assert.False((await EngagementActivationWorkspace.StateAsync(db,denied,f.EngagementId)).Succeeded);
    Assert.Equal((await EngagementActivationWorkspace.StateAsync(db,a,foreign.EngagementId)).ErrorCode,(await EngagementActivationWorkspace.StateAsync(db,a,Guid.NewGuid())).ErrorCode);
    var s=(await EngagementActivationWorkspace.StateAsync(db,a,f.EngagementId)).Value!;var r=new EngagementActivationRequest(Guid.NewGuid(),s.ReviewBasis);
    var p=(await EngagementActivationWorkspace.PreviewAsync(db,a,f.EngagementId,r)).Value!;r=r with{Reviewed=true,ExpectedRequestHash=p.RequestHash};
    db.EngagementHolds.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,EngagementId=f.EngagementId,HoldKind="Independence",Reason="Synthetic unresolved hold",CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();
    var held=(await EngagementActivationWorkspace.StateAsync(db,a,f.EngagementId)).Value!;Assert.False(held.Eligible);Assert.Contains(held.Blockers,x=>x.Code=="holds.active");
    Assert.False((await EngagementActivationWorkspace.ExecuteAsync(db,a,f.EngagementId,r)).Succeeded);Assert.Empty(await db.EngagementActivations.ToListAsync());
    await db.EngagementHolds.Where(h=>h.EngagementId==f.EngagementId).ExecuteUpdateAsync(x=>x.SetProperty(h=>h.Released,true).SetProperty(h=>h.ReleasedAt,DateTimeOffset.UtcNow));
    await db.ClientSafetyStates.Where(x=>x.Id==f.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(c=>c.InputGeneration,c=>c.InputGeneration+1));
    var stale=(await EngagementActivationWorkspace.StateAsync(db,a,f.EngagementId)).Value!;Assert.Contains(stale.Blockers,x=>x.Code=="acceptance.stale");
    Assert.False((await EngagementActivationWorkspace.ExecuteAsync(db,a,f.EngagementId,r)).Succeeded);
    await db.Users.Where(u=>u.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    Assert.False((await EngagementActivationWorkspace.StateAsync(db,a,f.EngagementId)).Succeeded);Assert.Empty(await db.EngagementActivations.ToListAsync());
  }
  [Fact]
  public async Task LateRevocationRollsBackActivationAndEngagementTogether()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);var a=PbcSeed.Actor(f.Staff,"Partner");EngagementActivationRequest r;
    await using(var setup=new AuditSphereDbContext(pg.Options)){
      await EngagementActivationReviewSeed.PopulateAsync(setup,f);var s=(await EngagementActivationWorkspace.StateAsync(setup,a,f.EngagementId)).Value!;
      r=new(Guid.NewGuid(),s.ReviewBasis);var p=(await EngagementActivationWorkspace.PreviewAsync(setup,a,f.EngagementId,r)).Value!;r=r with{Reviewed=true,ExpectedRequestHash=p.RequestHash};}
    var hook=new RevokeAfterSave(async()=>{await using var changed=new AuditSphereDbContext(pg.Options);
      await changed.RoleGrants.Where(g=>g.UserId==f.Staff.Id&&g.Role=="Partner").ExecuteUpdateAsync(x=>x.SetProperty(g=>g.RevokedAt,DateTimeOffset.UtcNow));});
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(hook).Options))
      Assert.False((await EngagementActivationWorkspace.ExecuteAsync(db,a,f.EngagementId,r)).Succeeded);
    Assert.True(hook.Fired);await using var inspect=new AuditSphereDbContext(pg.Options);Assert.Empty(await inspect.EngagementActivations.ToListAsync());
    var e=await inspect.Engagements.SingleAsync(x=>x.Id==f.EngagementId);Assert.Equal(("Draft",true,9007199254740993L),(e.Status,e.ProfessionalWorkBlocked,e.Generation));
  }
  private sealed class RevokeAfterSave(Func<Task> revoke):SaveChangesInterceptor {
    public bool Fired{get;private set;}
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData d,int result,CancellationToken ct=default){if(!Fired){Fired=true;await revoke();}return result;}
  }
}
