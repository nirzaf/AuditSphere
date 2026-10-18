using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

internal static class PermanentFileFreezeMigrationAssertions
{
  private const string FreezeMigration = "20261009141822_PermanentFileFreeze";
  public static async Task AssertDowngradeBlockedAsync(AuditSphereDbContext db, string migrationSuffix)
  {
    var migrations = db.Database.GetMigrations().ToArray();
    var index = Array.FindIndex(migrations, x => x.EndsWith(migrationSuffix, StringComparison.Ordinal));
    Assert.True(index > 0, $"Migration ending in {migrationSuffix} was not found.");

    var applied = await db.Database.GetAppliedMigrationsAsync();
    Assert.Contains(migrations[index], applied);
    Assert.Contains(FreezeMigration, applied);

    var failure = await Record.ExceptionAsync(() =>
      db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]));
    Assert.NotNull(failure);
    while (failure is not PostgresException && failure.InnerException is not null)
      failure = failure.InnerException;
    var error = Assert.IsType<PostgresException>(failure);

    Assert.Contains(error.SqlState, new[] { "55000", "P0001" });
    Assert.True(
      error.MessageText.Contains("retained", StringComparison.OrdinalIgnoreCase) ||
      error.MessageText.Contains("downgrade", StringComparison.OrdinalIgnoreCase) ||
      error.MessageText.Contains("compliance safety boundary", StringComparison.OrdinalIgnoreCase),
      $"Expected a retention or downgrade safety refusal, got: {error.MessageText}");
    var appliedAfterRefusal = await db.Database.GetAppliedMigrationsAsync();
    Assert.Contains(migrations[index], appliedAfterRefusal);
    Assert.Contains(FreezeMigration, appliedAfterRefusal);
  }
}
