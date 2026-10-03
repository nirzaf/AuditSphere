using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace AuditSphereOps.Domain.Tests;
[Trait("Profile", "Database")]
public sealed class ClientConversionReviewTests
{
 [Fact]
 public async Task ConcurrentExactConversionRetainsOneClientAndOriginalActorOwnedReceipt()
 {
  await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
  var id=await ClientConversionReviewSeed.PopulateAsync(db,f);var actor=PbcSeed.Actor(f.Admin,"Administrator");
  var r=new ClientConversionRequest(Guid.NewGuid(),new("Reviewed synthetic canonical client", "Synthetic trading name", "REG-001", "Synthetic jurisdiction"));
  var p=await ClientConversionWorkspace.PreviewAsync(db,actor,id,r);Assert.True(p.Succeeded,p.Message);Assert.Null(p.Value!.ExistingClientId);Assert.Contains("No invitation",p.Value.PortalEffect);
  r=r with {Reviewed=true,RequestHash=p.Value.RequestHash,ReviewBasis=p.Value.ReviewBasis};
  async Task<string> Save(){await using var c=new AuditSphereDbContext(pg.Options);var v=await ClientConversionWorkspace.ExecuteAsync(c,actor,id,r);Assert.True(v.Succeeded,v.Message);return JsonSerializer.Serialize(v.Value);}
  var values=await Task.WhenAll(Save(),Save());Assert.Equal(values[0],values[1]);var receipt=await db.ClientConversions.AsNoTracking().SingleAsync();
  Assert.Equal(1,await db.PracticeClients.CountAsync(x=>x.Id==receipt.ClientId));Assert.Equal("PROSPECT",(await db.PracticeClients.AsNoTracking().SingleAsync(x=>x.Id==receipt.ClientId)).Status);
  Assert.Equal(receipt.ClientId,(await db.Proposals.AsNoTracking().SingleAsync(x=>x.Id==id)).PracticeClientId);
  Assert.True((await ClientConversionWorkspace.LookupAsync(db,actor,id,r.RequestId,r.RequestHash)).Value!.Found);
  Assert.False((await ClientConversionWorkspace.LookupAsync(db,PbcSeed.Actor(f.Staff,"RelationshipManager"),id,r.RequestId,r.RequestHash)).Value!.Found);
  Assert.False((await ClientConversionWorkspace.ExecuteAsync(db,actor,id,r with {Fields=r.Fields with {LegalName="Changed intent"}})).Succeeded);
  Assert.Equal("P0001",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE client_conversions SET preview_json='{{}}' WHERE id={receipt.Id}"))).SqlState);
  Assert.Equal("P0001",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM client_conversions WHERE id={receipt.Id}"))).SqlState);
  Assert.Single(await db.AcceptanceDecisions.Where(x=>x.PracticeClientId==receipt.ClientId && x.Decision=="Pending").ToListAsync());
  Assert.Single(await db.ClientPortalIntents.Where(x=>x.PracticeClientId==receipt.ClientId).ToListAsync());
 }
 [Fact]
 public async Task UnreviewedWrongFirmAndStaleContactNeverPublishConversion()
 {
  await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);var other=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
  var id=await ClientConversionReviewSeed.PopulateAsync(db,f);var actor=PbcSeed.Actor(f.Admin,"Administrator");var r=new ClientConversionRequest(Guid.NewGuid(),new("Synthetic client conversion"));
  Assert.False((await ClientConversionWorkspace.ExecuteAsync(db,actor,id,r)).Succeeded);
  Assert.Equal((await ClientConversionWorkspace.StateAsync(db,PbcSeed.Actor(other.Admin,"Administrator"),id)).ErrorCode,(await ClientConversionWorkspace.StateAsync(db,PbcSeed.Actor(other.Admin,"Administrator"),Guid.NewGuid())).ErrorCode);
  var p=await ClientConversionWorkspace.PreviewAsync(db,actor,id,r);Assert.True(p.Succeeded,p.Message);r=r with {Reviewed=true,RequestHash=p.Value!.RequestHash,ReviewBasis=p.Value.ReviewBasis};
  var opportunity=(await db.Proposals.AsNoTracking().SingleAsync(x=>x.Id==id)).OpportunityId;var lead=(await db.Opportunities.AsNoTracking().SingleAsync(x=>x.Id==opportunity)).LeadId;
  await db.Leads.Where(x=>x.Id==lead).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.PrimaryContactName,"Changed contact"));
  Assert.Equal("revision.stale",(await ClientConversionWorkspace.ExecuteAsync(db,actor,id,r)).ErrorCode);Assert.Empty(await db.ClientConversions.ToListAsync());
  Assert.Null((await db.Proposals.AsNoTracking().SingleAsync(x=>x.Id==id)).PracticeClientId);
  await db.Users.Where(x=>x.Id==f.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Disabled,true));Assert.False((await ClientConversionWorkspace.PreviewAsync(db,actor,id,r)).Succeeded);
 }
 [Fact]
 public async Task CanonicalReusePreservesMetadataAndRequiresFreshClientGeneration()
 {
  await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
  var id=await ClientConversionReviewSeed.PopulateAsync(db,f);var actor=PbcSeed.Actor(f.Admin,"Administrator");
  var existing=await db.PracticeClients.AsNoTracking().SingleAsync(x=>x.Id==f.ClientId);
  var r=new ClientConversionRequest(Guid.NewGuid(),new(existing.LegalName,"Proposed name that must not overwrite canonical metadata",existing.RegistrationNumber,existing.Jurisdiction));
  var preview=await ClientConversionWorkspace.PreviewAsync(db,actor,id,r);Assert.True(preview.Succeeded,preview.Message);Assert.Equal(f.ClientId,preview.Value!.ExistingClientId);
  r=r with {Reviewed=true,RequestHash=preview.Value.RequestHash,ReviewBasis=preview.Value.ReviewBasis};
  await db.ClientSafetyStates.Where(x=>x.Id==f.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.InputGeneration,s=>s.InputGeneration+1));
  Assert.Equal("revision.stale",(await ClientConversionWorkspace.ExecuteAsync(db,actor,id,r)).ErrorCode);
  preview=await ClientConversionWorkspace.PreviewAsync(db,actor,id,r);Assert.True(preview.Succeeded,preview.Message);r=r with {ReviewBasis=preview.Value!.ReviewBasis};
  var converted=await ClientConversionWorkspace.ExecuteAsync(db,actor,id,r);Assert.True(converted.Succeeded,converted.Message);Assert.Equal(f.ClientId,converted.Value!.ClientId);
  var current=await db.PracticeClients.AsNoTracking().SingleAsync(x=>x.Id==f.ClientId);Assert.Equal(existing.CommercialName,current.CommercialName);Assert.Equal(existing.Status,current.Status);
 }
 [Fact]
 public async Task LegacyConversionJoinsCallerTransactionAndRollbackPublishesNothing()
 {
  await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
  var id=await ClientConversionReviewSeed.PopulateAsync(db,f);var actor=PbcSeed.Actor(f.Admin,"Administrator");Guid client;
  await using(var tx=await db.Database.BeginTransactionAsync()) {
   var converted=await PracticeCrmService.ConvertToClientDraftAsync(db,actor,new(id,"Caller rollback synthetic client"));Assert.True(converted.Succeeded,converted.Message);client=converted.Value;
   Assert.Equal(client,(await db.Proposals.AsNoTracking().SingleAsync(x=>x.Id==id)).PracticeClientId);await tx.RollbackAsync();
  }
  await using var fresh=new AuditSphereDbContext(pg.Options);Assert.False(await fresh.PracticeClients.AnyAsync(x=>x.Id==client));Assert.Null((await fresh.Proposals.AsNoTracking().SingleAsync(x=>x.Id==id)).PracticeClientId);
  Assert.False(await fresh.ClientPortalIntents.AnyAsync(x=>x.PracticeClientId==client));Assert.Empty(await fresh.ClientConversions.ToListAsync());
 }

}
