using System.Text.Json;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class AnnualEngagementPreparationTests
{
  [Theory]
  [InlineData("2024-01-01", "2024-12-31", "2025-01-01", "2025-12-31")]
  [InlineData("2023-07-01", "2024-06-30", "2024-07-01", "2025-06-30")]
  [InlineData("2024-02-29", "2025-02-28", "2025-03-01", "2026-02-28")]
  public void AnnualCalendarStartsAfterThePriorPeriodAndHandlesLeapYears(string start, string end, string nextStart, string nextEnd)
  {
    var source = new Engagement { Id = Guid.NewGuid(), ServiceRoute = "AccountingOnly", ServiceProfileId = "SYNTHETIC", PeriodStart = start, PeriodEnd = end };
    var fields = AnnualEngagementPreparationWorkspace.Suggest(source)!;
    Assert.Equal((nextStart, nextEnd, source.Id), (fields.PeriodStart, fields.PeriodEnd, fields.AnnualSourceEngagementId));
    Assert.Null(AnnualEngagementPreparationWorkspace.Suggest(new Engagement { PeriodStart = "2026-01-01", PeriodEnd = "2026-02-28" }));
    Assert.DoesNotContain("AnnualSourceEngagementId", JsonSerializer.Serialize(new EngagementCreationFields("AccountingOnly", "SYNTHETIC", start, end)));
    Assert.DoesNotContain("AnnualTemplateRevisionId", JsonSerializer.Serialize(new EngagementCreationFields("AccountingOnly", "SYNTHETIC", start, end)));
  }

  [Fact]
  public async Task ConcurrentReviewedAnnualCreationRetainsSourceAndCreatesOneBlockedPeriodWithNoInheritedDecision()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); await EngagementCreationReviewSeed.PopulateAsync(db, f);
    var actor = PbcSeed.Actor(f.Staff, "Manager");
    var source = await EngagementLifecycleService.CreateDraftAsync(db, actor, new(f.ClientId, "AccountingOnly", "2026-01-01", "2026-12-31", "SYNTHETIC"));
    Assert.True(source.Succeeded, source.Message);
    db.ClientContacts.AddRange(
      new ClientContact { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        FullName = "Synthetic valid liaison", Role = "Liaison", Email = "liaison@example.test", Primary = true,
        ValidFrom = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero) },
      new ClientContact { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        FullName = "Expired liaison", Role = "Liaison", Email = "old@example.test",
        ValidTo = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero) },
      new ClientContact { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        FullName = "Inactive liaison", Role = "Liaison", Email = "inactive@example.test", IsActive = false });
    db.AcceptanceDecisions.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
      EngagementId = source.Value, ServiceRoute = "AccountingOnly", Decision = "Accepted", Generation = 1,
      Rationale = "Synthetic previous-period evidence", EvaluationTemplateVersion = "synthetic.v1", EvaluationSnapshotDigest = new string('a', 64),
      DecidedAt = DateTimeOffset.UtcNow, DecidedByUserId = f.Admin.Id });
    await db.SaveChangesAsync();
    var state = await AnnualEngagementPreparationWorkspace.StateAsync(db, actor, f.ClientId, source.Value);
    Assert.True(state.Succeeded, state.Message); Assert.Contains("Partner continuance", state.Value!.RequiresFreshWork);
    Assert.Equal("Synthetic valid liaison", Assert.Single(state.Value.ContactSuggestions).Name);
    Assert.Equal("AVAILABLE", state.Value.HistoricalMappingState);
    Assert.Empty(state.Value.HistoricalMappings);
    var client = (await EngagementCreationWorkspace.StateAsync(db, actor, f.ClientId)).Value!;
    var request = new EngagementCreationRequest(Guid.NewGuid(), client.ReviewBasis, state.Value.Fields);
    var preview = await AnnualEngagementPreparationWorkspace.PreviewAsync(db, actor, f.ClientId, request);
    Assert.True(preview.Succeeded, preview.Message); request = request with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash };
    async Task<AuditSphereOps.Domain.Shared.CommandResult<EngagementCreationReceipt>> Create()
    {
      await using var other = new AuditSphereDbContext(pg.Options);
      return await AnnualEngagementPreparationWorkspace.ExecuteAsync(other, actor, f.ClientId, request);
    }
    var results = await Task.WhenAll(Create(), Create()); Assert.All(results, x => Assert.True(x.Succeeded, x.Message));
    Assert.Equal(results[0].Value!.Id, results[1].Value!.Id);
    var receipt = results[0].Value!; Assert.Equal(source.Value, receipt.Fields.AnnualSourceEngagementId);
    var target = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == receipt.EngagementId);
    Assert.Equal(("2027-01-01", "2027-12-31", "Draft", true), (target.PeriodStart, target.PeriodEnd, target.Status, target.ProfessionalWorkBlocked));
    Assert.Null(await EngagementRiskEvidence.LatestAsync(db, f.FirmId, target, default));
    Assert.Empty(await db.EngagementActivations.ToListAsync()); Assert.Single(await db.EngagementCreations.ToListAsync());
    await db.Engagements.Where(x => x.Id == source.Value).ExecuteUpdateAsync(x => x.SetProperty(e => e.ServiceProfileId, "SOURCE-AMENDED"));
    Assert.Equal(receipt.Id, (await AnnualEngagementPreparationWorkspace.ExecuteAsync(db, actor, f.ClientId, request)).Value!.Id);
    Assert.Equal(receipt.Id, (await EngagementCreationWorkspace.LookupAsync(db, actor, f.ClientId, request.RequestId, request.ExpectedRequestHash)).Value!.Receipt!.Id);
  }

  [Fact]
  public async Task ChangedOrForeignAnnualReferenceFailsWithoutPublishingANewShell()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); await EngagementCreationReviewSeed.PopulateAsync(db, f);
    var actor = PbcSeed.Actor(f.Staff, "Manager");
    var source = await EngagementLifecycleService.CreateDraftAsync(db, actor, new(f.ClientId, "AccountingOnly", "2026-01-01", "2026-12-31", "SYNTHETIC"));
    Assert.False((await AnnualEngagementPreparationWorkspace.StateAsync(db, actor, f.ClientId, foreign.EngagementId)).Succeeded);
    Assert.False((await AnnualEngagementPreparationWorkspace.StateAsync(db, actor, foreign.ClientId, source.Value)).Succeeded);
    var state = (await AnnualEngagementPreparationWorkspace.StateAsync(db, actor, f.ClientId, source.Value)).Value!;
    var client = (await EngagementCreationWorkspace.StateAsync(db, actor, f.ClientId)).Value!;
    var request = new EngagementCreationRequest(Guid.NewGuid(), client.ReviewBasis, state.Fields);
    var preview = (await AnnualEngagementPreparationWorkspace.PreviewAsync(db, actor, f.ClientId, request)).Value!;
    Assert.False((await AnnualEngagementPreparationWorkspace.PreviewAsync(db, actor, f.ClientId,
      request with { Fields = request.Fields with { PeriodEnd = "2028-12-31" } })).Succeeded);
    await db.Engagements.Where(x => x.Id == source.Value).ExecuteUpdateAsync(x => x.SetProperty(e => e.ServiceProfileId, "CHANGED"));
    var result = await AnnualEngagementPreparationWorkspace.ExecuteAsync(db, actor, f.ClientId,
      request with { Reviewed = true, ExpectedRequestHash = preview.RequestHash });
    Assert.Equal("generation.stale", result.ErrorCode); Assert.Empty(await db.EngagementCreations.ToListAsync());
  }
}
