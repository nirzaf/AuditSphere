using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Domain.Tests;

public sealed class RelationshipManagerAnnualPreparationTests
{
  [Fact]
  public async Task ClientScopedRelationshipManagerPreparesAnnualDraftButCannotMakeProfessionalDecisions()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); var actor = PbcSeed.Actor(f.Staff, "RelationshipManager");
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "RelationshipManager", f.ClientId, f.EngagementId));
    await db.SaveChangesAsync();
    Assert.False((await EngagementCreationWorkspace.StateAsync(db, actor, f.ClientId)).Succeeded);
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Succeeded);

    var grant = PbcSeed.Grant(f.FirmId, f.Staff, "RelationshipManager", f.ClientId);
    db.RoleGrants.Add(grant); await db.SaveChangesAsync();
    var source = await EngagementLifecycleService.CreateDraftAsync(db, actor,
      new(f.ClientId, "AccountingOnly", "2026-01-01", "2026-12-31", "SYNTHETIC"));
    Assert.True(source.Succeeded, source.Message);
    var profile = await WorkspaceQuery.ClientAsync(db, actor, f.ClientId);
    Assert.True(profile.Succeeded, profile.Message); Assert.True(profile.Value!.CanCreateEngagement);
    Assert.Contains(profile.Value.Engagements, x => x.Id == source.Value);
    var annual = await AnnualEngagementPreparationWorkspace.StateAsync(db, actor, f.ClientId, source.Value);
    Assert.True(annual.Succeeded, annual.Message);
    Assert.Equal("RESTRICTED", annual.Value!.HistoricalMappingState);
    Assert.Empty(annual.Value.HistoricalMappings);
    var state = (await EngagementCreationWorkspace.StateAsync(db, actor, f.ClientId)).Value!;
    var request = new EngagementCreationRequest(Guid.NewGuid(), state.ReviewBasis, annual.Value!.Fields);
    var preview = await AnnualEngagementPreparationWorkspace.PreviewAsync(db, actor, f.ClientId, request);
    Assert.True(preview.Succeeded, preview.Message);
    var created = await AnnualEngagementPreparationWorkspace.ExecuteAsync(db, actor, f.ClientId,
      request with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash });
    Assert.True(created.Succeeded, created.Message);
    var id = created.Value!.EngagementId;
    Assert.False((await WorkspaceQuery.EngagementAsync(db, actor, id)).Succeeded);
    Assert.False((await EngagementLifecycleService.ActivateAsync(db, actor, id)).Succeeded);
    Assert.False((await AcceptanceDecisionService.RecordAsync(db, actor,
      new(f.ClientId, id, "AccountingOnly", "Accepted", "No authority", null, 1))).Succeeded);
    Assert.Empty(await db.EngagementActivations.ToListAsync()); Assert.Empty(await db.AcceptanceDecisions.ToListAsync());
    Assert.False((await EngagementCreationWorkspace.StateAsync(db, actor, foreign.ClientId)).Succeeded);
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, foreign.ClientId)).Succeeded);
    var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    var roleMigration = Assert.Single(migrations, x => x.EndsWith("RelationshipManagerEngagementCreationReceipts", StringComparison.Ordinal));
    await db.GetService<IMigrator>().MigrateAsync(migrations[Array.IndexOf(migrations, roleMigration) - 1]);
    Assert.True(await db.EngagementCreations.AnyAsync(x => x.Id == created.Value.Id));
    await db.GetService<IMigrator>().MigrateAsync();
    Assert.Equal(created.Value.Id, (await EngagementCreationWorkspace.LookupAsync(db, actor, f.ClientId,
      request.RequestId, preview.Value.RequestHash)).Value!.Receipt!.Id);
    await db.RoleGrants.Where(x => x.Id == grant.Id).ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    Assert.False((await AnnualEngagementPreparationWorkspace.StateAsync(db, actor, f.ClientId, source.Value)).Succeeded);
    Assert.False((await EngagementCreationWorkspace.LookupAsync(db, actor, f.ClientId, request.RequestId, preview.Value.RequestHash)).Succeeded);
  }
}
