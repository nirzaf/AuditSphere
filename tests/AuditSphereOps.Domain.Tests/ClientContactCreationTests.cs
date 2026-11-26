using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

  [Fact]
  public async Task ReviewedContactMaintenanceReplacesTheExactPrimarySetAndReplaysOneReceipt()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var firstId = Guid.CreateVersion7();
    var secondId = Guid.CreateVersion7();
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.ClientContacts.AddRange(
        new ClientContact { Id = firstId, FirmId = f.FirmId, PracticeClientId = f.ClientId, FullName = "First primary", Email = "first@example.test", Role = "Finance", Primary = true, IsActive = true },
        new ClientContact { Id = secondId, FirmId = f.FirmId, PracticeClientId = f.ClientId, FullName = "Second primary", Email = "second@example.test", Role = "Finance", Primary = true, IsActive = true });
      await setup.SaveChangesAsync();
    }

    await using var db = new AuditSphereDbContext(pg.Options);
    var state = (await ClientMaintenanceWorkspace.StateAsync(db, actor, f.ClientId, ClientMaintenanceKinds.Contact, firstId)).Value!;
    Assert.Equal(2, state.CurrentPrimary.Count);
    var request = new ClientMaintenanceRequest(Guid.NewGuid(), state.ReviewBasis,
      Contact: state.Contact! with { FullName = "First primary amended", Primary = true });
    var preview = (await ClientMaintenanceWorkspace.PreviewAsync(db, actor, f.ClientId, ClientMaintenanceKinds.Contact, firstId, request)).Value!;
    Assert.Equal(2, preview.ReplacedPrimary.Count);
    Assert.Equal(firstId, Assert.Single(preview.ResultPrimary).Id);
    request = request with { Reviewed = true, ExpectedRequestHash = preview.RequestHash };

    async Task<AuditSphereOps.Domain.Shared.CommandResult<ClientMaintenanceReceiptValue>> Execute()
    {
      await using var worker = new AuditSphereDbContext(pg.Options);
      return await ClientMaintenanceWorkspace.ExecuteAsync(worker, actor, f.ClientId, ClientMaintenanceKinds.Contact, firstId, request);
    }
    var results = await Task.WhenAll(Execute(), Execute());
    Assert.All(results, x => Assert.True(x.Succeeded, x.Message));
    Assert.Equal(results[0].Value, results[1].Value);
    Assert.Equal(1, await db.ClientMaintenanceReceipts.CountAsync());
    Assert.Equal(2, await db.ClientSafetyStates.Where(x => x.Id == f.ClientId).Select(x => x.InputGeneration).SingleAsync());
    Assert.True((await db.ClientContacts.AsNoTracking().SingleAsync(x => x.Id == firstId)).Primary);
    Assert.False((await db.ClientContacts.AsNoTracking().SingleAsync(x => x.Id == secondId)).Primary);

    var lookup = await ClientMaintenanceWorkspace.LookupAsync(db, actor, f.ClientId, ClientMaintenanceKinds.Contact,
      firstId, request.RequestId, preview.RequestHash);
    Assert.Equal(results[0].Value, lookup.Value!.Receipt);
    var freshState = (await ClientMaintenanceWorkspace.StateAsync(db, actor, f.ClientId, ClientMaintenanceKinds.Contact, firstId)).Value!;
    var changed = request with { ReviewBasis = freshState.ReviewBasis, Contact = request.Contact! with { Role = "Changed role" } };
    var changedPreview = (await ClientMaintenanceWorkspace.PreviewAsync(db, actor, f.ClientId,
      ClientMaintenanceKinds.Contact, firstId, changed with { Reviewed = false, ExpectedRequestHash = null })).Value!;
    Assert.Equal(ErrorCodes.IdempotencyConflict, (await ClientMaintenanceWorkspace.ExecuteAsync(db, actor, f.ClientId,
      ClientMaintenanceKinds.Contact, firstId, changed with { ExpectedRequestHash = changedPreview.RequestHash })).ErrorCode);

    var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE client_maintenance_receipts SET before_json='{{}}' WHERE id={results[0].Value!.Id}"));
    Assert.Equal("55000", immutable.SqlState);
    var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM client_maintenance_receipts WHERE id={results[0].Value!.Id}"));
    Assert.Equal("55000", delete.SqlState);
    var migrations = db.Database.GetMigrations().ToArray();
    Assert.Equal("20261126211715_ClientMaintenanceReceipts", migrations[^1]);
    var downgrade = await Assert.ThrowsAnyAsync<Exception>(() => db.GetService<IMigrator>().MigrateAsync(migrations[^2]));
    Assert.Equal("55000", Assert.IsType<PostgresException>(downgrade.GetBaseException()).SqlState);
    Assert.Single(await db.ClientMaintenanceReceipts.ToListAsync());
  }

  [Fact]
  public async Task ReviewedProfileMaintenanceFencesGenerationAndInvalidContactPeriodsHaveNoEffects()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    await using var db = new AuditSphereDbContext(pg.Options);
    var state = (await ClientMaintenanceWorkspace.StateAsync(db, actor, f.ClientId, ClientMaintenanceKinds.Profile)).Value!;
    var request = new ClientMaintenanceRequest(Guid.NewGuid(), state.ReviewBasis,
      Profile: state.Profile! with { LegalName = "Reviewed client name", TaxRegistrationNumber = new string('t', 64), EntityType = new string('e', 64) });
    var preview = (await ClientMaintenanceWorkspace.PreviewAsync(db, actor, f.ClientId, ClientMaintenanceKinds.Profile, null, request)).Value!;
    Assert.NotNull(preview);
    foreach (var invalidProfile in new[]
    {
      request.Profile! with { TaxRegistrationNumber = new string('t', 65) },
      request.Profile! with { EntityType = new string('e', 65) }
    })
      Assert.Equal("crm.invalid", (await ClientMaintenanceWorkspace.PreviewAsync(db, actor, f.ClientId,
        ClientMaintenanceKinds.Profile, null, request with { Profile = invalidProfile })).ErrorCode);
    request = request with { Reviewed = true, ExpectedRequestHash = preview.RequestHash };
    var direct = await PracticeCrmService.UpdateClientProfileAsync(db, actor,
      new UpdateClientProfileRequest(f.ClientId, "Intervening profile", ExpectedGeneration: long.Parse(state.SafetyGeneration)));
    Assert.True(direct.Succeeded, direct.Message);
    var stale = await ClientMaintenanceWorkspace.ExecuteAsync(db, actor, f.ClientId, ClientMaintenanceKinds.Profile, null, request);
    Assert.Equal(ErrorCodes.GenerationStale, stale.ErrorCode);
    Assert.Equal("Intervening profile", await db.PracticeClients.Where(x => x.Id == f.ClientId).Select(x => x.LegalName).SingleAsync());
    Assert.Empty(await db.ClientMaintenanceReceipts.ToListAsync());

    var contactId = Guid.CreateVersion7();
    db.ClientContacts.Add(new ClientContact
    {
      Id = contactId, FirmId = f.FirmId, PracticeClientId = f.ClientId, FullName = "Valid contact",
      Email = "valid@example.test", Role = "Finance", IsActive = true
    });
    await db.SaveChangesAsync();
    var contactState = (await ClientMaintenanceWorkspace.StateAsync(db, actor, f.ClientId, ClientMaintenanceKinds.Contact, contactId)).Value!;
    var boundaryContact = contactState.Contact! with
    {
      Phone = new string('p', 32), Title = new string('t', 100), SignatoryAuthority = new string('s', 64)
    };
    var boundaryRequest = new ClientMaintenanceRequest(Guid.NewGuid(), contactState.ReviewBasis, Contact: boundaryContact);
    Assert.True((await ClientMaintenanceWorkspace.PreviewAsync(db, actor, f.ClientId,
      ClientMaintenanceKinds.Contact, contactId, boundaryRequest)).Succeeded);
    foreach (var invalid in new[]
    {
      contactState.Contact! with { Email = "not-an-email" },
      contactState.Contact! with { ValidFrom = DateTimeOffset.Parse("2026-03-02T00:00:00Z"), ValidTo = DateTimeOffset.Parse("2026-03-01T00:00:00Z") },
      contactState.Contact! with { Phone = new string('p', 33) },
      contactState.Contact! with { Title = new string('t', 101) },
      contactState.Contact! with { SignatoryAuthority = new string('s', 65) }
    })
    {
      var invalidRequest = new ClientMaintenanceRequest(Guid.NewGuid(), contactState.ReviewBasis, Contact: invalid);
      Assert.Equal("crm.invalid", (await ClientMaintenanceWorkspace.PreviewAsync(db, actor, f.ClientId,
        ClientMaintenanceKinds.Contact, contactId, invalidRequest)).ErrorCode);
    }
    Assert.Empty(await db.ClientMaintenanceReceipts.ToListAsync());
    Assert.Equal("valid@example.test", await db.ClientContacts.Where(x => x.Id == contactId).Select(x => x.Email).SingleAsync());
  }

  [Fact]
  public async Task RevocationDuringReviewedContactMaintenanceRollsBackPrimarySetAndReceipt()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    var contactId = Guid.CreateVersion7();
    var otherId = Guid.CreateVersion7();
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId));
      setup.ClientContacts.AddRange(
        new ClientContact { Id = contactId, FirmId = f.FirmId, PracticeClientId = f.ClientId, FullName = "Original contact", Email = "original@example.test", Role = "Finance", Primary = true, IsActive = true },
        new ClientContact { Id = otherId, FirmId = f.FirmId, PracticeClientId = f.ClientId, FullName = "Other primary", Email = "other@example.test", Role = "Finance", Primary = true, IsActive = true });
      await setup.SaveChangesAsync();
    }

    ClientMaintenanceRequest request;
    string requestHash;
    await using (var preparation = new AuditSphereDbContext(pg.Options))
    {
      var state = (await ClientMaintenanceWorkspace.StateAsync(preparation, actor, f.ClientId,
        ClientMaintenanceKinds.Contact, contactId)).Value!;
      request = new ClientMaintenanceRequest(Guid.NewGuid(), state.ReviewBasis,
        Contact: state.Contact! with { FullName = "Changed before revocation", Primary = true });
      requestHash = (await ClientMaintenanceWorkspace.PreviewAsync(preparation, actor, f.ClientId,
        ClientMaintenanceKinds.Contact, contactId, request)).Value!.RequestHash;
    }

    var hook = new RevokeAfterWrite(async () =>
    {
      await using var revoke = new AuditSphereDbContext(pg.Options);
      await revoke.RoleGrants.Where(x => x.UserId == f.Staff.Id && x.Role == "Manager")
        .ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using (var command = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(hook).Options))
    {
      var result = await ClientMaintenanceWorkspace.ExecuteAsync(command, actor, f.ClientId, ClientMaintenanceKinds.Contact,
        contactId, request with { Reviewed = true, ExpectedRequestHash = requestHash });
      Assert.False(result.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    }

    Assert.True(hook.Fired);
    await using var check = new AuditSphereDbContext(pg.Options);
    var retained = await check.ClientContacts.AsNoTracking().Where(x => x.Id == contactId)
      .Select(x => new { x.FullName, x.Email, x.Primary }).SingleAsync();
    Assert.Equal("Original contact", retained.FullName);
    Assert.Equal("original@example.test", retained.Email);
    Assert.True(retained.Primary);
    Assert.True((await check.ClientContacts.AsNoTracking().SingleAsync(x => x.Id == otherId)).Primary);
    Assert.Equal(1, await check.ClientSafetyStates.Where(x => x.Id == f.ClientId).Select(x => x.InputGeneration).SingleAsync());
    Assert.Empty(await check.ClientMaintenanceReceipts.ToListAsync());
    Assert.NotNull(await check.RoleGrants.Where(x => x.UserId == f.Staff.Id && x.Role == "Manager")
      .Select(x => x.RevokedAt).SingleAsync());
  }

  [Fact]
  public async Task ClientMaintenanceReceiptLookupIsBoundToActorClientFirmAndSessionEpoch()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    await using var db = new AuditSphereDbContext(pg.Options);
    var state = (await ClientMaintenanceWorkspace.StateAsync(db, actor, f.ClientId, ClientMaintenanceKinds.Profile)).Value!;
    var request = new ClientMaintenanceRequest(Guid.NewGuid(), state.ReviewBasis,
      Profile: state.Profile! with { CommercialName = "Receipt scope test" });
    var preview = (await ClientMaintenanceWorkspace.PreviewAsync(db, actor, f.ClientId,
      ClientMaintenanceKinds.Profile, null, request)).Value!;
    var saved = await ClientMaintenanceWorkspace.ExecuteAsync(db, actor, f.ClientId, ClientMaintenanceKinds.Profile, null,
      request with { Reviewed = true, ExpectedRequestHash = preview.RequestHash });
    Assert.True(saved.Succeeded, saved.Message);

    Assert.False((await ClientMaintenanceWorkspace.LookupAsync(db, actor, foreign.ClientId, ClientMaintenanceKinds.Profile,
      null, request.RequestId, preview.RequestHash)).Succeeded);
    Assert.False((await ClientMaintenanceWorkspace.LookupAsync(db, PbcSeed.Actor(foreign.Admin, "Administrator"), f.ClientId,
      ClientMaintenanceKinds.Profile, null, request.RequestId, preview.RequestHash)).Succeeded);
    Assert.False((await ClientMaintenanceWorkspace.LookupAsync(db, actor with { SessionEpoch = actor.SessionEpoch + 1 },
      f.ClientId, ClientMaintenanceKinds.Profile, null, request.RequestId, preview.RequestHash)).Succeeded);
    Assert.False((await ClientMaintenanceWorkspace.StateAsync(db, actor, foreign.ClientId, ClientMaintenanceKinds.Profile)).Succeeded);
    Assert.Equal(saved.Value, (await ClientMaintenanceWorkspace.LookupAsync(db, actor, f.ClientId,
      ClientMaintenanceKinds.Profile, null, request.RequestId, preview.RequestHash)).Value!.Receipt);
  }

  private sealed class RevokeAfterWrite(Func<Task> revoke):SaveChangesInterceptor
  {
    public bool Fired {get;private set;}
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,int result,CancellationToken cancellationToken=default)
    {if(!Fired){Fired=true;await revoke();}return result;}
  }
}
