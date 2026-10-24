using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>T058 service completion: cut-off testing, subsequent-settlement matching and
/// the sample-set view built on the existing selection/item-test entities.</summary>
[Trait("Profile", "Database")]
public sealed class AuditSamplingServiceTests
{
  [Fact(DisplayName = "Cut-off and subsequent matching derive their state from the recorded evidence")]
  public async Task CutOffAndSubsequentMatching_AreDerivedAndVisibleInTheSampleSet()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;
    Guid selectionId, procedureId, inPeriodItemId, nextPeriodItemId;
    ActorContext reviewer;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // A distinct reviewer is required: the preparer cannot approve their own schedule.
      var reviewerId = Guid.NewGuid();
      db.Users.Add(new AppUser
      {
        Id = reviewerId, FirmId = scope.FirmId, Subject = "sampling-reviewer-" + reviewerId.ToString("N"),
        TenantId = "tenant-planning", Email = "sampling-reviewer@example.test", DisplayName = "Sampling Reviewer",
        UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = reviewerId, Role = "Reviewer",
        ClientId = scope.ClientId, EngagementId = scope.EngagementId,
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Actor.UserId
      });
      await db.SaveChangesAsync();
      reviewer = new ActorContext(reviewerId, scope.FirmId, 1, ["Reviewer", "Partner"]);

