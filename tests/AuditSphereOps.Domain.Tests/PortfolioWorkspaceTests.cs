using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace AuditSphereOps.Domain.Tests;

public sealed class PortfolioWorkspaceTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task RevocationDuringProjectionRefusesMixedScopesEvenWhenAnotherGrantRemains(bool export)
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    var siblingId = Guid.NewGuid();
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Engagements.Add(new Engagement { Id = siblingId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
      setup.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Staff", f.ClientId, siblingId)); await setup.SaveChangesAsync();
      await PortfolioWorkspaceSeed.PopulateAsync(setup, f, 3);
    }
    var interceptor = new RevokeDuringRead(async () => {
      await using var mutation = new AuditSphereDbContext(pg.Options);
      await mutation.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.EngagementId == f.EngagementId)
        .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    if (export) Assert.False((await PortfolioQuery.ExportAsync(db, actor)).Succeeded);
    else Assert.False((await PortfolioQuery.WorkspaceAsync(db, actor)).Succeeded);
    Assert.True(interceptor.Fired);
    Assert.True((await PortfolioQuery.ListAsync(db, actor)).Succeeded);
  }

  private sealed class RevokeDuringRead(Func<Task> revoke) : DbCommandInterceptor
  {
    public bool Fired { get; private set; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (!Fired && command.CommandText.Contains("FROM release_candidates", StringComparison.Ordinal))
      { Fired = true; await revoke(); }
      return result;
    }
  }

  [Fact]
  public async Task MetricsRecentWindowsExportAndScopeExpansionUseCurrentExplicitGrants()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var root = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    var a = await SiblingClientSeed.SeedAsync(pg, root.FirmId, "PORTFOLIO-A");
    var b = await SiblingClientSeed.SeedAsync(pg, root.FirmId, "HIDDEN-PORTFOLIO-B");
    try
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      await PortfolioWorkspaceSeed.PopulateAsync(db, a.Fixture, 26);
      await PortfolioWorkspaceSeed.PopulateAsync(db, b.Fixture, 3);
      await PortfolioWorkspaceSeed.PopulateAsync(db, foreign, 3);
      var sibling = Guid.NewGuid();
      db.Engagements.Add(new Engagement { Id = sibling, FirmId = root.FirmId,
        PracticeClientId = a.Fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      var siblingFixture = a.Fixture with { EngagementId = sibling };
      await PortfolioWorkspaceSeed.PopulateAsync(db, siblingFixture, 3);
      var actor = PbcSeed.Actor(a.Fixture.Staff, "Staff");
      var result = await PortfolioQuery.WorkspaceAsync(db, actor);
      Assert.True(result.Succeeded, result.Message); var v = result.Value!;
      Assert.Equal(new PortfolioMetrics(1, 1, 1, 1, 25, 1), v.Metrics);
      Assert.Equal(26, v.CandidateTotal); Assert.Equal(25, v.Candidates.Count); Assert.Equal(2, v.PackageTotal);
      Assert.All(v.Candidates, r => { Assert.Equal(a.Fixture.EngagementId, r.EngagementId); Assert.Equal("9007199254740993", r.TargetRevision); });
      Assert.All(v.Packages, r => Assert.Equal(a.Fixture.EngagementId, r.EngagementId));
      var empty = (await PortfolioQuery.WorkspaceAsync(db, actor, "NO MATCH")).Value!;
      Assert.Equal(v.Metrics, empty.Metrics); Assert.Empty(empty.Clients.Items); Assert.Empty(empty.Candidates); Assert.Empty(empty.Packages);
      var csv = await PortfolioQuery.ExportAsync(db, actor);
      Assert.True(csv.Succeeded, csv.Message); Assert.Equal(29, csv.Value!.Csv.Split('\n').Length);
      Assert.Contains("9007199254740993", csv.Value.Csv); Assert.DoesNotContain(b.Fixture.ClientId.ToString(), csv.Value.Csv);
      Assert.DoesNotContain(sibling.ToString(), System.Text.Json.JsonSerializer.Serialize(v));
      var grant = await db.RoleGrants.SingleAsync(g => g.UserId == a.Fixture.Staff.Id && g.Role == "Staff");
      grant.EngagementId = null; await db.SaveChangesAsync();
      var expanded = (await PortfolioQuery.WorkspaceAsync(db, actor)).Value!;
      Assert.Equal(2, expanded.Metrics.Engagements); Assert.Equal(4, expanded.Metrics.PendingOperations);
      grant.RevokedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
      Assert.False((await PortfolioQuery.WorkspaceAsync(db, actor)).Succeeded);
      Assert.False((await PortfolioQuery.ExportAsync(db, actor)).Succeeded);
      Assert.False((await PortfolioQuery.WorkspaceAsync(db, PbcSeed.Actor(a.Fixture.Client, "ClientUser"))).Succeeded);
    }
    finally { Directory.Delete(a.StagingRoot, true); Directory.Delete(b.StagingRoot, true); }
  }

  [Fact]
  public async Task ExportEscapesFormulaPrefixesQuotesAndMultilineNamesAndRefusesUnboundedClientSets()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var client = await db.PracticeClients.SingleAsync(c => c.Id == f.ClientId);
    foreach (var value in new[] { "=1+1", "  +cmd", "-1", "@SUM(1)", "\tname", "\nname", "normal\"quoted\nname" })
    {
      client.LegalName = value; await db.SaveChangesAsync();
      var export = await PortfolioQuery.ExportAsync(db, actor);
      Assert.True(export.Succeeded, export.Message);
      var prefix = value.StartsWith("normal", StringComparison.Ordinal) ? "" : "'";
      Assert.Contains("\"" + prefix + value.Replace("\"", "\"\"") + "\"", export.Value!.Csv);
    }
    db.PracticeClients.AddRange(Enumerable.Range(0, 1000).Select(i => new PracticeClient {
      Id = Guid.NewGuid(), FirmId = f.FirmId, LegalName = "Bounded synthetic " + i, CreatedAt = DateTimeOffset.UtcNow }));
    await db.SaveChangesAsync();
    Assert.Equal("export.limit", (await PortfolioQuery.ExportAsync(db, actor)).ErrorCode);
    Assert.True((await PortfolioQuery.ExportAsync(db, actor, "normal")).Succeeded);
    Assert.False((await PortfolioQuery.WorkspaceAsync(db, actor, new string('a', 101))).Succeeded);
  }
}
