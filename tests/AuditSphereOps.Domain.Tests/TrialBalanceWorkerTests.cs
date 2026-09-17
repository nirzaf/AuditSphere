using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TbWorker = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

public sealed class TrialBalanceWorkerTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task AT07_UnbalancedDataset_IsPersistentlyRejected_OriginalRowsPreserved()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, engagementId) = await pg.SeedScopeAsync();
    var id = Guid.NewGuid();
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = id, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        Currency = "QAR", ImportedAt = DateTimeOffset.UtcNow
      });
      seed.TrialBalanceRows.AddRange(
        new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = id, AccountCode = "001", Amount = 100m, Currency = "QAR" },
        new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = id, AccountCode = "002", Amount = -90m, Currency = "QAR" });
      await seed.SaveChangesAsync();
    }

    var worker = CreateWorker(pg, firmId);
    Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync());

    await using var verify = new AuditSphereDbContext(pg.Options);
    var result = await verify.TrialBalanceDatasets.SingleAsync(x => x.Id == id);
    Assert.Equal("Rejected", result.ValidationStatus);
    Assert.False(result.Balanced);
    Assert.Equal(10m, result.ControlTotal);
    Assert.Equal(new[] { 100m, -90m }, await verify.TrialBalanceRows
      .Where(x => x.DatasetId == id).OrderBy(x => x.AccountCode).Select(x => x.Amount).ToArrayAsync());
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ConcurrentWorkers_ClaimEachPendingDatasetExactlyOnce()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, engagementId) = await pg.SeedScopeAsync();
    var balanced = Guid.NewGuid();
    var unbalanced = Guid.NewGuid();
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = balanced, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        Currency = "QAR", ImportedAt = DateTimeOffset.UtcNow
      });
      seed.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = unbalanced, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        Currency = "QAR", ImportedAt = DateTimeOffset.UtcNow
      });
      seed.TrialBalanceRows.AddRange(
        new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = balanced, AccountCode = "001", Amount = 5m, Currency = "QAR" },
        new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = balanced, AccountCode = "002", Amount = -5m, Currency = "QAR" },
        new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = unbalanced, AccountCode = "001", Amount = 1m, Currency = "QAR" });
      await seed.SaveChangesAsync();
    }

    var worker = CreateWorker(pg, firmId);
    // Both start simultaneously; FOR UPDATE SKIP LOCKED plus the terminal-status filter
    // must yield exactly one claim per dataset across both workers.
    var gate = new TaskCompletionSource();
    var first = Task.Run(async () => { await gate.Task; return await worker.ProcessNextAsync(); });
    var second = Task.Run(async () => { await gate.Task; return await worker.ProcessNextAsync(); });
    gate.TrySetResult();
    var results = await Task.WhenAll(first, second);
    var remaining = await worker.DrainAsync();
    Assert.Equal(2, results.Count(r => r) + remaining);

    await using var verify = new AuditSphereDbContext(pg.Options);
    var statuses = await verify.TrialBalanceDatasets.ToDictionaryAsync(x => x.Id, x => x.ValidationStatus);
    Assert.Equal("Accepted", statuses[balanced]);
    Assert.Equal("Rejected", statuses[unbalanced]);
    Assert.Equal(0m, (await verify.TrialBalanceDatasets.SingleAsync(x => x.Id == balanced)).ControlTotal);
  }

  private static TbWorker CreateWorker(PgTestSchema pg, Guid firmId)
  {
    var factory = new OperationContextFactory(new DbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var options = new WorkerOptions(firmId, "Test");
    var handler = new TrialBalanceValidationHandler();
    var registry = new DurableOperationRegistry([handler], options);
    return new(new OperationDispatcher(store, registry, options),
      new TrialBalanceDiscovery(factory, store, handler, options), NullLogger<TbWorker>.Instance);
  }

  private sealed class DbContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }
}