      var program = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
        new PublishAuditProgramRequest("2026.3", AuditProgramCatalog.SourceHash));
      Assert.True((await AuditProgramService.AdoptAsync(db, scope.Actor,
        new AdoptAuditProgramRequest(scope.EngagementId, program.Value!.ProgramVersionId))).Succeeded);
      var procedure = await db.AuditProcedures.SingleAsync(x =>
        x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-03-06");
      procedureId = procedure.Id;
      Assert.True((await AuditProgramTestHelpers.MarkApplicableAsync(db, scope.Actor, procedure.Id, null)).Succeeded);

      // An approved receivable listing is the source population for the selection.
      var schedule = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor, new CreateScheduleRequest(
        scope.EngagementId, "RECEIVABLES_LEAD", "receivable-ledger-2026", "receipt-receivables-1",
        new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "debits positive; credits negative",
        Hashing.Sha256Hex("receivable-listing"), 1640m,
        [
          new("inv-001", 1, "1100", "Invoice 001", 900m, "QAR", null, new DateOnly(2026, 12, 20), null, null, "{\"source\":\"listing\"}"),
          new("inv-002", 2, "1100", "Invoice 002", 700m, "QAR", null, new DateOnly(2026, 12, 30), null, null, "{\"source\":\"listing\"}"),
          new("inv-003", 3, "1100", "Invoice 003", 40m, "QAR", null, new DateOnly(2026, 12, 31), null, null, "{\"source\":\"listing\"}")
        ]));
      Assert.True(schedule.Succeeded, schedule.Message);
      Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
        new ReviewScheduleRequest(schedule.Value!.ScheduleId, "Complete receivable listing; no unexplained residual.", true))).Succeeded);

      // Two sampled receivable invoices: one booked and documented in the period, one
      // booked in the period but shipped in the next period (a cut-off exception).
      var selection = await AuditFieldworkService.CreateSelectionAsync(db, scope.Actor, new CreateSelectionRequest(
        scope.EngagementId, procedure.Id, schedule.Value.ScheduleId, null, "MUS interval 500", "Monetary-unit selection over the receivable ledger.",
        [
          new("inv-001", 900m, "QAR", "Monetary unit selection", null),
          new("inv-002", 700m, "QAR", "Monetary unit selection", null),
          new("inv-003", 40m, "QAR", "Below interval, incidental control", null)
        ]));
      Assert.True(selection.Succeeded, selection.Message);
      selectionId = selection.Value!.SelectionId;
      var items = await db.AuditSelectionItems.Where(x => x.SelectionId == selectionId)
        .ToDictionaryAsync(x => x.StableRowId, x => x.Id);
      inPeriodItemId = items["inv-001"];
      nextPeriodItemId = items["inv-002"];

      var unreviewedCutOff = await AuditSamplingService.RecordCutOffTestAsync(db, scope.Actor, new CutOffTestRequest(
        inPeriodItemId, new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 20),
        new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 21), "Must not test a submitted selection.",
        ["unreviewed-evidence"]));
      Assert.False(unreviewedCutOff.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, unreviewedCutOff.ErrorCode);
      Assert.Empty(await db.AuditCutOffTestRecords.Where(x => x.SelectionId == selectionId).ToListAsync());
      var selectionReview = await AuditFieldworkService.ReviewSelectionAsync(db, reviewer,
        new ReviewSelectionRequest(selectionId, AuditSelectionStatuses.Reviewed,
          "Independently reviewed the selected receivables against the approved population."));
      Assert.True(selectionReview.Succeeded, selectionReview.Message);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // In-period item: transaction, document and shipment all before the period end.
      var inPeriod = await AuditSamplingService.RecordCutOffTestAsync(db, scope.Actor, new CutOffTestRequest(
        inPeriodItemId, new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 20),
        new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 21), "Agreed invoice and delivery note to the sales ledger.",
        ["delivery-note-inv-001"]));
      Assert.True(inPeriod.Succeeded, inPeriod.Message);
      Assert.Equal(AuditCutOffDirections.BeforePeriodEnd, inPeriod.Value!.PeriodEndIndicator);
      Assert.False(inPeriod.Value.IsCutOffException);

      // Booked in the period but shipped after it: flagged, not silently passed.
      var nextPeriod = await AuditSamplingService.RecordCutOffTestAsync(db, scope.Actor, new CutOffTestRequest(
        nextPeriodItemId, new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 30),
        new DateOnly(2026, 12, 30), new DateOnly(2027, 1, 4), "Delivery note shows shipment in January 2027.",
        ["delivery-note-inv-002"]));
      Assert.True(nextPeriod.Succeeded, nextPeriod.Message);
      Assert.True(nextPeriod.Value!.IsCutOffException);
      var cutOffRecord = await db.AuditCutOffTestRecords.SingleAsync(x => x.SelectionItemId == nextPeriodItemId);
      Assert.Equal(procedureId, cutOffRecord.ProcedureId);

      // A correction creates a new immutable revision and retains the original exception evidence.
      var revised = await AuditSamplingService.RecordCutOffTestAsync(db, scope.Actor, new CutOffTestRequest(
        nextPeriodItemId, new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 30),
        new DateOnly(2026, 12, 30), new DateOnly(2026, 12, 29), "Corrected: goods shipped before year end.",
        ["delivery-note-inv-002-revised"], ExpectedRevision: 1));
      Assert.True(revised.Succeeded);
      Assert.Equal(2, revised.Value!.Revision);
      Assert.False(revised.Value.IsCutOffException);
      var cutOffHistory = await db.AuditCutOffTestRecords.Where(x => x.SelectionItemId == nextPeriodItemId)
        .OrderBy(x => x.Revision).ToListAsync();
      Assert.Equal(2, cutOffHistory.Count);
      Assert.True(cutOffHistory[0].IsCutOffException);
      Assert.Equal(1, cutOffHistory[0].Revision);
      Assert.Equal("delivery-note-inv-002", cutOffHistory[0].EvidenceReferencesJson.Trim('[', ']', '"'));
      Assert.Equal("delivery-note-inv-002-revised", cutOffHistory[1].EvidenceReferencesJson.Trim('[', ']', '"'));
      var staleCutOff = await AuditSamplingService.RecordCutOffTestAsync(db, scope.Actor, new CutOffTestRequest(
        nextPeriodItemId, new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 30),
        new DateOnly(2026, 12, 30), new DateOnly(2026, 12, 29), "Stale correction must not replace the latest evidence.",
        ["stale-reference"], ExpectedRevision: 1));
      Assert.False(staleCutOff.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, staleCutOff.ErrorCode);
      Assert.Equal(2, await db.AuditCutOffTestRecords.CountAsync(x => x.SelectionItemId == nextPeriodItemId));

      // A higher-scale amount must not be silently rounded into a false full settlement.
      var overPrecisionMatch = await AuditSamplingService.MatchSubsequentTransactionAsync(db, scope.Actor,
        new SubsequentMatchRequest(inPeriodItemId, 900.0000004m, "BANK-2027-01-15-CREDIT-001",
          new DateOnly(2027, 1, 15), "bank-statement-jan-2027", null));
      Assert.False(overPrecisionMatch.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, overPrecisionMatch.ErrorCode);

      // Subsequent settlement: full match settles the item.
      var settled = await AuditSamplingService.MatchSubsequentTransactionAsync(db, scope.Actor, new SubsequentMatchRequest(
        inPeriodItemId, 900m, "BANK-2027-01-15-CREDIT-001", new DateOnly(2027, 1, 15), "bank-statement-jan-2027", null));
      Assert.True(settled.Succeeded, settled.Message);
      Assert.Equal(AuditSubsequentMatchStates.Matched, settled.Value!.State);
      Assert.Equal(0m, settled.Value.UnmatchedAmount);

      // Partial settlement must carry an explanation and stays visible as unmatched value.
      var partial = await AuditSamplingService.MatchSubsequentTransactionAsync(db, scope.Actor, new SubsequentMatchRequest(
        nextPeriodItemId, 500m, "BANK-2027-02-02-CREDIT-014", new DateOnly(2027, 2, 2), "bank-statement-feb-2027",
        "Customer settled 500 of 700; the balance is disputed."));
      Assert.True(partial.Succeeded, partial.Message);
      Assert.Equal(AuditSubsequentMatchStates.PartiallyMatched, partial.Value!.State);
      Assert.Equal(200m, partial.Value.UnmatchedAmount);

      var revisedPartial = await AuditSamplingService.MatchSubsequentTransactionAsync(db, scope.Actor, new SubsequentMatchRequest(
        nextPeriodItemId, 600m, "BANK-2027-02-02-CREDIT-014", new DateOnly(2027, 2, 2), "bank-statement-feb-2027-corrected",
        "Additional receipt evidence supports a revised allocation.", ExpectedRevision: 1));
      Assert.True(revisedPartial.Succeeded, revisedPartial.Message);
      Assert.Equal(2, revisedPartial.Value!.Revision);
      Assert.Equal(100m, revisedPartial.Value.UnmatchedAmount);
      var stalePartial = await AuditSamplingService.MatchSubsequentTransactionAsync(db, scope.Actor, new SubsequentMatchRequest(
        nextPeriodItemId, 550m, "BANK-2027-02-02-CREDIT-014", new DateOnly(2027, 2, 2), "stale-evidence",
        "Stale revision attempt.", ExpectedRevision: 1));
      Assert.False(stalePartial.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, stalePartial.ErrorCode);
      var matchHistory = await db.AuditSubsequentMatchRecords.Where(x => x.SelectionItemId == nextPeriodItemId)
        .OrderBy(x => x.Revision).ToListAsync();
      Assert.Equal(2, matchHistory.Count);
      Assert.Equal(500m, matchHistory[0].MatchedAmount);
      Assert.Equal(600m, matchHistory[1].MatchedAmount);
      Assert.All(matchHistory, x => Assert.Equal(procedureId, x.ProcedureId));

      // A partial match without an explanation is refused.
      var unexplained = await AuditSamplingService.MatchSubsequentTransactionAsync(db, scope.Actor, new SubsequentMatchRequest(
        nextPeriodItemId, 100m, "BANK-2027-03-01", null, "evidence", null));
      Assert.False(unexplained.Succeeded);

      // An over-match beyond the item amount is refused.
      var over = await AuditSamplingService.MatchSubsequentTransactionAsync(db, scope.Actor, new SubsequentMatchRequest(
        inPeriodItemId, 1000m, "BANK-2027-04-01", null, "evidence", "over"));
      Assert.False(over.Succeeded);

      var sampleSet = await AuditSamplingService.GetSampleSetAsync(db, scope.Actor, selectionId);
      Assert.True(sampleSet.Succeeded, sampleSet.Message);
      var view = sampleSet.Value!;
      Assert.Equal(3, view.TotalCount);
      Assert.Equal(2, view.CutOffRecordedCount);
      Assert.Equal(1, view.SubsequentMatchedCount);
      var inv001 = view.Items.Single(x => x.StableRowId == "inv-001");
      Assert.False(inv001.CutOffException);
      Assert.Equal(AuditSubsequentMatchStates.Matched, inv001.SubsequentState);
      var inv002 = view.Items.Single(x => x.StableRowId == "inv-002");
      Assert.False(inv002.CutOffException); // revised to an in-period shipment
      Assert.Equal(AuditSubsequentMatchStates.PartiallyMatched, inv002.SubsequentState);
      Assert.Equal(600m, inv002.MatchedAmount);
      var untested = view.Items.Single(x => x.StableRowId == "inv-003");
      Assert.Equal(AuditItemTestResults.Pending, untested.TestResult);
      Assert.Null(untested.CutOffTestId);

      // Two stale writers for one item are serialized and only one can create revision three.
      await using var firstWriter = new AuditSphereDbContext(pg.Options);
      await using var secondWriter = new AuditSphereDbContext(pg.Options);
      var firstCorrection = AuditSamplingService.RecordCutOffTestAsync(firstWriter, scope.Actor, new CutOffTestRequest(
        nextPeriodItemId, new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 30),
        new DateOnly(2026, 12, 30), new DateOnly(2026, 12, 29), "Concurrent correction A.",
        ["concurrent-a"], ExpectedRevision: 2));
      var secondCorrection = AuditSamplingService.RecordCutOffTestAsync(secondWriter, scope.Actor, new CutOffTestRequest(
        nextPeriodItemId, new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 30),
        new DateOnly(2026, 12, 30), new DateOnly(2026, 12, 29), "Concurrent correction B.",
        ["concurrent-b"], ExpectedRevision: 2));
      var concurrentResults = await Task.WhenAll(firstCorrection, secondCorrection);
      Assert.Single(concurrentResults, x => x.Succeeded);
      Assert.Single(concurrentResults, x => !x.Succeeded && x.ErrorCode == ErrorCodes.StaleRevision);
      await using var historyDb = new AuditSphereDbContext(pg.Options);
      var concurrentHistory = await historyDb.AuditCutOffTestRecords.Where(x => x.SelectionItemId == nextPeriodItemId)
        .OrderBy(x => x.Revision).ToListAsync();
      Assert.Equal(3, concurrentHistory.Count);
      Assert.Equal(new long[] { 1, 2, 3 }, concurrentHistory.Select(x => x.Revision));
      var areaSummary = await AuditAreaSummaryQuery.GetAreaSummaryAsync(
        historyDb, scope.Actor, scope.EngagementId, AuditAreaCodes.Receivables);
      Assert.True(areaSummary.Succeeded, areaSummary.Message);
      Assert.Equal(2, areaSummary.Value!.CutOffRecordedCount);
      Assert.Equal(0, areaSummary.Value.CutOffExceptionCount);
      Assert.Equal(2, areaSummary.Value.SubsequentRecordedCount);
      Assert.Equal(1, areaSummary.Value.SubsequentMatchedCount);

      var selectionRow = await historyDb.AuditSelections.SingleAsync(x => x.Id == selectionId);
      var safety = await historyDb.ClientSafetyStates.SingleAsync(x => x.Id == scope.ClientId && x.FirmId == scope.FirmId);
      safety.InputGeneration = selectionRow.InputGeneration + 1;
      await historyDb.SaveChangesAsync();
      var stalePopulationMatch = await AuditSamplingService.MatchSubsequentTransactionAsync(historyDb, scope.Actor,
        new SubsequentMatchRequest(nextPeriodItemId, 650m, "BANK-STALE-GENERATION", new DateOnly(2027, 2, 3),
          "stale-generation-evidence", "Must not match a stale sample population.", ExpectedRevision: 2));
      Assert.False(stalePopulationMatch.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, stalePopulationMatch.ErrorCode);
      Assert.Equal(2, await historyDb.AuditSubsequentMatchRecords.CountAsync(x => x.SelectionItemId == nextPeriodItemId));

      // A user granted only on the sibling engagement cannot read this sample set.
      var siblingUserId = Guid.NewGuid();
      db.Users.Add(new AppUser
      {
        Id = siblingUserId, FirmId = scope.FirmId, Subject = "sibling-auditor-" + siblingUserId.ToString("N"),
        TenantId = "tenant-planning", Email = "sibling-auditor@example.test", DisplayName = "Sibling Auditor",
        UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = siblingUserId, Role = "Auditor",
        ClientId = fixture.Other.ClientId, EngagementId = fixture.Other.EngagementId,
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Actor.UserId
      });
      await db.SaveChangesAsync();
      var sibling = new ActorContext(siblingUserId, scope.FirmId, 1, ["Auditor"]);
      var denied = await AuditSamplingService.GetSampleSetAsync(db, sibling, selectionId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);

      // Unknown ids are denied rather than returning an empty sample.
      var unknown = await AuditSamplingService.GetSampleSetAsync(db, scope.Actor, Guid.NewGuid());
      Assert.False(unknown.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, unknown.ErrorCode);
    }
  }
}

