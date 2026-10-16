using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Domain.Tests;
/// <summary>Synthetic workpaper evidence only; no live or professional approval.</summary>
internal static class WorkpaperCommandReceiptSeed
{
  internal sealed record World(PbcSeed.Fixture Fixture,Guid WorkpaperId,Guid ProcedureId)
  {
    internal ActorContext Staff=>PbcSeed.Actor(Fixture.Staff,"Staff");
    internal ActorContext Admin=>PbcSeed.Actor(Fixture.Admin,"Administrator");
  }
  internal static async Task<World> SeedAsync(ITestPostgresDatabase pg)
  {
    var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);var procedure=Guid.NewGuid();
    db.AuditProcedures.Add(new(){Id=procedure,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,SourceProcedureId="SYN-WP",Title="SYNTHETIC procedure",Status="PLANNED",CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();
    var created=await AuditPlanningService.CreateWorkpaperAsync(db,PbcSeed.Actor(f.Staff,"Staff"),new(f.EngagementId,"SYN-WP","SYNTHETIC workpaper","Synthetic objective","synthetic-v1",procedure,"Synthetic procedure"));
    Assert.True(created.Succeeded,created.Message);return new(f,created.Value!.WorkpaperId,procedure);
  }
  internal static async Task<WorkpaperCommandRequest> SaveAsync(AuditSphereDbContext db,World w,string work="SYNTHETIC work performed",string conclusion="SYNTHETIC conclusion")
  {
    var state=await WorkpaperCommandWorkspace.StateAsync(db,w.Staff,w.WorkpaperId);Assert.True(state.Succeeded,state.Message);var s=state.Value!.Workpaper;
    var revision=s.Draft?.Lifecycle=="ACTIVE"?s.Draft.DraftRevision:"0";
    var r=new WorkpaperCommandRequest(Guid.NewGuid(),new(WorkpaperCommandActions.SaveDraft,Save:new(revision,s.Revision,s.InputGeneration,s.PolicyGeneration,work,conclusion)));
    var prepared=await WorkpaperCommandWorkspace.PrepareSaveAsync(db,w.Staff,w.WorkpaperId,r);Assert.True(prepared.Succeeded,prepared.Message);
    return r with{Fields=prepared.Value!.CanonicalInput,ExpectedRequestHash=prepared.Value.RequestHash};
  }
  internal static async Task<WorkpaperCommandRequest> ReviewAsync(AuditSphereDbContext db,World w,string action)
  {
    var state=await WorkpaperCommandWorkspace.StateAsync(db,w.Staff,w.WorkpaperId);Assert.True(state.Succeeded,state.Message);var s=state.Value!;
    Assert.Equal(Hashing.Sha256Hex(s.StateJson),s.ReviewBasis);var d=s.Workpaper.Draft!;
    var fields=action==WorkpaperCommandActions.Submit?new WorkpaperCommandFields(action,Submit:new(s.Workpaper.Revision,d.DraftRevision,d.LastSaveId,d.WorkPerformed,d.Conclusion)):new(action,Discard:new(d.DraftRevision));
    var r=new WorkpaperCommandRequest(Guid.NewGuid(),fields,s.ReviewBasis);var preview=await WorkpaperCommandWorkspace.PreviewAsync(db,w.Staff,w.WorkpaperId,r);Assert.True(preview.Succeeded,preview.Message);Assert.True(preview.Value!.CanProceed,string.Join(';',preview.Value.Blockers));
    return r with{Fields=preview.Value.CanonicalInput,ExpectedRequestHash=preview.Value.RequestHash,Reviewed=true};
  }
}
