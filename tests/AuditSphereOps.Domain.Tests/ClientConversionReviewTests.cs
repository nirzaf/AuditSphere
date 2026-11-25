using System.Text.Json;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Acceptance;
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
  var opportunityId=await db.Proposals.AsNoTracking().Where(x=>x.Id==id).Select(x=>x.OpportunityId).SingleAsync();
  var canonicalClientId=await db.Opportunities.AsNoTracking().Where(x=>x.Id==opportunityId).Select(x=>x.AcceptanceClientId).SingleAsync();
  Assert.NotNull(canonicalClientId);
  var r=new ClientConversionRequest(Guid.NewGuid(),new("Reviewed prospect", "Synthetic trading name"));
  var p=await ClientConversionWorkspace.PreviewAsync(db,actor,id,r);Assert.True(p.Succeeded,p.Message);Assert.Equal(canonicalClientId,p.Value!.ExistingClientId);Assert.Contains("No invitation",p.Value.PortalEffect);
  r=r with {Reviewed=true,RequestHash=p.Value.RequestHash,ReviewBasis=p.Value.ReviewBasis};
  async Task<string> Save(){await using var c=new AuditSphereDbContext(pg.Options);var v=await ClientConversionWorkspace.ExecuteAsync(c,actor,id,r);Assert.True(v.Succeeded,v.Message);return JsonSerializer.Serialize(v.Value);}
  var values=await Task.WhenAll(Save(),Save());Assert.Equal(values[0],values[1]);var receipt=await db.ClientConversions.AsNoTracking().SingleAsync();
  Assert.Equal(canonicalClientId,receipt.ClientId);Assert.Equal(1,await db.PracticeClients.CountAsync(x=>x.Id==receipt.ClientId));Assert.Equal(CrmStates.ClientAccepted,(await db.PracticeClients.AsNoTracking().SingleAsync(x=>x.Id==receipt.ClientId)).Status);
  Assert.Equal(receipt.ClientId,(await db.Proposals.AsNoTracking().SingleAsync(x=>x.Id==id)).PracticeClientId);
  Assert.True((await ClientConversionWorkspace.LookupAsync(db,actor,id,r.RequestId,r.RequestHash)).Value!.Found);
  Assert.False((await ClientConversionWorkspace.LookupAsync(db,PbcSeed.Actor(f.Staff,"RelationshipManager"),id,r.RequestId,r.RequestHash)).Value!.Found);
  Assert.False((await ClientConversionWorkspace.ExecuteAsync(db,actor,id,r with {Fields=r.Fields with {LegalName="Changed intent"}})).Succeeded);
  Assert.Equal("P0001",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE client_conversions SET preview_json='{{}}' WHERE id={receipt.Id}"))).SqlState);
  Assert.Equal("P0001",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM client_conversions WHERE id={receipt.Id}"))).SqlState);
  Assert.Single(await db.AcceptanceDecisions.Where(x=>x.PracticeClientId==receipt.ClientId &&
    x.OpportunityId==opportunityId && x.Decision=="Accepted").ToListAsync());
  Assert.Empty(await db.AcceptanceDecisions.Where(x=>x.PracticeClientId==receipt.ClientId && x.Decision=="Pending").ToListAsync());
  Assert.Single(await db.ClientPortalIntents.Where(x=>x.PracticeClientId==receipt.ClientId).ToListAsync());
 }
 [Fact]
 public async Task UnreviewedWrongFirmAndStaleContactNeverPublishConversion()
 {
  await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);var other=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
  var id=await ClientConversionReviewSeed.PopulateAsync(db,f);var actor=PbcSeed.Actor(f.Admin,"Administrator");var r=new ClientConversionRequest(Guid.NewGuid(),new("Reviewed prospect"));
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
  var opportunityId=await db.Proposals.AsNoTracking().Where(x=>x.Id==id).Select(x=>x.OpportunityId).SingleAsync();
  var canonicalClientId=await db.Opportunities.AsNoTracking().Where(x=>x.Id==opportunityId).Select(x=>x.AcceptanceClientId).SingleAsync();
  Assert.NotNull(canonicalClientId);
  var existing=await db.PracticeClients.AsNoTracking().SingleAsync(x=>x.Id==canonicalClientId);
  var r=new ClientConversionRequest(Guid.NewGuid(),new(existing.LegalName,"Proposed name that must not overwrite canonical metadata",existing.RegistrationNumber,existing.Jurisdiction));
  var preview=await ClientConversionWorkspace.PreviewAsync(db,actor,id,r);Assert.True(preview.Succeeded,preview.Message);Assert.Equal(canonicalClientId,preview.Value!.ExistingClientId);
  r=r with {Reviewed=true,RequestHash=preview.Value.RequestHash,ReviewBasis=preview.Value.ReviewBasis};
  await db.ClientSafetyStates.Where(x=>x.Id==canonicalClientId).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.InputGeneration,s=>s.InputGeneration+1));
  Assert.Equal("revision.stale",(await ClientConversionWorkspace.ExecuteAsync(db,actor,id,r)).ErrorCode);
  preview=await ClientConversionWorkspace.PreviewAsync(db,actor,id,r);Assert.True(preview.Succeeded,preview.Message);r=r with {ReviewBasis=preview.Value!.ReviewBasis};
  Assert.Equal("gate.blocked",(await ClientConversionWorkspace.ExecuteAsync(db,actor,id,r)).ErrorCode);
  var pending = await AssessmentWorkspaceQuery.GetAsync(db, PbcSeed.Actor(f.Admin,"Partner"), canonicalClientId!.Value,
    opportunityId: opportunityId);
  Assert.True(pending.Succeeded, pending.Message);
  Assert.Equal(AcceptancePaths.Continuance, pending.Value!.Checklist.Path);
  Assert.Equal("Accepted", pending.Value.Checklist.PriorDecision);
  Assert.True(pending.Value.Checklist.CanStartContinuance);
  Assert.False(pending.Value.Checklist.CanEdit);
  Assert.False(pending.Value.Checklist.CanDecide);
  var reevaluated=await CommercialAcceptanceTestSupport.AcceptOpportunityAsync(db,PbcSeed.Actor(f.Staff,"Manager"),PbcSeed.Actor(f.Admin,"Partner"),f.FirmId,opportunityId);
  Assert.True(reevaluated.Succeeded,reevaluated.Message);
  preview=await ClientConversionWorkspace.PreviewAsync(db,actor,id,r);Assert.True(preview.Succeeded,preview.Message);
  r=r with {ReviewBasis=preview.Value!.ReviewBasis};
  var converted=await ClientConversionWorkspace.ExecuteAsync(db,actor,id,r);Assert.True(converted.Succeeded,converted.Message);Assert.Equal(canonicalClientId,converted.Value!.ClientId);
  var current=await db.PracticeClients.AsNoTracking().SingleAsync(x=>x.Id==canonicalClientId);Assert.Equal(existing.CommercialName,current.CommercialName);Assert.Equal(existing.Status,current.Status);
 }
 [Fact]
 public async Task ReviewedContinuanceStartsFromRetainedAcceptanceAfterClientGenerationAdvances()
 {
  await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
  var proposalId=await ClientConversionReviewSeed.PopulateAsync(db,f);
  var opportunityId=await db.Proposals.AsNoTracking().Where(x=>x.Id==proposalId).Select(x=>x.OpportunityId).SingleAsync();
  var clientId=await db.Opportunities.AsNoTracking().Where(x=>x.Id==opportunityId).Select(x=>x.AcceptanceClientId).SingleAsync();
  Assert.NotNull(clientId);
  await db.ClientSafetyStates.Where(x=>x.FirmId==f.FirmId && x.Id==clientId)
    .ExecuteUpdateAsync(x=>x.SetProperty(s=>s.InputGeneration,s=>s.InputGeneration+1));
  var actor=PbcSeed.Actor(f.Admin,"Partner");
  var state=await AcceptanceWorkspaceQuery.GetAsync(db,actor,clientId!.Value,serviceRoute:"FinancialStatementAudit",opportunityId:opportunityId);
  Assert.True(state.Succeeded,state.Message);Assert.Equal(AcceptancePaths.Continuance,state.Value!.Path);
  Assert.Equal("2",state.Value.Generation);Assert.Equal("Accepted",state.Value.PriorDecision);
  Assert.True(state.Value.CanStartContinuance);Assert.False(state.Value.CanEdit);Assert.False(state.Value.CanDecide);
  var request=new AssessmentCommandRequest(Guid.NewGuid(),new AssessmentCommandFields("CONTINUANCE",state.Value.Generation,
    ServiceRoute:"FinancialStatementAudit",OpportunityId:opportunityId));
  var preview=await AssessmentCommandWorkspace.PreviewAsync(db,actor,clientId.Value,request);
  Assert.True(preview.Succeeded,preview.Message);
  request=request with {ReviewBasis=preview.Value!.ReviewBasis,RequestHash=preview.Value.RequestHash,Reviewed=true};
  var opened=await AssessmentCommandWorkspace.ExecuteAsync(db,actor,clientId.Value,request);
  Assert.True(opened.Succeeded,opened.Message);Assert.Equal("3",opened.Value!.ResultGeneration);
  Assert.False(await OpportunityAcceptanceGate.IsCurrentUnconditionalAsync(db,f.FirmId,opportunityId));
  var current=await AcceptanceWorkspaceQuery.GetAsync(db,actor,clientId.Value,serviceRoute:"FinancialStatementAudit",opportunityId:opportunityId);
  Assert.True(current.Succeeded,current.Message);Assert.Equal("3",current.Value!.Generation);
  Assert.Equal(AcceptancePaths.Continuance,current.Value.Path);Assert.True(current.Value.CanEdit);
  Assert.False(current.Value.CanStartContinuance);Assert.Single(await db.AcceptanceDecisions.AsNoTracking().Where(x=>
    x.FirmId==f.FirmId && x.PracticeClientId==clientId && x.OpportunityId==opportunityId &&
    x.Generation==3 && x.Decision=="Pending" && x.PriorDecisionId!=null).ToListAsync());
 }
 [Fact]
 public async Task LegacyConversionJoinsCallerTransactionAndRollbackPublishesNothing()
 {
  await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
  var id=await ClientConversionReviewSeed.PopulateAsync(db,f);var actor=PbcSeed.Actor(f.Admin,"Administrator");
  var opportunityId=await db.Proposals.AsNoTracking().Where(x=>x.Id==id).Select(x=>x.OpportunityId).SingleAsync();
  var client=await db.Opportunities.AsNoTracking().Where(x=>x.Id==opportunityId).Select(x=>x.AcceptanceClientId).SingleAsync();
  Assert.NotNull(client);
  await using(var tx=await db.Database.BeginTransactionAsync()) {
   var name=await db.PracticeClients.AsNoTracking().Where(x=>x.Id==client).Select(x=>x.LegalName).SingleAsync();
   var converted=await PracticeCrmService.ConvertToClientDraftAsync(db,actor,new(id,name));Assert.True(converted.Succeeded,converted.Message);Assert.Equal(client,converted.Value);
   Assert.Equal(client,(await db.Proposals.AsNoTracking().SingleAsync(x=>x.Id==id)).PracticeClientId);await tx.RollbackAsync();
  }
  await using var fresh=new AuditSphereDbContext(pg.Options);Assert.True(await fresh.PracticeClients.AnyAsync(x=>x.Id==client));Assert.Null((await fresh.Proposals.AsNoTracking().SingleAsync(x=>x.Id==id)).PracticeClientId);
  Assert.False(await fresh.ClientPortalIntents.AnyAsync(x=>x.PracticeClientId==client));Assert.Empty(await fresh.ClientConversions.ToListAsync());
 }

}
