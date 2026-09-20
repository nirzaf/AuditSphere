using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed class ClientAccountingTests
{
  private sealed record Scope(Guid FirmId, Guid ClientA, Guid ClientB, Guid EngagementA, Guid EngagementB,
    AppUser Preparer, AppUser Reviewer);

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ClientChartsPeriodsAndGlImport_AreTypedScopedAndClosedSafely()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparer,
        new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "LEDGER-A", "A-1"))).Succeeded);
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparer,
        new ClientAccountingProfileRequest(scope.ClientB, "QA", "QAR", 4, 1, "LEDGER-B", "B-1"))).Succeeded);
    }

    Guid periodId, chartId, openingBridgeId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var priorPeriodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2025", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), "STATUTORY", "QAR"))).Value;
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR", priorPeriodId))).Value;
      Assert.True((await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(scope.ClientA, periodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"))).Succeeded);
      openingBridgeId = (await ClientAccountingService.CreateOpeningBalanceBridgeAsync(db, preparer,
        new OpeningBalanceBridgeRequest(scope.ClientA, periodId, priorPeriodId, null, new string('f', 64), 100m, 100m, "signed-closing-2025"))).Value;
      chartId = (await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientA, "LEDGER-A", new DateOnly(2026, 1, 1))).Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
        new("cash", "1000", "Cash", "ASSET", "DEBIT", true),
        new("revenue", "4000", "Revenue", "INCOME", "CREDIT", true)
      ])).Succeeded);
      var cashId = await db.ClientAccounts.Where(x => x.ChartVersionId == chartId && x.AccountCode == "1000").Select(x => x.Id).SingleAsync();
      Assert.True((await ClientAccountingService.AddSourceAccountAliasesAsync(db, preparer, chartId,
        [new SourceAccountAliasInput(cashId, "LEDGER-A", "CASH_MAIN", "Main cash")])).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await ClientAccountingService.ApproveOpeningBalanceBridgeAsync(db, reviewer, openingBridgeId)).Succeeded);
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId)).Succeeded);

    var rawHash = new string('a', 64);
    Guid batchId, bookId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      bookId = await db.ClientReportingBooks.Where(x => x.ClientId == scope.ClientA && x.PeriodId == periodId)
        .Select(x => x.Id).SingleAsync();
      var imported = await ClientAccountingService.ImportGeneralLedgerAsync(db, preparer,
        new GeneralLedgerImportRequest(scope.ClientA, scope.EngagementA, periodId, bookId, "csv-v1", "gl-v1", rawHash,
          "CLIENT-A", "QAR", "receipt-a", [new("J-1", "INV-1", new DateOnly(2026, 6, 30), null, "user-a", "LEDGER-A", null, false, false,
            [new("J-1-L1", "1000", 100m, 0m, "QAR", 100m, 100m), new("J-1-L2", "4000", 0m, 100m, "QAR", -100m, -100m)])]));
      Assert.True(imported.Succeeded);
      batchId = imported.Value;
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var batch = await db.SourceImportBatches.SingleAsync(x => x.Id == batchId);
      Assert.Equal("SEALED", batch.Status);
      Assert.Equal(rawHash, batch.RawFileSha256Hex);
      Assert.NotEqual(batch.RawFileSha256Hex, batch.NormalizedDatasetDigest);
      Assert.Equal(2, await db.GeneralLedgerLines.CountAsync(x => x.ImportBatchId == batchId));
      Assert.Equal(1, await db.GeneralLedgerTransactions.CountAsync(x => x.ImportBatchId == batchId));
    }

    Guid reconciliationId, transactionId, eclId, inventoryId, specialistId, analyticalId, riskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var wrongBook = await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, periodId, Guid.NewGuid(), "CASH", null, batchId, ["1000"],
          new DateOnly(2026, 12, 31)));
      Assert.False(wrongBook.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, wrongBook.ErrorCode);
      reconciliationId = (await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, periodId, bookId, "CASH", null, batchId, ["1000"],
          new DateOnly(2026, 12, 31)))).Value;
      transactionId = await db.GeneralLedgerTransactions.Where(x => x.ImportBatchId == batchId).Select(x => x.Id).SingleAsync();
      eclId = (await AccountingAnalysisService.CreateEclAssessmentAsync(db, preparer,
        new EclAssessmentRequest(reconciliationId, new DateOnly(2026, 12, 31), "PROVISION_MATRIX_V1", "ecl-v1", .1m, .5m, 2m, 7m, new string('c', 64)))).Value;
      inventoryId = (await AccountingAnalysisService.CreateInventoryValuationAsync(db, preparer,
        new InventoryValuationRequest(reconciliationId, new DateOnly(2026, 12, 31), 10m, 12m, 11m, 1m, 100m, "inventory-v1", new string('d', 64)))).Value;
      specialistId = (await AccountingAnalysisService.RecordSpecialistScheduleAsync(db, preparer,
        new SpecialistScheduleRequest(scope.ClientA, scope.EngagementA, periodId, "ASSETS", "asset-v1",
          OpeningAmount: 100m, AdditionsAmount: 20m, DisposalsAmount: 0m, DepreciationAmount: 10m,
          ImpairmentAmount: 0m, InterestAmount: 0m, CurrentPortion: 0m, NonCurrentPortion: 0m,
          CapitalMovement: 0m, Dividends: 0m, TaxPaid: 0m, ManagementAmount: 110m,
          AssumptionsHash: new string('e', 64), EvidenceReference: "asset-register",
          DepreciationMethod: "STRAIGHT_LINE", UsefulLifeMonths: 120))).Value;
      analyticalId = (await AccountingAnalysisService.CreateAnalyticalReviewAsync(db, preparer,
        new AnalyticalReviewRequest(scope.ClientA, scope.EngagementA, periodId, null, "REVENUE", "monthly", 120m, 100m, 110m, "prior-year-total", "analytics-v1", "Seasonal movement explained by signed contracts."))).Value;
      riskId = (await AccountingAnalysisService.AddJournalRiskFlagAsync(db, preparer,
        new JournalRiskFlagRequest(scope.ClientA, scope.EngagementA, batchId, transactionId, "YEAR_END_MANUAL", "Manual year-end journal requires corroboration.", 75m, "journal-selection"))).Value;
      var auditResultId = await AddReviewedAccountingProcedureResultAsync(db, scope, "MAIN", scope.Preparer.Id, scope.Reviewer.Id);
      foreach (var link in new[]
      {
        (AccountingEvidenceKinds.Ecl, eclId), (AccountingEvidenceKinds.Inventory, inventoryId),
        (AccountingEvidenceKinds.Specialist, specialistId), (AccountingEvidenceKinds.Analytical, analyticalId),
        (AccountingEvidenceKinds.JournalRisk, riskId)
      })
      {
        var linked = await AccountingAnalysisService.LinkAccountingEvidenceToProcedureAsync(db, preparer,
          new LinkAccountingEvidenceRequest(link.Item1, link.Item2, auditResultId));
        Assert.True(linked.Succeeded, linked.Message);
      }
      var approvedReconciliation = await AccountingAnalysisService.ApproveReconciliationAsync(db, reviewer, reconciliationId);
      Assert.True(approvedReconciliation.Succeeded, approvedReconciliation.Message);
      var blockedClose = await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "Premature close");
      Assert.False(blockedClose.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blockedClose.ErrorCode);
      Assert.True((await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Ecl, eclId, AccountingEvidenceReviewDecisions.Approved))).Succeeded);
      Assert.True((await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Inventory, inventoryId, AccountingEvidenceReviewDecisions.Approved))).Succeeded);
      Assert.True((await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Specialist, specialistId, AccountingEvidenceReviewDecisions.Approved))).Succeeded);
      Assert.True((await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Analytical, analyticalId, AccountingEvidenceReviewDecisions.Approved))).Succeeded);
      Assert.True((await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.JournalRisk, riskId, AccountingEvidenceReviewDecisions.Cleared, "Reviewed against the selected rule and source journal."))).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var close = await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "All reconciliations approved");
      Assert.True(close.Succeeded, close.Message);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var blocked = await ClientAccountingService.ImportGeneralLedgerAsync(db, preparer,
        new GeneralLedgerImportRequest(scope.ClientA, scope.EngagementA, periodId, null, "csv-v1", "gl-v1", new string('b', 64),
          "CLIENT-A", "QAR", "receipt-b", [new("J-2", "INV-2", new DateOnly(2026, 7, 1), null, "user-a", "LEDGER-A", null, false, false,
            [new("J-2-L1", "1000", 10m, 0m, "QAR", 10m, 10m), new("J-2-L2", "4000", 0m, 10m, "QAR", -10m, -10m)])]));
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, blocked.ErrorCode);
    }

    // The same source code can exist independently in an unrelated client chart.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var otherChart = (await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientB, "LEDGER-B", new DateOnly(2026, 4, 1))).Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, preparer, otherChart, [
        new("cash-b", "1000", "Cash B", "ASSET", "DEBIT", true)
      ])).Succeeded);
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(2, await verify.ClientAccounts.CountAsync(x => x.AccountCode == "1000"));
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
      db.AccountingReconciliations.Add(new AccountingReconciliation
      {
        Id = reconciliationId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PeriodId = fixture.PeriodId, BookId = fixture.BookId, Area = "RECEIVABLES", AccountSelection = "1000",
        AsOfDate = new DateOnly(2026, 12, 31), SourceTotal = 100m, GlTotal = 100m, Residual = 0m,
        SourceHash = sourceHash, Status = "RECONCILED", InputGeneration = 1,
        CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      eclId = (await AccountingAnalysisService.CreateEclAssessmentAsync(db, preparer,
        new EclAssessmentRequest(reconciliationId, new DateOnly(2026, 12, 31), "PROVISION_MATRIX_V1", "ecl-v1", .1m, .5m, 2m, 7m, new string('c', 64)))).Value;
      inventoryId = (await AccountingAnalysisService.CreateInventoryValuationAsync(db, preparer,
        new InventoryValuationRequest(reconciliationId, new DateOnly(2026, 12, 31), 10m, 12m, 11m, 1m, 100m, "inventory-v1", new string('d', 64)))).Value;

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

  [Fact]
  [Trait("Profile", "Database")]
  public async Task StreamingGlImport_IsIdempotentAndSealsOnlyCompleteBatch()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var (periodId, bookId) = await CreateGlFixtureAsync(pg, scope, preparer);
    var chunkOne = new[]
    {
      new GeneralLedgerTransactionInput("J-1", "INV-1", new DateOnly(2026, 6, 30), null, "user-a", "LEDGER-A", null, false, false,
        [new("J-1-L1", "1000", 100m, 0m, "QAR", 100m, 100m), new("J-1-L2", "4000", 0m, 100m, "QAR", -100m, -100m)])
    };
    var chunkTwo = new[]
    {
      new GeneralLedgerTransactionInput("J-2", "INV-2", new DateOnly(2026, 7, 1), null, "user-a", "LEDGER-A", null, false, false,
        [new("J-2-L1", "1000", 25m, 0m, "QAR", 25m, 25m), new("J-2-L2", "4000", 0m, 25m, "QAR", -25m, -25m)])
    };
    var digestOne = ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", chunkOne);
    var digestTwo = ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", chunkTwo);
    Guid batchId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var started = await ClientAccountingService.BeginGeneralLedgerImportAsync(db, preparer,
        new GeneralLedgerImportStartRequest(scope.ClientA, scope.EngagementA, periodId, bookId, "stream-v1", "gl-v1",
          new string('9', 64), "CLIENT-A", "QAR", "stream-receipt", 2, 2, 4));
      Assert.True(started.Succeeded, started.Message);
      batchId = started.Value;

      var first = await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, preparer,
        new GeneralLedgerImportChunkRequest(batchId, 0, digestOne, chunkOne, Finalize: false));
      Assert.True(first.Succeeded, first.Message);
      Assert.Equal("LOADING", first.Value!.Status);
      Assert.Equal(1, first.Value.AcceptedChunkCount);

      var retry = await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, preparer,
        new GeneralLedgerImportChunkRequest(batchId, 0, digestOne, chunkOne, Finalize: false));
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(1, retry.Value!.AcceptedChunkCount);

      var premature = await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, preparer,
        new GeneralLedgerImportChunkRequest(batchId, 0, digestOne, chunkOne, Finalize: true));
      Assert.False(premature.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, premature.ErrorCode);

      var final = await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, preparer,
        new GeneralLedgerImportChunkRequest(batchId, 1, digestTwo, chunkTwo, Finalize: true));
      Assert.True(final.Succeeded, final.Message);
      Assert.Equal("SEALED", final.Value!.Status);
      Assert.Equal(2, final.Value.AcceptedChunkCount);
      Assert.Equal(2, final.Value.AcceptedTransactionCount);
      Assert.Equal(4, final.Value.AcceptedLineCount);
      Assert.NotNull(final.Value.NormalizedDatasetDigest);
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal("SEALED", await verify.SourceImportBatches.Where(x => x.Id == batchId).Select(x => x.Status).SingleAsync());
    Assert.Equal(2, await verify.GeneralLedgerImportChunks.CountAsync(x => x.ImportBatchId == batchId));
    Assert.Equal(2, await verify.GeneralLedgerTransactions.CountAsync(x => x.ImportBatchId == batchId));
    Assert.Equal(4, await verify.GeneralLedgerLines.CountAsync(x => x.ImportBatchId == batchId));
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GlCompletenessBridge_IsAccountExactAndPagedWithinScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var digest = Hashing.Sha256Hex("completeness-fixture");
    Guid periodId, bookId, datasetId, batchId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      Assert.True((await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(scope.ClientA, periodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"))).Succeeded);
      bookId = await db.ClientReportingBooks.Where(x => x.ClientId == scope.ClientA && x.PeriodId == periodId).Select(x => x.Id).SingleAsync();
      datasetId = Guid.CreateVersion7();
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        SourceKind = "Raw", LegalEntityKey = "CLIENT-A", Currency = "QAR", RawFileSha256Hex = digest,
        NormalizedDatasetDigest = digest, Sha256Hex = digest, Balanced = true, ValidationStatus = "Pending",
        ImportState = TrialBalanceImportStates.Loading, ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = scope.Preparer.Id
      });
      db.TrialBalanceRows.AddRange(
        new TrialBalanceRow { Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = "1000", AccountName = "Cash", Amount = 100m, Currency = "QAR", Entity = "CLIENT-A" },
        new TrialBalanceRow { Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = "4000", AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = "CLIENT-A" });
      batchId = Guid.CreateVersion7();
      db.SourceImportBatches.Add(new SourceImportBatch
      {
        Id = batchId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA, PeriodId = periodId,
        BookId = bookId, SourceKind = "GL", ProfileVersion = "gl-v1", ParserVersion = "parser-v1", RawFileSha256Hex = digest,
        NormalizedDatasetDigest = digest, LegalEntityKey = "CLIENT-A", Currency = "QAR", RowCount = 1,
        Status = "SEALED", ReceiptReference = "gl-receipt", CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      var transactionId = Guid.CreateVersion7();
      db.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
      {
        Id = transactionId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        ImportBatchId = batchId, StableJournalId = "J-1", DocumentNumber = "DOC-1", PostingDate = new DateOnly(2026, 6, 30),
        SourceUser = "user-a", SourceSystem = "LEDGER-A", Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
      });
      db.GeneralLedgerLines.AddRange(
        new GeneralLedgerLine { Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA, ImportBatchId = batchId, TransactionId = transactionId, StableLineId = "J-1-L1", AccountCode = "1000", Debit = 100m, OriginalCurrency = "QAR", OriginalAmount = 100m, FunctionalAmount = 100m, CreatedAt = DateTimeOffset.UtcNow },
        new GeneralLedgerLine { Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA, ImportBatchId = batchId, TransactionId = transactionId, StableLineId = "J-1-L2", AccountCode = "4000", Credit = 100m, OriginalCurrency = "QAR", OriginalAmount = -100m, FunctionalAmount = -100m, CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      dataset.ValidationStatus = "Accepted";
      dataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await AccountingAnalysisService.CreateGeneralLedgerCompletenessBridgeAsync(db, preparer,
        new GeneralLedgerCompletenessRequest(scope.ClientA, scope.EngagementA, periodId, bookId, datasetId, batchId, "tb-gl-completeness"));
      Assert.True(created.Succeeded, created.Message);
      var bridge = await db.GeneralLedgerCompletenessBridges.SingleAsync(x => x.Id == created.Value);
      Assert.Equal("RECONCILED", bridge.Status);
      Assert.Equal(2, bridge.MatchedAccountCount);
      Assert.Equal(0m, bridge.AbsoluteResidual);

      var firstPage = await AccountingAnalysisService.GetGeneralLedgerPageAsync(db, preparer, batchId, 1, 1);
      Assert.True(firstPage.Succeeded, firstPage.Message);
      Assert.Single(firstPage.Value!.Rows);
      Assert.True(firstPage.Value.HasNextPage);
      var secondPage = await AccountingAnalysisService.GetGeneralLedgerPageAsync(db, preparer, batchId, 2, 1);
      Assert.True(secondPage.Succeeded, secondPage.Message);
      Assert.Single(secondPage.Value!.Rows);
      Assert.False(secondPage.Value.HasNextPage);

      Assert.True((await AccountingAnalysisService.ReviewGeneralLedgerCompletenessAsync(db, reviewer, bridge.Id, approve: true)).Succeeded);
      Assert.Equal(AccountingWorkflowStates.Approved, await db.GeneralLedgerCompletenessBridges.Where(x => x.Id == bridge.Id).Select(x => x.Status).SingleAsync());
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ClosedPeriodRestatement_PreservesIssuedPackagesAndRequiresIndependentApproval()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid periodId, originalPackageId, revisedPackageId, restatementId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      Assert.True((await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "Issued package")).Succeeded);
      originalPackageId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "original");
      revisedPackageId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 90m, "CASH", "revised");
      db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "AccountingReviewer"));
      await db.SaveChangesAsync();

      var created = await ClientAccountingService.CreatePeriodRestatementAsync(db, preparer,
        new CreatePeriodRestatementRequest(scope.ClientA, periodId, originalPackageId, revisedPackageId,
          "IAS 8", "Prior-period error identified", "restatement-evidence"));
      Assert.True(created.Succeeded, created.Message);

      var selfApproval = await ClientAccountingService.ApprovePeriodRestatementAsync(db, preparer, created.Value);
      Assert.False(selfApproval.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, selfApproval.ErrorCode);

      var approved = await ClientAccountingService.ApprovePeriodRestatementAsync(db, reviewer, created.Value);
      Assert.True(approved.Succeeded, approved.Message);
      var restatement = await db.ClientPeriodRestatements.SingleAsync(x => x.Id == created.Value);
      restatementId = restatement.Id;
      Assert.Equal(AccountingWorkflowStates.Approved, restatement.Status);
      Assert.Equal(scope.Reviewer.Id, restatement.ApprovedByUserId);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(2, await verify.FinancialPackages.CountAsync(x => x.ClientId == scope.ClientA));
    Assert.Equal(AccountingPackageStates.PackageValidated,
      await verify.FinancialPackages.Where(x => x.Id == originalPackageId).Select(x => x.Status).SingleAsync());
    Assert.Equal(AccountingPackageStates.PackageValidated,
      await verify.FinancialPackages.Where(x => x.Id == revisedPackageId).Select(x => x.Status).SingleAsync());
    var tampered = "tampered";
    var mutation = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE client_period_restatements SET reason = {tampered} WHERE id = {restatementId}"));
    Assert.Equal("55000", mutation.SqlState);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task PeriodReopen_RecordsImmutableRevisionLineage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var partner = Actor(scope.Reviewer, "Partner");
    Guid periodId, amendmentId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      Assert.True((await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "Initial close")).Succeeded);
      Assert.True((await ClientAccountingService.ReopenPeriodAsync(db, partner, periodId, "Correct the approved opening bridge")).Succeeded);

      var period = await db.ClientReportingPeriods.SingleAsync(x => x.Id == periodId);
      Assert.Equal(AccountingWorkflowStates.Draft, period.Status);
      Assert.Equal(3, period.Revision);
      var first = await db.ClientPeriodAmendments.SingleAsync(x => x.PeriodId == periodId);
      Assert.Equal(2, first.PreviousRevision);
      Assert.Equal(3, first.AmendmentRevision);
      Assert.Equal("Correct the approved opening bridge", first.Reason);
      amendmentId = first.Id;

      Assert.True((await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "Corrected close")).Succeeded);
      Assert.True((await ClientAccountingService.ReopenPeriodAsync(db, partner, periodId, "Record the final correction")).Succeeded);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var amendments = await verify.ClientPeriodAmendments.Where(x => x.PeriodId == periodId)
      .OrderBy(x => x.AmendmentRevision).ToListAsync();
    Assert.Equal(2, amendments.Count);
    Assert.Equal((2L, 3L), (amendments[0].PreviousRevision, amendments[0].AmendmentRevision));
    Assert.Equal((4L, 5L), (amendments[1].PreviousRevision, amendments[1].AmendmentRevision));
    var mutation = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE client_period_amendments SET reason = {"tampered"} WHERE id = {amendmentId}"));
    Assert.Equal("55000", mutation.SqlState);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task PeriodClose_RequiresCurrentReviewsForMatchingFinancialPackages()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var partner = Actor(scope.Reviewer, "Partner");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      var packageId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "period-close-gate");
      await db.SaveChangesAsync();

      var blocked = await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "Package approvals pending");
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);

      Assert.True((await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "management-close-gate", "Management approved the exact package."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "accounting-close-gate", "Accounting review completed."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db, partner,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "partner-close-gate", "Partner approval completed."))).Succeeded);

      var closed = await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "All package approvals complete");
      Assert.True(closed.Succeeded, closed.Message);
      Assert.Equal(AccountingWorkflowStates.Closed,
        await db.ClientReportingPeriods.Where(x => x.Id == periodId).Select(x => x.Status).SingleAsync());
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task PeriodRollForward_CopiesDraftBooksAndRequiresOpeningEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid priorPeriodId, nextPeriodId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      priorPeriodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2025", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), "STATUTORY", "QAR"))).Value;
      Assert.True((await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(scope.ClientA, priorPeriodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"))).Succeeded);
      Assert.True((await ClientAccountingService.ClosePeriodAsync(db, reviewer, priorPeriodId, "Prior period issued")).Succeeded);

      var rolled = await ClientAccountingService.RollForwardPeriodAsync(db, preparer,
        new RollForwardPeriodRequest(scope.ClientA, priorPeriodId, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
          "STATUTORY", "QAR", new string('f', 64), 100m, 100m, "signed-prior-closing"));
      Assert.True(rolled.Succeeded, rolled.Message);
      nextPeriodId = rolled.Value;

      var next = await db.ClientReportingPeriods.SingleAsync(x => x.Id == nextPeriodId);
      Assert.Equal(priorPeriodId, next.PriorPeriodId);
      Assert.Equal(AccountingWorkflowStates.Draft, next.Status);
      Assert.Equal(1, next.Revision);
      var book = await db.ClientReportingBooks.SingleAsync(x => x.PeriodId == nextPeriodId);
      Assert.Equal("STAT", book.Code);
      Assert.Equal(AccountingWorkflowStates.Draft, book.Status);
      var bridge = await db.OpeningBalanceBridges.SingleAsync(x => x.CurrentPeriodId == nextPeriodId);
      Assert.Equal("RECONCILED", bridge.Status);
      Assert.Null(bridge.ApprovedByUserId);

      var duplicate = await ClientAccountingService.RollForwardPeriodAsync(db, preparer,
        new RollForwardPeriodRequest(scope.ClientA, priorPeriodId, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
          "STATUTORY", "QAR", new string('f', 64), 100m, 100m, "signed-prior-closing"));
      Assert.False(duplicate.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicate.ErrorCode);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(1, await verify.ClientReportingPeriods.CountAsync(x => x.PriorPeriodId == priorPeriodId));
    Assert.Equal(1, await verify.OpeningBalanceBridges.CountAsync(x => x.CurrentPeriodId == nextPeriodId));
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task FinancialPackageReviews_AreStageBoundAndImmutable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var partner = Actor(scope.Reviewer, "Partner");
    Guid packageId, managementDecisionId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      packageId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "review");
      await db.SaveChangesAsync();

      var management = await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "signed-management-approval", "Management approval received offline."));
      Assert.True(management.Succeeded, management.Message);
      managementDecisionId = management.Value;

      var accounting = await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "accounting-review-session", "Tie-outs and validations reviewed."));
      Assert.True(accounting.Succeeded, accounting.Message);

      var current = await FinancialPackageReviewService.RequireCurrentAsync(db, partner, packageId, requirePartner: false);
      Assert.True(current.Succeeded, current.Message);
      var missingPartner = await FinancialPackageReviewService.RequireCurrentAsync(db, partner, packageId, requirePartner: true);
      Assert.False(missingPartner.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, missingPartner.ErrorCode);

      var queue = await FinancialPackageReviewService.GetStaffQueueAsync(db, partner);
      Assert.True(queue.Succeeded, queue.Message);
      var queuedPackage = Assert.Single(queue.Value!);
      Assert.Equal(packageId, queuedPackage.PackageId);
      Assert.Equal(FinancialPackageReviewStages.PartnerApproval, queuedPackage.NextAction);

      var duplicate = await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "signed-management-approval", "Management approval received offline."));
      Assert.False(duplicate.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicate.ErrorCode);

      var partnerApproval = await FinancialPackageReviewService.RecordAsync(db, partner,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "partner-review-session", "Partner approval recorded."));
      Assert.True(partnerApproval.Succeeded, partnerApproval.Message);
      var complete = await FinancialPackageReviewService.RequireCurrentAsync(db, partner, packageId, requirePartner: true);
      Assert.True(complete.Succeeded, complete.Message);
      var emptyQueue = await FinancialPackageReviewService.GetStaffQueueAsync(db, partner);
      Assert.True(emptyQueue.Succeeded, emptyQueue.Message);
      Assert.Empty(emptyQueue.Value!);

      var reviews = await FinancialPackageReviewService.GetAsync(db, partner, packageId);
      Assert.True(reviews.Succeeded, reviews.Message);
      Assert.Equal(3, reviews.Value!.Count);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var mutation = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE financial_package_review_decisions SET comment = {"tampered"} WHERE id = {managementDecisionId}"));
    Assert.Equal("55000", mutation.SqlState);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task FinancialPackageRelease_BindsCandidateToCurrentPackageReviews()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var partner = Actor(scope.Reviewer, "Partner");
    var manifestBytes = System.Text.Encoding.UTF8.GetBytes("financial-package-release-manifest");
    var manifest = Hashing.Sha256Hex(manifestBytes);
    Guid packageId, candidateId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      packageId = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "release");
      await db.SaveChangesAsync();

      Assert.True((await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "management-package-approval", "Management approved the exact package."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "accounting-package-review", "Accounting review completed."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db, partner,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "partner-package-review", "Partner approval completed."))).Succeeded);

      var approval = await ApprovalService.CreateAsync(db, partner,
        new CreateApprovalRequest(ReleaseTargetKinds.FinancialPackage, packageId, 1, 1, 1, manifest));
      Assert.True(approval.Succeeded, approval.Message);

      var candidate = await ReleaseService.CreateCandidateAsync(db, partner,
        new CreateReleaseCandidateRequest(approval.Value, ReleaseTargetKinds.FinancialPackage,
          packageId, 1, 1, 1, manifest));
      Assert.True(candidate.Succeeded, candidate.Message);
      candidateId = candidate.Value;

      var checkpointStore = new LocalAppendOnlyCheckpointStore(
        Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N")));
      var checkpoint = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, checkpointStore, partner,
        new RecordReleaseCheckpointRequest(candidateId, 1, "financial-package-release-001", manifest, manifestBytes));
      Assert.True(checkpoint.Succeeded, checkpoint.Message);

      var issued = await ReleaseService.IssueAsync(db, partner,
        new IssueReleaseRequest(candidateId, 1, manifest, "financial-package-release-001"));
      Assert.True(issued.Succeeded, $"{issued.ErrorCode}: {issued.Message}");
      Assert.Equal(packageId, await db.Releases.Where(x => x.Id == issued.Value).Select(x => x.PackageId).SingleAsync());
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var candidateRow = await verify.ReleaseCandidates.SingleAsync(x => x.Id == candidateId);
    Assert.Equal(ReleaseTargetKinds.FinancialPackage, candidateRow.TargetKind);
    Assert.Equal(ReleaseStates.Issued, candidateRow.Status);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ClientPackageView_IsScopedAndSupportsSignedInManagementDecision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var clientUser = User(scope.FirmId, "ClientApprover");
    clientUser.UserKind = "Client";
    Guid packageA, packageB;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      packageA = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "client-view-a");
      packageB = await AddPackageAsync(db, scope, scope.ClientB, scope.EngagementB, 200m, "CASH", "client-view-b");
      db.Users.Add(clientUser);
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, UserId = clientUser.Id,
        Role = "ClientUser", ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Preparer.Id
      });
      await db.SaveChangesAsync();
    }

    var actor = Actor(clientUser, "ClientUser");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var view = await FinancialPackageReviewService.GetClientViewAsync(db, actor, packageA);
      Assert.True(view.Succeeded, view.Message);
      Assert.Equal(packageA, view.Value!.PackageId);
      Assert.Equal("PENDING", view.Value.ManagementDecision);
      Assert.Contains(view.Value.StatementTotals, x => x.StatementSection == "STATEMENT" && x.Amount == 100m);

      var decision = await FinancialPackageReviewService.RecordAsync(db, actor,
        new FinancialPackageReviewRequest(packageA, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "client-portal-ack-1", "Management reviewed the supplied package."));
      Assert.True(decision.Succeeded, decision.Message);

      var approved = await FinancialPackageReviewService.GetClientViewAsync(db, actor, packageA);
      Assert.True(approved.Succeeded, approved.Message);
      Assert.Equal(FinancialPackageReviewDecisions.Approved, approved.Value!.ManagementDecision);

      var internalReviews = await FinancialPackageReviewService.GetAsync(db, actor, packageA);
      Assert.False(internalReviews.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, internalReviews.ErrorCode);

      var otherClient = await FinancialPackageReviewService.GetClientViewAsync(db, actor, packageB);
      Assert.False(otherClient.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, otherClient.ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void RestrictedConsolidation_IsDeterministicAndFailsClosed()
  {
    var components = new[]
    {
      new ConsolidationComponentBalance(Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), "CASH", 100m, "QAR", 100m, "CONTROLLED", Hashing.Sha256Hex("component-a")),
      new ConsolidationComponentBalance(Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.NewGuid(), "REVENUE", -100m, "QAR", 100m, "CONTROLLED", Hashing.Sha256Hex("component-b"))
    };
    var first = ConsolidationCalculator.Compute("QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026", components, []);
    var second = ConsolidationCalculator.Compute("QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026", components, []);
    Assert.Equal(first.RunHash, second.RunHash);
    Assert.Equal(0m, first.SignedTotal);
    Assert.Equal(first.Lines.Select(x => x.ConsolidatedAmount), new[] { 100m, -100m });
    Assert.Throws<InvalidOperationException>(() => ConsolidationCalculator.Compute(
      "QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026",
      [components[0] with { OwnershipPercent = 80m }, components[1]], []));
    Assert.Throws<InvalidOperationException>(() => ConsolidationCalculator.Compute(
      "QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026",
      [components[0], components[1]], [new ConsolidationElimination(Guid.NewGuid(), "CASH", 1m, "USD")]));
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void CurrencyTranslation_RequiresPositiveApprovedRate()
  {
    Assert.Equal(125m, CurrencyTranslationCalculator.Translate(100m, "USD", "QAR", 1.25m));
    Assert.Throws<InvalidOperationException>(() => CurrencyTranslationCalculator.Translate(100m, "USD", "QAR", 0m));
    Assert.Throws<InvalidOperationException>(() => CurrencyTranslationCalculator.Translate(100m, "QAR", "QAR", 1.25m));
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GroupWorkflow_UsesApprovedComponentPackagesWithoutMutatingThem()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    Guid groupId, consolidationScopeId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer, new ClientGroupRequest("GROUP-A", "Group A"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientA, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m, "ownership-a"))).Succeeded);
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientB, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m, "ownership-b"))).Succeeded);
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = scope.Preparer.Id,
        Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = scope.Preparer.Id,
        Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id
      });
      await db.SaveChangesAsync();
      consolidationScopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026"))).Value;
      await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH");
      await AddPackageAsync(db, scope, scope.ClientB, scope.EngagementB, -100m, "REVENUE");
      await db.SaveChangesAsync();
    }

    Guid packageA, packageB;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      packageA = await db.FinancialPackages.Where(x => x.ClientId == scope.ClientA).Select(x => x.Id).SingleAsync();
      packageB = await db.FinancialPackages.Where(x => x.ClientId == scope.ClientB).Select(x => x.Id).SingleAsync();
      Assert.True((await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientA, scope.EngagementA, packageA, 100m, "CONTROLLED", "STATUTORY", "tax-v1", "map-a"))).Succeeded);
      Assert.True((await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientB, scope.EngagementB, packageB, 100m, "CONTROLLED", "STATUTORY", "tax-v1", "map-b"))).Succeeded);
      var components = await db.ConsolidationComponents.Where(x => x.ScopeVersionId == consolidationScopeId).Select(x => x.Id).ToListAsync();
      var missingPackageReview = await ConsolidationService.ApproveComponentAsync(db, reviewer, components[0]);
      Assert.False(missingPackageReview.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, missingPackageReview.ErrorCode);
      foreach (var packageId in new[] { packageA, packageB })
      {
        Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
          new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
            FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
            "management-review-fixture", "Management approval fixture."))).Succeeded);
        Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
          new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
            FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
            "accounting-review-fixture", "Accounting review fixture."))).Succeeded);
        Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
          new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
            FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
            "partner-review-fixture", "Partner approval fixture."))).Succeeded);
      }
      foreach (var componentId in components)
        Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId)).Succeeded);
      var missingCapability = await ConsolidationService.ApproveScopeAsync(db, reviewer, consolidationScopeId);
      Assert.False(missingCapability.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, missingCapability.ErrorCode);
      var capabilityId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, groupId, "GROUP_REPORTING", "IFRS", "2026", "ANNUAL", "QAR",
          "STATUTORY", ConsolidationCalculator.RestrictedMethod, "PARTNER", "GROUP"))).Value;
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, capabilityId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "local-consolidation-profile")).Succeeded);
      var selfApproval = await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, capabilityId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "self-approval-must-fail");
      Assert.False(selfApproval.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, selfApproval.ErrorCode);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, Actor(scope.Preparer, "Partner"), capabilityId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "method-owner-approval-fixture")).Succeeded);
      Assert.True((await ConsolidationService.ApproveScopeAsync(db, reviewer, consolidationScopeId)).Succeeded);
    }

    Guid journalId, runId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      journalId = (await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(consolidationScopeId, "GC-001", "GROUP_RECLASSIFICATION", "QAR", "group-adjustment-001",
        [new(null, "CASH", 100m, 0m, "Group-only cash reclassification"),
         new(null, "REVENUE", 0m, 100m, "Group-only revenue reclassification")]))).Value;
      Assert.True((await ConsolidationService.ApproveConsolidationJournalAsync(db, reviewer, journalId)).Succeeded);
      runId = (await ConsolidationService.RunAsync(db, preparer, consolidationScopeId)).Value;
      var changedJournalId = (await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(consolidationScopeId, "GC-002", "GROUP_RECLASSIFICATION", "QAR", "group-adjustment-002",
        [new(null, "CASH", 50m, 0m, "Post-run group-only cash reclassification"),
         new(null, "REVENUE", 0m, 50m, "Post-run group-only revenue reclassification")]))).Value;
      Assert.True((await ConsolidationService.ApproveConsolidationJournalAsync(db, reviewer, changedJournalId)).Succeeded);
      var stale = await ConsolidationService.ApproveRunAsync(db, reviewer, runId);
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, stale.ErrorCode);
      var rebuiltRunId = (await ConsolidationService.RunAsync(db, preparer, consolidationScopeId)).Value;
      Assert.NotEqual(runId, rebuiltRunId);
      Assert.True((await ConsolidationService.ApproveRunAsync(db, reviewer, rebuiltRunId)).Succeeded);
      Assert.Equal(4, await db.ConsolidationRunLines.CountAsync(x => x.RunId == runId));
      Assert.Equal(2, await db.ConsolidationRunLines.CountAsync(x => x.RunId == runId && x.ConsolidationJournalId == journalId));
      Assert.Equal(6, await db.ConsolidationRunLines.CountAsync(x => x.RunId == rebuiltRunId));
      Assert.Equal(2, await db.ConsolidationRunLines.CountAsync(x => x.RunId == rebuiltRunId && x.ConsolidationJournalId == changedJournalId));
      Assert.All(await db.ConsolidationComponents.Where(x => x.ScopeVersionId == consolidationScopeId).ToListAsync(), x => Assert.Equal(AccountingWorkflowStates.Approved, x.Status));
    }
  }

  private static async Task<Scope> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientA = Guid.NewGuid();
    var clientB = Guid.NewGuid();
    var engagementA = Guid.NewGuid();
    var engagementB = Guid.NewGuid();
    var preparer = User(firmId, "preparer");
    var reviewer = User(firmId, "reviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.AddRange(
      new PracticeClient { Id = clientA, FirmId = firmId, LegalName = "CLIENT A", CreatedAt = DateTimeOffset.UtcNow },
      new PracticeClient { Id = clientB, FirmId = firmId, LegalName = "CLIENT B", CreatedAt = DateTimeOffset.UtcNow });
    db.Engagements.AddRange(
      new Engagement { Id = engagementA, FirmId = firmId, PracticeClientId = clientA, ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow },
      new Engagement { Id = engagementB, FirmId = firmId, PracticeClientId = clientB, ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.AddRange(new ClientSafetyState { Id = clientA, FirmId = firmId }, new ClientSafetyState { Id = clientB, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"), Grant(firmId, reviewer, "AccountingReviewer"),
      Grant(firmId, reviewer, "Partner"));
    await db.SaveChangesAsync();
    return new Scope(firmId, clientA, clientB, engagementA, engagementB, preparer, reviewer);
  }

  private static async Task<(Guid PeriodId, Guid BookId)> CreateGlFixtureAsync(
    PgTestSchema pg, Scope scope, ActorContext preparer)
  {
    Guid periodId, chartId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparer,
        new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "LEDGER-A", "A-1"))).Succeeded);
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      var book = await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(scope.ClientA, periodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"));
      Assert.True(book.Succeeded, book.Message);
      chartId = (await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientA, "LEDGER-A", new DateOnly(2026, 1, 1))).Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
        new("cash", "1000", "Cash", "ASSET", "DEBIT", true),
        new("revenue", "4000", "Revenue", "INCOME", "CREDIT", true)
      ])).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, Actor(scope.Reviewer, "AccountingReviewer"), chartId)).Succeeded);
    await using var verify = new AuditSphereDbContext(pg.Options);
    var bookId = await verify.ClientReportingBooks.Where(x => x.ClientId == scope.ClientA && x.PeriodId == periodId)
      .Select(x => x.Id).SingleAsync();
    return (periodId, bookId);
  }

  private static async Task<Guid> AddPackageAsync(AuditSphereDbContext db, Scope scope, Guid clientId, Guid engagementId, decimal amount, string destination, string? suffix = null)
  {
    var now = DateTimeOffset.UtcNow;
    var datasetId = Guid.CreateVersion7();
    var planId = Guid.CreateVersion7();
    var mappingId = Guid.CreateVersion7();
    var adjustedId = Guid.CreateVersion7();
    var packageId = Guid.CreateVersion7();
    var digest = Hashing.Sha256Hex($"package-{clientId:D}-{suffix ?? destination}");
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, SourceKind = "Raw",
      Revision = 1, LegalEntityKey = clientId.ToString("D"), Currency = "QAR", RawFileSha256Hex = digest,
      NormalizedDatasetDigest = digest, Sha256Hex = digest, Balanced = true, ValidationStatus = "Accepted",
      ImportState = TrialBalanceImportStates.Sealed, ControlTotal = 0m, ImportedAt = now, ImportedByUserId = scope.Preparer.Id
    });
    db.AdjustmentPlans.Add(new AdjustmentPlan
    {
      Id = planId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
      Status = "Finalized", ResultHash = digest, CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, DatasetId = datasetId,
      Version = 1, Generation = 1, TaxonomyVersion = "tax-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      Status = AccountingPackageStates.MappingApproved, CreatedByUserId = scope.Preparer.Id, ApprovedByUserId = scope.Reviewer.Id,
      ApprovedAt = now, CreatedAt = now
    });
    db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
    {
      Id = adjustedId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
      AdjustmentPlanId = planId, Currency = "QAR", ResultHash = digest, CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });
    db.FinancialPackages.Add(new FinancialPackage
    {
      Id = packageId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, AdjustedDatasetId = adjustedId,
      MappingVersionId = mappingId, AdjustmentPlanId = planId, Framework = "IFRS", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      TaxonomyVersion = "tax-v1", TemplateVersion = "template-v1", CalculationEngineVersion = "test-engine",
      CalculationHash = digest, Currency = "QAR", Status = AccountingPackageStates.PackageValidated, CreatedAt = now
    });
    db.FinancialPackageLines.Add(new FinancialPackageLine
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, FinancialPackageId = packageId,
      SourceAccountCode = destination == "CASH" ? "1000" : "4000", DestinationCode = destination, StatementSection = "STATEMENT",
      Amount = amount, Fraction = 1m, Currency = "QAR", AdjustedSnapshotId = adjustedId, CreatedAt = now
    });
    return packageId;
  }

  private static async Task<Guid> AddReviewedAccountingProcedureResultAsync(
    AuditSphereDbContext db, Scope scope, string suffix, Guid preparerId, Guid reviewerId,
    Guid? clientId = null, Guid? engagementId = null)
  {
    var client = clientId ?? scope.ClientA;
    var engagement = engagementId ?? scope.EngagementA;
    var now = DateTimeOffset.UtcNow;
    var procedureId = Guid.CreateVersion7();
    var workpaperId = Guid.CreateVersion7();
    var resultId = Guid.CreateVersion7();
    var sourceProcedureId = "ACCT-" + suffix;
    db.AuditProcedures.Add(new AuditProcedure
    {
      Id = procedureId, FirmId = scope.FirmId, ClientId = client, EngagementId = engagement,
      SourceProcedureId = sourceProcedureId, SourceSectionNumber = 1, SourceSectionTitle = "Accounting evidence",
      SourceWording = "Review the accounting evidence.", ApplicabilityStatus = AuditApplicabilityStatuses.Applicable,
      CurrentResultRevision = 1, Title = "Accounting evidence review", Status = AuditProcedureStatuses.Reviewed, CreatedAt = now
    });
    db.Workpapers.Add(new Workpaper
    {
      Id = workpaperId, FirmId = scope.FirmId, ClientId = client, EngagementId = engagement, ProcedureId = procedureId,
      ActorId = preparerId, Index = sourceProcedureId, Title = "Accounting evidence review", Objective = "Support accounting evidence",
      TemplateVersion = "accounting-fixture-v1", Procedure = "Review the accounting evidence.", WorkPerformed = "Reviewed the supplied accounting evidence.",
      Conclusion = "No exception noted.", Revision = 1, Status = WorkpaperStatuses.SubmittedSnapshot, SubmittedAt = now, CreatedAt = now
    });
    db.WorkpaperSubmissions.Add(new WorkpaperSubmission
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = client, EngagementId = engagement,
      WorkpaperId = workpaperId, ActorId = preparerId, Revision = 1,
      WorkPerformed = "Reviewed the supplied accounting evidence.", Conclusion = "No exception noted.", SubmittedAt = now
    });
    db.AuditProcedureResults.Add(new AuditProcedureResult
    {
      Id = resultId, FirmId = scope.FirmId, ClientId = client, EngagementId = engagement, AuditProcedureId = procedureId,
      WorkpaperId = workpaperId, Revision = 1, InputGeneration = 1, WorkPerformed = "Reviewed the supplied accounting evidence.",
      StructuredResultJson = "{\"result\":\"PASS\"}", EvidenceReferencesJson = "[\"accounting-fixture\"]",
      Conclusion = "No exception noted.", Status = AuditProcedureResultStatuses.Reviewed, PreparedByUserId = preparerId,
      ReviewedByUserId = reviewerId, ReviewComment = "Evidence and conclusion agree.", SubmittedAt = now, ReviewedAt = now
    });
    db.AuditProcedureReviews.Add(new AuditProcedureReview
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = client, EngagementId = engagement,
      AuditProcedureResultId = resultId, AuditProcedureId = procedureId, ResultRevision = 1,
      Decision = AuditProcedureReviewDecisions.Reviewed, Comment = "Evidence and conclusion agree.", ReviewerUserId = reviewerId, CreatedAt = now
    });
    await db.SaveChangesAsync();
    return resultId;
  }

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = "sub-" + name + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = name + "@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, string role) => new(user.Id, user.FirmId, user.SessionEpoch, [role]);
}
