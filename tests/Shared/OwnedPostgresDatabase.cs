using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Testing;

internal sealed class OwnedPostgresDatabase : IAsyncDisposable, ITestPostgresDatabase
{
  private const string DefaultTestConnection =
    "Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=postgres";
  private readonly string adminConnectionString;
  private readonly string ownershipMarker;
  private readonly string manifestPath;

  public string RunRoot { get; }
  public string DatabaseName { get; }
  public string ConnectionString { get; }
  public DbContextOptions<AuditSphereDbContext> Options { get; }

  private OwnedPostgresDatabase(
    string runRoot,
    string manifestPath,
    string databaseName,
    string connectionString,
    DbContextOptions<AuditSphereDbContext> options,
    string adminConnectionString,
    string ownershipMarker)
  {
    RunRoot = runRoot;
    this.manifestPath = manifestPath;
    DatabaseName = databaseName;
    ConnectionString = connectionString;
    Options = options;
    this.adminConnectionString = adminConnectionString;
    this.ownershipMarker = ownershipMarker;
  }

  public static async Task<OwnedPostgresDatabase> CreateAsync(string caseId, string? targetMigration = null)
  {
    if (!Regex.IsMatch(caseId, "^[A-Z0-9-]{1,48}$", RegexOptions.CultureInvariant))
      throw new ArgumentException("A stable scenario CaseId is required.", nameof(caseId));

    var runId = Regex.Replace(Environment.GetEnvironmentVariable("E2E_RUN_ID") ?? "local", "[^A-Za-z0-9-]", "-");
    if (runId.Length > 40) runId = runId[..40];
    var databaseName = $"auditsphere_e2e_{Guid.NewGuid():N}";
    var baseRoot = Environment.GetEnvironmentVariable("E2E_RUN_ROOT") ??
      Path.Combine(Path.GetTempPath(), "AuditSphereOps-e2e");
    var runRoot = Path.GetFullPath(Path.Combine(baseRoot, $"{runId}-{caseId}-{Guid.NewGuid():N}"));
    if (runRoot == Path.GetPathRoot(runRoot))
      throw new InvalidOperationException("Refusing to use a filesystem root for E2E artifacts.");
    Directory.CreateDirectory(runRoot);

    var manifestPath = Path.Combine(runRoot, "ownership.json");
    using var testProcess = Process.GetCurrentProcess();
    var manifest = new OwnershipManifest(runId, caseId, DateTimeOffset.UtcNow,
      databaseName, "ALLOCATING", [new ProcessOwner("test-runner", testProcess.Id, testProcess.StartTime.ToUniversalTime())]);
    await WriteManifestAsync(manifestPath, manifest);

    var adminConnectionString = CreateAdminConnectionString();
    var adminBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString);
    if (adminBuilder.Host != "127.0.0.1")
      throw new InvalidOperationException("E2E database provisioning is restricted to PostgreSQL on 127.0.0.1.");

    var ownershipMarker = $"AuditSphere E2E owner {runId} / {caseId}";
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>()
      .UseNpgsql(CreateDatabaseConnectionString(adminBuilder.ConnectionString, databaseName))
      .Options;
    var owned = new OwnedPostgresDatabase(runRoot, manifestPath, databaseName,
      CreateDatabaseConnectionString(adminBuilder.ConnectionString, databaseName), options,
      adminBuilder.ConnectionString, ownershipMarker);

