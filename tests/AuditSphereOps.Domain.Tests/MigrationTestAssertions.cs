using System.Reflection;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

internal static class MigrationTestAssertions
{
  public static async Task AssertDownOperationBlockedAsync(
    AuditSphereDbContext db,
    string migrationId,
    string sqlState,
    string expectedMessage)
  {
    var assembly = db.GetService<IMigrationsAssembly>();
    var migrationType = assembly.Migrations[migrationId].AsType();
    var migration = assembly.CreateMigration(migrationType.GetTypeInfo(), db.Database.ProviderName!);
    var down = migrationType.GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.NotNull(down);

    var builder = new MigrationBuilder(db.Database.ProviderName!);
    down.Invoke(migration, [builder]);
    var guard = Assert.IsType<SqlOperation>(builder.Operations.OfType<SqlOperation>().First());
    Assert.Contains(expectedMessage, guard.Sql, StringComparison.Ordinal);

    await using var transaction = await db.Database.BeginTransactionAsync();
    var refusal = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(guard.Sql));
    await transaction.RollbackAsync();

    Assert.Equal(sqlState, refusal.SqlState);
    Assert.Contains(expectedMessage, refusal.MessageText, StringComparison.Ordinal);
  }
}
