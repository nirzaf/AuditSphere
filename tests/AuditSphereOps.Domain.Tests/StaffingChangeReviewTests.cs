using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class StaffingChangeReviewTests
{
  [Fact]
  public async Task ConcurrentReviewedAssignmentAndRevocationRetainActorOwnedImmutableReceipts()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var r = new StaffingChangeRequest(Guid.NewGuid(), new("ASSIGN", f.Reviewer.Id, StaffingLevels.StaffAssociate));
    var p = (await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, r)).Value!;
    Assert.Contains("Full Control", p.SharePointEffect);
    r = r with { Reviewed = true, RequestHash = p.RequestHash, ReviewBasis = p.ReviewBasis };
    async Task<string> Save()
    {
      await using var c = new AuditSphereDbContext(pg.Options);
      var result = await StaffingChangeWorkspace.ExecuteAsync(c, actor, f.EngagementId, r);
      Assert.True(result.Succeeded, result.Message); return JsonSerializer.Serialize(result.Value);
    }
    var results = await Task.WhenAll(Save(), Save()); Assert.Equal(results[0], results[1]);
    var receipt = await db.StaffingChanges.AsNoTracking().SingleAsync();
    Assert.Single(await db.EngagementStaffAssignments.ToListAsync());
    Assert.True((await StaffingChangeWorkspace.LookupAsync(db, actor, f.EngagementId, r.RequestId, r.RequestHash)).Value!.Found);
    Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE staffing_changes SET preview_json='{{}}' WHERE id={receipt.Id}"))).SqlState);
    Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM staffing_changes WHERE id={receipt.Id}"))).SqlState);
    var revoke = new StaffingChangeRequest(Guid.NewGuid(), new("REVOKE", f.Reviewer.Id, StaffingLevels.StaffAssociate, receipt.AssignmentId));
    var rp = (await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, revoke)).Value!;
    Assert.True(rp.RemovesLocalGrant);
    revoke = revoke with { Reviewed = true, RequestHash = rp.RequestHash, ReviewBasis = rp.ReviewBasis };
    var revoked = await StaffingChangeWorkspace.ExecuteAsync(db, actor, f.EngagementId, revoke);
    Assert.True(revoked.Succeeded, revoked.Message);
    Assert.True((await StaffingChangeWorkspace.ExecuteAsync(db, actor, f.EngagementId, revoke)).Succeeded);
    Assert.Equal(f.Reviewer.SessionEpoch + 1, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == f.Reviewer.Id)).SessionEpoch);
    Assert.True((await StaffingChangeWorkspace.LookupAsync(db, actor, f.EngagementId, r.RequestId, r.RequestHash)).Value!.Found);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId)); await db.SaveChangesAsync();
    Assert.False((await StaffingChangeWorkspace.LookupAsync(db, PbcSeed.Actor(f.Staff, "Manager"), f.EngagementId, r.RequestId, r.RequestHash)).Value!.Found);
    await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Disabled, true));
    Assert.False((await StaffingChangeWorkspace.LookupAsync(db, actor, f.EngagementId, r.RequestId, r.RequestHash)).Succeeded);
  }

  [Fact]
  public async Task InvalidUnreviewedSelfWrongScopeAndStaleTargetChangesNeverPublish()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg); var other = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var r = new StaffingChangeRequest(Guid.NewGuid(), new("ASSIGN", f.Reviewer.Id, StaffingLevels.StaffAssociate));
    Assert.False((await StaffingChangeWorkspace.ExecuteAsync(db, actor, f.EngagementId, r)).Succeeded);
    Assert.False((await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, r with { Fields = r.Fields with { UserId = actor.UserId } })).Succeeded);
    Assert.Equal((await StaffingChangeWorkspace.PreviewAsync(db, actor, other.EngagementId, r)).ErrorCode,
      (await StaffingChangeWorkspace.PreviewAsync(db, actor, Guid.NewGuid(), r)).ErrorCode);
    var p = (await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, r)).Value!;
    r = r with { Reviewed = true, RequestHash = p.RequestHash, ReviewBasis = p.ReviewBasis };
    await db.Users.Where(x => x.Id == f.Reviewer.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    Assert.Equal("revision.stale", (await StaffingChangeWorkspace.ExecuteAsync(db, actor, f.EngagementId, r)).ErrorCode);
    Assert.Empty(await db.StaffingChanges.ToListAsync()); Assert.Empty(await db.EngagementStaffAssignments.ToListAsync());
    Assert.False((await StaffingChangeWorkspace.ExecuteAsync(db, actor, f.EngagementId, r with { RequestHash = new string('f',64) })).Succeeded);
    await db.Users.Where(x => x.Id == f.Reviewer.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Disabled, true));
    Assert.False((await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, r)).Succeeded);
  }
}
