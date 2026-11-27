using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
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
  public async Task ReconciliationApprovalSerializesItemEditsAndUnsupportedApprovedSourceBlocksCloseWithoutMutation()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    var (periodId, bookId) = await CreateGlFixtureAsync(pg, scope, preparer);
    var datasetId = Guid.CreateVersion7();
    var sourceHash = Hashing.Sha256Hex("closed-period-reconciliation-source");
    Guid reconciliationId;
    Guid proofId;
    Guid openItemId;

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
      await db.SaveChangesAsync(timeout.Token);
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId, timeout.Token);
      dataset.ValidationStatus = "Accepted";
      dataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync(timeout.Token);

      await using var callerTransaction = await db.Database.BeginTransactionAsync(timeout.Token);
      var created = await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, periodId, bookId, "CASH",
          datasetId, null, ["1000"], new DateOnly(2026, 12, 31)), timeout.Token);
      Assert.True(created.Succeeded, created.Message);
      Assert.Same(callerTransaction, db.Database.CurrentTransaction);
      reconciliationId = created.Value;
      var added = await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, reconciliationId,
        [new("ITEM-PLUS", 10m, "QAR", new DateOnly(2026, 12, 30), "timing", "receipt-plus", "OPEN"),
         new("ITEM-MINUS", -10m, "QAR", new DateOnly(2026, 12, 30), "timing", "receipt-minus", "OPEN")], timeout.Token);
      Assert.True(added.Succeeded, added.Message);
      Assert.Same(callerTransaction, db.Database.CurrentTransaction);
      openItemId = await db.AccountingReconciliationItems.Where(x =>
        x.ReconciliationId == reconciliationId && x.StableItemId == "ITEM-PLUS")
        .Select(x => x.Id).SingleAsync(timeout.Token);
      var proof = await AccountingAnalysisService.CalculateReconciliationProofAsync(db, preparer, reconciliationId, timeout.Token);
      Assert.True(proof.Succeeded, proof.Message);
      Assert.True(proof.Value!.IsReconciled);
      Assert.Same(callerTransaction, db.Database.CurrentTransaction);
      proofId = proof.Value.ProofId;
      await callerTransaction.CommitAsync(timeout.Token);
      Assert.Null(db.Database.CurrentTransaction);
    }

    // Approval and an item edit serialize on period -> reconciliation. The edit waits
    // for the review publication, then sees the approved row and leaves its proof inputs intact.
    var pauseApproval = new PauseReconciliationApprovalSave();
    await using (var approvalDb = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pauseApproval).Options))
    await using (var itemDb = new AuditSphereDbContext(pg.Options))
    await using (var approvalObserver = new AuditSphereDbContext(pg.Options))
    {
      Task<CommandResult>? approvalTask = null;
      Task<CommandResult>? itemTask = null;
      try
      {
        approvalTask = AccountingAnalysisService.ApproveReconciliationAsync(approvalDb, reviewer, reconciliationId, timeout.Token);
        await pauseApproval.Reached.Task.WaitAsync(timeout.Token);
        await itemDb.Database.OpenConnectionAsync(timeout.Token);
        await approvalObserver.Database.OpenConnectionAsync(timeout.Token);
        var approvalPid = ((NpgsqlConnection)approvalDb.Database.GetDbConnection()).ProcessID;
        var itemPid = ((NpgsqlConnection)itemDb.Database.GetDbConnection()).ProcessID;
        var activeItemTask = AccountingAnalysisService.AddReconciliationItemsAsync(itemDb, preparer, reconciliationId,
          [new("ITEM-AFTER-APPROVAL", 1m, "QAR", new DateOnly(2026, 12, 30), "late item", "receipt-late", "OPEN")], timeout.Token);
        itemTask = activeItemTask;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        var itemBlockedByApproval = false;
        while (DateTimeOffset.UtcNow < deadline && !itemBlockedByApproval)
        {
          itemBlockedByApproval = await approvalObserver.Database.SqlQuery<bool>(
            $"SELECT {approvalPid} = ANY(pg_blocking_pids({itemPid})) AS \"Value\"").SingleAsync(timeout.Token);
          if (!itemBlockedByApproval) await Task.Delay(20, timeout.Token);
        }
        Assert.True(itemBlockedByApproval, "An item edit must wait for reconciliation approval publication.");
        Assert.False(activeItemTask.IsCompleted);

        pauseApproval.Release.TrySetResult(true);
        var approved = await approvalTask.WaitAsync(timeout.Token);
        Assert.True(approved.Succeeded, approved.Message);
        var itemOutcome = await activeItemTask.WaitAsync(timeout.Token);
        Assert.False(itemOutcome.Succeeded);
        Assert.Equal(ErrorCodes.ProtectedState, itemOutcome.ErrorCode);
      }
      finally
      {
        pauseApproval.Release.TrySetResult(true);
        if (approvalTask is not null)
          try { await approvalTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        if (itemTask is not null)
          try { await itemTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
      }

      var approvedBeforeAppend = await itemDb.AccountingReconciliations.AsNoTracking()
        .SingleAsync(x => x.Id == reconciliationId, timeout.Token);
      var proofCountBeforeAppend = await itemDb.AccountingReconciliationProofs.CountAsync(
        x => x.ReconciliationId == reconciliationId, timeout.Token);
      var appendedProof = await AccountingAnalysisService.CalculateReconciliationProofAsync(
        itemDb, preparer, reconciliationId, timeout.Token);
      Assert.True(appendedProof.Succeeded, appendedProof.Message);
      Assert.True(appendedProof.Value!.IsReconciled);
      Assert.Equal(AccountingWorkflowStates.Approved, appendedProof.Value.Status);
      var approvedAfterAppend = await itemDb.AccountingReconciliations.AsNoTracking()
        .SingleAsync(x => x.Id == reconciliationId, timeout.Token);
      Assert.Equal(AccountingWorkflowStates.Approved, approvedAfterAppend.Status);
      Assert.Equal(approvedBeforeAppend.Residual, approvedAfterAppend.Residual);
      Assert.Equal(approvedBeforeAppend.ReviewedByUserId, approvedAfterAppend.ReviewedByUserId);
      Assert.Equal(approvedBeforeAppend.ReviewedAt, approvedAfterAppend.ReviewedAt);
      Assert.Equal(approvedBeforeAppend.Revision, approvedAfterAppend.Revision);
      Assert.Equal(proofCountBeforeAppend + 1, await itemDb.AccountingReconciliationProofs.CountAsync(
        x => x.ReconciliationId == reconciliationId, timeout.Token));

      var approvedLink = await AccountingAnalysisService.LinkReconciliationCorrectionAsync(
        itemDb, preparer, reconciliationId, openItemId, Guid.NewGuid(), timeout.Token);
      Assert.False(approvedLink.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, approvedLink.ErrorCode);
      var approvedRevision = await AccountingAnalysisService.ReviseReconciliationAsync(
        itemDb, preparer, reconciliationId, "Attempt to rewrite approved evidence", timeout.Token);
      Assert.False(approvedRevision.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, approvedRevision.ErrorCode);
      Assert.Equal(2, await itemDb.AccountingReconciliationItems.CountAsync(
        x => x.ReconciliationId == reconciliationId, timeout.Token));
    }

    await using var closeDb = new AuditSphereDbContext(pg.Options);
    var periodStatusBeforeClose = await closeDb.ClientReportingPeriods.AsNoTracking()
      .Where(x => x.Id == periodId).Select(x => x.Status).SingleAsync(timeout.Token);
    Assert.True(AccountingWorkflowStates.IsOpenReportingPeriodStatus(periodStatusBeforeClose));
    var proofsBeforeClose = await closeDb.AccountingReconciliationProofs.AsNoTracking()
      .Where(x => x.ReconciliationId == reconciliationId).OrderBy(x => x.CreatedAt)
      .Select(x => new
      {
        x.Id, x.FormulaVersion, x.SourceTotal, x.GlTotal, x.ItemsSignedTotal, x.Residual,
        x.IsReconciled, x.ItemCount, x.ItemManifestDigest, x.SourceHash, x.InputGeneration,
        x.CreatedByUserId, x.CreatedAt
      }).ToArrayAsync(timeout.Token);
    var blockedClose = await ClientAccountingService.ClosePeriodAsync(
      closeDb, reviewer, periodId, "Close after generic reconciliation", timeout.Token);
    Assert.False(blockedClose.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, blockedClose.ErrorCode);
    Assert.Contains("distinct independent control source", blockedClose.Message!, StringComparison.OrdinalIgnoreCase);

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(periodStatusBeforeClose,
      await verify.ClientReportingPeriods.AsNoTracking().Where(x => x.Id == periodId)
        .Select(x => x.Status).SingleAsync(timeout.Token));
    var proofsAfterClose = await verify.AccountingReconciliationProofs.AsNoTracking()
      .Where(x => x.ReconciliationId == reconciliationId).OrderBy(x => x.CreatedAt)
      .Select(x => new
      {
        x.Id, x.FormulaVersion, x.SourceTotal, x.GlTotal, x.ItemsSignedTotal, x.Residual,
        x.IsReconciled, x.ItemCount, x.ItemManifestDigest, x.SourceHash, x.InputGeneration,
        x.CreatedByUserId, x.CreatedAt
      }).ToArrayAsync(timeout.Token);
    Assert.Equal(proofsBeforeClose, proofsAfterClose);

    var retained = await verify.AccountingReconciliations.SingleAsync(x => x.Id == reconciliationId, timeout.Token);
    Assert.Equal(AccountingWorkflowStates.Approved, retained.Status);
    Assert.Equal(scope.Reviewer.Id, retained.ReviewedByUserId);
    Assert.Equal(1, await verify.AccountingReconciliations.CountAsync(x => x.PeriodId == periodId, timeout.Token));
    Assert.Equal(2, await verify.AccountingReconciliationItems.CountAsync(x => x.ReconciliationId == reconciliationId, timeout.Token));
    var retainedProofIds = await verify.AccountingReconciliationProofs.Where(x => x.ReconciliationId == reconciliationId)
      .Select(x => x.Id).ToArrayAsync(timeout.Token);
    Assert.Equal(2, retainedProofIds.Length);
    Assert.Contains(proofId, retainedProofIds);
    Assert.Equal(new[] { "ITEM-MINUS", "ITEM-PLUS" }, await verify.AccountingReconciliationItems
      .Where(x => x.ReconciliationId == reconciliationId).OrderBy(x => x.StableItemId)
      .Select(x => x.StableItemId).ToArrayAsync(timeout.Token));
  }

  private sealed class PausePeriodCloseSave : SaveChangesInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
      InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      if (eventData.Context!.ChangeTracker.Entries<ClientReportingPeriod>().Any(x =>
          x.State == EntityState.Modified && x.Entity.Status == AccountingWorkflowStates.Closed))
      {
        Reached.TrySetResult(true);
        await Release.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }

  private sealed class PauseReconciliationApprovalSave : SaveChangesInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
      InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      if (eventData.Context!.ChangeTracker.Entries<AccountingReconciliation>().Any(x =>
          x.State == EntityState.Modified && x.Entity.Status == AccountingWorkflowStates.Approved))
      {
        Reached.TrySetResult(true);
        await Release.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }
}
