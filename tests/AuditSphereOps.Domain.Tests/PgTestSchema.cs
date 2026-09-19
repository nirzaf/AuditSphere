using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AuditSphereOps.Domain.Completion;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Shared PostgreSQL test host. Each instance creates a uniquely named schema in
/// auditsphere_tests, migrates it, and drops only that schema on dispose.
/// The loopback/database guard refuses any other target; there is no InMemory fallback.
/// </summary>
public sealed class PgTestSchema : IAsyncDisposable
{
  public string Schema { get; }
  public DbContextOptions<AuditSphereDbContext> Options { get; }
  public string ConnectionString { get; }

  private readonly string _adminConnectionString;

  private PgTestSchema(
    string schema,
    DbContextOptions<AuditSphereDbContext> options,
    string connectionString,
    string adminConnectionString)
  {
    Schema = schema;
    Options = options;
    ConnectionString = connectionString;
    _adminConnectionString = adminConnectionString;
  }

  public static async Task<PgTestSchema> CreateAsync(string? targetMigration = null)
  {
    var rawConnection = Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_CONNECTION") ??
      "Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres";

    var baseBuilder = new NpgsqlConnectionStringBuilder(rawConnection);
    if (baseBuilder.Database != "auditsphere_tests" || baseBuilder.Host != "127.0.0.1")
      throw new InvalidOperationException("This test requires the loopback auditsphere_tests database.");

    var schema = "test_" + Guid.NewGuid().ToString("N");

    // Administrative connection: unpooled so it never occupies or waits for slots
    // in any shared connection pool and is disposed immediately.
    var adminBuilder = new NpgsqlConnectionStringBuilder(rawConnection)
    {
      Pooling = false,
      Timeout = 60
    };
    await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
    {
      await admin.OpenAsync();
      if (admin.PostgreSqlVersion.Major != 18)
        throw new InvalidOperationException("Database tests require PostgreSQL 18.");

      await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin);
      await create.ExecuteNonQueryAsync();
    }

    // Per-schema connection string: each test schema gets its own search path and pool.
    var schemaBuilder = new NpgsqlConnectionStringBuilder(rawConnection)
    {
      SearchPath = schema,
      Pooling = true,
      Timeout = 60
    };
    schemaBuilder["Maximum Pool Size"] = "6";

    var schemaConnectionString = schemaBuilder.ConnectionString;
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>()
      .UseNpgsql(schemaConnectionString).Options;
    await using (var db = new AuditSphereDbContext(options))
      await db.GetService<IMigrator>().MigrateAsync(targetMigration);

    return new PgTestSchema(schema, options, schemaConnectionString, adminBuilder.ConnectionString);
  }

  /// <summary>Seeds one valid firm/client/engagement scope and returns its identifiers.</summary>
  public async Task<(Guid FirmId, Guid ClientId, Guid EngagementId)> SeedScopeAsync()
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(Options);
    db.PracticeClients.Add(new AuditSphereOps.Domain.Practice.PracticeClient
    {
      Id = clientId,
      FirmId = firmId,
      LegalName = "TEST CLIENT " + clientId.ToString("N")[..8],
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
    {
      Id = engagementId,
      FirmId = firmId,
      PracticeClientId = clientId,
      CreatedAt = DateTimeOffset.UtcNow
    });
    var hasRecoveryEpoch = await db.Database.SqlQuery<int>($"""
      SELECT count(*)::int AS "Value"
      FROM information_schema.columns
      WHERE table_schema = current_schema() AND table_name = 'firm_safety_states'
        AND column_name = 'recovery_epoch'
      """).SingleAsync() == 1;
    if (hasRecoveryEpoch)
      db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    else
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO firm_safety_states (id, operating_mode, deployment_epoch, policy_generation)
        VALUES ({firmId}, 'LOCAL_ONLY', 1, 1)
        """);
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    await db.SaveChangesAsync();
    return (firmId, clientId, engagementId);
  }

  public async ValueTask DisposeAsync()
  {
    try
    {
      NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
    }
    catch
    {
      // Best-effort pool eviction
    }

    try
    {
      await using var admin = new NpgsqlConnection(_adminConnectionString);
      await admin.OpenAsync();
      await using var drop = new NpgsqlCommand($"DROP SCHEMA {Schema} CASCADE", admin);
      await drop.ExecuteNonQueryAsync();
    }
    catch
    {
      // Best-effort cleanup
    }
  }
}
