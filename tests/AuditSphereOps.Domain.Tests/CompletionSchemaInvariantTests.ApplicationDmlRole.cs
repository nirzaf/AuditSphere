using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class CompletionSchemaInvariantTests
{
  /// <summary>
  /// Gives a migration probe the same database boundary as ordinary application DML: no login,
  /// ownership, superuser, role-management, database-creation, or schema-definition privileges.
  /// The test fixture authenticates as its database administrator only to create and remove this role.
  /// </summary>
  private sealed class ApplicationDmlRole : IAsyncDisposable
  {
    private readonly string _connectionString;
    private bool _disposed;

    private ApplicationDmlRole(string name, string connectionString)
    {
      Name = name;
      _connectionString = connectionString;
    }

    public string Name { get; }

    public static async Task<ApplicationDmlRole> CreateAsync(PgTestSchema pg)
    {
      var role = new ApplicationDmlRole(
        "wu004_dml_" + Guid.NewGuid().ToString("N"), pg.ConnectionString);
      await using var connection = new NpgsqlConnection(pg.ConnectionString);
      await connection.OpenAsync();

      await using (var create = new NpgsqlCommand(
        $"CREATE ROLE {role.Name} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT", connection))
        await create.ExecuteNonQueryAsync();

      try
      {
        var schema = new NpgsqlCommandBuilder().QuoteIdentifier(pg.Schema);
        await using var grant = new NpgsqlCommand($"""
          GRANT USAGE ON SCHEMA {schema} TO {role.Name};
          GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO {role.Name};
          GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {schema} TO {role.Name};
          """, connection);
        await grant.ExecuteNonQueryAsync();
        return role;
      }
      catch
      {
        await role.DisposeAsync();
        throw;
      }
    }

    public async Task SetLocalAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
      await using var command = new NpgsqlCommand($"SET LOCAL ROLE {Name}", connection, transaction);
      await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
      if (_disposed) return;
      _disposed = true;

      await using var connection = new NpgsqlConnection(_connectionString);
      await connection.OpenAsync();
      await using var cleanup = new NpgsqlCommand($"DROP OWNED BY {Name}; DROP ROLE {Name}", connection);
      await cleanup.ExecuteNonQueryAsync();
    }
  }
}
