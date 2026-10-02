using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class PortfolioQueryTests
{
  [Fact]
  public async Task EngagementScopeExcludesSiblingCountsAndOtherClients_AndRevocationDenies()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Engagements.Add(new Engagement { Id = Guid.NewGuid(), FirmId = fixture.FirmId,
      PracticeClientId = fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(fixture.Staff, "Staff");
    var result = await PortfolioQuery.ListAsync(db, actor);
    Assert.True(result.Succeeded, result.Message);
    var row = Assert.Single(result.Value!.Items);
    Assert.Equal(fixture.ClientId, row.Id);
    Assert.Equal(1, row.Engagements);
    Assert.NotEqual(foreign.ClientId, row.Id);
    Assert.Equal(1, result.Value.Total);
    Assert.Single((await PortfolioQuery.ListAsync(db, actor, "pbc test")).Value!.Items);
    Assert.Single((await PortfolioQuery.ListAsync(db, actor, fixture.ClientId.ToString())).Value!.Items);
    Assert.Empty((await PortfolioQuery.ListAsync(db, actor, foreign.ClientId.ToString())).Value!.Items);
    Assert.False((await PortfolioQuery.ListAsync(db, actor, pageSize: 101)).Succeeded);
    Assert.False((await PortfolioQuery.ListAsync(db, PbcSeed.Actor(fixture.Client, "ClientUser"))).Succeeded);
    var grant = await db.RoleGrants.SingleAsync(g => g.UserId == fixture.Staff.Id);
    grant.RevokedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    Assert.False((await PortfolioQuery.ListAsync(db, actor)).Succeeded);
  }
}
