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

  private readonly NpgsqlConnection _admin;

  private PgTestSchema(string schema, DbContextOptions<AuditSphereDbContext> options, NpgsqlConnection admin)
  {
    Schema = schema;
    Options = options;
    _admin = admin;
  }

  public static async Task<PgTestSchema> CreateAsync(string? targetMigration = null)
  {
    var builder = new NpgsqlConnectionStringBuilder(
      Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_CONNECTION") ??
      "Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres");
    if (builder.Database != "auditsphere_tests" || builder.Host != "127.0.0.1")
      throw new InvalidOperationException("This test requires the loopback auditsphere_tests database.");

    var schema = "test_" + Guid.NewGuid().ToString("N");
    var admin = new NpgsqlConnection(builder.ConnectionString);
    await admin.OpenAsync();
    if (admin.PostgreSqlVersion.Major != 18)
    {
      await admin.DisposeAsync();
      throw new InvalidOperationException("Database tests require PostgreSQL 18.");
    }
    await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin))
      await create.ExecuteNonQueryAsync();

    builder.SearchPath = schema;
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>()
      .UseNpgsql(builder.ConnectionString).Options;
    await using (var db = new AuditSphereDbContext(options))
      await db.GetService<IMigrator>().MigrateAsync(targetMigration);

    return new PgTestSchema(schema, options, admin);
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
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    await db.SaveChangesAsync();
    return (firmId, clientId, engagementId);
  }

  public async ValueTask DisposeAsync()
  {
    try
    {
      await using var drop = new NpgsqlCommand($"DROP SCHEMA {Schema} CASCADE", _admin);
      await drop.ExecuteNonQueryAsync();
    }
    finally
    {
      await _admin.DisposeAsync();
    }
  }
}
