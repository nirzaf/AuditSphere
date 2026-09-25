using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WorkerHost = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task AnalyticalAggregate_RequiresClientOrGroupScopeAndOmitsComponentIds()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    var periodA = Guid.CreateVersion7();
    var periodB = Guid.CreateVersion7();
    Guid groupId;
    var limited = User(scope.FirmId, "limited");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.ClientReportingPeriods.AddRange(
        new ClientReportingPeriod
        {
          Id = periodA, FirmId = scope.FirmId, ClientId = scope.ClientA, PeriodCode = "2026-A",
          StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Basis = "STATUTORY", Currency = "QAR",
          CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
        },
        new ClientReportingPeriod
        {
          Id = periodB, FirmId = scope.FirmId, ClientId = scope.ClientB, PeriodCode = "2026-B",
          StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Basis = "STATUTORY", Currency = "QAR",
          CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
        });
      db.Users.Add(limited);
      var limitedGrant = Grant(scope.FirmId, limited, "AccountingPreparer");
      limitedGrant.ClientId = scope.ClientA;
      db.RoleGrants.Add(limitedGrant);
      await db.SaveChangesAsync();

      Assert.True((await AccountingAnalysisService.CreateAnalyticalReviewAsync(db, preparer,
        new AnalyticalReviewRequest(scope.ClientA, scope.EngagementA, periodA, null, "REVENUE", "monthly",
          120m, 100m, null, "prior-year-total", "analytics-v1", "Client A movement explained."))).Succeeded);
      Assert.True((await AccountingAnalysisService.CreateAnalyticalReviewAsync(db, preparer,
        new AnalyticalReviewRequest(scope.ClientB, scope.EngagementB, periodB, null, "REVENUE", "monthly",
          180m, 150m, null, "prior-year-total", "analytics-v1", "Client B movement explained."))).Succeeded);

      var all = await AccountingAnalysisService.GetAnalyticalReviewAggregateAsync(db, preparer);
      Assert.True(all.Succeeded, all.Message);
      var allRows = all.Value!;
      Assert.Equal(300m, allRows.Sum(x => x.CurrentTotal));
      Assert.Equal(2, allRows.Sum(x => x.ClientCount));

      var clientOnly = await AccountingAnalysisService.GetAnalyticalReviewAggregateAsync(db,
        new ActorContext(scope.Preparer.Id, scope.FirmId, scope.Preparer.SessionEpoch, ["AccountingPreparer"], scope.ClientA));
      Assert.True(clientOnly.Succeeded, clientOnly.Message);
      var clientRows = clientOnly.Value!;
      Assert.Single(clientRows);
      Assert.Equal(120m, clientRows[0].CurrentTotal);

      var denied = await AccountingAnalysisService.GetAnalyticalReviewAggregateAsync(db,
        new ActorContext(limited.Id, scope.FirmId, limited.SessionEpoch, ["AccountingPreparer"], scope.ClientB));
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);

      groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
        new ClientGroupRequest("AGG-1", "Aggregate test group"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientA, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m, "group-a"))).Succeeded);
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientB, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m, "group-b"))).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var groupAggregate = await AccountingAnalysisService.GetAnalyticalReviewAggregateAsync(db, reviewer, groupId);
      Assert.True(groupAggregate.Succeeded, groupAggregate.Message);
      var groupRows = groupAggregate.Value!;
      Assert.Equal(300m, groupRows.Sum(x => x.CurrentTotal));
      Assert.All(groupRows, x => Assert.Equal(groupId, x.GroupId));
      var serialized = System.Text.Json.JsonSerializer.Serialize(groupRows);
      Assert.DoesNotContain(scope.ClientA.ToString("D"), serialized, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain(scope.ClientB.ToString("D"), serialized, StringComparison.OrdinalIgnoreCase);

      var noGroupGrant = await AccountingAnalysisService.GetAnalyticalReviewAggregateAsync(db, preparer, groupId);
      Assert.False(noGroupGrant.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, noGroupGrant.ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ValuationEvidence_BlocksWhenClientGenerationChanges()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var fixture = await CreateGlFixtureAsync(pg, scope, preparer);
    var sourceHash = new string('a', 64);
    Guid reconciliationId, eclId, inventoryId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      reconciliationId = Guid.CreateVersion7();
      var datasetId = Guid.CreateVersion7();
      var proposedJournalId = Guid.CreateVersion7();
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PeriodId = fixture.PeriodId, BookId = fixture.BookId, Basis = "STATUTORY", SourceKind = "Raw",
        LegalEntityKey = "CLIENT-A", Currency = "QAR", RawFileSha256Hex = sourceHash,
        NormalizedDatasetDigest = sourceHash, Sha256Hex = sourceHash, Balanced = true,
        ValidationStatus = "Pending", ImportState = TrialBalanceImportStates.Loading,
        ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = scope.Preparer.Id
      });
      db.AdjustmentJournals.Add(new AdjustmentJournal
      {
        Id = proposedJournalId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        BaseDatasetId = datasetId, JournalNumber = "AJ-EVIDENCE-001", Purpose = AdjustmentJournalPurposes.ReportingAdjustment,
        PeriodId = fixture.PeriodId, BookId = fixture.BookId, Basis = "STATUTORY", Currency = "QAR",
        Origin = AdjustmentJournalOrigins.AuditProposed, Reason = "Valuation difference", EvidenceReference = "valuation-test",
        Status = "Draft", CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.AccountingReconciliations.Add(new AccountingReconciliation
      {
        Id = reconciliationId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PeriodId = fixture.PeriodId, BookId = fixture.BookId, TrialBalanceDatasetId = datasetId, Area = "RECEIVABLES", AccountSelection = "1000",
        AsOfDate = new DateOnly(2026, 12, 31), SourceTotal = 100m, GlTotal = 100m, Residual = 0m,
        SourceHash = sourceHash, Status = "RECONCILED", InputGeneration = 1,
        CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      eclId = (await AccountingAnalysisService.CreateEclAssessmentAsync(db, preparer,
        new EclAssessmentRequest(reconciliationId, new DateOnly(2026, 12, 31), "PROVISION_MATRIX_V1", "ecl-v1", .1m, .5m, 2m, 7m, new string('c', 64), BookedAmount: 8m, ProposedJournalId: proposedJournalId))).Value;
      inventoryId = (await AccountingAnalysisService.CreateInventoryValuationAsync(db, preparer,
        new InventoryValuationRequest(reconciliationId, new DateOnly(2026, 12, 31), 10m, 12m, 11m, 1m, 100m, "inventory-v1", new string('d', 64), proposedJournalId))).Value;

      var ecl = await db.EclAssessments.SingleAsync(x => x.Id == eclId);
      Assert.Equal(8m, ecl.BookedAmount);
      Assert.Equal(-1m, ecl.Difference);
      Assert.Equal(proposedJournalId, ecl.ProposedJournalId);
      var inventory = await db.InventoryValuationAssessments.SingleAsync(x => x.Id == inventoryId);
      Assert.Equal(9m, inventory.Difference);
      Assert.Equal(proposedJournalId, inventory.ProposedJournalId);

      var clientState = await db.ClientSafetyStates.SingleAsync(x => x.FirmId == scope.FirmId && x.Id == scope.ClientA);
      clientState.InputGeneration++;
      await db.SaveChangesAsync();

      var eclReview = await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Ecl, eclId, AccountingEvidenceReviewDecisions.Approved));
      Assert.False(eclReview.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, eclReview.ErrorCode);

      var inventoryReview = await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Inventory, inventoryId, AccountingEvidenceReviewDecisions.Approved));
      Assert.False(inventoryReview.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, inventoryReview.ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task EnabledValuationProfiles_MatchGoldenFixturesAndRejectUnsupportedBoundaries()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var fixture = await CreateGlFixtureAsync(pg, scope, preparer);
    var sourceHash = new string('g', 64);
    var reconciliationId = Guid.CreateVersion7();

    await using var db = new AuditSphereDbContext(pg.Options);
    db.AccountingReconciliations.Add(new AccountingReconciliation
    {
      Id = reconciliationId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
      PeriodId = fixture.PeriodId, BookId = fixture.BookId, Area = "RECEIVABLES", AccountSelection = "1100",
      AsOfDate = new DateOnly(2026, 12, 31), SourceTotal = 100m, GlTotal = 100m, Residual = 0m,
      SourceHash = sourceHash, Status = "RECONCILED", InputGeneration = 1,
      CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var ecl = await AccountingAnalysisService.CreateEclAssessmentAsync(db, preparer,
      new EclAssessmentRequest(reconciliationId, new DateOnly(2026, 12, 31), "PROVISION_MATRIX_V1", "ecl-golden-v1",
        .1m, .5m, 2m, 7m, new string('a', 64), BookedAmount: 8m));
    Assert.True(ecl.Succeeded, ecl.Message);
    var savedEcl = await db.EclAssessments.SingleAsync(x => x.Id == ecl.Value);
    Assert.Equal(100m, savedEcl.EligibleExposure);
    Assert.Equal(7m, savedEcl.CalculatedExpectedLoss);
    Assert.Equal(-1m, savedEcl.Difference);

    var zeroEcl = await AccountingAnalysisService.CreateEclAssessmentAsync(db, preparer,
      new EclAssessmentRequest(reconciliationId, new DateOnly(2026, 12, 31), "PROVISION_MATRIX_V1", "ecl-boundary-v1",
        0m, 1m, 0m, 0m, new string('b', 64), BookedAmount: 0m));
    Assert.True(zeroEcl.Succeeded, zeroEcl.Message);
    Assert.Equal(0m, await db.EclAssessments.Where(x => x.Id == zeroEcl.Value).Select(x => x.CalculatedExpectedLoss).SingleAsync());

    var unsupportedEcl = await AccountingAnalysisService.CreateEclAssessmentAsync(db, preparer,
      new EclAssessmentRequest(reconciliationId, new DateOnly(2026, 12, 31), "DEFAULT_PERCENTAGE", "ecl-unsupported-v1",
        .1m, .5m, 0m, 0m, new string('c', 64)));
    Assert.False(unsupportedEcl.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, unsupportedEcl.ErrorCode);

    var inventory = await AccountingAnalysisService.CreateInventoryValuationAsync(db, preparer,
      new InventoryValuationRequest(reconciliationId, new DateOnly(2026, 12, 31), 10m, 12m, 11m, 1m, 100m,
        "inventory-golden-v1", new string('d', 64)));
    Assert.True(inventory.Succeeded, inventory.Message);
    var savedInventory = await db.InventoryValuationAssessments.SingleAsync(x => x.Id == inventory.Value);
    Assert.Equal(109m, savedInventory.CalculatedAmount);
    Assert.Equal(9m, savedInventory.Difference);

    var zeroInventory = await AccountingAnalysisService.CreateInventoryValuationAsync(db, preparer,
      new InventoryValuationRequest(reconciliationId, new DateOnly(2026, 12, 31), 0m, 12m, 11m, 0m, 0m,
        "inventory-boundary-v1", new string('e', 64)));
    Assert.True(zeroInventory.Succeeded, zeroInventory.Message);
    Assert.Equal(0m, await db.InventoryValuationAssessments.Where(x => x.Id == zeroInventory.Value).Select(x => x.CalculatedAmount).SingleAsync());

    var negativeInventory = await AccountingAnalysisService.CreateInventoryValuationAsync(db, preparer,
      new InventoryValuationRequest(reconciliationId, new DateOnly(2026, 12, 31), -1m, 12m, 11m, 0m, 0m,
        "inventory-negative-v1", new string('f', 64)));
    Assert.False(negativeInventory.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, negativeInventory.ErrorCode);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task SpecialistAndAnalyticalEvidence_BlocksWhenClientGenerationChanges()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var fixture = await CreateGlFixtureAsync(pg, scope, preparer);
    Guid specialistId, analyticalId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var incompleteAsset = await AccountingAnalysisService.RecordSpecialistScheduleAsync(db, preparer,
        new SpecialistScheduleRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, "ASSETS", "asset-v1",
          OpeningAmount: 100m, AdditionsAmount: 20m, DisposalsAmount: 0m, DepreciationAmount: 10m,
          ImpairmentAmount: 0m, InterestAmount: 0m, CurrentPortion: 0m, NonCurrentPortion: 0m,
          CapitalMovement: 0m, Dividends: 0m, TaxPaid: 0m, ManagementAmount: 110m,
          AssumptionsHash: new string('e', 64), EvidenceReference: "asset-register"));
      Assert.False(incompleteAsset.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, incompleteAsset.ErrorCode);

      specialistId = (await AccountingAnalysisService.RecordSpecialistScheduleAsync(db, preparer,
        new SpecialistScheduleRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, "ASSETS", "asset-v1",
          OpeningAmount: 100m, AdditionsAmount: 20m, DisposalsAmount: 0m, DepreciationAmount: 10m,
          ImpairmentAmount: 0m, InterestAmount: 0m, CurrentPortion: 0m, NonCurrentPortion: 0m,
          CapitalMovement: 0m, Dividends: 0m, TaxPaid: 0m, ManagementAmount: 110m,
          AssumptionsHash: new string('e', 64), EvidenceReference: "asset-register",
          DepreciationMethod: "STRAIGHT_LINE", UsefulLifeMonths: 120))).Value;
      analyticalId = (await AccountingAnalysisService.CreateAnalyticalReviewAsync(db, preparer,
        new AnalyticalReviewRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, null, "REVENUE", "monthly",
          120m, 100m, 110m, "prior-year-total", "analytics-v1", "Seasonal movement explained by signed contracts."))).Value;
      var schedule = await db.SpecialistAccountingSchedules.SingleAsync(x => x.Id == specialistId);
      Assert.Equal("STRAIGHT_LINE", schedule.DepreciationMethod);
      Assert.Equal(120, schedule.UsefulLifeMonths);
      Assert.Equal(110m, schedule.ClosingAmount);

      var clientState = await db.ClientSafetyStates.SingleAsync(x => x.FirmId == scope.FirmId && x.Id == scope.ClientA);
      clientState.InputGeneration++;
      await db.SaveChangesAsync();

      var specialistReview = await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Specialist, specialistId, AccountingEvidenceReviewDecisions.Approved));
      Assert.False(specialistReview.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, specialistReview.ErrorCode);

      var analyticalReview = await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Analytical, analyticalId, AccountingEvidenceReviewDecisions.Approved));
      Assert.False(analyticalReview.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, analyticalReview.ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task SpecialistAreaSchedules_RetainTypedInputsAndRequireReviewEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var fixture = await CreateGlFixtureAsync(pg, scope, preparer);
    var assumptions = new string('a', 64);
    var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      ids["PAYROLL"] = (await AccountingAnalysisService.RecordSpecialistScheduleAsync(db, preparer,
        new SpecialistScheduleRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, "PAYROLL", "payroll-v1",
          0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 7000m, assumptions, "payroll-sample",
          PayrollGrossAmount: 10000m, PayrollDeductionsAmount: 3000m, PayrollNetAmount: 7000m,
          PayrollContractReference: "contract-001", PayrollBankPaymentReference: "bank-payment-001"))).Value;
      ids["LOANS"] = (await AccountingAnalysisService.RecordSpecialistScheduleAsync(db, preparer,
        new SpecialistScheduleRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, "LOANS", "loan-v1",
          1000m, 200m, 0m, 0m, 0m, 50m, 700m, 500m, 0m, 0m, 0m, 1200m, assumptions, "loan-schedule",
          LoanRepaymentAmount: 50m, LoanMaturityDate: new DateOnly(2028, 12, 31), LoanCovenantReference: "covenant-001"))).Value;
      ids["EQUITY"] = (await AccountingAnalysisService.RecordSpecialistScheduleAsync(db, preparer,
        new SpecialistScheduleRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, "EQUITY", "equity-v1",
          1000m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 100m, 25m, 0m, 1285m, assumptions, "equity-rollforward",
          EquityProfitOrLossAmount: 200m, EquityOciAmount: 10m, RelatedPartyDisclosureReference: "related-party-note-001"))).Value;
      ids["RELATED_PARTIES"] = (await AccountingAnalysisService.RecordSpecialistScheduleAsync(db, preparer,
        new SpecialistScheduleRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, "RELATED_PARTIES", "related-party-v1",
          10m, 5m, 2m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 13m, assumptions, "related-party-register",
          RelatedPartyDisclosureReference: "related-party-note-002"))).Value;
      ids["TAX"] = (await AccountingAnalysisService.RecordSpecialistScheduleAsync(db, preparer,
        new SpecialistScheduleRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, "TAX", "tax-v1",
          0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 200m, assumptions, "tax-schedule",
          TaxJurisdiction: "QA", TaxRuleVersion: "qa-cit-v1", TaxBaseAmount: 1000m, TaxRate: .2m,
          TaxReturnEvidenceReference: "return-001", TaxPaymentEvidenceReference: "payment-001",
          TaxCorrespondenceReference: "correspondence-001"))).Value;
      ids["FORECAST"] = (await AccountingAnalysisService.RecordSpecialistScheduleAsync(db, preparer,
        new SpecialistScheduleRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, "FORECAST", "forecast-v1",
          0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 400m, assumptions, "forecast-pack",
          ForecastOwner: "management", ForecastHorizonEnd: new DateOnly(2027, 12, 31),
          ForecastCashInputAmount: 1000m, ForecastDebtInputAmount: 600m,
          ForecastSensitivityReference: "sensitivity-001", ForecastSensitivityResult: "Headroom remains positive."))).Value;

      Assert.Equal(7000m, await db.SpecialistAccountingSchedules.Where(x => x.Id == ids["PAYROLL"]).Select(x => x.ClosingAmount).SingleAsync());
      Assert.Equal(1200m, await db.SpecialistAccountingSchedules.Where(x => x.Id == ids["LOANS"]).Select(x => x.ClosingAmount).SingleAsync());
      Assert.Equal(1285m, await db.SpecialistAccountingSchedules.Where(x => x.Id == ids["EQUITY"]).Select(x => x.ClosingAmount).SingleAsync());
      Assert.Equal(200m, await db.SpecialistAccountingSchedules.Where(x => x.Id == ids["TAX"]).Select(x => x.ClosingAmount).SingleAsync());
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var auditResultId = await AddReviewedAccountingProcedureResultAsync(db, scope, "TYPED", scope.Preparer.Id, scope.Reviewer.Id);
      foreach (var id in ids.Values)
      {
        var linked = await AccountingAnalysisService.LinkAccountingEvidenceToProcedureAsync(db, preparer,
          new LinkAccountingEvidenceRequest(AccountingEvidenceKinds.Specialist, id, auditResultId));
        Assert.True(linked.Succeeded, linked.Message);
      }
      foreach (var id in ids.Where(x => x.Key != "FORECAST").Select(x => x.Value))
      {
        var review = await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
          new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Specialist, id, AccountingEvidenceReviewDecisions.Approved));
        Assert.True(review.Succeeded, review.Message);
      }
      var forecastReview = await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Specialist, ids["FORECAST"], AccountingEvidenceReviewDecisions.Approved,
          Conclusion: "Management forecast has positive liquidity headroom under the supplied sensitivity."));
      Assert.True(forecastReview.Succeeded, forecastReview.Message);
      var forecast = await db.SpecialistAccountingSchedules.SingleAsync(x => x.Id == ids["FORECAST"]);
      Assert.Equal(AccountingEvidenceReviewDecisions.Approved, forecast.Status);
      Assert.Contains("positive liquidity", forecast.ReviewConclusion, StringComparison.OrdinalIgnoreCase);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AccountingEvidenceApproval_RequiresReviewedProcedureLinkAndPreservesScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var fixture = await CreateGlFixtureAsync(pg, scope, preparer);
    Guid scheduleId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      scheduleId = (await AccountingAnalysisService.RecordSpecialistScheduleAsync(db, preparer,
        new SpecialistScheduleRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, "ASSETS", "asset-v1",
          100m, 20m, 0m, 10m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 110m, new string('a', 64), "asset-register",
          DepreciationMethod: "STRAIGHT_LINE", UsefulLifeMonths: 120))).Value;
      var blocked = await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Specialist, scheduleId, AccountingEvidenceReviewDecisions.Approved));
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);

      var resultId = await AddReviewedAccountingProcedureResultAsync(db, scope, "LINK", scope.Preparer.Id, scope.Reviewer.Id);
      var linked = await AccountingAnalysisService.LinkAccountingEvidenceToProcedureAsync(db, preparer,
        new LinkAccountingEvidenceRequest(AccountingEvidenceKinds.Specialist, scheduleId, resultId));
      Assert.True(linked.Succeeded, linked.Message);
      Assert.True((await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Specialist, scheduleId, AccountingEvidenceReviewDecisions.Approved))).Succeeded);

      var foreignResultId = await AddReviewedAccountingProcedureResultAsync(db, scope, "FOREIGN", scope.Preparer.Id, scope.Reviewer.Id,
        scope.ClientB, scope.EngagementB);
      var wrongScope = await AccountingAnalysisService.LinkAccountingEvidenceToProcedureAsync(db, preparer,
        new LinkAccountingEvidenceRequest(AccountingEvidenceKinds.Specialist, scheduleId, foreignResultId));
      Assert.False(wrongScope.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, wrongScope.ErrorCode);
      Assert.Equal(1, await db.AccountingEvidenceAuditLinks.CountAsync(x => x.EvidenceId == scheduleId));
    }
  }
}