    var databaseCreated = false;
    try
    {
      await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
      {
        await admin.OpenAsync();
        var version = (int)(await new NpgsqlCommand("SELECT current_setting('server_version_num')::integer", admin)
          .ExecuteScalarAsync())!;
        if (version != 180006)
          throw new InvalidOperationException($"E2E requires PostgreSQL 18.6 (180006); server reported {version}.");
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await create.ExecuteNonQueryAsync();
        databaseCreated = true;
        await using var comment = new NpgsqlCommand(
          $"COMMENT ON DATABASE \"{databaseName}\" IS '{ownershipMarker}'", admin);
        await comment.ExecuteNonQueryAsync();
      }

      await using (var db = new AuditSphereDbContext(options))
        await db.GetService<IMigrator>().MigrateAsync(targetMigration);
      await WriteManifestAsync(manifestPath, manifest with { State = "READY" });
      return owned;
    }
    catch
    {
      await owned.CleanupAsync(allowUnmarkedNewDatabase: databaseCreated);
      throw;
    }
  }

  public async Task RecordProcessAsync(string role, System.Diagnostics.Process process)
  {
    var start = process.StartTime.ToUniversalTime();
    var json = await File.ReadAllTextAsync(manifestPath);
    var current = JsonSerializer.Deserialize<OwnershipManifest>(json)
      ?? throw new InvalidDataException("Scenario ownership manifest is invalid.");
    await WriteManifestAsync(manifestPath, current with
    {
      Processes = [.. current.Processes, new ProcessOwner(role, process.Id, start)]
    });
  }

  public async ValueTask DisposeAsync()
    => await CleanupAsync(allowUnmarkedNewDatabase: false);

  private async Task CleanupAsync(bool allowUnmarkedNewDatabase)
  {
    using (var pooled = new NpgsqlConnection(ConnectionString)) NpgsqlConnection.ClearPool(pooled);
    await using var admin = new NpgsqlConnection(adminConnectionString);
    await admin.OpenAsync();
    await using var marker = new NpgsqlCommand(
      "SELECT shobj_description(oid, 'pg_database') FROM pg_database WHERE datname = @name", admin);
    marker.Parameters.AddWithValue("name", DatabaseName);
    await using var reader = await marker.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
      await MarkCleanedAsync();
      return;
    }
    var observed = reader.IsDBNull(0) ? null : reader.GetString(0);
    await reader.CloseAsync();
    if (observed != ownershipMarker && !(allowUnmarkedNewDatabase && observed is null))
      throw new InvalidOperationException("Refusing to drop an E2E database without its exact ownership marker.");

    await using var drop = new NpgsqlCommand($"DROP DATABASE \"{DatabaseName}\" WITH (FORCE)", admin);
    await drop.ExecuteNonQueryAsync();
    await MarkCleanedAsync();
  }

  private async Task MarkCleanedAsync()
  {
    var json = await File.ReadAllTextAsync(manifestPath);
    var current = JsonSerializer.Deserialize<OwnershipManifest>(json)
      ?? throw new InvalidDataException("Scenario ownership manifest is invalid.");
    await WriteManifestAsync(manifestPath, current with { State = "CLEANED" });
  }

  private static string CreateAdminConnectionString()
  {
    var supplied = Environment.GetEnvironmentVariable("E2E_ADMIN_CONNECTION");
    var raw = supplied ?? Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_CONNECTION") ?? DefaultTestConnection;
    var builder = new NpgsqlConnectionStringBuilder(raw) { Pooling = false, Timeout = 30 };
    if (builder.Host != "127.0.0.1")
      throw new InvalidOperationException("E2E database provisioning is restricted to PostgreSQL on 127.0.0.1.");
    if (supplied is not null && builder.Database != "postgres")
      throw new InvalidOperationException("E2E_ADMIN_CONNECTION must target the local postgres administration database.");
    if (supplied is null && builder.Database != "auditsphere_tests")
      throw new InvalidOperationException("AUDITSPHERE_TEST_CONNECTION must target auditsphere_tests.");
    builder.Database = "postgres";
    return builder.ConnectionString;
  }

  private static string CreateDatabaseConnectionString(string adminConnection, string databaseName)
  {
    var builder = new NpgsqlConnectionStringBuilder(adminConnection)
    {
      Database = databaseName,
      Pooling = true,
      Timeout = 30,
      MaxPoolSize = 10
    };
    return builder.ConnectionString;
  }

  private static async Task WriteManifestAsync(string path, OwnershipManifest manifest)
  {
    var temp = path + ".tmp";
    await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    File.Move(temp, path, overwrite: true);
  }

  private sealed record OwnershipManifest(
    string RunId, string CaseId, DateTimeOffset CreatedAt, string DatabaseName, string State,
    IReadOnlyList<ProcessOwner> Processes);
  private sealed record ProcessOwner(string Role, int ProcessId, DateTimeOffset StartedAtUtc);
}
