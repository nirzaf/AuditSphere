using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSourceDatabaseRetainsProvenanceRowsAndHeaderWhileAllowingTechnicalValidation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var (periodId, _) = await CreateGlFixtureAsync(pg, scope, Actor(scope.Preparer, "AccountingPreparer"));
    var id = Guid.CreateVersion7();
    var identity = Hashing.Sha256Hex("synthetic database-guard source, not an accepted accounting source");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await using var tx = await db.Database.BeginTransactionAsync();
      var dataset = DatabaseGuardNativeDataset(scope, periodId, id, identity);
      db.TrialBalanceDatasets.Add(dataset);
      await db.SaveChangesAsync();
      db.TrialBalanceRows.Add(new TrialBalanceRow
      {
        Id = Guid.CreateVersion7(), DatasetId = id, AccountCode = "GUARD", AccountName = "Synthetic guard fixture",
        Currency = "QAR", Entity = "GUARD-ENTITY", Amount = 0m, SourceDebit = 0m, SourceCredit = 0m
      });
      db.NativeTrialBalanceSources.Add(DatabaseGuardNativeEvidence(scope, id, identity));
      await db.SaveChangesAsync();
      dataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync();
      await tx.CommitAsync();
    }

    // These bypass Application commands deliberately: retained history must survive direct SQL too.
    string[] forbidden =
    [
      "UPDATE native_trial_balance_sources SET manifest_json='{}' WHERE dataset_id=@id",
      "DELETE FROM native_trial_balance_sources WHERE dataset_id=@id",
      "UPDATE trial_balance_datasets SET legal_entity_key='OTHER' WHERE id=@id",
      "UPDATE trial_balance_datasets SET source_kind='Raw' WHERE id=@id",
      "UPDATE trial_balance_rows SET amount=1 WHERE dataset_id=@id",
      "DELETE FROM trial_balance_rows WHERE dataset_id=@id",
      "TRUNCATE native_trial_balance_sources",
      "TRUNCATE trial_balance_rows",
      "TRUNCATE trial_balance_datasets CASCADE"
    ];
    foreach (var sql in forbidden)
    {
      await using var connection = new NpgsqlConnection(pg.ConnectionString);
      await connection.OpenAsync();
      await using var tx = await connection.BeginTransactionAsync();
      await using var command = new NpgsqlCommand(sql, connection, tx);
      command.Parameters.AddWithValue("id", id);
      var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
      // Existing row append-only trigger sorts before the later sealed-row trigger.
      var existingRowGuard = sql.StartsWith("UPDATE trial_balance_rows", StringComparison.Ordinal) ||
        sql.StartsWith("DELETE FROM trial_balance_rows", StringComparison.Ordinal);
      Assert.True(error.SqlState == (existingRowGuard ? "P0001" : "55000"), $"{sql}: {error.SqlState} {error.MessageText}");
      if (existingRowGuard) Assert.Contains("trial_balance_rows is append-only", error.MessageText, StringComparison.Ordinal);
      await tx.RollbackAsync();
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(1, await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE trial_balance_datasets SET validation_status='Accepted', balanced=true, control_total=0 WHERE id={id}"));
      Assert.Equal(identity, (await db.TrialBalanceDatasets.SingleAsync(x => x.Id == id)).Sha256Hex);
      Assert.Equal("{}", (await db.NativeTrialBalanceSources.SingleAsync(x => x.DatasetId == id)).ManifestJson);
      Assert.Single(await db.TrialBalanceRows.Where(x => x.DatasetId == id).ToListAsync());
      var previous = db.Database.GetMigrations().TakeWhile(x => x != "20261024052643_NativeTrialBalanceSourceHandoff").Last();
      // Npgsql wraps migration-time object-state errors in its execution-strategy exception.
      var wrapper = await Assert.ThrowsAsync<InvalidOperationException>(() => db.GetService<IMigrator>().MigrateAsync(previous));
      var rollback = Assert.IsType<PostgresException>(wrapper.InnerException);
      Assert.Equal("55000", rollback.SqlState);
      Assert.Contains("Native source history must be retained", rollback.MessageText, StringComparison.Ordinal);
      Assert.Equal(identity, (await db.NativeTrialBalanceSources.AsNoTracking().SingleAsync(x => x.DatasetId == id)).SourceIdentityHash);
    }
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSourceDatabaseRejectsAnUnsealedOrOrphanedSourceAtCommit()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var (periodId, _) = await CreateGlFixtureAsync(pg, scope, Actor(scope.Preparer, "AccountingPreparer"));
    foreach (var state in new[] { TrialBalanceImportStates.Loading, TrialBalanceImportStates.Sealed })
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      await using var tx = await db.Database.BeginTransactionAsync();
      var dataset = DatabaseGuardNativeDataset(scope, periodId, Guid.CreateVersion7(), Hashing.Sha256Hex(state));
      dataset.ImportState = state;
      db.TrialBalanceDatasets.Add(dataset);
      await db.SaveChangesAsync();
      var error = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
      Assert.Equal("55000", error.SqlState);
    }
    await using (var invalid = new AuditSphereDbContext(pg.Options))
    {
      await using var tx = await invalid.Database.BeginTransactionAsync();
      var id = Guid.CreateVersion7();
      var identity = Hashing.Sha256Hex("invalid generated-byte hash fixture");
      invalid.TrialBalanceDatasets.Add(DatabaseGuardNativeDataset(scope, periodId, id, identity));
      await invalid.SaveChangesAsync();
      var evidence = DatabaseGuardNativeEvidence(scope, id, identity);
      evidence.GeneratedRowsHash = new string('0', 64);
      invalid.NativeTrialBalanceSources.Add(evidence);
      var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
      var databaseError = Assert.IsType<PostgresException>(rejected.InnerException);
      Assert.Equal("23514", databaseError.SqlState);
      Assert.Equal("ck_native_tb_source_values", databaseError.ConstraintName);
      await tx.RollbackAsync();
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.False(await verify.TrialBalanceDatasets.AnyAsync(x => x.SourceKind == "Native"));
    Assert.False(await verify.NativeTrialBalanceSources.AnyAsync());
    var migrations = verify.Database.GetMigrations().ToArray();
    var previous = migrations.TakeWhile(x => x != "20261024052643_NativeTrialBalanceSourceHandoff").Last();
    var periodsBefore = await verify.ClientReportingPeriods.CountAsync();
    await verify.GetService<IMigrator>().MigrateAsync(previous);
    Assert.Equal(previous, (await verify.Database.GetAppliedMigrationsAsync()).Last());
    await verify.GetService<IMigrator>().MigrateAsync(migrations.Last());
    Assert.Equal(periodsBefore, await verify.ClientReportingPeriods.CountAsync());
    Assert.False(await verify.NativeTrialBalanceSources.AnyAsync());
  }

  private static NativeTrialBalanceSource DatabaseGuardNativeEvidence(Scope scope, Guid id, string identity) => new()
  {
    DatasetId = id, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
    ActorId = scope.Preparer.Id, ActorEpoch = 1, RequestId = Guid.CreateVersion7(), RequestHash = identity,
    SourceIdentityHash = identity, ManifestJson = "{}", ManifestHash = Hashing.Sha256Hex("{}"),
    GeneratedRowsJson = "[{}]", GeneratedRowsHash = Hashing.Sha256Hex("[{}]"), RowCount = 1, CreatedAt = DateTimeOffset.UtcNow
  };

  private static TrialBalanceDataset DatabaseGuardNativeDataset(Scope scope, Guid periodId, Guid id, string identity) => new()
  {
    Id = id, FirmId = scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
    PeriodId = periodId, SourceKind = "Native", Revision = 1, Basis = "STATUTORY", Currency = "QAR",
    LegalEntityKey = "GUARD-ENTITY", ImportProfileVersion = "native-period-ledger.v1", SourceLayout = "DEBIT_CREDIT",
    NormalizedDatasetDigest = identity, Sha256Hex = identity, RawFileSha256Hex = "",
    ImportState = TrialBalanceImportStates.Loading, ImportedByUserId = scope.Preparer.Id, ImportedAt = DateTimeOffset.UtcNow
  };
}
