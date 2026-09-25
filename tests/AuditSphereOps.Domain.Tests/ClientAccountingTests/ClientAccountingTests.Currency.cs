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
  [Trait("Profile", "Unit")]
  public void CurrencyOperations_KeepRemeasurementTranslationAndDisplaySeparate()
  {
    var monetary = CurrencyRemeasurementCalculator.Remeasure(100m, "USD", "QAR", true, 3.7m, 3.6m, 360m);
    var nonMonetary = CurrencyRemeasurementCalculator.Remeasure(100m, "USD", "QAR", false, 3.7m, 3.6m, 360m);
    var liability = CurrencyRemeasurementCalculator.Remeasure(-100m, "USD", "QAR", true, 3.7m, 3.6m, -360m);
    Assert.Equal("CLOSING", monetary.RateBasis);
    Assert.Equal(370m, monetary.RemeasuredAmount);
    Assert.Equal("HISTORICAL", nonMonetary.RateBasis);
    Assert.Equal(360m, nonMonetary.RemeasuredAmount);
    Assert.Equal(10m, monetary.ForeignExchangeAdjustment);
    Assert.Equal(0m, nonMonetary.ForeignExchangeAdjustment);
    Assert.Equal(-10m, liability.ForeignExchangeAdjustment);

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
    Assert.Equal(10m, CurrencyRemeasurementCalculator.Remeasure(100m, "USD", "QAR", true, 3.7m, 0m, 360m).ForeignExchangeAdjustment);
    Assert.Equal(0m, CurrencyRemeasurementCalculator.Remeasure(100m, "USD", "QAR", false, 0m, 3.6m, 360m).ForeignExchangeAdjustment);
    Assert.Throws<InvalidOperationException>(() => CurrencyRemeasurementCalculator.Remeasure(100m, "USD", "QAR", true, 0m, 3.6m, 360m));

    Assert.Equal(370m, DisplayCurrencyConversionCalculator.Convert(100m, "USD", "QAR", 3.7m));
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task CurrencyRemeasurement_IsEvidenceBoundAndRequiresIndependentApproval()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    Guid periodId, rateSetId, policyId, monetarySnapshotId, nonMonetarySnapshotId;
    var sourceLineIds = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToArray();
    var asOf = new DateOnly(2026, 12, 31);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparer,
        new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "LEDGER-A", "A-1"))).Succeeded);
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), asOf, "STATUTORY", "QAR"))).Value;

      rateSetId = Guid.CreateVersion7();
      policyId = Guid.CreateVersion7();
      var now = DateTimeOffset.UtcNow;
      db.ExchangeRateSetVersions.Add(new ExchangeRateSetVersion
      {
        Id = rateSetId, FirmId = scope.FirmId, Code = "FX-REMEASURE-2026", Version = 1, Source = "approved test evidence",
        EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = asOf, Status = AccountingWorkflowStates.Approved,
        CreatedByUserId = scope.Preparer.Id, ApprovedByUserId = scope.Reviewer.Id, CreatedAt = now, ApprovedAt = now
      });
      db.TranslationPolicyVersions.Add(new TranslationPolicyVersion
      {
        Id = policyId, FirmId = scope.FirmId, Code = "FX-REMEASURE-IFRS", FunctionalCurrency = "QAR",
        PresentationCurrency = "QAR", ClosingRateRule = "CLOSING", AverageRateRule = "AVERAGE",
        HistoricalRateRule = "HISTORICAL", Status = AccountingWorkflowStates.Approved,
        CreatedByUserId = scope.Preparer.Id, ApprovedByUserId = scope.Reviewer.Id, CreatedAt = now, ApprovedAt = now
      });
      db.ExchangeRates.AddRange(
        new ExchangeRate { Id = Guid.CreateVersion7(), FirmId = scope.FirmId, RateSetVersionId = rateSetId, FromCurrency = "USD", ToCurrency = "QAR", RateDate = asOf, RateType = "CLOSING", Rate = 3.7m, Direction = "DIRECT", CreatedAt = now },
        new ExchangeRate { Id = Guid.CreateVersion7(), FirmId = scope.FirmId, RateSetVersionId = rateSetId, FromCurrency = "USD", ToCurrency = "QAR", RateDate = new DateOnly(2026, 1, 1), RateType = "HISTORICAL", Rate = 3.6m, Direction = "DIRECT", CreatedAt = now });

      var bindingId = Guid.CreateVersion7();
      var documentReferenceId = Guid.CreateVersion7();
      db.RepositoryBindings.Add(new RepositoryBinding
      {
        Id = bindingId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        TenantId = "tenant-test", SiteId = "site-test", DriveId = "drive-test", RootFolderId = "root-test",
        Classification = "WORKING", DesiredAccess = "APP_MEDIATED", ObservedAccess = "APP_MEDIATED",
        CapabilityProfile = "TEST", CreatedAt = now
      });
      db.DocumentReferences.Add(new DocumentReference
      {
        Id = documentReferenceId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        RepositoryBindingId = bindingId, Provider = "SharePoint", DriveId = "drive-test", ItemId = "evidence-test",
        Path = "/AuditSphere/Test/open-items.xlsx", Purpose = "Evidence", CreatedAt = now
      });
      monetarySnapshotId = Guid.CreateVersion7();
      nonMonetarySnapshotId = Guid.CreateVersion7();
      db.DocumentSnapshots.AddRange(
        new DocumentSnapshot { Id = monetarySnapshotId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA, DocumentReferenceId = documentReferenceId, DriveId = "drive-test", ItemId = "evidence-test", VersionId = "v1", Sha256Hex = new string('a', 64), ByteCount = 100, CapturedBy = "test", CapturedAt = now },
        new DocumentSnapshot { Id = nonMonetarySnapshotId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA, DocumentReferenceId = documentReferenceId, DriveId = "drive-test", ItemId = "evidence-test", VersionId = "v2", Sha256Hex = new string('b', 64), ByteCount = 100, CapturedBy = "test", CapturedAt = now });
      var importBatchId = Guid.CreateVersion7();
      db.SourceImportBatches.Add(new SourceImportBatch
      {
        Id = importBatchId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PeriodId = periodId, SourceKind = "GL", ProfileVersion = "test-v1", ParserVersion = "test-v1",
        RawFileSha256Hex = new string('c', 64), NormalizedDatasetDigest = new string('d', 64),
        LegalEntityKey = "SYNTHETIC-ENTITY", Currency = "QAR", RowCount = 6,
        ExpectedTransactionCount = 3, ExpectedLineCount = 6, AcceptedTransactionCount = 3, AcceptedLineCount = 6,
        Status = "SEALED", ReceiptReference = "synthetic-gl-test", CreatedByUserId = scope.Preparer.Id, CreatedAt = now
      });
      for (var index = 0; index < sourceLineIds.Length; index++)
      {
        var transactionId = Guid.CreateVersion7();
        db.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
        {
          Id = transactionId, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
          ImportBatchId = importBatchId, StableJournalId = $"SYN-GL-{index + 1}",
          PostingDate = asOf, Currency = "QAR", SourceSystem = "synthetic-test", CreatedAt = now
        });
        db.GeneralLedgerLines.AddRange(
          new GeneralLedgerLine
          {
            Id = sourceLineIds[index], FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
            ImportBatchId = importBatchId, TransactionId = transactionId, StableLineId = $"FOREIGN-{index + 1}",
            AccountCode = "1200", Debit = 370m, OriginalCurrency = "USD", OriginalAmount = 100m,
            FunctionalAmount = 370m, CreatedAt = now
          },
          new GeneralLedgerLine
          {
            Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
            ImportBatchId = importBatchId, TransactionId = transactionId, StableLineId = $"OFFSET-{index + 1}",
            AccountCode = "4000", Credit = 370m, OriginalCurrency = "QAR", OriginalAmount = -370m,
            FunctionalAmount = -370m, CreatedAt = now
          });
      }
      await db.SaveChangesAsync();
    }

    Guid scheduleId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var request = new CurrencyRemeasurementScheduleRequest(scope.ClientA, scope.EngagementA, periodId,
        rateSetId, policyId, asOf,
        [new("AR-INV-100", monetarySnapshotId, true, "USD", 100m, 360m, null, sourceLineIds[0]),
         new("EQUITY-HIST-1", nonMonetarySnapshotId, false, "USD", 100m, 360m, new DateOnly(2026, 1, 1), sourceLineIds[1])]);
      var prepared = await CurrencyRemeasurementService.PrepareAsync(db, preparer, request);
      Assert.True(prepared.Succeeded, prepared.Message);
      scheduleId = prepared.Value;
      var replay = await CurrencyRemeasurementService.PrepareAsync(db, preparer, request);
      Assert.True(replay.Succeeded);
      Assert.Equal(scheduleId, replay.Value);
      var selfApproval = await CurrencyRemeasurementService.ApproveAsync(db, preparer, scheduleId);
      Assert.False(selfApproval.Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var approved = await CurrencyRemeasurementService.ApproveAsync(db, reviewer, scheduleId);
      Assert.True(approved.Succeeded, $"{approved.ErrorCode}: {approved.Message}");
      var view = await CurrencyRemeasurementService.GetAsync(db, preparer, scheduleId);
      Assert.True(view.Succeeded);
      Assert.Equal(AccountingWorkflowStates.Approved, view.Value!.Status);
      Assert.Equal(10m, view.Value.TotalForeignExchangeAdjustment);
      Assert.Equal(2, view.Value.Items.Count);
      Assert.Equal(10m, view.Value.Items.Single(x => x.IsMonetary).ForeignExchangeAdjustment);
      Assert.Equal(0m, view.Value.Items.Single(x => !x.IsMonetary).ForeignExchangeAdjustment);
      Assert.Equal(new string('a', 64), view.Value.Items.Single(x => x.IsMonetary).EvidenceSha256);
      Assert.Equal(sourceLineIds[0], view.Value.Items.Single(x => x.IsMonetary).SourceGeneralLedgerLineId);
      Assert.Matches("^[0-9a-f]{64}$", view.Value.Items.Single(x => x.IsMonetary).SourceGlLineDigest);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var staleRequest = new CurrencyRemeasurementScheduleRequest(scope.ClientA, scope.EngagementA, periodId,
        rateSetId, policyId, asOf,
        [new("AR-INV-101", monetarySnapshotId, true, "USD", 100m, 355m, null, sourceLineIds[2])]);
      var stalePrepared = await CurrencyRemeasurementService.PrepareAsync(db, preparer, staleRequest);
      Assert.True(stalePrepared.Succeeded, stalePrepared.Message);
      var importBatchId = await db.GeneralLedgerLines.AsNoTracking().Where(x => x.Id == sourceLineIds[2])
        .Select(x => x.ImportBatchId).SingleAsync();
      await db.SourceImportBatches.Where(x => x.Id == importBatchId).ExecuteUpdateAsync(setters =>
        setters.SetProperty(x => x.Status, "REJECTED"));
      var staleApproval = await CurrencyRemeasurementService.ApproveAsync(db, reviewer, stalePrepared.Value);
      Assert.False(staleApproval.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleApproval.ErrorCode);
      Assert.Equal(AccountingWorkflowStates.Stale, await db.CurrencyRemeasurementSchedules
        .Where(x => x.Id == stalePrepared.Value).Select(x => x.Status).SingleAsync());
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE general_ledger_lines SET original_amount = 101 WHERE id = {sourceLineIds[0]}"));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM general_ledger_transactions WHERE id = (SELECT transaction_id FROM general_ledger_lines WHERE id = {sourceLineIds[0]})"));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM currency_remeasurement_items WHERE schedule_id = {scheduleId}"));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM currency_remeasurement_schedules WHERE id = {scheduleId}"));
    }
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

    Guid componentUsd, componentQar, translationId, legacyTranslationId;
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
      var partner = Actor(scope.Partner, "Partner");
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
        Assert.True((await FinancialPackageReviewService.RecordAsync(db, partner,
          new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
            FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
            "fx-partner", "Partner approved the exact component package."))).Succeeded);
      }
      Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentUsd)).Succeeded);
      Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentQar)).Succeeded);

      var legacyComponent = await db.ConsolidationComponents.SingleAsync(x => x.Id == componentUsd);
      legacyTranslationId = Guid.CreateVersion7();
      db.TranslationResults.Add(new TranslationResult
      {
        Id = legacyTranslationId, FirmId = scope.FirmId, GroupId = groupId, ScopeVersionId = consolidationScopeId,
        ComponentId = componentUsd, RateSetVersionId = rateSetId, TranslationPolicyVersionId = policyId,
        CalculationVersion = TranslationCalculationVersions.ComponentTranslationV1,
        SourcePackageHash = legacyComponent.PackageHash, RateDate = rateDate, RateType = "CLOSING", AppliedRate = 3.64m,
        FromCurrency = "USD", ToCurrency = "QAR", TranslatedAmount = 364m, ForeignExchangeAdjustment = 264m,
        Status = AccountingWorkflowStates.Approved, CreatedByUserId = preparer.UserId, ApprovedByUserId = reviewer.UserId,
        CreatedAt = DateTimeOffset.UtcNow, ApprovedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();

      var pinnedInputRejected = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, componentUsd,
        rateSetId, policyId, rateDate, "AVERAGE");
      Assert.False(pinnedInputRejected.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, pinnedInputRejected.ErrorCode);

      var stalePreparer = new ActorContext(preparer.UserId, preparer.FirmId, preparer.SessionEpoch + 1, preparer.Roles);
      var staleTranslation = await CurrencyTranslationService.TranslateComponentAsync(db, stalePreparer, componentUsd,
        rateSetId, policyId, rateDate, "CLOSING");
      Assert.False(staleTranslation.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, staleTranslation.ErrorCode);

      translationId = (await CurrencyTranslationService.TranslateComponentAsync(db, preparer, componentUsd,
        rateSetId, policyId, rateDate, "CLOSING")).Value;
      var selfApproval = await CurrencyTranslationService.ApproveTranslationAsync(db, preparer, translationId);
      Assert.False(selfApproval.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, selfApproval.ErrorCode);

      var unrelatedPartner = User(scope.FirmId, "unrelated-group-partner");
      db.Users.Add(unrelatedPartner);
      db.RoleGrants.Add(Grant(scope.FirmId, unrelatedPartner, "Partner"));
      await db.SaveChangesAsync();
      var unrelatedApproval = await CurrencyTranslationService.ApproveTranslationAsync(db,
        Actor(unrelatedPartner, "Partner"), translationId);
      Assert.False(unrelatedApproval.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, unrelatedApproval.ErrorCode);
      Assert.True((await CurrencyTranslationService.ApproveTranslationAsync(db, reviewer, translationId)).Succeeded);
      var translation = await db.TranslationResults.SingleAsync(x => x.Id == translationId);
      Assert.Equal(364m, translation.TranslatedAmount);
      Assert.Equal(TranslationCalculationVersions.ComponentTranslationV2, translation.CalculationVersion);
      Assert.Equal(0m, translation.ForeignExchangeAdjustment);
      Assert.Equal(0m, translation.RoundingAdjustment);
      var legacyTranslation = await db.TranslationResults.SingleAsync(x => x.Id == legacyTranslationId);
      Assert.Equal(TranslationCalculationVersions.ComponentTranslationV1, legacyTranslation.CalculationVersion);
      Assert.Equal(264m, legacyTranslation.ForeignExchangeAdjustment);

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

  [Fact(DisplayName = "GOLD-R2R-07: Foreign operation translates per-line with -48 reserve, balances to 0, and consolidates")]
  [Trait("Profile", "Database")]
  public async Task ForeignOperation_GoldR2R07_TranslatesPerLineWithExplainedReserveAndConsolidates()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    var partner = Actor(scope.Partner, "Partner");
    var methodOwner = Actor(scope.Partner, "Partner");
    var rateDate = new DateOnly(2026, 12, 31);
    Guid rateSetId, policyId, consolidationScopeId, groupId, packageGold7;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
        new ClientGroupRequest("GROUP-GOLD-7", "Consolidation Group for GOLD-R2R-07"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientA, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m, "sub-gold7"))).Succeeded);
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = scope.Preparer.Id,
        Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = scope.Partner.Id,
        Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id
      });

      rateSetId = (await CurrencyTranslationService.CreateRateSetAsync(db, reviewer,
        new ExchangeRateSetRequest("FX-RATES-GOLD7", "Central Bank", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1))).Value;

      // Rates for GOLD-R2R-07: Closing 4.0, Historical 3.5, Average 3.6
      Assert.True((await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
        new ExchangeRateInput("USD", "QAR", rateDate, "CLOSING", 4.0m, "DIRECT"))).Succeeded);
      Assert.True((await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
        new ExchangeRateInput("USD", "QAR", rateDate, "HISTORICAL", 3.5m, "DIRECT"))).Succeeded);
      Assert.True((await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
        new ExchangeRateInput("USD", "QAR", rateDate, "AVERAGE", 3.6m, "DIRECT"))).Succeeded);
      Assert.True((await CurrencyTranslationService.ApproveRateSetAsync(db, methodOwner, rateSetId)).Succeeded);

      policyId = (await CurrencyTranslationService.CreatePolicyAsync(db, reviewer,
        new TranslationPolicyRequest("FX-POL-GOLD7", "USD", "QAR", "CLOSING", "AVERAGE", "HISTORICAL"))).Value;
      Assert.True((await CurrencyTranslationService.ApprovePolicyAsync(db, methodOwner, policyId)).Succeeded);

      consolidationScopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", ConsolidationCalculator.ForeignOperationMethod,
          "OPENING-GOLD7", rateSetId, policyId, rateDate, "CLOSING"))).Value;

      // GOLD-R2R-07 lines: Cash 100 (ASSETS), Capital -80 (EQUITY), Revenue -40 (INCOME), Expense 20 (EXPENSE)
      packageGold7 = await AddMultiLinePackageAsync(db, scope, scope.ClientA, scope.EngagementA,
      [
        ("CASH", "ASSETS", 100m),
        ("CAPITAL", "EQUITY", -80m),
        ("REVENUE", "INCOME", -40m),
        ("EXPENSE", "EXPENSE", 20m)
      ], "gold7", "USD");
      await db.SaveChangesAsync();
    }

    Guid componentId, translationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var mappingId = await db.FinancialPackages.Where(x => x.Id == packageGold7).Select(x => x.MappingVersionId).SingleAsync();
      componentId = (await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientA, scope.EngagementA, packageGold7, 100m,
          "CONTROLLED", "STATUTORY", "tax-v1", mappingId.ToString("D")))).Value;

      Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageGold7, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline, "fx", "Approved."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageGold7, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn, "fx", "Reviewed."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db, partner,
        new FinancialPackageReviewRequest(packageGold7, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn, "fx", "Approved."))).Succeeded);
      var approveRes = await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId);
      Assert.True(approveRes.Succeeded, approveRes.Message);

      // Translate component
      var translateResult = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, componentId,
        rateSetId, policyId, rateDate, "CLOSING");
      Assert.True(translateResult.Succeeded);
      translationId = translateResult.Value;

      var translation = await db.TranslationResults.SingleAsync(x => x.Id == translationId);
      // GOLD-R2R-07: cash 100*4.0 = 400, capital -80*3.5 = -280, revenue -40*3.6 = -144, expense 20*3.6 = 72
      // Translation reserve = -48
      // TranslatedTotal including reserve = 0m
      Assert.Equal(0m, translation.TranslatedAmount);
      Assert.Equal(-48m, translation.TranslationReserve);
      Assert.Equal(-48m, translation.ForeignExchangeAdjustment);
      Assert.Equal(0m, translation.RoundingAdjustment);

      // Approve translation
      Assert.True((await CurrencyTranslationService.ApproveTranslationAsync(db, reviewer, translationId)).Succeeded);

      // Capability profile and method owner approval
      var capabilityId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, groupId, "GROUP_REPORTING", "IFRS", "2026", "ANNUAL", "QAR",
          "STATUTORY", ConsolidationCalculator.ForeignOperationMethod, "PARTNER", "GROUP"))).Value;
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, capabilityId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "fx-gold7-local")).Succeeded);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, methodOwner, capabilityId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "fx-gold7-method-owner")).Succeeded);

      // Approve scope
      var approveScopeRes = await ConsolidationService.ApproveScopeAsync(db, reviewer, consolidationScopeId);
      Assert.True(approveScopeRes.Succeeded, approveScopeRes.Message);

      // Run consolidation
      var runResult = await ConsolidationService.RunAsync(db, preparer, consolidationScopeId);
      Assert.True(runResult.Succeeded);
      var runId = runResult.Value;

      var run = await db.ConsolidationRuns.SingleAsync(x => x.Id == runId);
      Assert.Equal(0m, run.SignedTotal);

      var lines = await db.ConsolidationRunLines.Where(x => x.RunId == runId).ToListAsync();
      var cashLine = lines.Single(x => x.TaxonomyCode == "CASH");
      var capLine = lines.Single(x => x.TaxonomyCode == "CAPITAL");
      var revLine = lines.Single(x => x.TaxonomyCode == "REVENUE");
      var expLine = lines.Single(x => x.TaxonomyCode == "EXPENSE");
      var ctaLine = lines.Single(x => x.TaxonomyCode == AccountingDefaults.CumulativeTranslationReserveSection);

      Assert.Equal(400m, cashLine.ConsolidatedAmount);
      Assert.Equal(-280m, capLine.ConsolidatedAmount);
      Assert.Equal(-144m, revLine.ConsolidatedAmount);
      Assert.Equal(72m, expLine.ConsolidatedAmount);
      Assert.Equal(-48m, ctaLine.ConsolidatedAmount);

      // Sum of consolidated lines is identically 0
      Assert.Equal(0m, lines.Sum(x => x.ConsolidatedAmount));
    }
  }

  private static readonly IReadOnlyList<(string Destination, string Section, decimal Amount)> Gold07Lines =
  [
    ("CASH", "ASSETS", 100m),
    ("CAPITAL", "EQUITY", -80m),
    ("REVENUE", "INCOME", -40m),
    ("EXPENSE", "EXPENSE", 20m)
  ];

  private static readonly IReadOnlyList<(string RateType, decimal Rate)> Gold07Rates =
  [
    ("CLOSING", 4.0m),
    ("HISTORICAL", 3.5m),
    ("AVERAGE", 3.6m)
  ];

  private sealed record ForeignOperationFixture(
    Guid GroupId, Guid RateSetId, Guid PolicyId, Guid ScopeId, Guid PackageId, Guid ComponentId);

  /// <summary>
  /// Builds the reviewed foreign-operation fixture up to an approved component: group with one
  /// controlled member, approved rate set and translation policy, draft scope, and one package
  /// whose management, accounting and partner reviews are complete. Translation and scope
  /// approval stay in the test so each case can pin its own inputs and expectations. The rate
  /// rows are always USD-to-QAR observations that populate the set for approval; a same-currency
  /// component (functionalCurrency "QAR") consumes none of them because identity needs no rate.
  /// </summary>
  private static async Task<ForeignOperationFixture> SeedForeignOperationAsync(
    AuditSphereDbContext db, Scope scope,
    IReadOnlyList<(string Destination, string Section, decimal Amount)> lines,
    IReadOnlyList<(string RateType, decimal Rate)> rates,
    string suffix, DateOnly rateDate, string functionalCurrency = "USD")
  {
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    var partner = Actor(scope.Partner, "Partner");

    var groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
      new ClientGroupRequest($"GROUP-{suffix}", $"Consolidation Group for {suffix}"))).Value;
    Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
      new GroupMembershipRequest(groupId, scope.ClientA, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m, $"sub-{suffix}"))).Succeeded);
    db.GroupAccessGrants.Add(new GroupAccessGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = scope.Preparer.Id,
      Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id
    });
    db.GroupAccessGrants.Add(new GroupAccessGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = scope.Partner.Id,
      Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id
    });

    var rateSetId = (await CurrencyTranslationService.CreateRateSetAsync(db, reviewer,
      new ExchangeRateSetRequest($"FX-RATES-{suffix}", "Central Bank", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1))).Value;
    foreach (var (rateType, rate) in rates)
      Assert.True((await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
        new ExchangeRateInput("USD", "QAR", rateDate, rateType, rate, "DIRECT"))).Succeeded);
    Assert.True((await CurrencyTranslationService.ApproveRateSetAsync(db, partner, rateSetId)).Succeeded);

    var policyId = (await CurrencyTranslationService.CreatePolicyAsync(db, reviewer,
      new TranslationPolicyRequest($"FX-POL-{suffix}", functionalCurrency, "QAR", "CLOSING", "AVERAGE", "HISTORICAL"))).Value;
    Assert.True((await CurrencyTranslationService.ApprovePolicyAsync(db, partner, policyId)).Succeeded);

    var scopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
      new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", ConsolidationCalculator.ForeignOperationMethod,
        $"OPENING-{suffix}", rateSetId, policyId, rateDate, "CLOSING"))).Value;

    var packageId = await AddMultiLinePackageAsync(db, scope, scope.ClientA, scope.EngagementA, lines, suffix, functionalCurrency);
    await db.SaveChangesAsync();
    var mappingId = await db.FinancialPackages.Where(x => x.Id == packageId).Select(x => x.MappingVersionId).SingleAsync();

    var componentId = (await ConsolidationService.SubmitComponentAsync(db, preparer,
      new ConsolidationComponentRequest(scopeId, scope.ClientA, scope.EngagementA, packageId, 100m,
        "CONTROLLED", "STATUTORY", "tax-v1", mappingId.ToString("D")))).Value;
    Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
      new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
        FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline, "fx", "Approved."))).Succeeded);
    Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
      new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
        FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn, "fx", "Reviewed."))).Succeeded);
    Assert.True((await FinancialPackageReviewService.RecordAsync(db, partner,
      new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
        FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn, "fx", "Approved."))).Succeeded);
    var approveComponent = await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId);
    Assert.True(approveComponent.Succeeded, approveComponent.Message);
    return new ForeignOperationFixture(groupId, rateSetId, policyId, scopeId, packageId, componentId);
  }

  private static async Task ApproveForeignScopeAsync(AuditSphereDbContext db, Scope scope, ForeignOperationFixture fixture)
  {
    var reviewer = Actor(scope.Reviewer, "Partner");
    var methodOwner = Actor(scope.Partner, "Partner");
    var capabilityId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(null, fixture.GroupId, "GROUP_REPORTING", "IFRS", "2026", "ANNUAL", "QAR",
        "STATUTORY", ConsolidationCalculator.ForeignOperationMethod, "PARTNER", "GROUP"))).Value;
    Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, capabilityId,
      AccountingCapabilityAcceptanceStages.LocalConstruction, "fx-local")).Succeeded);
    Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, methodOwner, capabilityId,
      AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "fx-method-owner")).Succeeded);
    var approveScope = await ConsolidationService.ApproveScopeAsync(db, reviewer, fixture.ScopeId);
    Assert.True(approveScope.Succeeded, approveScope.Message);
  }

  [Fact(DisplayName = "GOLD-R2R-07 replay: identical retries, independent approval and stable approved readback")]
  [Trait("Profile", "Database")]
  public async Task ForeignOperation_Gold07_ReplayApprovalAndReadbackAreStable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    var partner = Actor(scope.Partner, "Partner");
    var rateDate = new DateOnly(2026, 12, 31);
    Guid scopeId, runId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var fixture = await SeedForeignOperationAsync(db, scope, Gold07Lines, Gold07Rates, "gold7-replay", rateDate);
      scopeId = fixture.ScopeId;
      var first = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
        fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
      Assert.True(first.Succeeded, first.Message);
      var replay = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
        fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
      Assert.True(replay.Succeeded, replay.Message);
      Assert.Equal(first.Value, replay.Value);
      Assert.Equal(1, await db.TranslationResults.CountAsync(x => x.ComponentId == fixture.ComponentId));

      var approveTranslation = await CurrencyTranslationService.ApproveTranslationAsync(db, reviewer, first.Value);
      Assert.True(approveTranslation.Succeeded, approveTranslation.Message);
      await ApproveForeignScopeAsync(db, scope, fixture);

      var run = await ConsolidationService.RunAsync(db, preparer, fixture.ScopeId);
      Assert.True(run.Succeeded, run.Message);
      runId = run.Value;
      var replayRun = await ConsolidationService.RunAsync(db, preparer, fixture.ScopeId);
      Assert.True(replayRun.Succeeded, replayRun.Message);
      Assert.Equal(runId, replayRun.Value);
      Assert.Equal(1, await db.ConsolidationRuns.CountAsync(x => x.ScopeVersionId == fixture.ScopeId));

      var approveRun = await ConsolidationService.ApproveRunAsync(db, partner, runId);
      Assert.True(approveRun.Succeeded, approveRun.Message);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var report = await ConsolidationService.GetLatestReportAsync(db, preparer, scopeId);
      Assert.True(report.Succeeded, report.Message);
      Assert.Equal("CURRENT_APPROVED", report.Value!.State);
      Assert.Equal(0m, report.Value.Lines.Sum(x => x.ConsolidatedAmount));
      var readback = await ConsolidationService.GetLatestReportAsync(db, preparer, scopeId);
      Assert.True(readback.Succeeded, readback.Message);
      Assert.Equal("CURRENT_APPROVED", readback.Value!.State);
      Assert.Equal(report.Value.Lines.Count, readback.Value.Lines.Count);
    }
  }

  [Fact(DisplayName = "A missing AVERAGE rate fails closed instead of falling back to one aggregate rate")]
  [Trait("Profile", "Database")]
  public async Task ForeignOperation_MissingRequiredAverageDoesNotFallback()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var rateDate = new DateOnly(2026, 12, 31);
    await using var db = new AuditSphereDbContext(pg.Options);
    var fixture = await SeedForeignOperationAsync(db, scope, Gold07Lines,
      Gold07Rates.Where(x => x.RateType != "AVERAGE").ToList(), "gold7-noavg", rateDate);
    var result = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
      fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, result.ErrorCode);
    Assert.Contains(TranslationRatePurposes.Average, result.Message);
    Assert.Empty(await db.TranslationResults.Where(x => x.ComponentId == fixture.ComponentId).ToListAsync());
  }

  [Fact(DisplayName = "A missing HISTORICAL rate fails closed instead of falling back to one aggregate rate")]
  [Trait("Profile", "Database")]
  public async Task ForeignOperation_MissingRequiredHistoricalDoesNotFallback()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var rateDate = new DateOnly(2026, 12, 31);
    await using var db = new AuditSphereDbContext(pg.Options);
    var fixture = await SeedForeignOperationAsync(db, scope, Gold07Lines,
      Gold07Rates.Where(x => x.RateType != "HISTORICAL").ToList(), "gold7-nohist", rateDate);
    var result = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
      fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, result.ErrorCode);
    Assert.Contains(TranslationRatePurposes.Historical, result.Message);
    Assert.Empty(await db.TranslationResults.Where(x => x.ComponentId == fixture.ComponentId).ToListAsync());
  }

  [Fact(DisplayName = "A zero translation reserve still translates every line at its own rate purpose")]
  [Trait("Profile", "Database")]
  public async Task ForeignOperation_ZeroReserveStillUsesPerLinePurposes()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    var rateDate = new DateOnly(2026, 12, 31);
    // Offsetting rate effects: reserve movement is zero, yet equity and profit lines still carry
    // their own historical and average rates instead of one header closing rate.
    IReadOnlyList<(string Destination, string Section, decimal Amount)> lines =
    [
      ("CASH", "ASSETS", 100m),
      ("PAYABLES", "LIABILITIES", -120m),
      ("CAPITAL", "EQUITY", -80m),
      ("REVENUE", "INCOME", -40m),
      ("EXPENSE", "EXPENSE", 140m)
    ];
    await using var db = new AuditSphereDbContext(pg.Options);
    var fixture = await SeedForeignOperationAsync(db, scope, lines, Gold07Rates, "gold7-zeroreserve", rateDate);
    var translate = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
      fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
    Assert.True(translate.Succeeded, translate.Message);
    var translation = await db.TranslationResults.SingleAsync(x => x.Id == translate.Value);
    Assert.Equal(0m, translation.TranslationReserve);
    Assert.Equal(0m, translation.ForeignExchangeAdjustment);
    Assert.True((await CurrencyTranslationService.ApproveTranslationAsync(db, reviewer, translate.Value)).Succeeded);
    await ApproveForeignScopeAsync(db, scope, fixture);
    var run = await ConsolidationService.RunAsync(db, preparer, fixture.ScopeId);
    Assert.True(run.Succeeded, run.Message);
    var runLines = await db.ConsolidationRunLines.Where(x => x.RunId == run.Value).ToListAsync();
    Assert.Equal(400m, runLines.Single(x => x.TaxonomyCode == "CASH").ConsolidatedAmount);
    Assert.Equal(-480m, runLines.Single(x => x.TaxonomyCode == "PAYABLES").ConsolidatedAmount);
    Assert.Equal(-280m, runLines.Single(x => x.TaxonomyCode == "CAPITAL").ConsolidatedAmount);
    Assert.Equal(-144m, runLines.Single(x => x.TaxonomyCode == "REVENUE").ConsolidatedAmount);
    Assert.Equal(504m, runLines.Single(x => x.TaxonomyCode == "EXPENSE").ConsolidatedAmount);
    Assert.DoesNotContain(runLines, x => x.TaxonomyCode == AccountingDefaults.CumulativeTranslationReserveSection);
    Assert.Equal(0m, runLines.Sum(x => x.ConsolidatedAmount));

    // The same per-line values survive group approval and report readback: a zero reserve
    // movement never collapses equity and profit lines onto one header closing rate.
    var partner = Actor(scope.Partner, "Partner");
    var approveRun = await ConsolidationService.ApproveRunAsync(db, partner, run.Value);
    Assert.True(approveRun.Succeeded, approveRun.Message);
    var report = await ConsolidationService.GetLatestReportAsync(db, preparer, fixture.ScopeId);
    Assert.True(report.Succeeded, report.Message);
    Assert.Equal("CURRENT_APPROVED", report.Value!.State);
    Assert.Equal(run.Value, report.Value!.RunId);
    Assert.NotNull(report.Value!.ApprovedAt);
    Assert.Equal(400m, report.Value!.Lines.Single(x => x.TaxonomyCode == "CASH").ConsolidatedAmount);
    Assert.Equal(-480m, report.Value!.Lines.Single(x => x.TaxonomyCode == "PAYABLES").ConsolidatedAmount);
    Assert.Equal(-280m, report.Value!.Lines.Single(x => x.TaxonomyCode == "CAPITAL").ConsolidatedAmount);
    Assert.Equal(-144m, report.Value!.Lines.Single(x => x.TaxonomyCode == "REVENUE").ConsolidatedAmount);
    Assert.Equal(504m, report.Value!.Lines.Single(x => x.TaxonomyCode == "EXPENSE").ConsolidatedAmount);
    Assert.DoesNotContain(report.Value!.Lines, x => x.TaxonomyCode == AccountingDefaults.CumulativeTranslationReserveSection);
    Assert.Equal(0m, report.Value!.Lines.Sum(x => x.ConsolidatedAmount));
  }

  [Fact(DisplayName = "An unknown taxonomy code is an explicit unmapped issue resolved only by an approved mapping")]
  [Trait("Profile", "Database")]
  public async Task ForeignOperation_UnknownTaxonomyRequiresApprovedMapping()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var rateDate = new DateOnly(2026, 12, 31);
    IReadOnlyList<(string Destination, string Section, decimal Amount)> lines =
    [
      ("9999-UNCLASSIFIED", "UNCLASSIFIED", 100m),
      ("CASH", "ASSETS", -100m)
    ];
    await using var db = new AuditSphereDbContext(pg.Options);
    var fixture = await SeedForeignOperationAsync(db, scope, lines, Gold07Rates, "gold7-unmapped", rateDate);
    var unmapped = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
      fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
    Assert.False(unmapped.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, unmapped.ErrorCode);
    Assert.Contains("has no approved statement section", unmapped.Message);
    Assert.Empty(await db.TranslationResults.Where(x => x.ComponentId == fixture.ComponentId).ToListAsync());

    var mappingId = await db.FinancialPackages.Where(x => x.Id == fixture.PackageId).Select(x => x.MappingVersionId).SingleAsync();
    db.MappingAllocations.Add(new MappingAllocation
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
      MappingVersionId = mappingId, SourceAccountCode = "9999", DestinationCode = "9999-UNCLASSIFIED",
      StatementSection = "ASSETS", Fraction = 1m, Rationale = "Approved classification for the unclassified account.",
      CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var mapped = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
      fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
    Assert.True(mapped.Succeeded, mapped.Message);

    db.MappingAllocations.Add(new MappingAllocation
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
      MappingVersionId = mappingId, SourceAccountCode = "9998", DestinationCode = "9999-UNCLASSIFIED",
      StatementSection = "LIABILITIES", Fraction = 1m, Rationale = "Conflicting classification.",
      CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var ambiguous = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
      fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
    Assert.False(ambiguous.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, ambiguous.ErrorCode);
    Assert.Contains("more than one approved mapping section", ambiguous.Message);
  }

  [Fact(DisplayName = "The run manifest records each line's own consumed rate identity")]
  [Trait("Profile", "Database")]
  public async Task ForeignOperation_ManifestUsesExactLineRateIdentity()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    var rateDate = new DateOnly(2026, 12, 31);
    await using var db = new AuditSphereDbContext(pg.Options);
    var fixture = await SeedForeignOperationAsync(db, scope, Gold07Lines, Gold07Rates, "gold7-manifest", rateDate);
    var translate = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
      fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
    Assert.True(translate.Succeeded, translate.Message);
    Assert.True((await CurrencyTranslationService.ApproveTranslationAsync(db, reviewer, translate.Value)).Succeeded);
    await ApproveForeignScopeAsync(db, scope, fixture);
    var run = await ConsolidationService.RunAsync(db, preparer, fixture.ScopeId);
    Assert.True(run.Succeeded, run.Message);

    var lineIds = await db.FinancialPackageLines.AsNoTracking()
      .Where(x => x.FinancialPackageId == fixture.PackageId)
      .ToDictionaryAsync(x => x.DestinationCode, x => x.Id);
    var manifest = await db.ConsolidationRuns.AsNoTracking()
      .Where(x => x.Id == run.Value).Select(x => x.InputManifest).SingleAsync();
    var rows = manifest.Split('\n');

    var cashRow = rows.Single(r => r.Contains(lineIds["CASH"].ToString("D")));
    var capitalRow = rows.Single(r => r.Contains(lineIds["CAPITAL"].ToString("D")));
    var revenueRow = rows.Single(r => r.Contains(lineIds["REVENUE"].ToString("D")));
    var ctaRow = rows.Single(r => r.Contains(AccountingDefaults.CumulativeTranslationReserveSection));

    // Each row carries its own source line and its own consumed rate purpose and rate: the
    // equity line records the historical rate it consumed, never another line's closing rate.
    var cashFields = cashRow.Split('|');
    var capitalFields = capitalRow.Split('|');
    var revenueFields = revenueRow.Split('|');
    var ctaFields = ctaRow.Split('|');
    Assert.Equal(TranslationRatePurposes.Closing, cashFields[^2]);
    Assert.Equal("4.000000", cashFields[^1]);
    Assert.Equal(TranslationRatePurposes.Historical, capitalFields[^2]);
    Assert.Equal("3.500000", capitalFields[^1]);
    Assert.Equal(TranslationRatePurposes.Average, revenueFields[^2]);
    Assert.Equal("3.600000", revenueFields[^1]);
    Assert.Equal(TranslationRatePurposes.Closing, ctaFields[^2]);
    Assert.Equal("4.000000", ctaFields[^1]);
  }

  [Fact(DisplayName = "A genuine rate or component change makes an approved report stale")]
  [Trait("Profile", "Database")]
  public async Task ForeignOperation_ChangedRateOrComponentStalesOldReview()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    var partner = Actor(scope.Partner, "Partner");
    var rateDate = new DateOnly(2026, 12, 31);

    Guid rateChangedScope;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var fixture = await SeedForeignOperationAsync(db, scope, Gold07Lines, Gold07Rates, "gold7-ratechange", rateDate);
      rateChangedScope = fixture.ScopeId;
      var translate = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
        fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
      Assert.True(translate.Succeeded, translate.Message);
      Assert.True((await CurrencyTranslationService.ApproveTranslationAsync(db, reviewer, translate.Value)).Succeeded);
      await ApproveForeignScopeAsync(db, scope, fixture);
      var run = await ConsolidationService.RunAsync(db, preparer, fixture.ScopeId);
      Assert.True(run.Succeeded, run.Message);
      Assert.True((await ConsolidationService.ApproveRunAsync(db, partner, run.Value)).Succeeded);
      var current = await ConsolidationService.GetLatestReportAsync(db, preparer, fixture.ScopeId);
      Assert.Equal("CURRENT_APPROVED", current.Value!.State);

      var averageRate = await db.ExchangeRates.SingleAsync(x => x.RateSetVersionId == fixture.RateSetId && x.RateType == "AVERAGE");
      averageRate.Rate = 3.9m;
      await db.SaveChangesAsync();
      var afterRateChange = await ConsolidationService.GetLatestReportAsync(db, preparer, fixture.ScopeId);
      Assert.Equal("STALE", afterRateChange.Value!.State);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var fixture = await SeedForeignOperationAsync(db, scope, Gold07Lines, Gold07Rates, "gold7-linechange", rateDate);
      var translate = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
        fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
      Assert.True(translate.Succeeded, translate.Message);
      Assert.True((await CurrencyTranslationService.ApproveTranslationAsync(db, reviewer, translate.Value)).Succeeded);
      await ApproveForeignScopeAsync(db, scope, fixture);
      var run = await ConsolidationService.RunAsync(db, preparer, fixture.ScopeId);
      Assert.True(run.Succeeded, run.Message);
      Assert.True((await ConsolidationService.ApproveRunAsync(db, partner, run.Value)).Succeeded);
      var current = await ConsolidationService.GetLatestReportAsync(db, preparer, fixture.ScopeId);
      Assert.Equal("CURRENT_APPROVED", current.Value!.State);

      // A genuine source change: new package lines appear in the component's approved package.
      var now = DateTimeOffset.UtcNow;
      var snapshotId = await db.FinancialPackageLines.AsNoTracking()
        .Where(x => x.FinancialPackageId == fixture.PackageId).Select(x => x.AdjustedSnapshotId).FirstAsync();
      foreach (var (sourceAccount, code, amount) in new[] { ("9900", "CASH", 10m), ("9910", "CASH", -10m) })
      {
        db.FinancialPackageLines.Add(new FinancialPackageLine
        {
          Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
          FinancialPackageId = fixture.PackageId, SourceAccountCode = sourceAccount, DestinationCode = code,
          StatementSection = "ASSETS", Amount = amount, Fraction = 1m, Currency = "USD",
          AdjustedSnapshotId = snapshotId, CreatedAt = now
        });
      }
      await db.SaveChangesAsync();
      var afterLineChange = await ConsolidationService.GetLatestReportAsync(db, preparer, fixture.ScopeId);
      Assert.Equal("STALE", afterLineChange.Value!.State);
    }
  }

  [Fact(DisplayName = "Zero, negative and same-currency rate observations are refused at the rate-set boundary")]
  [Trait("Profile", "Database")]
  public async Task RateSet_RejectsZeroNegativeAndSameCurrencyObservations()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var reviewer = Actor(scope.Reviewer, "Partner");
    await using var db = new AuditSphereDbContext(pg.Options);
    var rateSetId = (await CurrencyTranslationService.CreateRateSetAsync(db, reviewer,
      new ExchangeRateSetRequest("FX-RATES-REJECTED", "Central Bank", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1))).Value;
    var rateDate = new DateOnly(2026, 12, 31);
    // A zero rate, a negative rate and a same-currency observation are all refused before any
    // row exists: nothing approvable may ever rest on them.
    foreach (var (from, to, rate) in new[] { ("USD", "QAR", 0m), ("USD", "QAR", -3.6m), ("QAR", "QAR", 1m) })
    {
      var refused = await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
        new ExchangeRateInput(from, to, rateDate, "AVERAGE", rate, "DIRECT"));
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
      Assert.Contains("Only positive DIRECT rate-set entries are supported by this profile.", refused.Message);
    }
    Assert.Empty(await db.ExchangeRates.Where(x => x.RateSetVersionId == rateSetId).ToListAsync());

    // The only accepted shape is a positive DIRECT observation between two distinct currencies.
    var accepted = await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
      new ExchangeRateInput("USD", "QAR", rateDate, "AVERAGE", 3.6m, "DIRECT"));
    Assert.True(accepted.Succeeded, accepted.Message);
    var observation = Assert.Single(await db.ExchangeRates.Where(x => x.RateSetVersionId == rateSetId).ToListAsync());
    Assert.Equal(3.6m, observation.Rate);
    Assert.Equal(ExchangeRateDirections.Direct, observation.Direction);
  }

  [Fact(DisplayName = "A same-currency component is identity rate 1 and consumes no rate observation")]
  [Trait("Profile", "Database")]
  public async Task ForeignOperation_SameCurrencyComponentTranslatesAsIdentity()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    var rateDate = new DateOnly(2026, 12, 31);
    await using var db = new AuditSphereDbContext(pg.Options);
    var fixture = await SeedForeignOperationAsync(db, scope, Gold07Lines, Gold07Rates, "gold7-identity", rateDate, "QAR");
    var translate = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
      fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
    Assert.True(translate.Succeeded, translate.Message);
    var translation = await db.TranslationResults.SingleAsync(x => x.Id == translate.Value);
    Assert.Equal("QAR", translation.FromCurrency);
    Assert.Equal(translation.FromCurrency, translation.ToCurrency);
    // Identity is rate exactly 1: the approved set holds no same-currency observation and none
    // may be added, yet the translation is complete.
    Assert.Equal(1m, translation.AppliedRate);
    Assert.Empty(await db.ExchangeRates.Where(x => x.RateSetVersionId == fixture.RateSetId &&
      x.FromCurrency == x.ToCurrency).ToListAsync());
    Assert.Equal(0m, translation.TranslatedAmount);
    Assert.Equal(0m, translation.ForeignExchangeAdjustment);
    Assert.Equal(0m, translation.TranslationReserve);

    // Approval revalidates the same identity instead of hunting for a missing rate observation.
    Assert.True((await CurrencyTranslationService.ApproveTranslationAsync(db, reviewer, translate.Value)).Succeeded);
    Assert.Equal(AccountingWorkflowStates.Approved,
      (await db.TranslationResults.SingleAsync(x => x.Id == translate.Value)).Status);
  }

  [Fact(DisplayName = "A valid asset-only profile translates with the closing rate alone and zero reserve")]
  [Trait("Profile", "Database")]
  public async Task ForeignOperation_AssetOnlyProfileNeedsOnlyClosingRate()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    var rateDate = new DateOnly(2026, 12, 31);
    IReadOnlyList<(string Destination, string Section, decimal Amount)> lines =
    [
      ("CASH", "ASSETS", 100m),
      ("ACCUMULATED-DEPRECIATION", "ASSETS", -100m)
    ];
    await using var db = new AuditSphereDbContext(pg.Options);
    // The rate set carries only the closing observation this profile can consume: no average or
    // historical rate exists, and asset lines legitimately require none. A missing rate blocks
    // only when a line's own purpose actually needs it.
    var fixture = await SeedForeignOperationAsync(db, scope, lines,
      Gold07Rates.Where(x => x.RateType == "CLOSING").ToList(), "gold7-assetonly", rateDate);
    var translate = await CurrencyTranslationService.TranslateComponentAsync(db, preparer, fixture.ComponentId,
      fixture.RateSetId, fixture.PolicyId, rateDate, "CLOSING");
    Assert.True(translate.Succeeded, translate.Message);
    var translation = await db.TranslationResults.SingleAsync(x => x.Id == translate.Value);
    Assert.Equal(4.0m, translation.AppliedRate);
    Assert.Equal(0m, translation.TranslatedAmount);
    Assert.Equal(0m, translation.ForeignExchangeAdjustment);
    Assert.Equal(0m, translation.TranslationReserve);
    Assert.True((await CurrencyTranslationService.ApproveTranslationAsync(db, reviewer, translate.Value)).Succeeded);
  }
}
