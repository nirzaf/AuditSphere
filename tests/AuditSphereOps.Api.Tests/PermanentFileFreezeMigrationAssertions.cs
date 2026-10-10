using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

internal static class PermanentFileFreezeMigrationAssertions
{
  private const string FreezeMigration = "20261009141822_PermanentFileFreeze";
  private const string Refusal = "PermanentFileFreeze is a compliance safety boundary and cannot be downgraded automatically.";

  public static async Task AssertDowngradeBlockedAsync(AuditSphereDbContext db, string migrationSuffix)
  {
    var migrations = db.Database.GetMigrations().ToArray();
    var index = Array.FindIndex(migrations, x => x.EndsWith(migrationSuffix, StringComparison.Ordinal));
    Assert.True(index > 0, $"Migration ending in {migrationSuffix} was not found.");

    var applied = await db.Database.GetAppliedMigrationsAsync();
    Assert.Contains(migrations[index], applied);
    Assert.Contains(FreezeMigration, applied);

    var error = await Assert.ThrowsAsync<PostgresException>(() =>
      db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]));

    Assert.Equal("P0001", error.SqlState);
    Assert.Equal(Refusal, error.MessageText);
    var appliedAfterRefusal = await db.Database.GetAppliedMigrationsAsync();
    Assert.Contains(migrations[index], appliedAfterRefusal);
    Assert.Contains(FreezeMigration, appliedAfterRefusal);
  }
}
