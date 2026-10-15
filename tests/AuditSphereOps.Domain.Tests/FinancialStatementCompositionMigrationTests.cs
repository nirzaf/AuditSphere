using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Domain.Tests;

public sealed class FinancialStatementCompositionMigrationTests
{
  private const string Previous = "20261014234828_FinancialStatementComposition";
  private const string Current = "20261015000622_FinancialStatementReviewCompositionBinding";

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ReviewCompositionBindingMigration_CanRollbackAndReapply()
  {
    await using var pg = await PgTestSchema.CreateAsync(Previous);
    await using var db = new AuditSphereDbContext(pg.Options);
    var migrator = db.GetService<IMigrator>();

    await migrator.MigrateAsync(Current);
    Assert.Contains(Current, await db.Database.GetAppliedMigrationsAsync());

    await migrator.MigrateAsync(Previous);
    Assert.DoesNotContain(Current, await db.Database.GetAppliedMigrationsAsync());

    await migrator.MigrateAsync();
    Assert.Contains(Current, await db.Database.GetAppliedMigrationsAsync());
  }
}
