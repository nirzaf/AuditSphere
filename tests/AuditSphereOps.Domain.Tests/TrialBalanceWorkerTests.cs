using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed class TrialBalanceWorkerTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task AT07_UnbalancedDataset_IsPersistentlyRejected_OriginalRowsPreserved()
  {
    var connection = new NpgsqlConnectionStringBuilder(
      Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_CONNECTION") ??
      "Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres");
    if (connection.Database != "auditsphere_tests" || connection.Host != "127.0.0.1")
      throw new InvalidOperationException("This test requires the loopback auditsphere_tests database.");
    // A unique schema isolates this test from other synthetic data. No database reset.
    var schema = "test_at07_" + Guid.NewGuid().ToString("N");
    await using var admin = new NpgsqlConnection(connection.ConnectionString);
    await admin.OpenAsync();
    await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin))
      await create.ExecuteNonQueryAsync();
    try
    {
      connection.SearchPath = schema;
      var options = new DbContextOptionsBuilder<AuditSphereDbContext>()
        .UseNpgsql(connection.ConnectionString).Options;
      var id = Guid.NewGuid();
      await using (var seed = new AuditSphereDbContext(options))
      {
        await seed.Database.MigrateAsync();
        seed.TrialBalanceDatasets.Add(new TrialBalanceDataset
        {
          Id = id, FirmId = Guid.NewGuid(), ClientId = Guid.NewGuid(),
          EngagementId = Guid.NewGuid(), Currency = "QAR", ImportedAt = DateTimeOffset.UtcNow
        });
        seed.TrialBalanceRows.AddRange(
          new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = id, AccountCode = "001", Amount = 100m, Currency = "QAR" },
          new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = id, AccountCode = "002", Amount = -90m, Currency = "QAR" });
        await seed.SaveChangesAsync();
      }
      var worker = new AuditSphereOps.Worker.Worker(new Factory(options),
        NullLogger<AuditSphereOps.Worker.Worker>.Instance);
      Assert.True(await worker.ProcessNextAsync());
      Assert.False(await worker.ProcessNextAsync());
      await using var verify = new AuditSphereDbContext(options);
      var result = await verify.TrialBalanceDatasets.SingleAsync(x => x.Id == id);
      Assert.Equal("Rejected", result.ValidationStatus);
      Assert.False(result.Balanced);
      Assert.Equal(10m, result.ControlTotal);
      Assert.Equal(new[] { 100m, -90m }, await verify.TrialBalanceRows
        .Where(x => x.DatasetId == id).OrderBy(x => x.AccountCode).Select(x => x.Amount).ToArrayAsync());
    }
    finally
    {
      // Only the random schema created by this test is removed.
      await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin);
      await drop.ExecuteNonQueryAsync();
    }
  }

  private sealed class Factory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }
}
