using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Synthetic actors and findings only; no live or professional approval.</summary>
internal static class FindingCommandReceiptSeed
{
  internal sealed record World(PbcSeed.Fixture Fixture, Guid FindingId)
  {
    internal ActorContext Staff => PbcSeed.Actor(Fixture.Staff,"Staff");
    internal ActorContext Manager => PbcSeed.Actor(Fixture.Reviewer,"Manager");
  }
  internal static async Task<World> SeedAsync(ITestPostgresDatabase pg)
  {
    var f=await PbcSeed.SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Reviewer,"Manager",f.ClientId,f.EngagementId));
    await db.SaveChangesAsync();
    var created=await AuditPlanningService.CreateFindingAsync(db,PbcSeed.Actor(f.Staff,"Staff"),new(f.EngagementId,"Control deficiency","SYNTHETIC finding",false,123.456789m,null));
    Assert.True(created.Succeeded,created.Message);return new(f,created.Value!.FindingId);
  }
  internal static FindingCommandFields Fields(string action) => action switch
  {
    FindingCommandActions.RecordResponse => new(action,Response:new("SYNTHETIC management response",false)),
    FindingCommandActions.Designate => new(action,Designation:new("SYNTHETIC recommendation")),
    _ => new(action)
  };
  internal static async Task<FindingCommandRequest> ReviewAsync(AuditSphereDbContext db,ActorContext actor,Guid id,FindingCommandFields fields)
  {
    var state=await FindingCommandWorkspace.StateAsync(db,actor,id);Assert.True(state.Succeeded,state.Message);
    var request=new FindingCommandRequest(Guid.NewGuid(),state.Value!.Finding.Revision,state.Value.ReviewBasis,fields);
    var preview=await FindingCommandWorkspace.PreviewAsync(db,actor,id,request);Assert.True(preview.Succeeded,preview.Message);
    Assert.True(preview.Value!.CanProceed,string.Join(';',preview.Value.Blockers));
    return request with{Fields=preview.Value.CanonicalInput,ExpectedRequestHash=preview.Value.RequestHash,Reviewed=true};
  }
}
