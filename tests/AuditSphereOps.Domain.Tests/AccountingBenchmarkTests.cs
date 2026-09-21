using System.Diagnostics;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WorkerHost = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
[Trait("Profile", "Benchmark")]
public sealed class AccountingBenchmarkTests
{
  private const int ClientCount = 4;
  private const int TransactionsPerClient = 500;
  private const int LinesPerTransaction = 4;

  private sealed record Fixture(Guid ClientId, Guid EngagementId, Guid PeriodId, Guid BookId,
    Guid DatasetId, Guid ImportBatchId, AppUser Preparer);

  private sealed class TestDbContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }

  [Fact]
  public async Task RepresentativeAccountingWorkload_CompletesDurablyAndReportsMeasurements()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, fixtures) = await SeedAsync(pg);
    var handler = new GeneralLedgerCompletenessHandler();
    var operationFactory = new OperationContextFactory(new TestDbContextFactory(pg.Options));
    var operationStore = new PostgresOperationStore(operationFactory);

    var enqueueStarted = Stopwatch.GetTimestamp();
    var queued = await Task.WhenAll(fixtures.Select(async fixture =>
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var actor = new ActorContext(fixture.Preparer.Id, firmId, fixture.Preparer.SessionEpoch, ["AccountingPreparer"]);
      return await AccountingAnalysisService.EnqueueGeneralLedgerCompletenessBridgeAsync(db, actor,
        new GeneralLedgerCompletenessRequest(fixture.ClientId, fixture.EngagementId, fixture.PeriodId, fixture.BookId,
          fixture.DatasetId, fixture.ImportBatchId, $"benchmark:{fixture.ClientId:D}"), operationStore, handler);
    }));
    var enqueueElapsed = Stopwatch.GetElapsedTime(enqueueStarted);
    Assert.All(queued, result => Assert.True(result.Succeeded, result.Message));

    var options = new WorkerOptions(firmId, "Test");
    var workers = Enumerable.Range(0, 2).Select(_ => new WorkerHost(
      new OperationDispatcher(operationStore, new DurableOperationRegistry([handler], options), options),
      Array.Empty<IPendingOperationDiscovery>(), NullLogger<WorkerHost>.Instance)).ToArray();
    var processStarted = Stopwatch.GetTimestamp();
    var processed = await Task.WhenAll(workers.Select(async worker =>
    {
      var count = 0;
      while (await worker.ProcessNextAsync()) count++;
      return count;
    }));
    var processElapsed = Stopwatch.GetElapsedTime(processStarted);
    Assert.Equal(ClientCount, processed.Sum());

    var pageStarted = Stopwatch.GetTimestamp();
    await using var verify = new AuditSphereDbContext(pg.Options);
    var first = fixtures[0];
    var page = await AccountingAnalysisService.GetGeneralLedgerPageAsync(verify,
      new ActorContext(first.Preparer.Id, firmId, first.Preparer.SessionEpoch, ["AccountingPreparer"]),
      first.ImportBatchId, 1, 500);
    var pageElapsed = Stopwatch.GetElapsedTime(pageStarted);
    Assert.True(page.Succeeded, page.Message);
    Assert.Equal(500, page.Value!.Rows.Count);
    Assert.True(page.Value.HasNextPage);

    var groupComponents = Enumerable.Range(0, ClientCount * 4).SelectMany(index =>
    {
      var componentId = Guid.CreateVersion7();
      var clientId = fixtures[index % fixtures.Count].ClientId;
      return new[]
      {
        new ConsolidationComponentBalance(componentId, clientId, "CASH", 100m, "QAR", 100m, "CONTROLLED",
          Hashing.Sha256Hex($"benchmark-package:{index}"), "STATUTORY", "tax-v1", $"mapping-{index}", Guid.CreateVersion7()),
        new ConsolidationComponentBalance(componentId, clientId, "REVENUE", -100m, "QAR", 100m, "CONTROLLED",
          Hashing.Sha256Hex($"benchmark-package:{index}"), "STATUTORY", "tax-v1", $"mapping-{index}", Guid.CreateVersion7())
      };
    }).ToArray();
    var groupStarted = Stopwatch.GetTimestamp();
    var group = ConsolidationCalculator.Compute("QAR", ConsolidationCalculator.RestrictedMethod,
      "OPENING-2026", groupComponents, []);
    var groupElapsed = Stopwatch.GetElapsedTime(groupStarted);
    Assert.Equal(0m, group.SignedTotal);
    Assert.Equal(groupComponents.Length, group.DetailLines.Count);

    var operations = await verify.DurableOperations.AsNoTracking().ToListAsync();
    Assert.Equal(ClientCount, operations.Count);
    Assert.All(operations, operation => Assert.Equal(OperationState.COMPLETED, operation.Status));
    Assert.Equal(ClientCount, await verify.GeneralLedgerCompletenessBridges.CountAsync(x => x.Status == "RECONCILED"));
    Assert.Equal(ClientCount * TransactionsPerClient, await verify.GeneralLedgerTransactions.CountAsync());
    Assert.Equal(ClientCount * TransactionsPerClient * LinesPerTransaction, await verify.GeneralLedgerLines.CountAsync());

    Console.WriteLine($"ACCOUNTING_BENCHMARK clients={ClientCount} transactions={ClientCount * TransactionsPerClient} " +
      $"lines={ClientCount * TransactionsPerClient * LinesPerTransaction} enqueue_ms={enqueueElapsed.TotalMilliseconds:F1} " +
      $"worker_ms={processElapsed.TotalMilliseconds:F1} first_page_ms={pageElapsed.TotalMilliseconds:F1} " +
      $"group_lines={groupComponents.Length} group_ms={groupElapsed.TotalMilliseconds:F1}");
  }

  private static async Task<(Guid FirmId, IReadOnlyList<Fixture> Fixtures)> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var preparer = User(firmId, "benchmark-preparer");
    var now = DateTimeOffset.UtcNow;
    var fixtures = new List<Fixture>(ClientCount);

    await using var db = new AuditSphereDbContext(pg.Options);
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.Users.Add(preparer);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = firmId, UserId = preparer.Id, Role = "AccountingPreparer",
      GrantedAt = now, GrantedByUserId = preparer.Id
    });

    for (var clientIndex = 0; clientIndex < ClientCount; clientIndex++)
    {
      var clientId = Guid.NewGuid();
      var engagementId = Guid.NewGuid();
      var periodId = Guid.NewGuid();
      var bookId = Guid.NewGuid();
      var datasetId = Guid.NewGuid();
      var importBatchId = Guid.NewGuid();
      var entity = $"ENTITY-{clientIndex + 1}";
      var digest = Hashing.Sha256Hex($"accounting-benchmark:{entity}");
      var total = Enumerable.Range(0, TransactionsPerClient).Sum(AmountFor);

      db.PracticeClients.Add(new PracticeClient
      {
        Id = clientId, FirmId = firmId, LegalName = "ACCOUNTING BENCHMARK " + entity, CreatedAt = now
      });
      db.Engagements.Add(new Engagement
      {
        Id = engagementId, FirmId = firmId, PracticeClientId = clientId, Status = "Active",
        ProfessionalWorkBlocked = false, CreatedAt = now
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = firmId, ClientId = clientId, PeriodCode = "2026",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
        CreatedByUserId = preparer.Id, CreatedAt = now
      });
      db.ClientReportingBooks.Add(new ClientReportingBook
      {
        Id = bookId, FirmId = firmId, ClientId = clientId, PeriodId = periodId, Code = "STAT",
        Basis = "STATUTORY", InclusionRule = "STATUTORY_ONLY", Currency = "QAR",
        Status = AccountingWorkflowStates.Active, CreatedByUserId = preparer.Id, CreatedAt = now
      });
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        SourceKind = "Raw", Revision = 1, LegalEntityKey = entity,
        Currency = "QAR", RawFileSha256Hex = digest, NormalizedDatasetDigest = digest, Sha256Hex = digest,
        ImportProfileVersion = "benchmark-v1", SourceLayout = TrialBalanceLayouts.SignedNet, Balanced = true,
        ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Loading, ControlTotal = 0m,
        ImportedAt = now, ImportedByUserId = preparer.Id
      });
      foreach (var (account, amount) in new[]
      {
        ("1000", total), ("1100", total), ("2000", -total), ("4000", -total)
      })
        db.TrialBalanceRows.Add(new TrialBalanceRow
        {
          Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = account,
          AccountName = "Benchmark " + account, Amount = amount, Currency = "QAR", Entity = entity
        });

      db.SourceImportBatches.Add(new SourceImportBatch
      {
        Id = importBatchId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        PeriodId = periodId, BookId = bookId, SourceKind = "GL", ProfileVersion = "benchmark-v1",
        ParserVersion = "benchmark-v1", RawFileSha256Hex = digest, NormalizedDatasetDigest = digest,
        LegalEntityKey = entity, Currency = "QAR", RowCount = TransactionsPerClient * LinesPerTransaction,
        ExpectedChunkCount = 1, ExpectedTransactionCount = TransactionsPerClient,
        ExpectedLineCount = TransactionsPerClient * LinesPerTransaction, AcceptedChunkCount = 1,
        AcceptedTransactionCount = TransactionsPerClient, AcceptedLineCount = TransactionsPerClient * LinesPerTransaction,
        Status = "SEALED", ReceiptReference = "accounting-benchmark:" + entity,
        CreatedByUserId = preparer.Id, CreatedAt = now
      });
      for (var transactionIndex = 0; transactionIndex < TransactionsPerClient; transactionIndex++)
      {
        var amount = AmountFor(transactionIndex);
        var transactionId = Guid.CreateVersion7();
        db.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
        {
          Id = transactionId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
          ImportBatchId = importBatchId, StableJournalId = $"{entity}-J-{transactionIndex:0000}",
          DocumentNumber = $"{entity}-D-{transactionIndex:0000}",
          PostingDate = new DateOnly(2026, 1, 1).AddDays(transactionIndex % 365),
          DocumentDate = new DateOnly(2026, 1, 1).AddDays(transactionIndex % 365),
          SourceUser = "benchmark", SourceSystem = "benchmark", Currency = "QAR",
          IsManual = transactionIndex % 10 == 0, CreatedAt = now
        });
        AddLine(db, firmId, clientId, engagementId, importBatchId, transactionId, entity, transactionIndex, "1000", amount, 0m, amount);
        AddLine(db, firmId, clientId, engagementId, importBatchId, transactionId, entity, transactionIndex, "1100", amount, 0m, amount);
        AddLine(db, firmId, clientId, engagementId, importBatchId, transactionId, entity, transactionIndex, "2000", 0m, amount, -amount);
        AddLine(db, firmId, clientId, engagementId, importBatchId, transactionId, entity, transactionIndex, "4000", 0m, amount, -amount);
      }
      fixtures.Add(new Fixture(clientId, engagementId, periodId, bookId, datasetId, importBatchId, preparer));
    }

    await db.SaveChangesAsync();
    await db.Database.ExecuteSqlRawAsync("UPDATE trial_balance_datasets SET import_state = 'SEALED'");
    return (firmId, fixtures);
  }

  private static void AddLine(AuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId,
    Guid importBatchId, Guid transactionId, string entity, int transactionIndex, string accountCode,
    decimal debit, decimal credit, decimal amount)
  {
    db.GeneralLedgerLines.Add(new GeneralLedgerLine
    {
      Id = Guid.CreateVersion7(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      ImportBatchId = importBatchId, TransactionId = transactionId,
      StableLineId = $"{entity}-J-{transactionIndex:0000}-{accountCode}", AccountCode = accountCode,
      Debit = debit, Credit = credit, OriginalCurrency = "QAR", OriginalAmount = amount,
      FunctionalAmount = amount, CreatedAt = DateTimeOffset.UtcNow
    });
  }

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = name + "-" + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = name + "@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static decimal AmountFor(int transactionIndex) => transactionIndex == 0
    ? 999_999_999_999.123456m
    : 100m + transactionIndex % 37 + 0.123456m;
}
