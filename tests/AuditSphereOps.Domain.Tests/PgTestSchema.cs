using System.Security.Cryptography;
using System.Text;
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
public interface ITestPostgresDatabase
{
  DbContextOptions<AuditSphereDbContext> Options { get; }
  string ConnectionString { get; }
}

public sealed class PgTestSchema : IAsyncDisposable, ITestPostgresDatabase
{
  /// <summary>The schema holding the application tables inside this test's own database.</summary>
  public string Schema => "public";
  public DbContextOptions<AuditSphereDbContext> Options { get; }
  public string ConnectionString { get; }

  private readonly string _database;
  private readonly string _adminConnectionString;

  private PgTestSchema(
    string database,
    DbContextOptions<AuditSphereDbContext> options,
    string connectionString,
    string adminConnectionString)
  {
    _database = database;
    Options = options;
    ConnectionString = connectionString;
    _adminConnectionString = adminConnectionString;
  }

  // Template creation is serialised in-process; the advisory lock serialises it across concurrent test processes.
  private static readonly SemaphoreSlim TemplateGate = new(1, 1);

  public static async Task<PgTestSchema> CreateAsync(string? targetMigration = null)
  {
    var rawConnection = Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_CONNECTION") ??
      "Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres";

    var baseBuilder = new NpgsqlConnectionStringBuilder(rawConnection);
    if (baseBuilder.Database != "auditsphere_tests" || baseBuilder.Host != "127.0.0.1")
      throw new InvalidOperationException("This test requires the loopback auditsphere_tests database.");

    var testDatabase = "test_" + Guid.NewGuid().ToString("N");

    // Administrative connection: unpooled so it never occupies or waits for slots in a shared pool.
    var adminConnectionString = new NpgsqlConnectionStringBuilder(rawConnection) { Pooling = false, Timeout = 60 }.ConnectionString;

    if (targetMigration is null)
    {
      // A clone of the migrated template is a file copy; replaying 187 migrations per test was the dominant cost.
      var template = await EnsureTemplateAsync(rawConnection, adminConnectionString);
      await ExecuteAdminAsync(adminConnectionString, $"CREATE DATABASE {testDatabase} TEMPLATE {template}");
    }
    else
    {
      // Partial migration targets cannot use the full-migration template, so they migrate a fresh database to the target.
      await ExecuteAdminAsync(adminConnectionString, $"CREATE DATABASE {testDatabase}");
    }

    // Per-test database connection string: each test gets its own database and pool.
    var databaseBuilder = new NpgsqlConnectionStringBuilder(rawConnection)
    {
      Database = testDatabase,
      Pooling = true,
      Timeout = 60,
      IncludeErrorDetail = true
    };
    databaseBuilder["Maximum Pool Size"] = "6";

    var connectionString = databaseBuilder.ConnectionString;
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>()
      .UseNpgsql(connectionString).Options;
    if (targetMigration is not null)
    {
      await using var db = new AuditSphereDbContext(options);
      await db.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    return new PgTestSchema(testDatabase, options, connectionString, adminConnectionString);
  }

  /// <summary>
  /// Returns the name of a template database holding the full migration set. The name is a fingerprint of the migration
  /// list, so a new migration produces a new template and stale templates are never reused. A template is built under a
  /// temporary name and renamed only after every migration succeeds, so a failed build can never be cloned.
  /// </summary>
  private static async Task<string> EnsureTemplateAsync(string rawConnection, string adminConnectionString)
  {
    var probeOptions = new DbContextOptionsBuilder<AuditSphereDbContext>().UseNpgsql(rawConnection).Options;
    string[] migrations;
    await using (var probe = new AuditSphereDbContext(probeOptions))
      migrations = probe.Database.GetMigrations().ToArray();
    var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', migrations)))).ToLowerInvariant()[..16];
    var template = "auditsphere_tpl_" + fingerprint;

    await TemplateGate.WaitAsync();
    try
    {
      await using var admin = new NpgsqlConnection(adminConnectionString);
      await admin.OpenAsync();
      if (admin.PostgreSqlVersion.Major != 18)
        throw new InvalidOperationException("Database tests require PostgreSQL 18.");

      await ExecuteOnAsync(admin, "SELECT pg_advisory_lock(hashtext('auditsphere_tpl'))");
      try
      {
        if (await ScalarOnAsync(admin, $"SELECT 1 FROM pg_database WHERE datname = '{template}'") is null)
        {
          var building = template + "_building";
          await ExecuteOnAsync(admin, $"DROP DATABASE IF EXISTS {building} WITH (FORCE)");
          await ExecuteOnAsync(admin, $"CREATE DATABASE {building}");
          try
          {
            var buildBuilder = new NpgsqlConnectionStringBuilder(rawConnection) { Database = building, Pooling = false, Timeout = 60 };
            var buildOptions = new DbContextOptionsBuilder<AuditSphereDbContext>().UseNpgsql(buildBuilder.ConnectionString).Options;
            await using (var db = new AuditSphereDbContext(buildOptions))
              await db.GetService<IMigrator>().MigrateAsync();
            NpgsqlConnection.ClearAllPools();
            await ExecuteOnAsync(admin, $"ALTER DATABASE {building} RENAME TO {template}");
          }
          catch
          {
            NpgsqlConnection.ClearAllPools();
            await ExecuteOnAsync(admin, $"DROP DATABASE IF EXISTS {building} WITH (FORCE)");
            throw;
          }
        }
      }
      finally
      {
        await ExecuteOnAsync(admin, "SELECT pg_advisory_unlock(hashtext('auditsphere_tpl'))");
      }
      return template;
    }
    finally
    {
      TemplateGate.Release();
    }
  }

  private static async Task ExecuteAdminAsync(string adminConnectionString, string sql)
  {
    await using var admin = new NpgsqlConnection(adminConnectionString);
    await admin.OpenAsync();
    if (admin.PostgreSqlVersion.Major != 18)
      throw new InvalidOperationException("Database tests require PostgreSQL 18.");
    await ExecuteOnAsync(admin, sql);
  }

  private static async Task ExecuteOnAsync(NpgsqlConnection connection, string sql)
  {
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
  }

  private static async Task<object?> ScalarOnAsync(NpgsqlConnection connection, string sql)
  {
    await using var command = new NpgsqlCommand(sql, connection);
    return await command.ExecuteScalarAsync();
  }

  /// <summary>Seeds one valid firm/client/engagement scope and returns its identifiers.</summary>
  public async Task<(Guid FirmId, Guid ClientId, Guid EngagementId)> SeedScopeAsync()
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(Options);
    var hasEntityType = await db.Database.SqlQuery<int>($"""
      SELECT count(*)::int AS "Value"
      FROM information_schema.columns
      WHERE table_schema = current_schema() AND table_name = 'practice_clients'
        AND column_name = 'entity_type'
      """).SingleAsync() == 1;
    if (hasEntityType)
    {
      db.PracticeClients.Add(new AuditSphereOps.Domain.Practice.PracticeClient
      {
        Id = clientId,
        FirmId = firmId,
        LegalName = "TEST CLIENT " + clientId.ToString("N")[..8],
        CreatedAt = DateTimeOffset.UtcNow
      });
    }
    else
    {
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO practice_clients (id, firm_id, legal_name, created_at, status)
        VALUES ({clientId}, {firmId}, {"TEST CLIENT " + clientId.ToString("N")[..8]}, {DateTimeOffset.UtcNow}, 'Active')
        """);
    }
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
      await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_database} WITH (FORCE)", admin);
      await drop.ExecuteNonQueryAsync();
    }
    catch
    {
      // Best-effort cleanup
    }
  }
}
