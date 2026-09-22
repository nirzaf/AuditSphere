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
  public async Task OwnershipInterest_RejectsDuplicateAndCircularHierarchy()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var reviewer = Actor(scope.Reviewer, "Partner");
    Guid scopeVersionId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
        new ClientGroupRequest("OWNERSHIP-1", "Ownership test group"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientA, new DateOnly(2026, 1, 1), null,
          "CONTROLLED", 100m, 100m, "ownership-a"))).Succeeded);
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientB, new DateOnly(2026, 1, 1), null,
          "CONTROLLED", 100m, 100m, "ownership-b"))).Succeeded);
      scopeVersionId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", ConsolidationCalculator.RestrictedMethod,
          "OPENING-OWNERSHIP-2026"))).Value;

      var first = await ConsolidationService.AddOwnershipInterestAsync(db, reviewer,
        new OwnershipInterestRequest(scopeVersionId, scope.ClientA, scope.ClientB,
          new DateOnly(2026, 1, 1), null, 100m, 100m, "CONTROLLED", "DIRECT", "ownership-edge-a-b"));
      Assert.True(first.Succeeded, first.Message);

      var duplicate = await ConsolidationService.AddOwnershipInterestAsync(db, reviewer,
        new OwnershipInterestRequest(scopeVersionId, scope.ClientA, scope.ClientB,
          new DateOnly(2026, 1, 1), null, 100m, 100m, "CONTROLLED", "DIRECT", "ownership-edge-duplicate"));
      Assert.False(duplicate.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicate.ErrorCode);

      var cycle = await ConsolidationService.AddOwnershipInterestAsync(db, reviewer,
        new OwnershipInterestRequest(scopeVersionId, scope.ClientB, scope.ClientA,
          new DateOnly(2026, 1, 1), null, 100m, 100m, "CONTROLLED", "DIRECT", "ownership-edge-b-a"));
      Assert.False(cycle.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, cycle.ErrorCode);
      Assert.Equal(1, await db.OwnershipInterestVersions.CountAsync(x => x.ScopeVersionId == scopeVersionId));
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GlImport_RejectsUndefinedClientDimensionValue()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var fixture = await CreateGlFixtureAsync(pg, scope, preparer);
    var request = new GeneralLedgerImportRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, fixture.BookId,
      "csv-v1", "gl-v1", new string('9', 64), "CLIENT-A", "QAR", "dimension-test",
      [new("J-DIM", "INV-DIM", new DateOnly(2026, 6, 30), null, "user-a", "LEDGER-A", null, false, false,
        [new("J-DIM-L1", "1000", 10m, 0m, "QAR", 10m, 10m, Department: "FINANCE"),
         new("J-DIM-L2", "4000", 0m, 10m, "QAR", -10m, -10m)])]);

    await using var db = new AuditSphereDbContext(pg.Options);
    var blocked = await ClientAccountingService.ImportGeneralLedgerAsync(db, preparer, request);
    Assert.False(blocked.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.ImportRejected, blocked.ErrorCode);
    Assert.Contains("dimension", blocked.Message!, StringComparison.OrdinalIgnoreCase);

    Assert.True((await ClientAccountingService.AddDimensionDefinitionsAsync(db, preparer, scope.ClientA,
      [new("DEPARTMENT", "FINANCE", "Finance")])).Succeeded);
    var imported = await ClientAccountingService.ImportGeneralLedgerAsync(db, preparer, request);
    Assert.True(imported.Succeeded, imported.Message);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AccountingSetup_DefaultsBlankCurrencyToQar()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");

    await using var db = new AuditSphereDbContext(pg.Options);
    var profileId = (await ClientAccountingService.CreateProfileAsync(db, preparer,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "", 1, 1, "LEDGER-A", "A-1"))).Value;
    var profile = await db.ClientAccountingProfiles.SingleAsync(x => x.Id == profileId);
    Assert.Equal(AccountingDefaults.DefaultCurrency, profile.FunctionalCurrency);

    var periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
      new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", ""))).Value;
    var period = await db.ClientReportingPeriods.SingleAsync(x => x.Id == periodId);
    Assert.Equal(AccountingDefaults.DefaultCurrency, period.Currency);

    var bookId = (await ClientAccountingService.CreateBookAsync(db, preparer,
      new ReportingBookRequest(scope.ClientA, periodId, "STAT", "STATUTORY", "STATUTORY_ONLY", ""))).Value;
    var book = await db.ClientReportingBooks.SingleAsync(x => x.Id == bookId);
    Assert.Equal(AccountingDefaults.DefaultCurrency, book.Currency);

    db.AcceptanceDecisions.Add(new AcceptanceDecision
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
      ServiceRoute = "AccountingOnly", Decision = "Accepted", Generation = 1,
      Rationale = "Approved test fixture", EvaluationTemplateVersion = "TEST-1",
      EvaluationSnapshotDigest = new string('a', 64),
      DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var missingPermissibility = await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(scope.ClientA, null, "AUDIT_ONLY", "IFRS", "2026", "ANNUAL", "",
        "STATUTORY", "", "PARTNER", "AUDIT", "FinancialStatementAudit"));
    Assert.False(missingPermissibility.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, missingPermissibility.ErrorCode);
    var capabilityId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(scope.ClientA, null, "ENTITY_REPORTING", "IFRS", "2026", "ANNUAL", "",
        "STATUTORY", "", "PARTNER", "ENTITY", "AccountingOnly"))).Value;
    var capability = await db.AccountingCapabilityProfiles.SingleAsync(x => x.Id == capabilityId);
    Assert.Equal(AccountingDefaults.DefaultCurrency, capability.ReportingCurrency);
    Assert.Equal("AccountingOnly", capability.ServiceRoute);

    var invalidKind = await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(scope.ClientA, null, "GROUP_REPORTING", "IFRS", "2026", "ANNUAL", "",
        "STATUTORY", "", "PARTNER", "ENTITY"));
    Assert.False(invalidKind.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, invalidKind.ErrorCode);

    var unsupportedKind = await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(scope.ClientA, null, "BOOKKEEPING", "IFRS", "2026", "ANNUAL", "",
        "STATUTORY", "", "PARTNER", "ENTITY"));
    Assert.False(unsupportedKind.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, unsupportedKind.ErrorCode);
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
  public async Task ReconciliationApproval_StalesWhenSourceDigestChanges()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var (periodId, bookId) = await CreateGlFixtureAsync(pg, scope, preparer);
    var sourceHash = Hashing.Sha256Hex("reconciliation-source");
    var datasetId = Guid.CreateVersion7();

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PeriodId = periodId, BookId = bookId, Basis = "STATUTORY", SourceKind = "Raw", LegalEntityKey = "CLIENT-A",
        Currency = "QAR", RawFileSha256Hex = sourceHash, NormalizedDatasetDigest = sourceHash, Sha256Hex = sourceHash,
        Balanced = true, ValidationStatus = "Pending", ImportState = TrialBalanceImportStates.Loading,
        ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = scope.Preparer.Id
      });
      db.TrialBalanceRows.Add(new TrialBalanceRow
      {
        Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = "1000", AccountName = "Cash",
        Amount = 100m, Currency = "QAR", Entity = "CLIENT-A"
      });
      await db.SaveChangesAsync();
      var sealedDataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      sealedDataset.ValidationStatus = "Accepted";
      sealedDataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync();
      var reconciliation = await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, periodId, bookId, "CASH",
          datasetId, null, ["1000"], new DateOnly(2026, 12, 31)));
      Assert.True(reconciliation.Succeeded, reconciliation.Message);
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      dataset.NormalizedDatasetDigest = Hashing.Sha256Hex("reconciliation-source-replaced");
      await db.SaveChangesAsync();
      var approval = await AccountingAnalysisService.ApproveReconciliationAsync(db, reviewer, reconciliation.Value);
      Assert.False(approval.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, approval.ErrorCode);
      Assert.Equal(AccountingWorkflowStates.Stale,
        await db.AccountingReconciliations.Where(x => x.Id == reconciliation.Value).Select(x => x.Status).SingleAsync());
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ReceivableAging_RetainsBasisBucketsCreditTreatmentAndSettlementLinks()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var fixture = await CreateGlFixtureAsync(pg, scope, preparer);
    var sourceHash = Hashing.Sha256Hex("aging-source");
    Guid datasetId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      datasetId = Guid.CreateVersion7();
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PeriodId = fixture.PeriodId, BookId = fixture.BookId, Basis = "STATUTORY", SourceKind = "Raw",
        LegalEntityKey = "CLIENT-A", Currency = "QAR", RawFileSha256Hex = sourceHash,
        NormalizedDatasetDigest = sourceHash, Sha256Hex = sourceHash, Balanced = true,
        ValidationStatus = "Pending", ImportState = TrialBalanceImportStates.Loading,
        ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = scope.Preparer.Id
      });
      db.TrialBalanceRows.Add(new TrialBalanceRow
      {
        Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = "1000", AccountName = "Receivables",
        Amount = 100m, Currency = "QAR", Entity = "CLIENT-A"
      });
      await db.SaveChangesAsync();
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      dataset.ValidationStatus = "Accepted";
      dataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync();

      var missingPolicy = await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, fixture.BookId,
          "RECEIVABLES", datasetId, null, ["1000"], new DateOnly(2026, 12, 31)));
      Assert.False(missingPolicy.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, missingPolicy.ErrorCode);

      var created = await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, fixture.PeriodId, fixture.BookId,
          "RECEIVABLES", datasetId, null, ["1000"], new DateOnly(2026, 12, 31),
          AccountingAgingRules.DueDateBasis, AccountingAgingRules.StandardRuleVersion));
      Assert.True(created.Succeeded, created.Message);

      var wrongBucket = await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, created.Value,
        [new ReconciliationItemInput("AR-001", 100m, "QAR", new DateOnly(2026, 11, 30), "open invoice", "invoice-001", "OPEN",
          DateBasis: AccountingAgingRules.DueDateBasis, AgingBucket: "CURRENT", IsCredit: false)]);
      Assert.False(wrongBucket.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, wrongBucket.ErrorCode);

      var added = await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, created.Value,
        [new ReconciliationItemInput("AR-001", 100m, "QAR", new DateOnly(2026, 11, 30), "open invoice", "invoice-001", "OPEN",
            DateBasis: AccountingAgingRules.DueDateBasis, AgingBucket: "31_60", IsCredit: false,
            SettlementDate: new DateOnly(2027, 1, 5), SettlementReference: "receipt-001"),
         new ReconciliationItemInput("AR-002", -25m, "QAR", new DateOnly(2026, 12, 31), "credit balance", "credit-001", "OPEN",
            DateBasis: AccountingAgingRules.DueDateBasis, AgingBucket: "CURRENT", IsCredit: true)]);
      Assert.True(added.Succeeded, added.Message);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var items = await verify.AccountingReconciliationItems.OrderBy(x => x.StableItemId).ToListAsync();
    Assert.Equal(2, items.Count);
    Assert.Equal(31, items[0].AgeDays);
    Assert.Equal("DUE_DATE", items[0].DateBasis);
    Assert.Equal("31_60", items[0].AgingBucket);
    Assert.False(items[0].IsCredit);
    Assert.Equal(new DateOnly(2027, 1, 5), items[0].SettlementDate);
    Assert.Equal("receipt-001", items[0].SettlementReference);
    Assert.True(items[1].IsCredit);
    Assert.Equal("CURRENT", items[1].AgingBucket);
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
        [new("J-1-L1", "1000", 100m, 0m, "QAR", 100m, 100m), new("J-1-L2", "4000", 0m, 100m, "QAR", -100m, -100m)],
        new DateOnly(2026, 6, 29))
    };
    var chunkTwo = new[]
    {
      new GeneralLedgerTransactionInput("J-2", "INV-2", new DateOnly(2026, 7, 1), null, "user-a", "LEDGER-A", null, false, false,
        [new("J-2-L1", "1000", 25m, 0m, "QAR", 25m, 25m), new("J-2-L2", "4000", 0m, 25m, "QAR", -25m, -25m)])
    };
    var digestOne = ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", chunkOne);
    var digestTwo = ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", chunkTwo);
    var noServiceDateChunk = chunkOne.Select(x => x with { ServiceDate = null }).ToArray();
    Assert.NotEqual(digestOne, ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", noServiceDateChunk));
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
    Assert.Equal(new DateOnly(2026, 6, 29), await verify.GeneralLedgerTransactions.Where(x => x.ImportBatchId == batchId && x.StableJournalId == "J-1").Select(x => x.ServiceDate).SingleAsync());
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
    Guid priorPeriodId, periodId, priorBookId, bookId, datasetId, openingDatasetId, batchId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      priorPeriodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2025", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), "STATUTORY", "QAR"))).Value;
      Assert.True((await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(scope.ClientA, priorPeriodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"))).Succeeded);
      priorBookId = await db.ClientReportingBooks.Where(x => x.ClientId == scope.ClientA && x.PeriodId == priorPeriodId).Select(x => x.Id).SingleAsync();
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR", priorPeriodId))).Value;
      Assert.True((await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(scope.ClientA, periodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"))).Succeeded);
      bookId = await db.ClientReportingBooks.Where(x => x.ClientId == scope.ClientA && x.PeriodId == periodId).Select(x => x.Id).SingleAsync();
      datasetId = Guid.CreateVersion7();
      openingDatasetId = Guid.CreateVersion7();
      var openingDigest = Hashing.Sha256Hex("completeness-opening-fixture");
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = openingDatasetId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PeriodId = priorPeriodId, BookId = priorBookId, Basis = "STATUTORY",
        SourceKind = "Raw", LegalEntityKey = "CLIENT-A", Currency = "QAR", RawFileSha256Hex = openingDigest,
        NormalizedDatasetDigest = openingDigest, Sha256Hex = openingDigest, Balanced = true, ValidationStatus = "Pending",
        ImportState = TrialBalanceImportStates.Loading, ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = scope.Preparer.Id
      });
      db.TrialBalanceRows.AddRange(
        new TrialBalanceRow { Id = Guid.CreateVersion7(), DatasetId = openingDatasetId, AccountCode = "1000", AccountName = "Cash", Amount = 0m, Currency = "QAR", Entity = "CLIENT-A" },
        new TrialBalanceRow { Id = Guid.CreateVersion7(), DatasetId = openingDatasetId, AccountCode = "4000", AccountName = "Revenue", Amount = 0m, Currency = "QAR", Entity = "CLIENT-A" });
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PeriodId = periodId, BookId = bookId, Basis = "STATUTORY",
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
      var openingDataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == openingDatasetId);
      openingDataset.ValidationStatus = "Accepted";
      openingDataset.ImportState = TrialBalanceImportStates.Sealed;
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      dataset.ValidationStatus = "Accepted";
      dataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync();
    }

    var factory = new OperationContextFactory(new TestDbContextFactory(pg.Options));
    var operationStore = new PostgresOperationStore(factory);
    var operationHandler = new GeneralLedgerCompletenessHandler();
    var workerOptions = new WorkerOptions(scope.FirmId, "Test");
    Guid operationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var queued = await AccountingAnalysisService.EnqueueGeneralLedgerCompletenessBridgeAsync(db, preparer,
        new GeneralLedgerCompletenessRequest(scope.ClientA, scope.EngagementA, periodId, bookId, datasetId, batchId, "tb-gl-completeness", openingDatasetId),
        operationStore, operationHandler);
      Assert.True(queued.Succeeded, queued.Message);
      operationId = queued.Value;
    }
    var worker = new WorkerHost(
      new OperationDispatcher(operationStore, new DurableOperationRegistry([operationHandler], workerOptions), workerOptions),
      Array.Empty<IPendingOperationDiscovery>(), NullLogger<WorkerHost>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync());

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var retry = await AccountingAnalysisService.EnqueueGeneralLedgerCompletenessBridgeAsync(db, preparer,
        new GeneralLedgerCompletenessRequest(scope.ClientA, scope.EngagementA, periodId, bookId, datasetId, batchId, "tb-gl-completeness", openingDatasetId),
        operationStore, operationHandler);
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(operationId, retry.Value);
      var bridge = await db.GeneralLedgerCompletenessBridges.SingleAsync();
      Assert.Equal("RECONCILED", bridge.Status);
      Assert.Equal(2, bridge.MatchedAccountCount);
      Assert.Equal(0m, bridge.AbsoluteResidual);
      Assert.Equal(openingDatasetId, bridge.OpeningTrialBalanceDatasetId);
      Assert.Equal(0m, bridge.OpeningMovementResidual);
      Assert.Equal(0, bridge.OpeningMovementMismatchedAccountCount);
      Assert.Equal(0, bridge.JournalExceptionCount);
      Assert.False(bridge.IncompleteExtract);
      var operation = await db.DurableOperations.SingleAsync(x => x.Id == operationId);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      Assert.Equal(bridge.Id.ToString("D"), operation.ResultIdentity);
      Assert.True(await db.OperationEvents.AnyAsync(x => x.OperationId == operationId && x.Kind == "gl.completeness-built.v1"));

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

  private sealed class TestDbContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
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
      var managementRecord = await db.FinancialPackageReviewDecisions.SingleAsync(x => x.Id == management.Value);
      var renderedArtifact = await db.FinancialPackageArtifacts.SingleAsync(x => x.Id == managementRecord.FinancialPackageArtifactId);
      Assert.Equal(renderedArtifact.ArtifactSha256Hex, managementRecord.ArtifactSha256Hex);
      Assert.Equal(FinancialPackageArtifactVersions.Text, managementRecord.ArtifactVersion);

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
      new ConsolidationComponentBalance(Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), "CASH", 100m, "QAR", 100m, "CONTROLLED", Hashing.Sha256Hex("component-a"), "STATUTORY", "tax-v1", "mapping-a", Guid.Parse("00000000-0000-0000-0000-000000000011")),
      new ConsolidationComponentBalance(Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.NewGuid(), "REVENUE", -100m, "QAR", 100m, "CONTROLLED", Hashing.Sha256Hex("component-b"), "STATUTORY", "tax-v1", "mapping-b", Guid.Parse("00000000-0000-0000-0000-000000000012"))
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
    var receivableEliminations = new[]
    {
      new ConsolidationElimination(Guid.NewGuid(), "CASH", -10m, "QAR", "SELLER", EliminationKind: ConsolidationEliminationKinds.ReceivablePayable),
      new ConsolidationElimination(Guid.NewGuid(), "REVENUE", 10m, "QAR", "BUYER", EliminationKind: ConsolidationEliminationKinds.ReceivablePayable)
    };
    var revenueEliminations = receivableEliminations.Select(x => x with { EliminationKind = ConsolidationEliminationKinds.RevenueExpense }).ToArray();
    var receivableRun = ConsolidationCalculator.Compute("QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026", components, receivableEliminations);
    var revenueRun = ConsolidationCalculator.Compute("QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026", components, revenueEliminations);
    Assert.NotEqual(receivableRun.RunHash, revenueRun.RunHash);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void CurrencyOperations_KeepRemeasurementTranslationAndDisplaySeparate()
  {
    var monetary = CurrencyRemeasurementCalculator.Remeasure(100m, "USD", "QAR", true, 3.7m, 3.6m);
    var nonMonetary = CurrencyRemeasurementCalculator.Remeasure(100m, "USD", "QAR", false, 3.7m, 3.6m);
    Assert.Equal("CLOSING", monetary.RateBasis);
    Assert.Equal(370m, monetary.RemeasuredAmount);
    Assert.Equal("HISTORICAL", nonMonetary.RateBasis);
    Assert.Equal(360m, nonMonetary.RemeasuredAmount);
    Assert.Equal(270m, monetary.ForeignExchangeAdjustment);

    var translation = ForeignOperationTranslationCalculator.Translate(
      openingNetAssets: 100m, closingNetAssets: 130m, currentProfit: 20m,
      openingRate: 3.6m, closingRate: 3.7m, averageRate: 3.65m,
      openingTranslationReserve: 5m, functionalCurrency: "USD", presentationCurrency: "QAR");
    Assert.Equal(360m, translation.OpeningNetAssetsTranslated);
    Assert.Equal(73m, translation.CurrentProfitTranslated);
    Assert.Equal(481m, translation.ClosingNetAssetsTranslated);
    Assert.Equal(48m, translation.TranslationReserveMovement);
    Assert.Equal(53m, translation.ClosingTranslationReserve);
    Assert.Equal(0m, translation.RoundingAdjustment);

    Assert.Equal(370m, DisplayCurrencyConversionCalculator.Convert(100m, "USD", "QAR", 3.7m));
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void AdvancedConsolidationMethods_RequireExplicitInputsAndConserveRollforwards()
  {
    var acquisition = AdvancedConsolidationCalculator.CalculateAcquisition(new AcquisitionAccountingInput(
      new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), 120m, 20m, 100m, 8m, 10m));
    Assert.Equal(30m, acquisition.Goodwill);
    Assert.Equal(0m, acquisition.BargainPurchase);
    Assert.Equal(110m, acquisition.FairValueAdjustedNetAssets);
    Assert.Equal(AdvancedConsolidationCalculator.AcquisitionNciMethod, acquisition.Method);

    var nci = AdvancedConsolidationCalculator.RollForwardNci(20m, 5m, 2m, 3m);
    Assert.Equal(24m, nci.ClosingNci);

    var ownership = AdvancedConsolidationCalculator.CalculateOwnershipChange(new OwnershipChangeInput(
      new DateOnly(2026, 6, 30), 80m, 60m, 10m, 0m, 100m, 20m, false));
    Assert.True(ownership.ControlRetained);
    Assert.Equal(20m, ownership.NciMovement);
    Assert.Equal(0m, ownership.DisposalGainOrLoss);

    var increasedOwnership = AdvancedConsolidationCalculator.CalculateOwnershipChange(new OwnershipChangeInput(
      new DateOnly(2026, 9, 30), 60m, 80m, 12m, 0m, 100m, 40m, false));
    Assert.True(increasedOwnership.ControlRetained);
    Assert.Equal(-20m, increasedOwnership.NciMovement);
    Assert.Throws<InvalidOperationException>(() => AdvancedConsolidationCalculator.CalculateOwnershipChange(new OwnershipChangeInput(
      new DateOnly(2026, 9, 30), 60m, 60m, 12m, 0m, 100m, 40m, true)));

    Assert.Throws<InvalidOperationException>(() => AdvancedConsolidationCalculator.EnsureNoNestedDoubleCount([
      new("ENTITY-A", Guid.NewGuid(), false), new("entity-a", Guid.NewGuid(), true)
    ]));
    var elimination = AdvancedConsolidationCalculator.CalculateAssetTransferElimination(30m, 6m, 0.25m);
    Assert.Equal(30m, elimination.UnrealizedProfitElimination);
    Assert.Equal(6m, elimination.DepreciationAdjustment);
    Assert.Equal(6m, elimination.RelatedTaxEffect);
    Assert.Equal(-18m, elimination.NetElimination);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void AdvancedConsolidationCandidateFixture_BalancesCurrentAndComparativeStatements()
  {
    var acquisition = AdvancedConsolidationCalculator.CalculateAcquisition(new AcquisitionAccountingInput(
      new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), 120m, 20m, 100m, 8m, 10m));
    var nci = AdvancedConsolidationCalculator.RollForwardNci(20m, 5m, 2m, 3m);
    var translation = ForeignOperationTranslationCalculator.Translate(
      100m, 130m, 20m, 3.6m, 3.7m, 3.65m, 5m, "USD", "QAR");
    var elimination = AdvancedConsolidationCalculator.CalculateAssetTransferElimination(30m, 6m, 0.25m);
    AdvancedConsolidationCalculator.EnsureNoNestedDoubleCount([
      new("PARENT", Guid.Parse("00000000-0000-0000-0000-000000000101"), true),
      new("SUBSIDIARY", Guid.Parse("00000000-0000-0000-0000-000000000102"), false)
    ]);

    var comparative = new[]
    {
      ("Translated net assets", translation.OpeningNetAssetsTranslated),
      ("Goodwill", acquisition.Goodwill),
      ("NCI", -nci.OpeningNci),
      ("Translation reserve", -5m),
      ("Parent equity", -365m)
    };
    var current = new[]
    {
      ("Translated net assets", translation.ClosingNetAssetsTranslated),
      ("Goodwill", acquisition.Goodwill),
      ("Asset-transfer elimination", elimination.NetElimination),
      ("NCI", -nci.ClosingNci),
      ("Translation reserve", -translation.ClosingTranslationReserve),
      ("Parent equity", -416m)
    };

    Assert.Equal(new[]
    {
      ("Translated net assets", 360m), ("Goodwill", 30m), ("NCI", -20m),
      ("Translation reserve", -5m), ("Parent equity", -365m)
    }, comparative);
    Assert.Equal(new[]
    {
      ("Translated net assets", 481m), ("Goodwill", 30m), ("Asset-transfer elimination", -18m),
      ("NCI", -24m), ("Translation reserve", -53m), ("Parent equity", -416m)
    }, current);
    Assert.Equal(0m, comparative.Sum(x => x.Item2));
    Assert.Equal(0m, current.Sum(x => x.Item2));
    Assert.Equal(121m, current[0].Item2 - comparative[0].Item2);
    Assert.Equal(-4m, current[3].Item2 - comparative[2].Item2);
    Assert.Equal(-48m, current[4].Item2 - comparative[3].Item2);
    Assert.Equal(-51m, current[5].Item2 - comparative[4].Item2);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void AdvancedMethodScheduleValidation_RequiresMethodSpecificInputs()
  {
    var valid = new[]
    {
      (AdvancedConsolidationMethods.ForeignCurrencyReserve,
        "{\"openingNetAssets\":100,\"closingNetAssets\":130,\"currentProfit\":20,\"openingRate\":3.6,\"closingRate\":3.7,\"averageRate\":3.65,\"openingTranslationReserve\":5,\"functionalCurrency\":\"USD\",\"presentationCurrency\":\"QAR\"}"),
      (AdvancedConsolidationMethods.AcquisitionNci,
        "{\"acquisitionDate\":\"2026-01-01\",\"controlDate\":\"2026-01-15\",\"consideration\":120,\"nciAtAcquisition\":20,\"fairValueNetAssets\":100,\"openingReserves\":8,\"fairValueAdjustments\":10,\"nciOpening\":20,\"nciProfit\":5,\"nciOci\":2,\"nciDistributions\":3}"),
      (AdvancedConsolidationMethods.OwnershipChange,
        "{\"effectiveDate\":\"2026-06-30\",\"previousOwnershipPercent\":80,\"newOwnershipPercent\":60,\"consideration\":10,\"fairValueRetainedInterest\":0,\"carryingNetAssets\":100,\"carryingNci\":20,\"controlLost\":false}"),
      (AdvancedConsolidationMethods.NestedGroup,
        "{\"components\":[{\"economicEntityKey\":\"PARENT\",\"sourceScopeVersionId\":\"00000000-0000-0000-0000-000000000101\",\"includedDirectly\":true},{\"economicEntityKey\":\"SUBSIDIARY\",\"sourceScopeVersionId\":\"00000000-0000-0000-0000-000000000102\",\"includedDirectly\":false}]}"),
      (AdvancedConsolidationMethods.AssetTransferElimination,
        "{\"unrealizedProfit\":30,\"postTransferDepreciation\":6,\"taxRate\":0.25}")
    };

    foreach (var (method, json) in valid)
      Assert.True(AdvancedConsolidationCalculator.TryValidateScheduleInput(method, json, out var error), $"{method}: {error}");

    Assert.False(AdvancedConsolidationCalculator.TryValidateScheduleInput(
      AdvancedConsolidationMethods.AcquisitionNci, "{\"consideration\":120}", out _));
    Assert.False(AdvancedConsolidationCalculator.TryValidateScheduleInput(
      AdvancedConsolidationMethods.NestedGroup,
      "{\"components\":[{\"economicEntityKey\":\"A\",\"sourceScopeVersionId\":\"00000000-0000-0000-0000-000000000101\",\"includedDirectly\":true},{\"economicEntityKey\":\"a\",\"sourceScopeVersionId\":\"00000000-0000-0000-0000-000000000102\",\"includedDirectly\":false}]}",
      out _));
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void AdvancedConsolidationExecution_RequiresBalancedCurrentAndComparativeEvidence()
  {
    const string valid = """
      {
        "acquisitionDate":"2026-01-01",
        "controlDate":"2026-01-15",
        "consideration":120,
        "nciAtAcquisition":20,
        "fairValueNetAssets":100,
        "openingReserves":8,
        "fairValueAdjustments":10,
        "nciOpening":20,
        "nciProfit":5,
        "nciOci":2,
        "nciDistributions":3,
        "statementLines":[
          {"code":"NET_ASSETS","comparativeAmount":20,"currentAmount":14},
          {"code":"NCI","comparativeAmount":-20,"currentAmount":-24},
          {"code":"GOODWILL","comparativeAmount":0,"currentAmount":30},
          {"code":"PARENT_EQUITY","comparativeAmount":0,"currentAmount":-20}
        ]
      }
      """;

    Assert.True(AdvancedConsolidationExecutionCalculator.TryCalculate(
      AdvancedConsolidationMethods.AcquisitionNci, valid, out var calculation, out var error), error);
    Assert.NotNull(calculation);
    Assert.Equal(0m, calculation!.ComparativeSignedTotal);
    Assert.Equal(0m, calculation.CurrentSignedTotal);
    Assert.Equal(Hashing.Sha256Hex(calculation.OutputManifest), calculation.OutputDigest);
    Assert.Contains("GOODWILL", calculation.CurrentStatementJson, StringComparison.Ordinal);

    var unbalanced = valid.Replace("\"PARENT_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":-20", "\"PARENT_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":-19", StringComparison.Ordinal);
    Assert.False(AdvancedConsolidationExecutionCalculator.TryCalculate(
      AdvancedConsolidationMethods.AcquisitionNci, unbalanced, out _, out var unbalancedError));
    Assert.Contains("balance", unbalancedError, StringComparison.OrdinalIgnoreCase);

    var missingMethodLine = valid
      .Replace("{\"code\":\"GOODWILL\",\"comparativeAmount\":0,\"currentAmount\":30},", string.Empty, StringComparison.Ordinal)
      .Replace("\"PARENT_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":-20", "\"PARENT_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":10", StringComparison.Ordinal);
    Assert.False(AdvancedConsolidationExecutionCalculator.TryCalculate(
      AdvancedConsolidationMethods.AcquisitionNci, missingMethodLine, out _, out var missingLineError));
    Assert.Contains("GOODWILL", missingLineError, StringComparison.Ordinal);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void AdvancedConsolidationExecution_GoldenFixturesCoverEachMethod()
  {
    var fixtures = new[]
    {
      (AdvancedConsolidationMethods.ForeignCurrencyReserve, """
        {
          "openingNetAssets":100,"closingNetAssets":130,"currentProfit":20,
          "openingRate":3.6,"closingRate":3.7,"averageRate":3.65,"openingTranslationReserve":5,
          "functionalCurrency":"USD","presentationCurrency":"QAR",
          "statementLines":[
            {"code":"TRANSLATION_RESERVE","comparativeAmount":-5,"currentAmount":-53},
            {"code":"BALANCING_EQUITY","comparativeAmount":5,"currentAmount":53}
          ]
        }
        """),
      (AdvancedConsolidationMethods.AcquisitionNci, """
        {
          "acquisitionDate":"2026-01-01","controlDate":"2026-01-15","consideration":120,
          "nciAtAcquisition":20,"fairValueNetAssets":100,"openingReserves":8,"fairValueAdjustments":10,
          "nciOpening":20,"nciProfit":5,"nciOci":2,"nciDistributions":3,
          "statementLines":[
            {"code":"NET_ASSETS","comparativeAmount":20,"currentAmount":14},
            {"code":"NCI","comparativeAmount":-20,"currentAmount":-24},
            {"code":"GOODWILL","comparativeAmount":0,"currentAmount":30},
            {"code":"PARENT_EQUITY","comparativeAmount":0,"currentAmount":-20}
          ]
        }
        """),
      (AdvancedConsolidationMethods.OwnershipChange, """
        {
          "effectiveDate":"2026-06-30","previousOwnershipPercent":80,"newOwnershipPercent":60,
          "consideration":10,"fairValueRetainedInterest":0,"carryingNetAssets":100,"carryingNci":20,"controlLost":false,
          "statementLines":[
            {"code":"NCI_MOVEMENT","comparativeAmount":0,"currentAmount":20},
            {"code":"OWNERSHIP_CHANGE_GAIN_LOSS","comparativeAmount":0,"currentAmount":0},
            {"code":"EQUITY","comparativeAmount":0,"currentAmount":-20}
          ]
        }
        """),
      (AdvancedConsolidationMethods.NestedGroup, """
        {
          "components":[
            {"economicEntityKey":"PARENT","sourceScopeVersionId":"00000000-0000-0000-0000-000000000101","includedDirectly":true},
            {"economicEntityKey":"SUBSIDIARY","sourceScopeVersionId":"00000000-0000-0000-0000-000000000102","includedDirectly":false}
          ],
          "statementLines":[{"code":"GROUP_BALANCE","comparativeAmount":0,"currentAmount":0}]
        }
        """),
      (AdvancedConsolidationMethods.AssetTransferElimination, """
        {
          "unrealizedProfit":30,"postTransferDepreciation":6,"taxRate":0.25,
          "statementLines":[
            {"code":"ASSET_TRANSFER_ELIMINATION","comparativeAmount":0,"currentAmount":-18},
            {"code":"EQUITY","comparativeAmount":0,"currentAmount":18}
          ]
        }
        """)
    };

    foreach (var (method, json) in fixtures)
    {
      Assert.True(AdvancedConsolidationExecutionCalculator.TryCalculate(method, json, out var calculation, out var error),
        $"{method}: {error}");
      Assert.NotNull(calculation);
      Assert.Equal(0m, calculation!.ComparativeSignedTotal);
      Assert.Equal(0m, calculation.CurrentSignedTotal);
      Assert.Equal(Hashing.Sha256Hex(calculation.OutputManifest), calculation.OutputDigest);
      Assert.Contains(method, calculation.OutputManifest, StringComparison.Ordinal);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AdvancedMethodSchedules_AreCanonicalScopedAndIdempotent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "Partner");
    var methodOwner = Actor(fixture.Preparer, "Partner");
    Guid scopeId, profileId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
        new ClientGroupRequest("ADV-SCHEDULE", "Advanced schedule group"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, fixture.ClientA, new DateOnly(2026, 1, 1), null,
          "CONTROLLED", 100m, 100m, "advanced-schedule-membership"))).Succeeded);
      db.GroupAccessGrants.AddRange(
        new GroupAccessGrant
        {
          Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
          Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id
        },
        new GroupAccessGrant
        {
          Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
          Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id
        });
      await db.SaveChangesAsync();
      scopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", AdvancedConsolidationMethods.AcquisitionNci,
          "OPENING-2026"))).Value;
      profileId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, groupId, AccountingCapabilityServiceKinds.GroupReporting, "IFRS", "2026",
          "ANNUAL", "QAR", "STATUTORY", AdvancedConsolidationMethods.AcquisitionNci, "PARTNER", "GROUP"))).Value;
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, profileId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "advanced-schedule-local")).Succeeded);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, methodOwner, profileId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "advanced-schedule-method-owner")).Succeeded);

      var invalid = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
        new AdvancedConsolidationMethodScheduleRequest(scopeId, AdvancedConsolidationMethods.AcquisitionNci,
          "IFRS", "{\"notSources\":[]}", "{\"consideration\":120}"));
      Assert.False(invalid.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, invalid.ErrorCode);

      var request = new AdvancedConsolidationMethodScheduleRequest(scopeId,
        AdvancedConsolidationMethods.AcquisitionNci, "IFRS",
        "{ \"sources\": [ { \"kind\": \"PACKAGE\", \"id\": \"pkg-1\" } ] }",
        "{ \"consideration\": 120, \"nci\": 20, \"fairValueNetAssets\": 100 }\n");
      var created = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer, request);
      Assert.True(created.Succeeded, created.Message);
      var duplicate = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer, request);
      Assert.True(duplicate.Succeeded);
      Assert.Equal(created.Value, duplicate.Value);
      var stored = await db.AdvancedConsolidationMethodSchedules.SingleAsync(x => x.Id == created.Value);
      Assert.Equal(AdvancedConsolidationMethodScheduleStates.Submitted, stored.Status);
      Assert.Equal(Hashing.Sha256Hex(stored.SourceManifestJson), stored.SourceManifestDigest);
      Assert.Equal(Hashing.Sha256Hex(stored.InputSnapshotJson), stored.InputSnapshotDigest);

      var scope = await db.ConsolidationScopeVersions.SingleAsync(x => x.Id == scopeId);
      scope.Status = AccountingWorkflowStates.Approved;
      await db.SaveChangesAsync();
      var invalidApproval = await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, created.Value);
      Assert.False(invalidApproval.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, invalidApproval.ErrorCode);

      var validCreated = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
        new AdvancedConsolidationMethodScheduleRequest(scopeId, AdvancedConsolidationMethods.AcquisitionNci,
          "IFRS", "{ \"sources\": [ { \"kind\": \"PACKAGE\", \"id\": \"pkg-2\" } ] }",
          "{ \"acquisitionDate\": \"2026-01-01\", \"controlDate\": \"2026-01-15\", \"consideration\": 120, \"nciAtAcquisition\": 20, \"fairValueNetAssets\": 100, \"openingReserves\": 8, \"fairValueAdjustments\": 10, \"nciOpening\": 20, \"nciProfit\": 5, \"nciOci\": 2, \"nciDistributions\": 3 }"));
      Assert.True(validCreated.Succeeded, validCreated.Message);
      var approved = await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, validCreated.Value);
      Assert.True(approved.Succeeded, approved.Message);
      Assert.Equal(AdvancedConsolidationMethodScheduleStates.Approved,
        await db.AdvancedConsolidationMethodSchedules.Where(x => x.Id == validCreated.Value).Select(x => x.Status).SingleAsync());
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task NestedAdvancedSchedule_BindsApprovedSourceRun()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "Partner");
    var methodOwner = Actor(fixture.Preparer, "Partner");

    await using var db = new AuditSphereDbContext(pg.Options);
    var groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
      new ClientGroupRequest("NESTED-SCHEDULE", "Nested schedule group"))).Value;
    Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
      new GroupMembershipRequest(groupId, fixture.ClientA, new DateOnly(2026, 1, 1), null,
        "CONTROLLED", 100m, 100m, "nested-schedule-membership"))).Succeeded);
    db.GroupAccessGrants.AddRange(
      new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
        Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id },
      new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
        Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id });
    await db.SaveChangesAsync();

    var targetScopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
      new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", AdvancedConsolidationMethods.NestedGroup,
        "OPENING-2026"))).Value;
    var profileId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(null, groupId, AccountingCapabilityServiceKinds.GroupReporting, "IFRS", "2026",
        "ANNUAL", "QAR", "STATUTORY", AdvancedConsolidationMethods.NestedGroup, "PARTNER", "GROUP"))).Value;
    Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, profileId,
      AccountingCapabilityAcceptanceStages.LocalConstruction, "nested-schedule-local")).Succeeded);
    Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, methodOwner, profileId,
      AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "nested-schedule-method-owner")).Succeeded);

    var packId = (await ConsolidationService.SubmitExternalComponentPackAsync(db, preparer,
      new ExternalComponentPackRequest(targetScopeId, fixture.ClientA, fixture.EngagementA, "2026-01-01", "2026-12-31",
        "IFRS", "QAR", "STATUTORY", "tax-v1", "mapping-v1", "nested-schedule-pack",
        new string('a', 64), new string('b', 64), 0m,
        [new("CASH", 100m, "QAR", "line-1"), new("EQUITY", -100m, "QAR", "line-2")]))).Value;
    Assert.True((await ConsolidationService.ReconcileExternalComponentPackAsync(db, reviewer,
      new ExternalComponentReconciliationRequest(packId, "nested-pack-reconciliation"))).Succeeded);
    Assert.True((await ConsolidationService.ApproveExternalComponentPackAsync(db, reviewer, packId)).Succeeded);
    var componentId = (await ConsolidationService.SubmitExternalComponentAsync(db, preparer,
      new ExternalComponentRequest(targetScopeId, packId))).Value;
    Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId)).Succeeded);

    var targetScope = await db.ConsolidationScopeVersions.SingleAsync(x => x.Id == targetScopeId);
    targetScope.Status = AccountingWorkflowStates.Approved;
    var sourceScopeId = Guid.NewGuid();
    var sourceRunId = Guid.NewGuid();
    var sourceRunHash = Hashing.Sha256Hex("nested-source-run");
    db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion
    {
      Id = sourceScopeId, FirmId = fixture.FirmId, GroupId = groupId, GroupRevision = targetScope.GroupRevision,
      PeriodId = Guid.NewGuid(), Version = 1, ReportingCurrency = "QAR", Method = ConsolidationCalculator.RestrictedMethod,
      Status = AccountingWorkflowStates.Approved, OpeningBasis = "NESTED-SOURCE", CreatedByUserId = reviewer.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.ConsolidationRuns.Add(new ConsolidationRun
    {
      Id = sourceRunId, FirmId = fixture.FirmId, GroupId = groupId, ScopeVersionId = sourceScopeId,
      EngineVersion = "fixture", InputManifest = "fixture", RunHash = sourceRunHash, ReportingCurrency = "QAR",
      SignedTotal = 0m, Status = AccountingWorkflowStates.Approved, CreatedByUserId = reviewer.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var input = $"{{\"components\":[{{\"economicEntityKey\":\"SUBGROUP\",\"sourceScopeVersionId\":\"{sourceScopeId:D}\",\"includedDirectly\":false}}]}}";
    var validInput = $"{{\"fixture\":\"approved-source-run\",\"components\":[{{\"economicEntityKey\":\"SUBGROUP\",\"sourceScopeVersionId\":\"{sourceScopeId:D}\",\"includedDirectly\":false}}]}}";
    var packHash = await db.ExternalComponentPacks.Where(x => x.Id == packId).Select(x => x.PackDigest).SingleAsync();
    var sources = $"\"sources\":[{{\"componentId\":\"{componentId:D}\",\"kind\":\"EXTERNAL_PACK\",\"id\":\"{packId:D}\",\"hash\":\"{packHash}\"}}]";
    var missingEvidence = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
      new AdvancedConsolidationMethodScheduleRequest(targetScopeId, AdvancedConsolidationMethods.NestedGroup,
        "IFRS", $"{{{sources}}}", input));
    Assert.True(missingEvidence.Succeeded, missingEvidence.Message);
    var missingApproval = await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, missingEvidence.Value);
    Assert.False(missingApproval.Succeeded);
    Assert.Equal(ErrorCodes.ManifestMismatch, missingApproval.ErrorCode);

    var validEvidence = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
      new AdvancedConsolidationMethodScheduleRequest(targetScopeId, AdvancedConsolidationMethods.NestedGroup,
        "IFRS", $"{{{sources},\"nestedScopes\":[{{\"scopeVersionId\":\"{sourceScopeId:D}\",\"runId\":\"{sourceRunId:D}\",\"runHash\":\"{sourceRunHash}\"}}]}}", validInput));
    Assert.True(validEvidence.Succeeded, validEvidence.Message);
    Assert.True((await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, validEvidence.Value)).Succeeded);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AdvancedConsolidationExecution_IsScopedVerifiedIdempotentlyAndSeparatelyApproved()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "Partner");
    var methodOwner = Actor(fixture.Preparer, "Partner");
    Guid scopeId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
        new ClientGroupRequest("ADV-EXECUTION", "Advanced execution group"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, fixture.ClientA, new DateOnly(2026, 1, 1), null,
          "CONTROLLED", 100m, 100m, "advanced-execution-membership"))).Succeeded);
      db.GroupAccessGrants.AddRange(
        new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
          Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id },
        new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
          Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id });
      await db.SaveChangesAsync();

      scopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", AdvancedConsolidationMethods.AcquisitionNci,
          "OPENING-2026"))).Value;
      var packId = (await ConsolidationService.SubmitExternalComponentPackAsync(db, preparer,
        new ExternalComponentPackRequest(scopeId, fixture.ClientA, fixture.EngagementA, "2026-01-01", "2026-12-31",
          "IFRS", "QAR", "STATUTORY", "tax-v1", "mapping-v1", "advanced-execution-fixture",
          new string('a', 64), new string('b', 64), 0m,
          [new("CASH", 100m, "QAR", "line-1"), new("EQUITY", -100m, "QAR", "line-2")]))).Value;
      Assert.True((await ConsolidationService.ReconcileExternalComponentPackAsync(db, reviewer,
        new ExternalComponentReconciliationRequest(packId, "external-pack-reconciliation"))).Succeeded);
      Assert.True((await ConsolidationService.ApproveExternalComponentPackAsync(db, reviewer, packId)).Succeeded);
      var componentId = (await ConsolidationService.SubmitExternalComponentAsync(db, preparer,
        new ExternalComponentRequest(scopeId, packId))).Value;
      Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId)).Succeeded);
      var packHash = await db.ExternalComponentPacks.Where(x => x.Id == packId).Select(x => x.PackDigest).SingleAsync();

      var profileId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, groupId, AccountingCapabilityServiceKinds.GroupReporting, "IFRS", "2026",
          "ANNUAL", "QAR", "STATUTORY", AdvancedConsolidationMethods.AcquisitionNci, "PARTNER", "GROUP"))).Value;
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, profileId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "advanced-execution-profile")).Succeeded);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, methodOwner, profileId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "advanced-execution-method-owner")).Succeeded);
      Assert.True((await ConsolidationService.ApproveScopeAsync(db, reviewer, scopeId)).Succeeded);
      var reviewedJournalId = (await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(scopeId, "ADV-ACQ-001", "ACQUISITION_NCI", "QAR", "advanced-reviewed-journal",
          [new(null, "GOODWILL", 30m, 0m, "Reviewed acquisition goodwill"),
           new(null, "PARENT_EQUITY", 0m, 30m, "Reviewed acquisition equity")]))).Value;
      Assert.True((await ConsolidationService.ApproveConsolidationJournalAsync(db, reviewer, reviewedJournalId)).Succeeded);

      var invalidSourceSchedule = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
        new AdvancedConsolidationMethodScheduleRequest(scopeId, AdvancedConsolidationMethods.AcquisitionNci, "IFRS",
          $"{{\"sources\":[{{\"componentId\":\"{componentId:D}\",\"kind\":\"EXTERNAL_PACK\",\"id\":\"{Guid.NewGuid():D}\",\"hash\":\"{packHash}\"}}]}}",
          "{}"));
      Assert.True(invalidSourceSchedule.Succeeded, invalidSourceSchedule.Message);
      var invalidSourceApproval = await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, invalidSourceSchedule.Value);
      Assert.False(invalidSourceApproval.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, invalidSourceApproval.ErrorCode);

      var sourceWithoutJournal = $"{{\"sources\":[{{\"componentId\":\"{componentId:D}\",\"kind\":\"EXTERNAL_PACK\",\"id\":\"{packId:D}\",\"hash\":\"{packHash}\"}}]}}";
      var missingJournalSchedule = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
        new AdvancedConsolidationMethodScheduleRequest(scopeId, AdvancedConsolidationMethods.AcquisitionNci, "IFRS",
          sourceWithoutJournal, "{\"fixture\":\"missing-reviewed-journal\"}"));
      Assert.True(missingJournalSchedule.Succeeded, missingJournalSchedule.Message);
      var missingJournalApproval = await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, missingJournalSchedule.Value);
      Assert.False(missingJournalApproval.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, missingJournalApproval.ErrorCode);

      var scheduleId = (await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
        new AdvancedConsolidationMethodScheduleRequest(scopeId, AdvancedConsolidationMethods.AcquisitionNci, "IFRS",
          $"{{\"sources\":[{{\"componentId\":\"{componentId:D}\",\"kind\":\"EXTERNAL_PACK\",\"id\":\"{packId:D}\",\"hash\":\"{packHash}\"}}],\"reviewedJournals\":[{{\"id\":\"{reviewedJournalId:D}\"}}]}}",
          "{\"acquisitionDate\":\"2026-01-01\",\"controlDate\":\"2026-01-15\",\"consideration\":120,\"nciAtAcquisition\":20,\"fairValueNetAssets\":100,\"openingReserves\":8,\"fairValueAdjustments\":10,\"nciOpening\":20,\"nciProfit\":5,\"nciOci\":2,\"nciDistributions\":3,\"statementLines\":[{\"code\":\"NET_ASSETS\",\"comparativeAmount\":20,\"currentAmount\":14},{\"code\":\"NCI\",\"comparativeAmount\":-20,\"currentAmount\":-24},{\"code\":\"GOODWILL\",\"comparativeAmount\":0,\"currentAmount\":30},{\"code\":\"PARENT_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":-20}]}"))).Value;
      Assert.True((await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, scheduleId)).Succeeded);

      var execution = await ConsolidationService.RunAsync(db, preparer, scopeId);
      Assert.True(execution.Succeeded, execution.Message);
      var repeated = await ConsolidationService.RunAsync(db, preparer, scopeId);
      Assert.True(repeated.Succeeded);
      Assert.Equal(execution.Value, repeated.Value);
      var stored = await db.AdvancedConsolidationExecutions.SingleAsync(x => x.Id == execution.Value);
      Assert.Equal(AdvancedConsolidationExecutionStates.Verified, stored.Status);
      Assert.Equal(0m, stored.ComparativeSignedTotal);
      Assert.Equal(0m, stored.CurrentSignedTotal);
      Assert.Equal(Hashing.Sha256Hex(stored.OutputManifest), stored.OutputDigest);
      var component = await db.ConsolidationComponents.SingleAsync(x => x.Id == componentId);
      var packageHash = component.PackageHash;
      component.PackageHash = Hashing.Sha256Hex("changed-after-execution");
      await db.SaveChangesAsync();
      var staleApproval = await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, stored.Id);
      Assert.False(staleApproval.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleApproval.ErrorCode);
      component.PackageHash = packageHash;
      await db.SaveChangesAsync();
      var reviewedJournal = await db.ConsolidationJournals.SingleAsync(x => x.Id == reviewedJournalId);
      reviewedJournal.Status = AccountingWorkflowStates.Draft;
      await db.SaveChangesAsync();
      var staleJournalApproval = await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, stored.Id);
      Assert.False(staleJournalApproval.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, staleJournalApproval.ErrorCode);
      reviewedJournal.Status = AccountingWorkflowStates.Approved;
      await db.SaveChangesAsync();
      reviewedJournal.JournalType = "OTHER";
      await db.SaveChangesAsync();
      var mismatchedJournalApproval = await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, stored.Id);
      Assert.False(mismatchedJournalApproval.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, mismatchedJournalApproval.ErrorCode);
      reviewedJournal.JournalType = "ACQUISITION_NCI";
      await db.SaveChangesAsync();
      var methodAcceptance = await db.AccountingCapabilityAcceptances.SingleAsync(x => x.CapabilityProfileId == profileId &&
        x.Stage == AccountingCapabilityAcceptanceStages.MethodOwnerApproval);
      methodAcceptance.Status = AccountingWorkflowStates.Retired;
      await db.SaveChangesAsync();
      var unacceptedApproval = await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, stored.Id);
      Assert.False(unacceptedApproval.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, unacceptedApproval.ErrorCode);
      methodAcceptance.Status = AccountingWorkflowStates.Approved;
      await db.SaveChangesAsync();
      Assert.True((await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, stored.Id)).Succeeded);
      Assert.Equal(AdvancedConsolidationExecutionStates.Approved,
        await db.AdvancedConsolidationExecutions.Where(x => x.Id == stored.Id).Select(x => x.Status).SingleAsync());
    }
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void ConsolidationEliminationKinds_RequireAnEnabledAccountingNature()
  {
    Assert.All(new[]
    {
      ConsolidationEliminationKinds.ReceivablePayable,
      ConsolidationEliminationKinds.RevenueExpense,
      ConsolidationEliminationKinds.Dividend,
      ConsolidationEliminationKinds.InvestmentEquity
    }, kind => Assert.True(ConsolidationEliminationKinds.IsIntercompany(kind)));
    Assert.False(ConsolidationEliminationKinds.IsIntercompany("INTERCOMPANY"));
    Assert.True(ConsolidationEliminationKinds.IsRunKind(ConsolidationEliminationKinds.GroupJournal));
    Assert.False(ConsolidationEliminationKinds.IsRunKind("UNCLASSIFIED"));
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
  [Trait("Profile", "Unit")]
  public void ForeignOperationTranslation_PreservesLineageAndSupportsMultipleLines()
  {
    var componentId = Guid.Parse("00000000-0000-0000-0000-000000000021");
    var rateSetId = Guid.Parse("00000000-0000-0000-0000-000000000022");
    var policyId = Guid.Parse("00000000-0000-0000-0000-000000000023");
    var translationId = Guid.Parse("00000000-0000-0000-0000-000000000024");
    var lines = new[]
    {
      new ConsolidationComponentBalance(componentId, Guid.NewGuid(), "CASH", 125m, "QAR", 100m, "CONTROLLED",
        Hashing.Sha256Hex("foreign-component"), "STATUTORY", "tax-v1", "mapping-v1", Guid.Parse("00000000-0000-0000-0000-000000000025"),
        "USD", translationId, rateSetId, policyId, new DateOnly(2026, 12, 31), "CLOSING", 1.25m),
      new ConsolidationComponentBalance(componentId, Guid.NewGuid(), "REVENUE", -125m, "QAR", 100m, "CONTROLLED",
        Hashing.Sha256Hex("foreign-component"), "STATUTORY", "tax-v1", "mapping-v1", Guid.Parse("00000000-0000-0000-0000-000000000026"),
        "USD", translationId, rateSetId, policyId, new DateOnly(2026, 12, 31), "CLOSING", 1.25m)
    };
    var calculation = ConsolidationCalculator.Compute("QAR", ConsolidationCalculator.ForeignOperationMethod, "OPENING-2026", lines, []);
    Assert.Equal(0m, calculation.SignedTotal);
    Assert.Equal(new[] { 125m, -125m }, calculation.Lines.Select(x => x.ConsolidatedAmount));
    Assert.Contains("USD", calculation.InputManifest);
    Assert.Throws<InvalidOperationException>(() => ConsolidationCalculator.Compute("QAR", ConsolidationCalculator.ForeignOperationMethod,
      "OPENING-2026", [lines[0] with { TranslationRate = 0m }, lines[1]], []));
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ForeignOperationGroup_RequiresPinnedApprovedTranslation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    var methodOwner = Actor(scope.Preparer, "Partner");
    var rateDate = new DateOnly(2026, 12, 31);
    Guid groupId, rateSetId, policyId, consolidationScopeId, packageUsd, packageQar;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer, new ClientGroupRequest("GROUP-FX", "Foreign Group"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientA, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m, "ownership-usd"))).Succeeded);
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientB, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m, "ownership-qar"))).Succeeded);
      db.GroupAccessGrants.AddRange(
        new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = scope.Preparer.Id,
          Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id },
        new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = scope.Preparer.Id,
          Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id });
      db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "Partner"));
      await db.SaveChangesAsync();

      rateSetId = (await CurrencyTranslationService.CreateRateSetAsync(db, reviewer,
        new ExchangeRateSetRequest("FX-2026", "approved-method-fixture",
          new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 2))).Value;
      var outOfRange = await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
        new ExchangeRateInput("USD", "QAR", new DateOnly(2027, 1, 1), "CLOSING", 3.64m, "DIRECT"));
      Assert.False(outOfRange.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, outOfRange.ErrorCode);
      var unsupportedDirection = await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
        new ExchangeRateInput("USD", "QAR", rateDate, "CLOSING", 3.64m, "INVERSE"));
      Assert.False(unsupportedDirection.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, unsupportedDirection.ErrorCode);
      Assert.True((await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
        new ExchangeRateInput("USD", "QAR", rateDate, "CLOSING", 3.64m, "DIRECT"))).Succeeded);
      var duplicateRate = await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
        new ExchangeRateInput("USD", "QAR", rateDate, "CLOSING", 3.65m, "DIRECT"));
      Assert.False(duplicateRate.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicateRate.ErrorCode);
      var rateSet = await db.ExchangeRateSetVersions.SingleAsync(x => x.Id == rateSetId);
      Assert.Equal(2, rateSet.Version);
      Assert.Equal(new DateOnly(2026, 1, 1), rateSet.EffectiveFrom);
      Assert.Equal(new DateOnly(2026, 12, 31), rateSet.EffectiveTo);
      Assert.True((await CurrencyTranslationService.ApproveRateSetAsync(db, methodOwner, rateSetId)).Succeeded);
      policyId = (await CurrencyTranslationService.CreatePolicyAsync(db, reviewer,
        new TranslationPolicyRequest("FX-POLICY-2026", "USD", "QAR", "CLOSING", "AVERAGE", "HISTORICAL"))).Value;
      var duplicatePolicy = await CurrencyTranslationService.CreatePolicyAsync(db, reviewer,
        new TranslationPolicyRequest("FX-POLICY-2026", "USD", "QAR", "CLOSING", "AVERAGE", "HISTORICAL"));
      Assert.False(duplicatePolicy.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicatePolicy.ErrorCode);
      Assert.True((await CurrencyTranslationService.ApprovePolicyAsync(db, methodOwner, policyId)).Succeeded);
      var unsupportedPolicyRateScope = await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", ConsolidationCalculator.ForeignOperationMethod, "OPENING-FX-2026",
          rateSetId, policyId, rateDate, "SPOT"));
      Assert.False(unsupportedPolicyRateScope.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, unsupportedPolicyRateScope.ErrorCode);

      consolidationScopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", ConsolidationCalculator.ForeignOperationMethod, "OPENING-FX-2026",
          rateSetId, policyId, rateDate, "CLOSING"))).Value;
      packageUsd = await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH", "foreign-usd", "USD");
      packageQar = await AddPackageAsync(db, scope, scope.ClientB, scope.EngagementB, -364m, "REVENUE", "foreign-qar", "QAR");
      await db.SaveChangesAsync();
    }

    Guid componentUsd, componentQar, translationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var mappings = await db.FinancialPackages.Where(x => x.Id == packageUsd || x.Id == packageQar)
        .ToDictionaryAsync(x => x.Id, x => x.MappingVersionId);
      componentUsd = (await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientA, scope.EngagementA, packageUsd, 100m,
          "CONTROLLED", "STATUTORY", "tax-v1", mappings[packageUsd].ToString("D")))).Value;
      componentQar = (await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientB, scope.EngagementB, packageQar, 100m,
          "CONTROLLED", "STATUTORY", "tax-v1", mappings[packageQar].ToString("D")))).Value;
      foreach (var packageId in new[] { packageUsd, packageQar })
      {
        Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
          new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
            FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
            "fx-management", "Management approved the exact component package."))).Succeeded);
        Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
          new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
            FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
            "fx-accounting", "Accounting reviewed the exact component package."))).Succeeded);
        Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
          new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
            FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
            "fx-partner", "Partner approved the exact component package."))).Succeeded);
      }
      Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentUsd)).Succeeded);
      Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentQar)).Succeeded);

      var pinnedInputRejected = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, componentUsd,
        rateSetId, policyId, rateDate, "AVERAGE");
      Assert.False(pinnedInputRejected.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, pinnedInputRejected.ErrorCode);

      translationId = (await CurrencyTranslationService.TranslateComponentAsync(db, preparer, componentUsd,
        rateSetId, policyId, rateDate, "CLOSING")).Value;
      var selfApproval = await CurrencyTranslationService.ApproveTranslationAsync(db, preparer, translationId);
      Assert.False(selfApproval.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, selfApproval.ErrorCode);
      Assert.True((await CurrencyTranslationService.ApproveTranslationAsync(db, reviewer, translationId)).Succeeded);
      var translation = await db.TranslationResults.SingleAsync(x => x.Id == translationId);
      Assert.Equal(364m, translation.TranslatedAmount);
      Assert.Equal(264m, translation.ForeignExchangeAdjustment);
      Assert.Equal(0m, translation.RoundingAdjustment);

      var capabilityId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, groupId, "GROUP_REPORTING", "IFRS", "2026", "ANNUAL", "QAR",
          "STATUTORY", ConsolidationCalculator.ForeignOperationMethod, "PARTNER", "GROUP"))).Value;
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, capabilityId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "fx-local-fixture")).Succeeded);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, methodOwner, capabilityId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "fx-method-owner-fixture")).Succeeded);
      Assert.True((await ConsolidationService.ApproveScopeAsync(db, reviewer, consolidationScopeId)).Succeeded);
    }

    Guid runId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      runId = (await ConsolidationService.RunAsync(db, preparer, consolidationScopeId)).Value;
      var run = await db.ConsolidationRuns.SingleAsync(x => x.Id == runId);
      Assert.Equal(0m, run.SignedTotal);
      Assert.Contains("USD", run.InputManifest);
      Assert.True((await ConsolidationService.ApproveRunAsync(db, reviewer, runId)).Succeeded);
      Assert.Equal(364m, await db.ConsolidationRunLines.Where(x => x.RunId == runId && x.ComponentId == componentUsd)
        .Select(x => x.ConsolidatedAmount).SingleAsync());
      Assert.Equal(-364m, await db.ConsolidationRunLines.Where(x => x.RunId == runId && x.ComponentId == componentQar)
        .Select(x => x.ConsolidatedAmount).SingleAsync());
      Assert.Equal(100m, await db.FinancialPackageLines.Where(x => x.FinancialPackageId == packageUsd).Select(x => x.Amount).SingleAsync());
      Assert.Equal("USD", await db.FinancialPackageLines.Where(x => x.FinancialPackageId == packageUsd).Select(x => x.Currency).SingleAsync());

      var nextScopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", ConsolidationCalculator.ForeignOperationMethod,
          "OPENING-FX-2027", rateSetId, policyId, rateDate.AddYears(1), "CLOSING", consolidationScopeId))).Value;
      var nextScope = await db.ConsolidationScopeVersions.AsNoTracking().SingleAsync(x => x.Id == nextScopeId);
      var priorRunHash = await db.ConsolidationRuns.Where(x => x.Id == runId).Select(x => x.RunHash).SingleAsync();
      Assert.Equal(consolidationScopeId, nextScope.PriorScopeVersionId);
      Assert.Equal(priorRunHash, nextScope.OpeningRunHash);
      Assert.NotEqual(string.Empty, nextScope.OpeningTranslationManifestHash);
      Assert.Equal(0m, nextScope.OpeningTranslationReserve);
      Assert.NotEqual(string.Empty, nextScope.RecurringEliminationManifest);

      Assert.True((await ConsolidationService.AddOwnershipInterestAsync(db, reviewer,
        new OwnershipInterestRequest(nextScopeId, scope.ClientA, scope.ClientB,
          new DateOnly(2027, 1, 1), null, 100m, 100m, "CONTROLLED", "DIRECT",
          "nested-hierarchy-awaits-approved-method"))).Succeeded);
      var nestedScope = await ConsolidationService.ApproveScopeAsync(db, reviewer, nextScopeId);
      Assert.False(nestedScope.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, nestedScope.ErrorCode);

      var nextMapping = await db.FinancialPackages.Where(x => x.Id == packageUsd).Select(x => x.MappingVersionId).SingleAsync();
      var nextComponentId = (await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(nextScopeId, scope.ClientA, scope.EngagementA, packageUsd, 100m,
          "CONTROLLED", "STATUTORY", "tax-v1", nextMapping.ToString("D")))).Value;
      Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, nextComponentId)).Succeeded);
      var changedClientId = Guid.NewGuid();
      db.PracticeClients.Add(new PracticeClient { Id = changedClientId, FirmId = scope.FirmId, LegalName = "CLIENT C", CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, changedClientId, new DateOnly(2027, 1, 1), null, "CONTROLLED", 100m, 100m,
          "ownership-c-added-after-next-scope"))).Succeeded);
      var staleTranslation = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, nextComponentId,
        rateSetId, policyId, rateDate.AddYears(1), "CLOSING");
      Assert.False(staleTranslation.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleTranslation.ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GroupWorkflow_UsesApprovedComponentPackagesWithoutMutatingThem()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    Guid groupId, consolidationScopeId, matchId, outsideMatchId;
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
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "", ConsolidationCalculator.RestrictedMethod, "OPENING-2026"))).Value;
      Assert.Equal(AccountingDefaults.DefaultCurrency,
        await db.ConsolidationScopeVersions.Where(x => x.Id == consolidationScopeId).Select(x => x.ReportingCurrency).SingleAsync());
      await AddPackageAsync(db, scope, scope.ClientA, scope.EngagementA, 100m, "CASH");
      await AddPackageAsync(db, scope, scope.ClientB, scope.EngagementB, -100m, "REVENUE");
      await db.SaveChangesAsync();
    }

    Guid packageA, packageB;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      packageA = await db.FinancialPackages.Where(x => x.ClientId == scope.ClientA).Select(x => x.Id).SingleAsync();
      packageB = await db.FinancialPackages.Where(x => x.ClientId == scope.ClientB).Select(x => x.Id).SingleAsync();
      var packageMappings = await db.FinancialPackages.Where(x => x.Id == packageA || x.Id == packageB)
        .ToDictionaryAsync(x => x.Id, x => x.MappingVersionId);
      var invalidLineage = await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientA, scope.EngagementA, packageA, 100m,
          "CONTROLLED", "STATUTORY", "tax-v1", "not-a-mapping-id"));
      Assert.False(invalidLineage.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, invalidLineage.ErrorCode);
      Assert.True((await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientA, scope.EngagementA, packageA, 100m, "CONTROLLED", "STATUTORY", "tax-v1", packageMappings[packageA].ToString("D")))).Succeeded);
      Assert.True((await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientB, scope.EngagementB, packageB, 100m, "CONTROLLED", "STATUTORY", "tax-v1", packageMappings[packageB].ToString("D")))).Succeeded);
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

      var missingDifferenceReason = await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, scope.ClientB,
          ConsolidationEliminationKinds.ReceivablePayable, "2026", "QAR", "IC-001", 100m, -90m, 90m, "ic-evidence"));
      Assert.False(missingDifferenceReason.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, missingDifferenceReason.ErrorCode);
      var unsupportedNature = await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, scope.ClientB,
          "UNCLASSIFIED", "2026", "QAR", "IC-UNSUPPORTED", 100m, -100m, 100m, "ic-evidence",
          "CASH", "REVENUE"));
      Assert.False(unsupportedNature.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, unsupportedNature.ErrorCode);
      matchId = (await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, scope.ClientB,
          ConsolidationEliminationKinds.ReceivablePayable, "2026", "QAR", "IC-001", 100m, -100m, 100m, "ic-evidence",
            "CASH", "REVENUE"))).Value;
      Assert.True((await ConsolidationService.ApproveIntercompanyMatchAsync(db, reviewer, matchId)).Succeeded);

      var groupedFirst = (await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, scope.ClientB,
          ConsolidationEliminationKinds.ReceivablePayable, "2026", "QAR", "IC-G-001", 10m, -10m, 10m, "ic-grouped-a",
            "CASH", "REVENUE", "", IntercompanyMatchModes.Grouped, "IC-GROUP-001"))).Value;
      var incompleteGroupedApproval = await ConsolidationService.ApproveIntercompanyMatchAsync(db, reviewer, groupedFirst);
      Assert.False(incompleteGroupedApproval.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, incompleteGroupedApproval.ErrorCode);
      var groupedSecond = (await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, scope.ClientB,
          ConsolidationEliminationKinds.ReceivablePayable, "2026", "QAR", "IC-G-002", 20m, -20m, 20m, "ic-grouped-b",
            "CASH", "REVENUE", "", IntercompanyMatchModes.Grouped, "IC-GROUP-001"))).Value;
      Assert.True((await ConsolidationService.ApproveIntercompanyMatchAsync(db, reviewer, groupedFirst)).Succeeded);
      Assert.True((await ConsolidationService.ApproveIntercompanyMatchAsync(db, reviewer, groupedSecond)).Succeeded);

      var outsideClientId = Guid.NewGuid();
      db.PracticeClients.Add(new PracticeClient { Id = outsideClientId, FirmId = scope.FirmId, LegalName = "RELATED PARTY OUTSIDE GROUP", CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      outsideMatchId = (await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, outsideClientId,
          "RELATED_PARTY", "2026", "QAR", "IC-OUTSIDE-001", 15m, -15m, 15m, "outside-related-party-review",
            "CASH", "REVENUE", "Outside the approved consolidation perimeter", IntercompanyMatchModes.OneToOne, "", true))).Value;
      Assert.True((await ConsolidationService.ApproveIntercompanyMatchAsync(db, reviewer, outsideMatchId)).Succeeded);
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
      var linkedJournalId = (await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(consolidationScopeId, "GC-LINKED", "GROUP_RECLASSIFICATION", "QAR", "linked-match-adjustment",
        [new(matchId, "CASH", 100m, 0m, "Reviewed linked elimination"),
         new(null, "REVENUE", 0m, 100m, "Reviewed linked elimination")]))).Value;
      Assert.True((await ConsolidationService.ApproveConsolidationJournalAsync(db, reviewer, linkedJournalId)).Succeeded);
      var duplicateLinkedJournal = await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(consolidationScopeId, "GC-DUPLICATE-LINK", "GROUP_RECLASSIFICATION", "QAR", "duplicate-linked-match",
        [new(matchId, "CASH", 100m, 0m, "Must not duplicate a reviewed match"),
         new(null, "REVENUE", 0m, 100m, "Must not duplicate a reviewed match")]));
      Assert.False(duplicateLinkedJournal.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicateLinkedJournal.ErrorCode);
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
      Assert.Equal(10, await db.ConsolidationRunLines.CountAsync(x => x.RunId == runId));
      Assert.Equal(2, await db.ConsolidationRunLines.CountAsync(x => x.RunId == runId && x.ConsolidationJournalId == journalId));
      Assert.Equal(2, await db.ConsolidationRunLines.CountAsync(x => x.RunId == runId && x.IntercompanyMatchId == matchId));
      Assert.Equal(12, await db.ConsolidationRunLines.CountAsync(x => x.RunId == rebuiltRunId));
      Assert.Equal(2, await db.ConsolidationRunLines.CountAsync(x => x.RunId == rebuiltRunId && x.ConsolidationJournalId == changedJournalId));
      Assert.Equal(1, await db.ConsolidationRunLines.CountAsync(x => x.RunId == rebuiltRunId && x.IntercompanyMatchId == matchId));
      Assert.Empty(await db.ConsolidationRunLines.Where(x => x.RunId == rebuiltRunId && x.IntercompanyMatchId == outsideMatchId).ToListAsync());
      Assert.All(await db.ConsolidationComponents.Where(x => x.ScopeVersionId == consolidationScopeId).ToListAsync(), x => Assert.Equal(AccountingWorkflowStates.Approved, x.Status));

      var newClientId = Guid.NewGuid();
      db.PracticeClients.Add(new PracticeClient { Id = newClientId, FirmId = scope.FirmId, LegalName = "CLIENT C", CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, newClientId, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m,
          "ownership-c-added-after-scope"))).Succeeded);
      var staleJournal = await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(consolidationScopeId, "GC-STALE", "GROUP_RECLASSIFICATION", "QAR", "stale-group-adjustment",
        [new(null, "CASH", 10m, 0m, "Must not write against a changed group perimeter"),
         new(null, "REVENUE", 0m, 10m, "Must not write against a changed group perimeter")]));
      Assert.False(staleJournal.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleJournal.ErrorCode);
      var changedPerimeter = await ConsolidationService.RunAsync(db, preparer, consolidationScopeId);
      Assert.False(changedPerimeter.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, changedPerimeter.ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ExternalComponentPack_WorkflowRequiresReconciliationAndPreservesHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    Guid groupId, scopeId, returnedPackId, approvedPackId, componentId;
    var lines = new[]
    {
      new ExternalComponentPackLineRequest("CASH", 100m, "QAR", "external-line-1"),
      new ExternalComponentPackLineRequest("REVENUE", -100m, "QAR", "external-line-2")
    };

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
        new ClientGroupRequest("GROUP-EXT", "External component group"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientA, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m,
          "external-membership"))).Succeeded);
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
      db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "Partner"));
      await db.SaveChangesAsync();
      scopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", ConsolidationCalculator.RestrictedMethod,
          "OPENING-2026"))).Value;

      returnedPackId = (await ConsolidationService.SubmitExternalComponentPackAsync(db, preparer,
        new ExternalComponentPackRequest(scopeId, scope.ClientA, scope.EngagementA, "2026-01-01", "2026-12-31", "IFRS",
          "QAR", "STATUTORY", "tax-external-v1", "mapping-external-v1", "client-upload-001",
          Hashing.Sha256Hex("raw-external-1"), Hashing.Sha256Hex("normalized-external-1"), 0m, lines))).Value;
      var unreconciled = await ConsolidationService.ApproveExternalComponentPackAsync(db, reviewer, returnedPackId);
      Assert.False(unreconciled.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, unreconciled.ErrorCode);
      Assert.True((await ConsolidationService.ReconcileExternalComponentPackAsync(db, reviewer,
        new ExternalComponentReconciliationRequest(returnedPackId, "external-tb-reconciliation-1"))).Succeeded);
      Assert.True((await ConsolidationService.ReturnExternalComponentPackAsync(db, reviewer, returnedPackId,
        "The client must resubmit the signed pack with the corrected source receipt.")).Succeeded);

      var missingBridge = await ConsolidationService.SubmitExternalComponentPackAsync(db, preparer,
        new ExternalComponentPackRequest(scopeId, scope.ClientA, scope.EngagementA, "2026-01-01", "2026-12-31", "IFRS",
          "QAR", "MANAGEMENT", "tax-external-v1", "mapping-external-v2", "client-upload-002",
          Hashing.Sha256Hex("raw-external-2"), Hashing.Sha256Hex("normalized-external-2"), 0m, lines, returnedPackId));
      Assert.False(missingBridge.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, missingBridge.ErrorCode);

      approvedPackId = (await ConsolidationService.SubmitExternalComponentPackAsync(db, preparer,
        new ExternalComponentPackRequest(scopeId, scope.ClientA, scope.EngagementA, "2026-01-01", "2026-12-31", "IFRS",
          "QAR", "MANAGEMENT", "tax-external-v1", "mapping-external-v2", "client-upload-002",
          Hashing.Sha256Hex("raw-external-2"), Hashing.Sha256Hex("normalized-external-2"), 0m, lines, returnedPackId,
          "basis-bridge-reviewed-by-group-accounting"))).Value;
      Assert.True((await ConsolidationService.ReconcileExternalComponentPackAsync(db, reviewer,
        new ExternalComponentReconciliationRequest(approvedPackId, "external-tb-reconciliation-2"))).Succeeded);
      Assert.True((await ConsolidationService.ApproveExternalComponentPackAsync(db, reviewer, approvedPackId)).Succeeded);

      componentId = (await ConsolidationService.SubmitExternalComponentAsync(db, preparer,
        new ExternalComponentRequest(scopeId, approvedPackId))).Value;
      Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId)).Succeeded);

      var capabilityId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, groupId, "GROUP_REPORTING", "IFRS", "2026", "ANNUAL", "QAR",
          "STATUTORY", ConsolidationCalculator.RestrictedMethod, "PARTNER", "GROUP"))).Value;
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, capabilityId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "external-pack-local-fixture")).Succeeded);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, Actor(scope.Preparer, "Partner"), capabilityId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "external-pack-method-owner-fixture")).Succeeded);
      Assert.True((await ConsolidationService.ApproveScopeAsync(db, reviewer, scopeId)).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var history = await db.ExternalComponentPacks.AsNoTracking().Where(x => x.ScopeVersionId == scopeId)
        .OrderBy(x => x.Version).ToListAsync();
      Assert.Collection(history,
        first =>
        {
          Assert.Equal(returnedPackId, first.Id);
          Assert.Equal(ExternalComponentPackStates.Returned, first.Status);
          Assert.Null(first.PriorPackId);
        },
        second =>
        {
          Assert.Equal(approvedPackId, second.Id);
          Assert.Equal(ExternalComponentPackStates.Approved, second.Status);
          Assert.Equal(returnedPackId, second.PriorPackId);
          Assert.Equal(ExternalComponentBridgeStates.Approved, second.CompatibilityBridgeStatus);
          Assert.Equal("basis-bridge-reviewed-by-group-accounting", second.CompatibilityBridgeReference);
          Assert.Equal(ExternalComponentReconciliationStates.Reconciled, second.ReconciliationStatus);
        });
      Assert.Null(await db.ConsolidationComponents.Where(x => x.Id == componentId).Select(x => x.PackageId).SingleAsync());
      Assert.Equal(approvedPackId, await db.ConsolidationComponents.Where(x => x.Id == componentId)
        .Select(x => x.ExternalComponentPackId).SingleAsync());

      var run = await ConsolidationService.RunAsync(db, preparer, scopeId);
      Assert.True(run.Succeeded, run.Message);
      Assert.Equal(0m, await db.ConsolidationRuns.Where(x => x.Id == run.Value).Select(x => x.SignedTotal).SingleAsync());
      Assert.Equal(2, await db.ConsolidationRunLines.CountAsync(x => x.RunId == run.Value));
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

  private static async Task<Guid> AddPackageAsync(AuditSphereDbContext db, Scope scope, Guid clientId, Guid engagementId, decimal amount,
    string destination, string? suffix = null, string currency = "QAR")
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
      Revision = 1, LegalEntityKey = clientId.ToString("D"), Currency = currency, RawFileSha256Hex = digest,
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
      AdjustmentPlanId = planId, Currency = currency, ResultHash = digest, CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });
    db.FinancialPackages.Add(new FinancialPackage
    {
      Id = packageId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, AdjustedDatasetId = adjustedId,
      MappingVersionId = mappingId, AdjustmentPlanId = planId, Framework = "IFRS", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      TaxonomyVersion = "tax-v1", TemplateVersion = "template-v1", CalculationEngineVersion = "test-engine",
      CalculationHash = digest, Currency = currency, Status = AccountingPackageStates.PackageValidated, CreatedAt = now
    });
    db.FinancialPackageLines.Add(new FinancialPackageLine
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, FinancialPackageId = packageId,
      SourceAccountCode = destination == "CASH" ? "1000" : "4000", DestinationCode = destination, StatementSection = "STATEMENT",
      Amount = amount, Fraction = 1m, Currency = currency, AdjustedSnapshotId = adjustedId, CreatedAt = now
    });
    var artifactBytes = System.Text.Encoding.UTF8.GetBytes($"package-artifact|{packageId:D}|{digest}");
    db.FinancialPackageArtifacts.Add(new FinancialPackageArtifact
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId,
      FinancialPackageId = packageId, PackageRevision = 1, PackageGeneration = 1, PackageHash = digest,
      ArtifactVersion = FinancialPackageArtifactVersions.Text, FrameworkVersion = "IFRS", TemplateVersion = "template-v1",
      ArtifactSha256Hex = Hashing.Sha256Hex(artifactBytes), ArtifactBytes = artifactBytes,
      CreatedByUserId = scope.Preparer.Id, CreatedAt = now
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
