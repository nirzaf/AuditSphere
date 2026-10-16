using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Shared;
using System.Text.Json;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Synthetic administrative review points; no professional or live approval.</summary>
internal static class ReviewPointCommandReceiptSeed
{
  internal sealed record World(PbcSeed.Fixture Fixture,Guid ReviewPointId)
  {
    internal ActorContext Staff=>PbcSeed.Actor(Fixture.Staff,"Staff");
    internal ActorContext Admin=>PbcSeed.Actor(Fixture.Admin,"Administrator");
  }
  internal static async Task<World> SeedAsync(ITestPostgresDatabase pg)
  {
    var f=await PbcSeed.SeedAsync(pg);var id=Guid.NewGuid();await using var db=new AuditSphereDbContext(pg.Options);
    db.ReviewPoints.Add(new(){Id=id,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,TargetKind="workpaper",TargetId=Guid.NewGuid(),TargetRevision=9007199254740993,
      RaisedByUserId=f.Staff.Id,RaisedAt=DateTimeOffset.UtcNow,Comment="SYNTHETIC review point\r\nمراجعة\tإدارية",Significant=true});
    await db.SaveChangesAsync();return new(f,id);
  }
  internal static async Task<ReviewPointCommandRequest> ReviewAsync(AuditSphereDbContext db,ActorContext actor,Guid id,string action)
  {
    var state=await ReviewPointCommandWorkspace.StateAsync(db,actor,id);Assert.True(state.Succeeded,state.Message);
    using var basis=JsonDocument.Parse(state.Value!.StateJson);
    Assert.Equal(state.Value.ReviewPoint.ClientId,basis.RootElement.GetProperty("ClientId").GetGuid());
    Assert.Equal(Hashing.Sha256Hex(state.Value.StateJson),state.Value.ReviewBasis);
    var r=new ReviewPointCommandRequest(Guid.NewGuid(),state.Value!.ReviewPoint.DispositionRevision,state.Value.ReviewBasis,new(action));
    var preview=await ReviewPointCommandWorkspace.PreviewAsync(db,actor,id,r);Assert.True(preview.Succeeded,preview.Message);Assert.True(preview.Value!.CanProceed,string.Join(';',preview.Value.Blockers));
    return r with{ExpectedRequestHash=preview.Value.RequestHash,Reviewed=true};
  }
  internal static async Task FreezeAsync(AuditSphereDbContext db,World w)
  {
    var f=w.Fixture;var report=Guid.NewGuid();db.AuditDeliverables.Add(new(){Id=report,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,Kind="MANAGEMENT_LETTER",Version=1,
      TemplateVersion="SYNTHETIC",InputDigest=new('a',64),ContentSha256=new('b',64),Content=[1],CreatedByUserId=f.Admin.Id,CreatedAt=DateTimeOffset.UtcNow});
    var signed=DateTimeOffset.UtcNow.AddDays(-61);db.EngagementFileFreezes.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,ReportDeliverableId=report,
      ReportSignedAt=signed,DueAt=signed.AddDays(60),State="FROZEN",FrozenAt=signed.AddDays(60),UpdatedAt=signed.AddDays(60)});await db.SaveChangesAsync();
  }
}
