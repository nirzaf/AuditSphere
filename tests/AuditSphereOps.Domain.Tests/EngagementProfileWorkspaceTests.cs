using System.Data.Common;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed class EngagementProfileWorkspaceTests
{
  [Fact]
  public async Task CompleteHoldCountsStablePagesMetadataAndNavigationRespectExactScope()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); await EngagementProfileWorkspaceSeed.PopulateAsync(db, f, 105);
    var sibling = new Engagement { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, CreatedAt = DateTimeOffset.UtcNow };
    db.Engagements.Add(sibling); db.EngagementHolds.Add(new EngagementHold { Id = Guid.NewGuid(), FirmId = f.FirmId,
      EngagementId = sibling.Id, HoldKind = "PRIVATE SIBLING", Reason = "EXCLUDED HOLD", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync(); var actor = PbcSeed.Actor(f.Staff, "Staff");
    var v = (await WorkspaceQuery.EngagementAsync(db, actor, f.EngagementId, paging: new())).Value!;
    Assert.Equal(new EngagementHoldMetrics(105,52,53), v.HoldMetrics); Assert.Equal(10, v.Holds.Count);
    Assert.Equal("9007199254740993", v.Generation); Assert.Equal("Synthetic annual audit profile", v.ServiceProfileId);
    Assert.Equal(new DateTimeOffset(2026,1,2,12,30,0,TimeSpan.Zero), v.CreatedAt); Assert.False(v.CanViewClientProfile);
    Assert.False(v.CanPrepareAccounting);
    Assert.Equal(100, (await WorkspaceQuery.EngagementAsync(db, actor, f.EngagementId)).Value!.Holds.Count);
    var last = (await WorkspaceQuery.EngagementAsync(db, actor, f.EngagementId, paging: new(2,50))).Value!;
    Assert.Equal(5, last.Holds.Count); Assert.Equal(v.HoldMetrics, last.HoldMetrics);
    Assert.Empty(last.Holds.Select(h => h.Id).Intersect(v.Holds.Select(h => h.Id)));
    Assert.DoesNotContain("EXCLUDED HOLD", System.Text.Json.JsonSerializer.Serialize(v));
    foreach (var page in new[] { new EngagementWorkspacePaging(-1), new(10001), new(0,100), new(0,0) })
      Assert.Equal("request.invalid", (await WorkspaceQuery.EngagementAsync(db, actor, f.EngagementId, paging: page)).ErrorCode);
    Assert.False((await WorkspaceQuery.EngagementAsync(db, actor, sibling.Id)).Succeeded);
    Assert.False((await WorkspaceQuery.EngagementAsync(db, actor, foreign.EngagementId)).Succeeded);
    Assert.False((await WorkspaceQuery.EngagementAsync(db, actor, Guid.NewGuid())).Succeeded);
    Assert.False((await WorkspaceQuery.EngagementAsync(db, PbcSeed.Actor(f.Client,"ClientUser"), f.EngagementId)).Succeeded);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Staff", f.ClientId)); await db.SaveChangesAsync();
    Assert.True((await WorkspaceQuery.EngagementAsync(db, actor, f.EngagementId)).Value!.CanViewClientProfile);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer", clientId: f.ClientId)); await db.SaveChangesAsync();
    Assert.True((await WorkspaceQuery.EngagementAsync(db, actor, f.EngagementId)).Value!.CanPrepareAccounting);
    await db.Users.Where(u => u.Id == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    Assert.False((await WorkspaceQuery.EngagementAsync(db, actor, f.EngagementId)).Succeeded);
  }

  [Fact]
  public async Task RevocationDuringHistoryReadRefusesAllMetadataAndCounts()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options)) { await EngagementProfileWorkspaceSeed.PopulateAsync(db,f); }
    var interceptor = new RevokeDuringHolds(async () => {
      await using var mutation = new AuditSphereDbContext(pg.Options);
      await mutation.RoleGrants.Where(g => g.UserId == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using var read = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options);
    var result = await WorkspaceQuery.EngagementAsync(read, PbcSeed.Actor(f.Staff,"Staff"), f.EngagementId, paging:new());
    Assert.True(interceptor.Fired); Assert.False(result.Succeeded); Assert.Null(result.Value);
  }
  private sealed class RevokeDuringHolds(Func<Task> revoke) : DbCommandInterceptor
  {
    public bool Fired { get; private set; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (!Fired && command.CommandText.Contains("FROM engagement_holds", StringComparison.Ordinal)) { Fired = true; await revoke(); }
      return result;
    }
  }
}
