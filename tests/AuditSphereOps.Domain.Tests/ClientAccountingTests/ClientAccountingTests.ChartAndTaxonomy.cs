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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WorkerHost = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task ChartVersionCreation_SerializesClientVersionAllocation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var pause = new PauseChartSave();
    var firstOptions = new DbContextOptionsBuilder<AuditSphereDbContext>()
      .UseNpgsql(pg.ConnectionString).AddInterceptors(pause).Options;
    await using var first = new AuditSphereDbContext(firstOptions);
    await using var second = new AuditSphereDbContext(pg.Options);
    var firstCreate = ClientAccountingService.CreateChartVersionAsync(first, preparer,
      scope.ClientA, "LEDGER-A", new DateOnly(2026, 1, 1));
    try
    {
      await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
      var secondCreate = ClientAccountingService.CreateChartVersionAsync(second, preparer,
        scope.ClientA, "LEDGER-B", new DateOnly(2027, 1, 1));
      await Task.Delay(200);
      Assert.False(secondCreate.IsCompleted);
      pause.Release();
      var firstResult = await firstCreate.WaitAsync(TimeSpan.FromSeconds(10));
      var secondResult = await secondCreate.WaitAsync(TimeSpan.FromSeconds(10));
      Assert.True(firstResult.Succeeded, firstResult.Message);
      Assert.True(secondResult.Succeeded, secondResult.Message);
      Assert.NotEqual(firstResult.Value, secondResult.Value);
    }
    finally
    {
      pause.Release();
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var versions = await verify.ClientChartVersions
      .Where(x => x.FirmId == scope.FirmId && x.ClientId == scope.ClientA)
      .OrderBy(x => x.Version).Select(x => x.Version).ToArrayAsync();
    Assert.Equal(new[] { 1, 2 }, versions);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ChartPublication_HoldsRevisionLockUntilDecisionIsSaved()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid chartId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      chartId = (await ClientAccountingService.CreateChartVersionAsync(setup, preparer,
        scope.ClientA, "LEDGER-A", new DateOnly(2026, 1, 1))).Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(setup, preparer, chartId,
        [new("cash", "1000", "Cash", "ASSET", "DEBIT", true)])).Succeeded);
    }

    var pause = new PauseChartSave();
    var firstOptions = new DbContextOptionsBuilder<AuditSphereDbContext>()
      .UseNpgsql(pg.ConnectionString).AddInterceptors(pause).Options;
    await using var first = new AuditSphereDbContext(firstOptions);
    await using var second = new AuditSphereDbContext(pg.Options);
    var firstDecision = ClientAccountingService.PublishChartVersionAsync(first, reviewer, chartId);
    try
    {
      await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
      var secondDecision = ClientAccountingService.PublishChartVersionAsync(second, reviewer, chartId);
      await Task.Delay(200);
      Assert.False(secondDecision.IsCompleted);
      pause.Release();
      Assert.True((await firstDecision.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded);
      var stale = await secondDecision.WaitAsync(TimeSpan.FromSeconds(10));
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, stale.ErrorCode);
    }
    finally
    {
      pause.Release();
    }
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("Profile", "Database")]
  public async Task ChartDraftWrite_HoldsRevisionLockUntilSave(bool addAlias)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid chartId, cashId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      chartId = (await ClientAccountingService.CreateChartVersionAsync(setup, preparer,
        scope.ClientA, "LEDGER-A", new DateOnly(2026, 1, 1))).Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(setup, preparer, chartId,
        [new("cash", "1000", "Cash", "ASSET", "DEBIT", true)])).Succeeded);
      cashId = await setup.ClientAccounts.Where(x => x.ChartVersionId == chartId)
        .Select(x => x.Id).SingleAsync();
    }

    var pause = new PauseChartSave();
    var writerOptions = new DbContextOptionsBuilder<AuditSphereDbContext>()
      .UseNpgsql(pg.ConnectionString).AddInterceptors(pause).Options;
    await using var writer = new AuditSphereDbContext(writerOptions);
    await using var publisher = new AuditSphereDbContext(pg.Options);
    var write = addAlias
      ? ClientAccountingService.AddSourceAccountAliasesAsync(writer, preparer, chartId,
        [new SourceAccountAliasInput(cashId, "LEDGER-A", "CASH_MAIN", "Main cash")])
      : ClientAccountingService.AddAccountsAsync(writer, preparer, chartId,
        [new("revenue", "4000", "Revenue", "INCOME", "CREDIT", true)]);
    try
    {
      await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
      var publish = ClientAccountingService.PublishChartVersionAsync(publisher, reviewer, chartId);
      await Task.Delay(200);
      Assert.False(publish.IsCompleted);
      pause.Release();
      Assert.True((await write.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded);
      Assert.True((await publish.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded);
    }
    finally
    {
      pause.Release();
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(AccountingWorkflowStates.Approved,
      (await verify.ClientChartVersions.SingleAsync(x => x.Id == chartId)).Status);
    if (addAlias)
      Assert.Equal(1, await verify.SourceAccountAliases.CountAsync(x => x.ChartVersionId == chartId));
    else
      Assert.Equal(2, await verify.ClientAccounts.CountAsync(x => x.ChartVersionId == chartId));
  }

  private sealed class PauseChartSave : SaveChangesInterceptor
  {
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => entered.Task;
    public void Release() => release.TrySetResult();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      entered.TrySetResult();
      await release.Task.WaitAsync(cancellationToken);
      return result;
    }
  }

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
            [new("J-1-L1", "1000", 100m, 0m, "USD", 27.027m, 100m), new("J-1-L2", "4000", 0m, 100m, "QAR", -100m, -100m)])]));
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
      var foreignLine = await db.GeneralLedgerLines.SingleAsync(x => x.ImportBatchId == batchId && x.StableLineId == "J-1-L1");
      Assert.Equal("USD", foreignLine.OriginalCurrency);
      Assert.Equal(27.027m, foreignLine.OriginalAmount);
      Assert.Equal(100m, foreignLine.FunctionalAmount);
    }

    Guid reconciliationId, transactionId, eclId, inventoryId, specialistId, analyticalId, riskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var wrongBook = await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, periodId, Guid.NewGuid(), "CASH", null, batchId, ["1000"],
          new DateOnly(2026, 12, 31)));
      Assert.False(wrongBook.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, wrongBook.ErrorCode);
      var otherPeriodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), "STATUTORY", "QAR"))).Value;
      var batch = await db.SourceImportBatches.SingleAsync(x => x.Id == batchId);
      batch.PeriodId = otherPeriodId;
      await db.SaveChangesAsync();
      var wrongPeriod = await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, periodId, bookId, "CASH", null, batchId, ["1000"],
          new DateOnly(2026, 12, 31)));
      Assert.False(wrongPeriod.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, wrongPeriod.ErrorCode);
      batch.PeriodId = periodId;
      await db.SaveChangesAsync();
      reconciliationId = (await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, periodId, bookId, "CASH", null, batchId, ["1000"],
          new DateOnly(2026, 12, 31)))).Value;
      var wrongCurrencyItem = await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, reconciliationId,
        [new("ITEM-USD", 10m, "USD", new DateOnly(2026, 12, 30), "timing", "receipt-usd", "OPEN")]);
      Assert.False(wrongCurrencyItem.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, wrongCurrencyItem.ErrorCode);
      var futureItem = await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, reconciliationId,
        [new("ITEM-FUTURE", 10m, "QAR", new DateOnly(2027, 1, 1), "timing", "receipt-future", "OPEN")]);
      Assert.False(futureItem.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, futureItem.ErrorCode);
      var missingDisposition = await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, reconciliationId,
        [new("ITEM-NO-DISPOSITION", 10m, "QAR", new DateOnly(2026, 12, 30), "timing", "receipt-open", " ")]);
      Assert.False(missingDisposition.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, missingDisposition.ErrorCode);
      var validItem = await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, reconciliationId,
        [new("ITEM-QAR", 10m, "qar", new DateOnly(2026, 12, 30), "timing", "receipt-qar", "OPEN")]);
      Assert.True(validItem.Succeeded, validItem.Message);
      var savedItem = await db.AccountingReconciliationItems.SingleAsync(x => x.ReconciliationId == reconciliationId);
      Assert.Equal("QAR", savedItem.Currency);
      Assert.Equal(1, savedItem.AgeDays);
      var offsetItem = await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, reconciliationId,
        [new("ITEM-QAR-OFFSET", -10m, "qar", new DateOnly(2026, 12, 30), "timing", "receipt-qar-offset", "OPEN")]);
      Assert.True(offsetItem.Succeeded, offsetItem.Message);
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
        new AnalyticalReviewRequest(scope.ClientA, scope.EngagementA, periodId, null, "REVENUE", "monthly", 120m, 100m, 110m, "prior-year-total", "analytics-v1", "Seasonal movement explained by signed contracts.", Currency: "", SeasonalityExplanation: "Signed contracts drive the seasonal movement."))).Value;
      var analytical = await db.AnalyticalReviews.SingleAsync(x => x.Id == analyticalId);
      Assert.Equal("QAR", analytical.Currency);
      Assert.Equal("SEASONAL_MOVEMENT", analytical.MovementFlags);
      Assert.NotEmpty(analytical.InputSnapshotJson);
      Assert.Equal(Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(analytical.InputSnapshotJson)), analytical.InputHash);
      var journalRiskCandidates = await AccountingAnalysisService.AnalyzeJournalRiskAsync(db, preparer,
        new JournalRiskAnalysisRequest(scope.ClientA, scope.EngagementA, batchId, new DateOnly(2026, 6, 30), 50m, 3));
      Assert.True(journalRiskCandidates.Succeeded, journalRiskCandidates.Message);
      Assert.Equal(2, journalRiskCandidates.Value!.Count);
      Assert.All(journalRiskCandidates.Value, candidate =>
      {
        Assert.Equal("journal-risk.v1", candidate.CriteriaVersion);
        Assert.True(candidate.SourceOriginAvailable);
        Assert.Equal(100m, candidate.AbsoluteAmount);
      });
      Assert.Contains(journalRiskCandidates.Value, candidate => candidate.RuleCode == "YEAR_END_ENTRY");
      Assert.Contains(journalRiskCandidates.Value, candidate => candidate.RuleCode == "HIGH_VALUE_ENTRY");
      riskId = (await AccountingAnalysisService.AddJournalRiskFlagAsync(db, preparer,
        new JournalRiskFlagRequest(scope.ClientA, scope.EngagementA, batchId, transactionId, "YEAR_END_MANUAL", "Manual year-end journal requires corroboration.", 75m, "journal-selection", SelectedForTesting: true, ManagementExplanation: "Management explained the year-end entry.", CorroborationReference: "bank-reconciliation-1"))).Value;
      var risk = await db.JournalRiskFlags.SingleAsync(x => x.Id == riskId);
      Assert.True(risk.SelectedForTesting);
      Assert.Equal("Management explained the year-end entry.", risk.ManagementExplanation);
      Assert.Equal("bank-reconciliation-1", risk.CorroborationReference);
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
      var proof = await AccountingAnalysisService.CalculateReconciliationProofAsync(db, preparer, reconciliationId);
      Assert.True(proof.Succeeded, proof.Message);
      Assert.True(proof.Value!.IsReconciled);
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
      var missingAnalyticalConclusion = await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Analytical, analyticalId, AccountingEvidenceReviewDecisions.Approved));
      Assert.False(missingAnalyticalConclusion.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, missingAnalyticalConclusion.ErrorCode);
      Assert.True((await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.Analytical, analyticalId, AccountingEvidenceReviewDecisions.Approved,
          Conclusion: "The seasonal revenue movement is supported by the retained query inputs and source evidence."))).Succeeded);
      Assert.Equal("The seasonal revenue movement is supported by the retained query inputs and source evidence.",
        await db.AnalyticalReviews.Where(x => x.Id == analyticalId).Select(x => x.ReviewConclusion).SingleAsync());
      Assert.True((await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db, reviewer,
        new ReviewAccountingEvidenceRequest(AccountingEvidenceKinds.JournalRisk, riskId, AccountingEvidenceReviewDecisions.Cleared, "Reviewed against the selected rule and source journal.", "Management response corroborated.", "bank-reconciliation-1"))).Succeeded);
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

      var nextClientChart = (await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientA, "LEDGER-A-NEXT", new DateOnly(2027, 1, 1))).Value;
      var crossChartParent = await ClientAccountingService.AddAccountsAsync(db, preparer, nextClientChart, [
        new("child-next", "1100", "Child", "ASSET", "DEBIT", true, "cash")
      ]);
      Assert.False(crossChartParent.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, crossChartParent.ErrorCode);
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, preparer, nextClientChart, [
        new("root-next", "1200", "Root", "ASSET", "DEBIT", false),
        new("child-next", "1300", "Child", "ASSET", "DEBIT", true, "root-next")
      ])).Succeeded);
      var postingParent = await ClientAccountingService.AddAccountsAsync(db, preparer, nextClientChart, [
        new("grandchild-next", "1400", "Grandchild", "ASSET", "DEBIT", true, "child-next")
      ]);
      Assert.False(postingParent.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, postingParent.ErrorCode);
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(2, await verify.ClientAccounts.CountAsync(x => x.AccountCode == "1000"));
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ChartValidation_RejectsDuplicateCodesCyclesPostingParentsAndInvalidDateRanges()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparer,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "LEDGER-A", "A-1"))).Succeeded);

    // Invalid date range on the period settings is rejected outright.
    var invalidPeriod = await ClientAccountingService.CreatePeriodAsync(db, preparer,
      new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1), "STATUTORY", "QAR"));
    Assert.False(invalidPeriod.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, invalidPeriod.ErrorCode);

    var periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
      new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"))).Value;
    Assert.True(periodId != Guid.Empty);
    var chartId = (await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientA, "LEDGER-A", new DateOnly(2026, 1, 1))).Value;

    // Duplicate account codes within one batch are rejected.
    var duplicates = await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
      new("dup-a", "1000", "Cash A", "ASSET", "DEBIT", true),
      new("dup-b", "1000", "Cash B", "ASSET", "DEBIT", true)
    ]);
    Assert.False(duplicates.Succeeded);
    Assert.Contains("unique within a chart version", duplicates.Message);

    // A posting account cannot be a chart parent.
    var postingParent = await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
      new("p", "1300", "Parent posting account", "ASSET", "DEBIT", true),
      new("c", "1400", "Child of a posting account", "ASSET", "DEBIT", true, "p")
    ]);
    Assert.False(postingParent.Succeeded);
    Assert.Contains("posting account cannot be a chart parent", postingParent.Message);

    // A parent cycle within one batch is rejected.
    var cycle = await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
      new("cyc-a", "1100", "Cycle A", "ASSET", "DEBIT", false, "cyc-b"),
      new("cyc-b", "1200", "Cycle B", "ASSET", "DEBIT", false, "cyc-a")
    ]);
    Assert.False(cycle.Succeeded);
    Assert.Contains("parent cycle", cycle.Message);

    // Valid accounts, then the same code again in a later batch is rejected.
    Assert.True((await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
      new("cash", "1000", "Cash", "ASSET", "DEBIT", true),
      new("control", "1500", "Control account", "ASSET", "DEBIT", false)
    ])).Succeeded);
    var existingCode = await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
      new("cash-two", "1000", "Second cash", "ASSET", "DEBIT", true)
    ]);
    Assert.False(existingCode.Succeeded);
    Assert.Equal(ErrorCodes.IdempotencyConflict, existingCode.ErrorCode);

    // Publishing freezes the version: revisions create new versions, never overwrites.
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Assert.True((await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId)).Succeeded);
    var mutatePublished = await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
      new("late", "1600", "Late account", "ASSET", "DEBIT", true)
    ]);
    Assert.False(mutatePublished.Succeeded);
    Assert.Equal(ErrorCodes.ProtectedState, mutatePublished.ErrorCode);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task TaxonomyNodes_AllowIncrementalParentsOnlyWithinVersion()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");

    await using var db = new AuditSphereDbContext(pg.Options);
    var taxonomyId = (await ClientAccountingService.CreateTaxonomyVersionAsync(db, reviewer,
      "TAX-2026", "IFRS", "Reporting taxonomy", new DateOnly(2026, 1, 1))).Value;
    Assert.True((await ClientAccountingService.AddTaxonomyNodesAsync(db, reviewer, taxonomyId,
      [new("ASSETS", "Assets", "SFP", "+", "DEBIT", "BALANCE_SHEET", true, "ALL")])).Succeeded);

    Assert.True((await ClientAccountingService.AddTaxonomyNodesAsync(db, reviewer, taxonomyId,
      [new("CASH", "Cash", "SFP", "+", "DEBIT", "CASH", true, "ALL", "ASSETS")])).Succeeded);
    var parentId = await db.ReportingTaxonomyNodes.Where(x => x.TaxonomyVersionId == taxonomyId && x.Code == "ASSETS").Select(x => x.Id).SingleAsync();
    Assert.Equal(parentId, await db.ReportingTaxonomyNodes.Where(x => x.TaxonomyVersionId == taxonomyId && x.Code == "CASH").Select(x => x.ParentNodeId).SingleAsync());

    var otherTaxonomyId = (await ClientAccountingService.CreateTaxonomyVersionAsync(db, reviewer,
      "TAX-2027", "IFRS", "Other taxonomy", new DateOnly(2027, 1, 1))).Value;
    var wrongVersion = await ClientAccountingService.AddTaxonomyNodesAsync(db, reviewer, otherTaxonomyId,
      [new("CASH", "Cash", "SFP", "+", "DEBIT", "CASH", true, "ALL", "ASSETS")]);
    Assert.False(wrongVersion.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, wrongVersion.ErrorCode);

    db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "AccountingReviewer"));
    await db.SaveChangesAsync();
    var preparerReviewer = Actor(scope.Preparer, "AccountingReviewer");
    Assert.True((await ClientAccountingService.PublishTaxonomyVersionAsync(db, preparerReviewer, taxonomyId)).Succeeded);
    var overlayId = (await ClientAccountingService.CreateTaxonomyOverlayAsync(db, reviewer,
      new TaxonomyOverlayRequest(taxonomyId, "TAX-2026-RETAIL", "Retail overlay", "INDUSTRY:RETAIL", new DateOnly(2026, 1, 1)))).Value;
    var clientScopedOverlay = await ClientAccountingService.CreateTaxonomyOverlayAsync(db, reviewer,
      new TaxonomyOverlayRequest(taxonomyId, "TAX-2026-CLIENT", "Client overlay", $"CLIENT:{scope.ClientA}", new DateOnly(2026, 1, 1)));
    Assert.False(clientScopedOverlay.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, clientScopedOverlay.ErrorCode);
    Assert.True((await ClientAccountingService.AddTaxonomyNodesAsync(db, reviewer, overlayId,
      [new("RETAIL_REVENUE", "Retail revenue", "SPL", "+", "CREDIT", "REVENUE", true, "INDUSTRY:RETAIL")])).Succeeded);
    var impact = await ClientAccountingService.GetTaxonomyPublishImpactAsync(db, reviewer, overlayId);
    Assert.True(impact.Succeeded, impact.Message);
    Assert.Empty(impact.Value!);
    Assert.True((await ClientAccountingService.PublishTaxonomyVersionAsync(db, preparerReviewer, overlayId)).Succeeded);
    var overlay = await db.ReportingTaxonomyVersions.SingleAsync(x => x.Id == overlayId);
    Assert.Equal(taxonomyId, overlay.BaseTaxonomyVersionId);
    Assert.Equal("INDUSTRY:RETAIL", overlay.OverlayScope);
  }
}
