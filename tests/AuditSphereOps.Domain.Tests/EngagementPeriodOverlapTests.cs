using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class EngagementPeriodOverlapTests
{
  [Theory]
  [InlineData("2027-06-01", "2028-05-31")]
  [InlineData("2026-06-01", "2027-05-31")]
  [InlineData("2027-03-01", "2027-04-30")]
  [InlineData("2026-01-01", "2028-12-31")]
  [InlineData("2027-12-31", "2028-12-31")]
  public async Task PreviewAndDirectCreationRejectOverlapsWithoutChangingExistingEvidence(string start, string end)
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await EngagementCreationReviewSeed.PopulateAsync(db, f); var actor = PbcSeed.Actor(f.Staff, "Manager");
    var original = new CreateEngagementDraftRequest(f.ClientId, "AccountingOnly", "2027-01-01", "2027-12-31", "SYNTHETIC");
    var first = await EngagementLifecycleService.CreateDraftAsync(db, actor, original); Assert.True(first.Succeeded, first.Message);
    var state = (await EngagementCreationWorkspace.StateAsync(db, actor, f.ClientId)).Value!;
    var preview = await EngagementCreationWorkspace.PreviewAsync(db, actor, f.ClientId,
      new(Guid.NewGuid(), state.ReviewBasis, new("AccountingOnly", "AMENDED", start, end)));
    Assert.Equal("engagement.period-overlap", preview.ErrorCode);
    var direct = await EngagementLifecycleService.CreateDraftAsync(db, actor, original with { PeriodStart = start, PeriodEnd = end });
    Assert.Equal("engagement.period-overlap", direct.ErrorCode);
    Assert.Equal(first.Value, (await EngagementLifecycleService.CreateDraftAsync(db, actor, original)).Value);
    var retained = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == first.Value);
    Assert.Equal(("2027-01-01", "2027-12-31", "SYNTHETIC", "Draft", true),
      (retained.PeriodStart, retained.PeriodEnd, retained.ServiceProfileId, retained.Status, retained.ProfessionalWorkBlocked));
    Assert.Equal(2, await db.Engagements.CountAsync());
    Assert.Empty(await db.EngagementCreations.ToListAsync()); Assert.Empty(await db.EngagementActivations.ToListAsync());
  }

  [Fact]
  public async Task AdjacentYearsAndSeparateServicesRemainAllowedAndScopesDoNotLeak()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await EngagementCreationReviewSeed.PopulateAsync(db, f); var actor = PbcSeed.Actor(f.Staff, "Manager");
    var original = new CreateEngagementDraftRequest(f.ClientId, "AccountingOnly", "2027-01-01", "2027-12-31", "SYNTHETIC");
    Assert.True((await EngagementLifecycleService.CreateDraftAsync(db, actor, original)).Succeeded);
    Assert.True((await EngagementLifecycleService.CreateDraftAsync(db, actor, original with { PeriodStart = "2028-01-01", PeriodEnd = "2028-12-31" })).Succeeded);
    Assert.True((await EngagementLifecycleService.CreateDraftAsync(db, actor, original with { ServiceRoute = "InternalAudit" })).Succeeded);
    Assert.Equal("scope.denied", (await EngagementLifecycleService.CreateDraftAsync(db, actor, original with { PracticeClientId = foreign.ClientId })).ErrorCode);
    Assert.False((await EngagementCreationWorkspace.StateAsync(db, actor, foreign.ClientId)).Succeeded);
    Assert.Empty(await db.EngagementActivations.ToListAsync());
  }

  [Fact]
  public async Task CompetingCreationAfterPreviewBlocksReviewedExecuteWithoutPublishingAReceipt()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await EngagementCreationReviewSeed.PopulateAsync(db, f); var actor = PbcSeed.Actor(f.Staff, "Manager");
    var state = (await EngagementCreationWorkspace.StateAsync(db, actor, f.ClientId)).Value!;
    var request = new EngagementCreationRequest(Guid.NewGuid(), state.ReviewBasis,
      new("AccountingOnly", "SYNTHETIC", "2027-01-01", "2027-12-31"));
    var preview = await EngagementCreationWorkspace.PreviewAsync(db, actor, f.ClientId, request);
    Assert.True(preview.Succeeded, preview.Message);
    Assert.True((await EngagementLifecycleService.CreateDraftAsync(db, actor,
      new(f.ClientId, "AccountingOnly", "2027-06-01", "2028-05-31", "COMPETING"))).Succeeded);
    var execute = await EngagementCreationWorkspace.ExecuteAsync(db, actor, f.ClientId,
      request with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash });
    Assert.Equal("engagement.period-overlap", execute.ErrorCode);
    Assert.Empty(await db.EngagementCreations.ToListAsync()); Assert.Equal(2, await db.Engagements.CountAsync());
  }

  [Fact]
  public async Task ConcurrentOverlappingRequestsPublishOnlyOneShell()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using (var setup = new AuditSphereDbContext(pg.Options)) await EngagementCreationReviewSeed.PopulateAsync(setup, f);
    var actor = PbcSeed.Actor(f.Staff, "Manager");
    async Task<AuditSphereOps.Domain.Shared.CommandResult<Guid>> Create(string end)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await EngagementLifecycleService.CreateDraftAsync(db, actor, new(f.ClientId, "AccountingOnly", "2027-01-01", end, "SYNTHETIC"));
    }
    var results = await Task.WhenAll(Create("2027-12-31"), Create("2028-12-31"));
    Assert.Single(results, x => x.Succeeded); Assert.Equal("engagement.period-overlap", Assert.Single(results, x => !x.Succeeded).ErrorCode);
    await using var inspect = new AuditSphereDbContext(pg.Options);
    Assert.Single(await inspect.Engagements.Where(x => x.ServiceProfileId == "SYNTHETIC").ToListAsync());
  }
}