/// <summary>T059 completion: batched confirmation creation and the confirmation summary view.</summary>
[Trait("Profile", "Database")]
public sealed class AuditConfirmationBatchTests
{
  [Fact(DisplayName = "Confirmation batch creates the area register atomically and the summary reports it")]
  public async Task ConfirmationBatch_IsAtomicAndSummarised()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var program = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
        new PublishAuditProgramRequest("2026.4", AuditProgramCatalog.SourceHash));
      Assert.True((await AuditProgramService.AdoptAsync(db, scope.Actor,
        new AdoptAuditProgramRequest(scope.EngagementId, program.Value!.ProgramVersionId))).Succeeded);
      var procedure = await db.AuditProcedures.SingleAsync(x =>
        x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-03-04");
      Assert.True((await AuditProgramTestHelpers.MarkApplicableAsync(db, scope.Actor, procedure.Id, null)).Succeeded);

      // A duplicate source record rejects the whole batch: no partial register.
      var duplicate = await AuditConfirmationBatchService.CreateConfirmationBatchAsync(db, scope.Actor,
        new ConfirmationBatchRequest(scope.EngagementId, procedure.Id, AuditAreaCodes.Receivables, "QAR",
          new DateOnly(2026, 12, 31),
          [
            new("CUST-001", 500m, "Customer A finance lead", "Signed contract contact list reviewed by partner."),
            new("CUST-001", 300m, "Customer A finance lead", "Signed contract contact list reviewed by partner.")
          ]));
      Assert.False(duplicate.Succeeded);
      Assert.Empty(await db.AuditConfirmationCases.ToListAsync());

      // An invalid currency is refused before any insert.
      var badCurrency = await AuditConfirmationBatchService.CreateConfirmationBatchAsync(db, scope.Actor,
        new ConfirmationBatchRequest(scope.EngagementId, procedure.Id, AuditAreaCodes.Receivables, "Q1",
          new DateOnly(2026, 12, 31),
          [new("CUST-001", 500m, "Customer A finance lead", "Signed contract contact list.")]));
      Assert.False(badCurrency.Succeeded);

      var batch = await AuditConfirmationBatchService.CreateConfirmationBatchAsync(db, scope.Actor,
        new ConfirmationBatchRequest(scope.EngagementId, procedure.Id, AuditAreaCodes.Receivables, "QAR",
          new DateOnly(2026, 12, 31),
          [
            new("CUST-001", 500m, "Customer A finance lead", "Signed contract contact list reviewed by partner."),
            new("CUST-002", 300m, "Customer B finance lead", "Signed contract contact list reviewed by partner."),
            new("CUST-003", 150m, "Customer C finance lead", "Signed contract contact list reviewed by partner.")
          ]));
      Assert.True(batch.Succeeded, batch.Message);
      Assert.Equal(3, batch.Value!.CreatedCount);
      Assert.Equal(950m, batch.Value.TotalBookedAmount);
      Assert.Equal(AuditAreaCodes.Receivables, batch.Value.AreaCode);
      Assert.All(batch.Value.Cases, x => Assert.Equal(AuditConfirmationStatuses.Draft, x.Status));
      Assert.Equal(3, await db.AuditConfirmationCases.CountAsync());
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var summary = await AuditConfirmationBatchService.GetConfirmationSummaryAsync(db, scope.Actor, scope.EngagementId);
      Assert.True(summary.Succeeded, summary.Message);
      Assert.Equal(3, summary.Value!.TotalCases);
      Assert.Equal(950m, summary.Value.TotalBookedAmount);
      Assert.Equal("QAR", summary.Value.Currency);
      Assert.Equal(3, summary.Value.Draft);
      Assert.Equal(0, summary.Value.Dispatched);
      Assert.Equal(0, summary.Value.RecomputedDifferenceCases);
      Assert.All(summary.Value.Items, x => Assert.Equal("QAR", x.Currency));

      // The area filter narrows the register; an unknown area returns nothing rather than all.
      var filtered = await AuditConfirmationBatchService.GetConfirmationSummaryAsync(db, scope.Actor,
        scope.EngagementId, areaCode: AuditAreaCodes.Payables);
      Assert.True(filtered.Succeeded);
      Assert.Equal(0, filtered.Value!.TotalCases);
      var wrongArea = await AuditConfirmationBatchService.GetConfirmationSummaryAsync(db, scope.Actor,
        scope.EngagementId, areaCode: AuditAreaCodes.Receivables);
      Assert.Equal(3, wrongArea.Value!.TotalCases);
    }
  }
}
