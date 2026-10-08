using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed class ClientContactCreationTests
{
  [Fact]
  public async Task ConcurrentReviewedCreationRetainsOneContactAndImmutableActorOwnedReceipt()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);
    var a=PbcSeed.Actor(f.Admin,"Administrator");await using var db=new AuditSphereDbContext(pg.Options);
    var first=await PracticeCrmService.CreateClientContactAsync(db,a,new(f.ClientId,"Original primary","original@example.test","Finance",Primary:true));
    Assert.True(first.Succeeded,first.Message);
    var state=(await ClientContactCreationWorkspace.StateAsync(db,a,f.ClientId)).Value!;
    var request=new ContactCreationRequest(Guid.NewGuid(),state.ReviewBasis,new("New primary","new@example.test","Finance",true));
    var preview=(await ClientContactCreationWorkspace.PreviewAsync(db,a,f.ClientId,request)).Value!;
    Assert.Equal(first.Value,Assert.Single(preview.ReplacedPrimary).Id);
    request=request with {Reviewed=true,ExpectedRequestHash=preview.RequestHash};
    async Task<AuditSphereOps.Domain.Shared.CommandResult<ContactCreationReceipt>> Create()
    {await using var worker=new AuditSphereDbContext(pg.Options);return await ClientContactCreationWorkspace.ExecuteAsync(worker,a,f.ClientId,request);}
    var results=await Task.WhenAll(Create(),Create());Assert.All(results,r=>Assert.True(r.Succeeded,r.Message));
    Assert.Equal(results[0].Value,results[1].Value);Assert.Equal(2,await db.ClientContacts.CountAsync());
    Assert.Single(await db.ClientContactCreations.ToListAsync());
    Assert.False((await db.ClientContacts.AsNoTracking().SingleAsync(c=>c.Id==first.Value)).Primary);
    Assert.True((await db.ClientContacts.AsNoTracking().SingleAsync(c=>c.Id==results[0].Value!.ContactId)).Primary);
    var fresh=(await ClientContactCreationWorkspace.StateAsync(db,a,f.ClientId)).Value!;
    var refreshedPreview=(await ClientContactCreationWorkspace.PreviewAsync(db,a,f.ClientId,request with {ReviewBasis=fresh.ReviewBasis})).Value!;
    Assert.Equal(preview.RequestHash,refreshedPreview.RequestHash);
    var repeat=await ClientContactCreationWorkspace.ExecuteAsync(db,a,f.ClientId,request with {ReviewBasis=fresh.ReviewBasis});
    Assert.Equal(results[0].Value,repeat.Value);Assert.Equal(fresh.SafetyGeneration,(await ClientContactCreationWorkspace.StateAsync(db,a,f.ClientId)).Value!.SafetyGeneration);
    var changed=request with {ReviewBasis=fresh.ReviewBasis,Fields=request.Fields with {Name="Different"}};
    var changedPreview=(await ClientContactCreationWorkspace.PreviewAsync(db,a,f.ClientId,changed)).Value!;
    Assert.Equal("idempotency.conflict",(await ClientContactCreationWorkspace.ExecuteAsync(db,a,f.ClientId,changed with {ExpectedRequestHash=changedPreview.RequestHash})).ErrorCode);
    var lookup=await ClientContactCreationWorkspace.LookupAsync(db,a,f.ClientId,request.RequestId,preview.RequestHash);
    Assert.Equal(results[0].Value,lookup.Value!.Receipt);
    Assert.False((await ClientContactCreationWorkspace.ExecuteAsync(db,a,f.ClientId,request with {Reviewed=false})).Succeeded);
    Assert.False((await ClientContactCreationWorkspace.ExecuteAsync(db,a,f.ClientId,request with {ExpectedRequestHash=null})).Succeeded);
    Assert.Equal(4,await db.RoleGrants.CountAsync());Assert.Empty(await db.UserAccessInvitations.ToListAsync());
    var immutable=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE client_contact_creations SET input_json='{{}}' WHERE id={results[0].Value!.Id}"));
    Assert.Equal("P0001",immutable.SqlState);
  }

  [Fact]
  public async Task ScopeEpochAndGuessedRequestsNeverExposeAnotherActorsReceipt()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);var foreign=await PbcSeed.SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);var admin=PbcSeed.Actor(f.Admin,"Administrator");
    var state=(await ClientContactCreationWorkspace.StateAsync(db,admin,f.ClientId)).Value!;
    var r=new ContactCreationRequest(Guid.NewGuid(),state.ReviewBasis,new("Contact","contact@example.test","Finance",false));
    var p=(await ClientContactCreationWorkspace.PreviewAsync(db,admin,f.ClientId,r)).Value!;
    var receipt=(await ClientContactCreationWorkspace.ExecuteAsync(db,admin,f.ClientId,r with {Reviewed=true,ExpectedRequestHash=p.RequestHash})).Value!;
    foreach(var actor in new[]{PbcSeed.Actor(f.Staff,"Staff"),PbcSeed.Actor(f.Client,"ClientUser"),PbcSeed.Actor(foreign.Admin,"Administrator")})
    {
      Assert.False((await ClientContactCreationWorkspace.StateAsync(db,actor,f.ClientId)).Succeeded);
      Assert.False((await ClientContactCreationWorkspace.LookupAsync(db,actor,f.ClientId,r.RequestId,p.RequestHash)).Succeeded);
    }
    Assert.Equal((await ClientContactCreationWorkspace.StateAsync(db,admin,foreign.ClientId)).ErrorCode,
      (await ClientContactCreationWorkspace.StateAsync(db,admin,Guid.NewGuid())).ErrorCode);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"Manager",f.ClientId));await db.SaveChangesAsync();
    var staff=PbcSeed.Actor(f.Staff,"Staff");
    Assert.False((await ClientContactCreationWorkspace.LookupAsync(db,staff,f.ClientId,r.RequestId,p.RequestHash)).Value!.Found);
    Assert.False((await ClientContactCreationWorkspace.LookupAsync(db,staff,f.ClientId,Guid.NewGuid(),p.RequestHash)).Value!.Found);
    Assert.Equal(receipt,(await ClientContactCreationWorkspace.LookupAsync(db,admin,f.ClientId,r.RequestId,p.RequestHash)).Value!.Receipt);
    await db.RoleGrants.Where(g=>g.UserId==f.Staff.Id&&g.Role=="Manager").ExecuteUpdateAsync(s=>s.SetProperty(g=>g.RevokedAt,DateTimeOffset.UtcNow));
    Assert.False((await ClientContactCreationWorkspace.StateAsync(db,staff,f.ClientId)).Succeeded);
    await db.Users.Where(u=>u.Id==f.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    Assert.False((await ClientContactCreationWorkspace.LookupAsync(db,admin,f.ClientId,r.RequestId,p.RequestHash)).Succeeded);
  }

  [Fact]
  public async Task StalePreviewInvalidFieldsAndUnreviewedIntentHaveNoSideEffects()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var a=PbcSeed.Actor(f.Admin,"Administrator");var s=(await ClientContactCreationWorkspace.StateAsync(db,a,f.ClientId)).Value!;
    var r=new ContactCreationRequest(Guid.NewGuid(),s.ReviewBasis,new("Name","name@example.test","Finance",false));
    var p=(await ClientContactCreationWorkspace.PreviewAsync(db,a,f.ClientId,r)).Value!;
    await db.ClientSafetyStates.Where(g=>g.Id==f.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(g=>g.InputGeneration,g=>g.InputGeneration+1));
    Assert.Equal("generation.stale",(await ClientContactCreationWorkspace.ExecuteAsync(db,a,f.ClientId,r with {Reviewed=true,ExpectedRequestHash=p.RequestHash})).ErrorCode);
    foreach(var fields in new[]{r.Fields with{Name=new string('x',201)},r.Fields with{Email="bad"},r.Fields with{Role="\n"}})
      Assert.False((await ClientContactCreationWorkspace.PreviewAsync(db,a,f.ClientId,r with {Fields=fields})).Succeeded);
    Assert.Empty(await db.ClientContacts.ToListAsync());Assert.Empty(await db.ClientContactCreations.ToListAsync());
  }

  [Fact]
  public async Task RevocationDuringContactPublicationRollsBackContactPrimaryAndReceipt()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);var a=PbcSeed.Actor(f.Staff,"Staff");
    ContactCreationRequest request;string hash;
    await using(var setup=new AuditSphereDbContext(pg.Options))
    {
      setup.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"Manager",f.ClientId));await setup.SaveChangesAsync();
      var s=(await ClientContactCreationWorkspace.StateAsync(setup,a,f.ClientId)).Value!;
      request=new(Guid.NewGuid(),s.ReviewBasis,new("Name","name@example.test","Finance",true));
      hash=(await ClientContactCreationWorkspace.PreviewAsync(setup,a,f.ClientId,request)).Value!.RequestHash;
    }
    var hook=new RevokeAfterWrite(async()=>{await using var m=new AuditSphereDbContext(pg.Options);
      await m.RoleGrants.Where(g=>g.UserId==f.Staff.Id&&g.Role=="Manager").ExecuteUpdateAsync(s=>s.SetProperty(g=>g.RevokedAt,DateTimeOffset.UtcNow));});
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(hook).Options))
      Assert.False((await ClientContactCreationWorkspace.ExecuteAsync(db,a,f.ClientId,request with{Reviewed=true,ExpectedRequestHash=hash})).Succeeded);
    Assert.True(hook.Fired);await using var check=new AuditSphereDbContext(pg.Options);
    Assert.Empty(await check.ClientContacts.ToListAsync());Assert.Empty(await check.ClientContactCreations.ToListAsync());
    Assert.Equal(1,(await check.ClientSafetyStates.SingleAsync()).InputGeneration);
  }
  private sealed class RevokeAfterWrite(Func<Task> revoke):SaveChangesInterceptor
  {
    public bool Fired {get;private set;}
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,int result,CancellationToken cancellationToken=default)
    {if(!Fired){Fired=true;await revoke();}return result;}
  }
}
