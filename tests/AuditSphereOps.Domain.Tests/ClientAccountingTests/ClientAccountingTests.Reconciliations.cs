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
  public async Task ReconciliationWritesWaitForPeriodCloseAndLeaveRetainedEvidenceUnchanged()
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

      var created = await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, periodId, bookId, "CASH",
          datasetId, null, ["1000"], new DateOnly(2026, 12, 31)), timeout.Token);
      Assert.True(created.Succeeded, created.Message);
      reconciliationId = created.Value;
      var added = await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, reconciliationId,
        [new("ITEM-PLUS", 10m, "QAR", new DateOnly(2026, 12, 30), "timing", "receipt-plus", "OPEN"),
         new("ITEM-MINUS", -10m, "QAR", new DateOnly(2026, 12, 30), "timing", "receipt-minus", "OPEN")], timeout.Token);
      Assert.True(added.Succeeded, added.Message);
      openItemId = await db.AccountingReconciliationItems.Where(x =>
        x.ReconciliationId == reconciliationId && x.StableItemId == "ITEM-PLUS")
        .Select(x => x.Id).SingleAsync(timeout.Token);
      var proof = await AccountingAnalysisService.CalculateReconciliationProofAsync(db, preparer, reconciliationId, timeout.Token);
      Assert.True(proof.Succeeded, proof.Message);
      Assert.True(proof.Value!.IsReconciled);
      proofId = proof.Value.ProofId;
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

    var pauseClose = new PausePeriodCloseSave();
    await using var closeDb = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pauseClose).Options);
    await using var createDb = new AuditSphereDbContext(pg.Options);
    await using var addDb = new AuditSphereDbContext(pg.Options);
    await using var observer = new AuditSphereDbContext(pg.Options);
    Task<CommandResult>? closeTask = null;
    Task<CommandResult<Guid>>? createTask = null;
    Task<CommandResult>? addTask = null;
    try
    {
      closeTask = ClientAccountingService.ClosePeriodAsync(closeDb, reviewer, periodId, "Close after reconciliation", timeout.Token);
      await pauseClose.Reached.Task.WaitAsync(timeout.Token);

      await createDb.Database.OpenConnectionAsync(timeout.Token);
      await addDb.Database.OpenConnectionAsync(timeout.Token);
      await observer.Database.OpenConnectionAsync(timeout.Token);
      var closePid = ((NpgsqlConnection)closeDb.Database.GetDbConnection()).ProcessID;
      var createPid = ((NpgsqlConnection)createDb.Database.GetDbConnection()).ProcessID;
      var addPid = ((NpgsqlConnection)addDb.Database.GetDbConnection()).ProcessID;
      var activeCreateTask = AccountingAnalysisService.CreateReconciliationAsync(createDb, preparer,
        new AccountingReconciliationRequest(scope.ClientA, scope.EngagementA, periodId, bookId, "CASH",
          datasetId, null, ["1000"], new DateOnly(2026, 12, 31)), timeout.Token);
      createTask = activeCreateTask;
      var activeAddTask = AccountingAnalysisService.AddReconciliationItemsAsync(addDb, preparer, reconciliationId,
        [new("ITEM-AFTER-CLOSE", 5m, "QAR", new DateOnly(2026, 12, 30), "late timing", "receipt-late", "OPEN")], timeout.Token);
      addTask = activeAddTask;

      var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
      var createBlocked = false;
      var addBlocked = false;
      while (DateTimeOffset.UtcNow < deadline && (!createBlocked || !addBlocked))
      {
        createBlocked = await observer.Database.SqlQuery<bool>($"""
          WITH RECURSIVE blockers(pid) AS (
            SELECT unnest(pg_blocking_pids({createPid}))
            UNION
            SELECT unnest(pg_blocking_pids(blockers.pid)) FROM blockers
          )
          SELECT EXISTS (SELECT 1 FROM blockers WHERE pid = {closePid}) AS "Value"
          """).SingleAsync(timeout.Token);
        addBlocked = await observer.Database.SqlQuery<bool>($"""
          WITH RECURSIVE blockers(pid) AS (
            SELECT unnest(pg_blocking_pids({addPid}))
            UNION
            SELECT unnest(pg_blocking_pids(blockers.pid)) FROM blockers
          )
          SELECT EXISTS (SELECT 1 FROM blockers WHERE pid = {closePid}) AS "Value"
          """).SingleAsync(timeout.Token);
        if (!createBlocked || !addBlocked) await Task.Delay(20, timeout.Token);
      }
      Assert.True(createBlocked, "Creating reconciliation evidence must be blocked by period close, directly or through queued writers.");
      Assert.True(addBlocked, "Adding reconciliation items must be blocked by period close, directly or through queued writers.");
      Assert.False(activeCreateTask.IsCompleted);
      Assert.False(activeAddTask.IsCompleted);

      pauseClose.Release.TrySetResult(true);
      var closed = await closeTask.WaitAsync(timeout.Token);
      Assert.True(closed.Succeeded, closed.Message);
      var createOutcome = await activeCreateTask.WaitAsync(timeout.Token);
      var addOutcome = await activeAddTask.WaitAsync(timeout.Token);
      Assert.False(createOutcome.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, createOutcome.ErrorCode);
      Assert.False(addOutcome.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, addOutcome.ErrorCode);
    }
    finally
    {
      pauseClose.Release.TrySetResult(true);
      if (closeTask is not null)
        try { await closeTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
      if (createTask is not null)
        try { await createTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
      if (addTask is not null)
        try { await addTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(AccountingWorkflowStates.Closed,
      await verify.ClientReportingPeriods.Where(x => x.Id == periodId).Select(x => x.Status).SingleAsync(timeout.Token));
    var closedProof = await AccountingAnalysisService.CalculateReconciliationProofAsync(
      verify, preparer, reconciliationId, timeout.Token);
    Assert.False(closedProof.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, closedProof.ErrorCode);
    var closedLink = await AccountingAnalysisService.LinkReconciliationCorrectionAsync(
      verify, preparer, reconciliationId, openItemId, Guid.NewGuid(), timeout.Token);
    Assert.False(closedLink.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, closedLink.ErrorCode);
    var closedApproval = await AccountingAnalysisService.ApproveReconciliationAsync(
      verify, reviewer, reconciliationId, timeout.Token);
    Assert.False(closedApproval.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, closedApproval.ErrorCode);
    var closedRevision = await AccountingAnalysisService.ReviseReconciliationAsync(
      verify, preparer, reconciliationId, "Closed periods require their authorized amendment path", timeout.Token);
    Assert.False(closedRevision.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, closedRevision.ErrorCode);

    var nextPeriod = await ClientAccountingService.CreatePeriodAsync(verify, preparer,
      new ReportingPeriodRequest(scope.ClientA, "2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
        "STATUTORY", "QAR"), timeout.Token);
    Assert.True(nextPeriod.Succeeded, nextPeriod.Message);
    var nextPeriodClosed = await ClientAccountingService.ClosePeriodAsync(
      verify, reviewer, nextPeriod.Value, "Close empty carry-forward target", timeout.Token);
    Assert.True(nextPeriodClosed.Succeeded, nextPeriodClosed.Message);
    var closedCarry = await AccountingAnalysisService.CarryForwardReconciliationItemsAsync(
      verify, preparer, reconciliationId, nextPeriod.Value, null, [openItemId], "carry-forward-evidence", timeout.Token);
    Assert.False(closedCarry.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, closedCarry.ErrorCode);
    Assert.Contains("closed", closedCarry.Message!, StringComparison.OrdinalIgnoreCase);

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
