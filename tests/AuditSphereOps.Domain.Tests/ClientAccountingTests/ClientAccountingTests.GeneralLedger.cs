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
        [new("J-2-L1", "1000", 25m, 0m, "USD", 20m, 25m), new("J-2-L2", "4000", 0m, 25m, "QAR", -25m, -25m)])
    };
    var digestOne = ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", chunkOne);
    var digestTwo = ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", chunkTwo);
    var noServiceDateChunk = chunkOne.Select(x => x with { ServiceDate = null }).ToArray();
    Assert.NotEqual(digestOne, ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", noServiceDateChunk));
    var sourceCurrencyVariant = chunkOne.Select(x => x with
    {
      Lines = x.Lines.Select((line, index) => index == 0
        ? line with { OriginalCurrency = "USD", OriginalAmount = 27.027m }
        : line).ToArray()
    }).ToArray();
    Assert.NotEqual(digestOne, ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", sourceCurrencyVariant));
    var normalizedCaseVariant = chunkOne.Select(x => x with
    {
      Lines = x.Lines.Select((line, index) => index == 0
        ? line with { OriginalCurrency = " qar ", StableLineId = " J-1-L1 ", AccountCode = " 1000 " }
        : line).ToArray()
    }).ToArray();
    Assert.Equal(digestOne, ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", normalizedCaseVariant));
    Guid batchId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var started = await ClientAccountingService.BeginGeneralLedgerImportAsync(db, preparer,
        new GeneralLedgerImportStartRequest(scope.ClientA, scope.EngagementA, periodId, bookId, "stream-v1", "gl-v1",
          new string('9', 64), "CLIENT-A", "QAR", "stream-receipt", 2, 2, 4));
      Assert.True(started.Succeeded, started.Message);
      batchId = started.Value;

      var invalidChunk = chunkOne.Select(x => x with
      {
        Lines = x.Lines.Select((line, index) => index == 0 ? line with { FunctionalAmount = 99m } : line).ToArray()
      }).ToArray();
      var invalidDigest = ClientAccountingService.ComputeGeneralLedgerChunkDigest("QAR", invalidChunk);
      var invalid = await ClientAccountingService.AppendGeneralLedgerChunkAsync(db, preparer,
        new GeneralLedgerImportChunkRequest(batchId, 0, invalidDigest, invalidChunk, Finalize: false));
      Assert.False(invalid.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ImportRejected, invalid.ErrorCode);

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
    var foreignLine = await verify.GeneralLedgerLines.SingleAsync(x => x.ImportBatchId == batchId && x.StableLineId == "J-2-L1");
    Assert.Equal("USD", foreignLine.OriginalCurrency);
    Assert.Equal(20m, foreignLine.OriginalAmount);
    Assert.Equal(25m, foreignLine.FunctionalAmount);
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
}
