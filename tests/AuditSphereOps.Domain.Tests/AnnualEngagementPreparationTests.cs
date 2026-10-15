using System.Text.Json;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
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
    Assert.Equal("MISSING", state.Value.MilestoneSuggestionState); Assert.Empty(state.Value.MilestoneSuggestions!);
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

  [Fact]
  public async Task AnnualMilestonesUseCurrentSourceRevisionAndStayRestrictedWithoutBlockingReviewedCreation()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); await EngagementCreationReviewSeed.PopulateAsync(db, f);
    var manager = PbcSeed.Actor(f.Staff, "Manager");
    var source = await EngagementLifecycleService.CreateDraftAsync(db, manager,
      new(f.ClientId, "AccountingOnly", "2025-01-01", "2025-12-31", "SYNTHETIC"));
    Assert.True(source.Succeeded, source.Message);
    db.AcceptanceDecisions.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
      EngagementId = source.Value, ServiceRoute = "AccountingOnly", Decision = "Accepted", Generation = 1,
      Rationale = "Synthetic previous-period evidence", EvaluationTemplateVersion = "synthetic.v1",
      EvaluationSnapshotDigest = new string('a', 64), DecidedAt = DateTimeOffset.UtcNow, DecidedByUserId = f.Admin.Id });
    await db.SaveChangesAsync();

    var firstPlan = await StatutoryMilestoneService.SaveMilestonesAsync(db, manager,
      new(source.Value, new(2026, 4, 30), new(2026, 1, 7), new(2026, 2, 15), new(2026, 3, 15),
        "Synthetic reviewed adjustment for annual preparation coverage."));
    Assert.True(firstPlan.Succeeded, firstPlan.Message);
    var firstState = await AnnualEngagementPreparationWorkspace.StateAsync(db, manager, f.ClientId, source.Value);
    Assert.True(firstState.Succeeded, firstState.Message);
    Assert.Equal("AVAILABLE", firstState.Value!.MilestoneSuggestionState);
    var firstSuggestions = firstState.Value.MilestoneSuggestions!;
    Assert.Equal(new DateOnly(2027, 1, 7), Assert.Single(firstSuggestions, x => x.Kind == "FIELDWORK_START").SuggestedDate);
    Assert.Equal(new DateOnly(2027, 2, 15), Assert.Single(firstSuggestions, x => x.Kind == "DRAFT_REPORT").SuggestedDate);
    Assert.Equal(new DateOnly(2027, 3, 15), Assert.Single(firstSuggestions, x => x.Kind == "FINAL_REPORT").SuggestedDate);
    var cutoff = Assert.Single(firstSuggestions, x => x.Kind == "STATUTORY_CUTOFF");
    Assert.Equal(new DateOnly(2026, 4, 30), cutoff.SourceDate); Assert.Null(cutoff.SuggestedDate);
    Assert.Equal("Enter a separately verified current-year statutory cutoff; the previous deadline is not reused.", cutoff.Notice);
    Assert.DoesNotContain(firstSuggestions, x => x.Kind.Contains("ARCHIVE", StringComparison.Ordinal));

    var revisedPlan = await StatutoryMilestoneService.SaveMilestonesAsync(db, manager,
      new(source.Value, new(2026, 4, 30), new(2026, 1, 8), new(2026, 2, 16), new(2026, 3, 16),
        "Synthetic reviewed adjustment for annual preparation coverage."));
    Assert.True(revisedPlan.Succeeded, revisedPlan.Message);
    var refreshed = await AnnualEngagementPreparationWorkspace.StateAsync(db, manager, f.ClientId, source.Value);
    Assert.True(refreshed.Succeeded, refreshed.Message);
    Assert.Equal(2L, Assert.Single(refreshed.Value!.MilestoneSuggestions!, x => x.Kind == "FIELDWORK_START").SourcePlanRevision);
    Assert.Equal(new DateOnly(2026, 1, 8), Assert.Single(refreshed.Value.MilestoneSuggestions!, x => x.Kind == "FIELDWORK_START").SourceDate);
    Assert.Equal(new DateOnly(2027, 1, 8), Assert.Single(refreshed.Value.MilestoneSuggestions!, x => x.Kind == "FIELDWORK_START").SuggestedDate);

    await db.RoleGrants.Where(x => x.FirmId == f.FirmId && x.UserId == f.Staff.Id &&
        x.Role == "Manager" && x.RevokedAt == null)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    var relationshipGrant = PbcSeed.Grant(f.FirmId, f.Staff, "RelationshipManager", f.ClientId);
    db.RoleGrants.Add(relationshipGrant); await db.SaveChangesAsync();
    var relationshipManager = PbcSeed.Actor(f.Staff, "RelationshipManager");
    var planRead = await StatutoryMilestoneService.GetMilestonesAsync(db, relationshipManager, source.Value);
    Assert.False(planRead.Succeeded); Assert.Equal("scope.denied", planRead.ErrorCode); Assert.Null(planRead.Value);
    var restricted = await AnnualEngagementPreparationWorkspace.StateAsync(db, relationshipManager, f.ClientId, source.Value);
    Assert.True(restricted.Succeeded, restricted.Message);
    Assert.Equal("RESTRICTED", restricted.Value!.MilestoneSuggestionState);
    Assert.Empty(restricted.Value.MilestoneSuggestions!);
    var restrictedJson = JsonSerializer.Serialize(restricted.Value);
    Assert.DoesNotContain("2026-01-07", restrictedJson);
    Assert.DoesNotContain("2026-02-15", restrictedJson);
    Assert.DoesNotContain("2026-03-15", restrictedJson);
    Assert.DoesNotContain("2026-04-30", restrictedJson);

    var milestoneCount = await db.EngagementMilestonePlans.CountAsync(x => x.EngagementId == source.Value);
    var decisionCount = await db.AcceptanceDecisions.CountAsync();
    var materialityApprovalCount = await db.MaterialityApprovals.CountAsync();
    var riskClearanceCount = await db.RiskPartnerClearances.CountAsync();
    var timeEntryCount = await db.TimeEntries.CountAsync();
    var client = await EngagementCreationWorkspace.StateAsync(db, relationshipManager, f.ClientId);
    Assert.True(client.Succeeded, client.Message);
    var request = new EngagementCreationRequest(Guid.NewGuid(), client.Value!.ReviewBasis, restricted.Value.Fields);
    var preview = await AnnualEngagementPreparationWorkspace.PreviewAsync(db, relationshipManager, f.ClientId, request);
    Assert.True(preview.Succeeded, preview.Message);
    var created = await AnnualEngagementPreparationWorkspace.ExecuteAsync(db, relationshipManager, f.ClientId,
      request with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash });
    Assert.True(created.Succeeded, created.Message);
    var target = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == created.Value!.EngagementId);
    Assert.Equal(("Draft", true, "2026-01-01", "2026-12-31"),
      (target.Status, target.ProfessionalWorkBlocked, target.PeriodStart, target.PeriodEnd));
    Assert.Equal(milestoneCount, await db.EngagementMilestonePlans.CountAsync(x => x.EngagementId == source.Value));
    Assert.False(await db.EngagementMilestonePlans.AnyAsync(x => x.EngagementId == target.Id));
    Assert.Equal(decisionCount, await db.AcceptanceDecisions.CountAsync());
    Assert.Equal(materialityApprovalCount, await db.MaterialityApprovals.CountAsync());
    Assert.Equal(riskClearanceCount, await db.RiskPartnerClearances.CountAsync());
    Assert.Equal(timeEntryCount, await db.TimeEntries.CountAsync());
    Assert.False(await db.AcceptanceDecisions.AnyAsync(x => x.EngagementId == target.Id));
    Assert.Null(await EngagementRiskEvidence.LatestAsync(db, f.FirmId, target, default));
    var storedCreation = await db.EngagementCreations.AsNoTracking().SingleAsync(x => x.Id == created.Value!.Id);
    Assert.DoesNotContain("milestone", storedCreation.InputJson, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void AnnualMilestoneProjectionFailsClosedForInvalidChronologyOffsetsAndDateOverflow()
  {
    var sourceEnd = new DateOnly(2025, 12, 31);
    var nextEnd = new DateOnly(2026, 12, 31);
    const string reviewNotice = "Historical dates need review; no automatic date was proposed.";

    var reversed = AnnualEngagementPreparationWorkspace.ProjectMilestones(
      Plan(new(2026, 3, 20), new(2026, 2, 15), new(2026, 3, 15), new(2026, 4, 30)), sourceEnd, nextEnd);
    Assert.All(reversed.Where(x => x.Kind != "STATUTORY_CUTOFF"), suggestion =>
    {
      Assert.Null(suggestion.SuggestedDate);
      Assert.Equal(reviewNotice, suggestion.Notice);
    });

    var offsetExceeded = AnnualEngagementPreparationWorkspace.ProjectMilestones(
      Plan(new(2026, 12, 30), new(2027, 1, 2), new(2027, 1, 3), new(2027, 4, 30)), sourceEnd, nextEnd);
    Assert.Equal(new DateOnly(2027, 12, 30), Assert.Single(offsetExceeded, x => x.Kind == "FIELDWORK_START").SuggestedDate);
    foreach (var kind in new[] { "DRAFT_REPORT", "FINAL_REPORT" })
    {
      var invalid = Assert.Single(offsetExceeded, x => x.Kind == kind);
      Assert.Equal(367 + (kind == "FINAL_REPORT" ? 1 : 0), invalid.OffsetDaysFromSourcePeriodEnd);
      Assert.Null(invalid.SuggestedDate); Assert.Equal(reviewNotice, invalid.Notice);
    }

    var overflow = AnnualEngagementPreparationWorkspace.ProjectMilestones(
      Plan(new(9999, 1, 1), new(9999, 1, 2), new(9999, 1, 3), new(9999, 4, 30)),
      new(9998, 12, 30), new(9999, 12, 30));
    Assert.All(overflow.Where(x => x.Kind != "STATUTORY_CUTOFF"), suggestion =>
    {
      Assert.Null(suggestion.SuggestedDate);
      Assert.Equal(reviewNotice, suggestion.Notice);
    });
  }

  private static MilestonePlanView Plan(DateOnly fieldwork, DateOnly draft, DateOnly finalReport, DateOnly cutoff) =>
    new(Guid.NewGuid(), Guid.NewGuid(), new(2025, 12, 31), cutoff, fieldwork, draft, finalReport,
      finalReport.AddDays(60), null, null, [], 1, Guid.NewGuid(), DateTimeOffset.UnixEpoch, false);
}
