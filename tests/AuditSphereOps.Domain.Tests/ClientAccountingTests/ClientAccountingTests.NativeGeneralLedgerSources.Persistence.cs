using System.Text.Json;
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
  public async Task NativeGlDatabaseRetainsActualSourceFactsAndRefusesHistoryErasure()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (f, sourceId, batchId) = await CreateNativeGlPersistenceSourceAsync(pg);
    Guid transactionId, lineId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var batch = await db.SourceImportBatches.SingleAsync(x => x.Id == batchId);
      Assert.Equal(SourceImportOrigins.NativeLedger, batch.OriginKind);
      Assert.Equal("GL", batch.SourceKind);
      Assert.Equal("", batch.RawFileSha256Hex);
      Assert.Null(batch.BookId);
      Assert.Equal(0, batch.ExpectedChunkCount);
      Assert.Empty(await db.GeneralLedgerImportChunks.Where(x => x.ImportBatchId == batchId).ToListAsync());
      var evidence = await db.NativeGeneralLedgerSources.SingleAsync(x => x.ImportBatchId == batchId);
      Assert.Equal(sourceId, evidence.TrialBalanceDatasetId);
      Assert.Equal(batch.NormalizedDatasetDigest, evidence.SourceIdentityHash);
      Assert.Equal(Hashing.Sha256Hex(evidence.GeneratedRowsJson), evidence.GeneratedRowsHash);
      Assert.Equal(Hashing.Sha256Hex(evidence.ManifestJson), evidence.ManifestHash);
      var transaction = await db.GeneralLedgerTransactions.SingleAsync(x => x.ImportBatchId == batchId);
      transactionId = transaction.Id;
      Assert.Equal(f.JournalId.ToString("D"), transaction.StableJournalId);
      Assert.Null(transaction.IsManual);
      Assert.Null(transaction.IsYearEnd);
      var lines = await db.GeneralLedgerLines.Where(x => x.ImportBatchId == batchId).ToListAsync();
      lineId = lines.First().Id;
      Assert.Equal(2, lines.Count);
      Assert.All(lines, x => { Assert.Null(x.IsManual); Assert.Null(x.IsYearEnd); });
      foreach (var line in lines)
      {
        var originalId = Guid.Parse(line.StableLineId);
        var original = await db.ClientOperationalJournalLines.SingleAsync(x => x.Id == originalId);
        Assert.Equal(original.ClientAccountId, line.ClientAccountId);
        Assert.Equal(original.Debit, line.Debit);
        Assert.Equal(original.Credit, line.Credit);
        Assert.Equal(original.Debit - original.Credit, line.FunctionalAmount);
      }
    }
    string[] forbidden =
    [
      "INSERT INTO general_ledger_import_chunks(id,firm_id,client_id,engagement_id,import_batch_id,chunk_number,chunk_digest,transaction_count,line_count,created_at) SELECT gen_random_uuid(),firm_id,client_id,engagement_id,id,0,repeat('a',64),1,2,now() FROM source_import_batches WHERE id=@batch",
      "UPDATE native_general_ledger_sources SET manifest_json='{}' WHERE import_batch_id=@batch",
      "DELETE FROM native_general_ledger_sources WHERE import_batch_id=@batch",
      "UPDATE source_import_batches SET origin_kind='EXTERNAL' WHERE id=@batch",
      "UPDATE source_import_batches SET normalized_dataset_digest=repeat('0',64) WHERE id=@batch",
      "UPDATE general_ledger_transactions SET is_manual=false WHERE id=@transaction",
      "UPDATE general_ledger_lines SET debit=999 WHERE id=@line",
      "DELETE FROM general_ledger_lines WHERE id=@line",
      "TRUNCATE native_general_ledger_sources",
      "TRUNCATE general_ledger_lines CASCADE",
      "TRUNCATE general_ledger_transactions CASCADE",
      "TRUNCATE source_import_batches CASCADE"
    ];
    foreach (var sql in forbidden)
    {
      await using var connection = new NpgsqlConnection(pg.ConnectionString);
      await connection.OpenAsync();
      await using var tx = await connection.BeginTransactionAsync();
      await using var command = new NpgsqlCommand(sql, connection, tx);
      command.Parameters.AddWithValue("batch", batchId);
      command.Parameters.AddWithValue("transaction", transactionId);
      command.Parameters.AddWithValue("line", lineId);
      var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
      Assert.Equal("55000", error.SqlState);
      // Existing GL row triggers sort before the new capability trigger; retain their exact contract.
      string[] expectedMessages = sql.StartsWith("UPDATE native_general_ledger_sources", StringComparison.Ordinal) ||
          sql.StartsWith("DELETE FROM native_general_ledger_sources", StringComparison.Ordinal)
        ? ["Generated native GL provenance is immutable."]
        : sql.StartsWith("INSERT INTO general_ledger_import_chunks", StringComparison.Ordinal)
          ? ["Native generated GL sources do not accept upload chunks."]
        : sql.StartsWith("UPDATE source_import_batches", StringComparison.Ordinal)
          ? ["A sealed source cannot be relabelled or rebound as native GL."]
          : sql.StartsWith("UPDATE general_ledger_", StringComparison.Ordinal) || sql.StartsWith("DELETE FROM general_ledger_", StringComparison.Ordinal)
            ? ["Imported general-ledger source records are append-only."]
            : ["Retained native GL history cannot be truncated.", "Retained native trial-balance provenance cannot be truncated."];
      Assert.True(expectedMessages.Contains(error.MessageText, StringComparer.Ordinal), $"{sql}: {error.MessageText}");
      await tx.RollbackAsync();
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    var migrations = verify.Database.GetMigrations().ToArray();
    var nativeMigration = migrations.Single(x => x.EndsWith("_NativeGeneralLedgerSourceHandoff", StringComparison.Ordinal));
    var previous = migrations.TakeWhile(x => x != nativeMigration).Last();
    var wrapper = await Assert.ThrowsAsync<InvalidOperationException>(() => verify.GetService<IMigrator>().MigrateAsync(previous));
    var rollback = Assert.IsType<PostgresException>(wrapper.InnerException);
    Assert.Equal("55000", rollback.SqlState);
    Assert.Contains("Native GL history must be retained", rollback.MessageText, StringComparison.Ordinal);
    Assert.True(await verify.NativeGeneralLedgerSources.AnyAsync(x => x.ImportBatchId == batchId));
    Assert.Equal(2, await verify.GeneralLedgerLines.CountAsync(x => x.ImportBatchId == batchId));
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeGlDatabaseRefusesOrphanedOriginAndAllowsEmptyRollbackWithoutErasingNativeTb()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeBehaviourFixtureAsync(pg);
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer");
    Guid sourceId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var request = NativeBehaviourRequest(f);
      var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer, request);
      Assert.True(preview.Succeeded, preview.Message);
      var source = await NativeTrialBalanceSourceWorkspace.SealAsync(db, preparer,
        request with { ReviewBasis = preview.Value!.ReviewBasis, Reviewed = true });
      Assert.True(source.Succeeded, source.Message);
      sourceId = source.Value!.DatasetId;
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await using var tx = await db.Database.BeginTransactionAsync();
      db.SourceImportBatches.Add(new SourceImportBatch
      {
        Id = Guid.CreateVersion7(), FirmId = f.Scope.FirmId, ClientId = f.Scope.ClientA, EngagementId = f.Scope.EngagementA,
        PeriodId = f.PeriodId, SourceKind = "GL", OriginKind = SourceImportOrigins.NativeLedger,
        ProfileVersion = "native-posted-gl.v1", ParserVersion = "native-gl-export.v1", LegalEntityKey = "NATIVE-GUARD",
        Currency = "QAR", Status = "LOADING", CreatedByUserId = f.Scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var refused = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
      Assert.Equal("55000", refused.SqlState);
    }
    await using (var loading = new AuditSphereDbContext(pg.Options))
    {
      await using var tx = await loading.Database.BeginTransactionAsync();
      var batchId = Guid.CreateVersion7();
      loading.SourceImportBatches.Add(new SourceImportBatch
      {
        Id = batchId, FirmId = f.Scope.FirmId, ClientId = f.Scope.ClientA, EngagementId = f.Scope.EngagementA,
        PeriodId = f.PeriodId, SourceKind = "GL", OriginKind = SourceImportOrigins.NativeLedger,
        ProfileVersion = "native-posted-gl.v1", ParserVersion = "native-gl-export.v1", LegalEntityKey = "NATIVE-GUARD",
        Currency = "QAR", Status = "LOADING", CreatedByUserId = f.Scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await loading.SaveChangesAsync();
      loading.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
      {
        Id = Guid.CreateVersion7(), FirmId = f.Scope.FirmId, ClientId = f.Scope.ClientA, EngagementId = f.Scope.EngagementA,
        ImportBatchId = batchId, StableJournalId = f.JournalId.ToString("D"), DocumentNumber = "ORIGIN-GUARD",
        PostingDate = new DateOnly(2026, 1, 15), Currency = "QAR", SourceUser = f.Scope.Preparer.Id.ToString("D"),
        SourceSystem = "AUDITSPHERE_NATIVE", IsManual = null, IsYearEnd = null, CreatedAt = DateTimeOffset.UtcNow
      });
      await loading.SaveChangesAsync();
      var wrapper = await Assert.ThrowsAsync<InvalidOperationException>(() => loading.SourceImportBatches.Where(x => x.Id == batchId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.OriginKind, SourceImportOrigins.External)));
      var refused = Assert.IsType<PostgresException>(wrapper.InnerException);
      Assert.Equal("55000", refused.SqlState);
      Assert.Equal("Source origin cannot be reclassified.", refused.MessageText);
      await tx.RollbackAsync();
    }
    await using (var external = new AuditSphereDbContext(pg.Options))
    {
      await using var tx = await external.Database.BeginTransactionAsync();
      var batchId = Guid.CreateVersion7();
      external.SourceImportBatches.Add(new SourceImportBatch
      {
        Id = batchId, FirmId = f.Scope.FirmId, ClientId = f.Scope.ClientA, EngagementId = f.Scope.EngagementA,
        PeriodId = f.PeriodId, SourceKind = "GL", ProfileVersion = "EXTERNAL-GUARD", ParserVersion = "TEST",
        LegalEntityKey = "EXTERNAL-RDB-GUARD", Currency = "QAR", Status = "LOADING",
        ExpectedChunkCount = 1, ExpectedTransactionCount = 1, ExpectedLineCount = 2,
        RawFileSha256Hex = Hashing.Sha256Hex("Synthetic SQL metadata guard, not an imported document"),
        CreatedByUserId = f.Scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await external.SaveChangesAsync();
      external.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
      {
        Id = Guid.CreateVersion7(), FirmId = f.Scope.FirmId, ClientId = f.Scope.ClientA, EngagementId = f.Scope.EngagementA,
        ImportBatchId = batchId, StableJournalId = "EXTERNAL-NULL-FLAG", DocumentNumber = "SQL-GUARD",
        PostingDate = new DateOnly(2026, 1, 1), Currency = "QAR", SourceUser = "Synthetic fixture", SourceSystem = "TEST",
        IsManual = null, IsYearEnd = false, CreatedAt = DateTimeOffset.UtcNow
      });
      var failure = await Assert.ThrowsAsync<DbUpdateException>(() => external.SaveChangesAsync());
      var error = Assert.IsType<PostgresException>(failure.InnerException);
      Assert.Equal("23514", error.SqlState);
      Assert.Equal("External GL imports require their explicit source flags.", error.MessageText);
      await tx.RollbackAsync();
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.False(await verify.SourceImportBatches.AnyAsync(x => x.OriginKind == SourceImportOrigins.NativeLedger));
    Assert.False(await verify.NativeGeneralLedgerSources.AnyAsync());
    var migrations = verify.Database.GetMigrations().ToArray();
    var nativeMigration = migrations.Single(x => x.EndsWith("_NativeGeneralLedgerSourceHandoff", StringComparison.Ordinal));
    await verify.GetService<IMigrator>().MigrateAsync(migrations.TakeWhile(x => x != nativeMigration).Last());
    Assert.True(await verify.NativeTrialBalanceSources.AnyAsync(x => x.DatasetId == sourceId));
    Assert.Equal(1, await verify.ClientOperationalJournals.CountAsync(x => x.PeriodId == f.PeriodId && x.Status == "POSTED"));
    await verify.GetService<IMigrator>().MigrateAsync(migrations.Last());
    Assert.True(await verify.NativeTrialBalanceSources.AnyAsync(x => x.DatasetId == sourceId));
    Assert.False(await verify.NativeGeneralLedgerSources.AnyAsync());
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeGlDatabaseRefusesInitiallyMalformedSelfHashedArtifact()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (_, _, originalId) = await CreateNativeGlPersistenceSourceAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await using var tx = await db.Database.BeginTransactionAsync();
    // Reuse genuine relational source facts. Only the initially published artifact is corrupt.
    var batch = await db.SourceImportBatches.AsNoTracking().SingleAsync(x => x.Id == originalId);
    batch.Id = Guid.CreateVersion7(); batch.Status = "LOADING"; batch.NormalizedDatasetDigest = new string('c', 64);
    db.SourceImportBatches.Add(batch); await db.SaveChangesAsync();
    var transactions = await db.GeneralLedgerTransactions.AsNoTracking().Where(x => x.ImportBatchId == originalId).ToListAsync();
    var transactionIds = new Dictionary<Guid, Guid>();
    foreach (var journal in transactions)
    {
      var oldId = journal.Id; journal.Id = Guid.CreateVersion7(); transactionIds.Add(oldId, journal.Id);
      journal.ImportBatchId = batch.Id; db.GeneralLedgerTransactions.Add(journal);
    }
    var lines = await db.GeneralLedgerLines.AsNoTracking().Where(x => x.ImportBatchId == originalId).ToListAsync();
    foreach (var line in lines)
    {
      line.Id = Guid.CreateVersion7(); line.ImportBatchId = batch.Id; line.TransactionId = transactionIds[line.TransactionId];
      db.GeneralLedgerLines.Add(line);
    }
    var evidence = await db.NativeGeneralLedgerSources.AsNoTracking().SingleAsync(x => x.ImportBatchId == originalId);
    evidence.ImportBatchId = batch.Id; evidence.RequestId = Guid.CreateVersion7(); evidence.SourceIdentityHash = batch.NormalizedDatasetDigest;
    evidence.ManifestJson = "{}"; evidence.ManifestHash = Hashing.Sha256Hex(evidence.ManifestJson);
    evidence.GeneratedRowsJson = JsonSerializer.Serialize(Enumerable.Range(0, lines.Count).Select(_ => new { }));
    evidence.GeneratedRowsHash = Hashing.Sha256Hex(evidence.GeneratedRowsJson);
    db.NativeGeneralLedgerSources.Add(evidence); await db.SaveChangesAsync();
    await db.SourceImportBatches.Where(x => x.Id == batch.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SEALED"));
    var refused = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
    Assert.Equal("55000", refused.SqlState);
    Assert.Equal("Native GL manifest must retain its exact native TB and source facts.", refused.MessageText);
  }

  private static async Task<(NativeBehaviourFixture Fixture, Guid SourceId, Guid BatchId)> CreateNativeGlPersistenceSourceAsync(PgTestSchema pg)
  {
    var f = await CreateNativeBehaviourFixtureAsync(pg);
    var preparer = Actor(f.Scope.Preparer, "AccountingPreparer");
    NativeTrialBalanceSourceView source;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var request = NativeBehaviourRequest(f);
      var preview = await NativeTrialBalanceSourceWorkspace.PreviewAsync(db, preparer, request);
      Assert.True(preview.Succeeded, preview.Message);
      var result = await NativeTrialBalanceSourceWorkspace.SealAsync(db, preparer,
        request with { Reviewed = true, ReviewBasis = preview.Value!.ReviewBasis });
      Assert.True(result.Succeeded, result.Message);
      source = result.Value!;
    }
    await ValidateNativeBehaviourSourceAsync(pg, f.Scope.FirmId, source.DatasetId);
    await AcceptNativeBehaviourSourceAsync(pg, f, source.DatasetId);
    await using var capture = new AuditSphereDbContext(pg.Options);
    var intent = new NativeGeneralLedgerSourceRequest(Guid.CreateVersion7(), f.Scope.ClientA, f.Scope.EngagementA,
      source.DatasetId, source.SourceIdentityHash, true, "Synthetic actual native GL persistence fixture");
    var glPreview = await NativeGeneralLedgerSourceWorkspace.PreviewAsync(capture, preparer, intent);
    Assert.True(glPreview.Succeeded, glPreview.Message);
    var sealedGl = await NativeGeneralLedgerSourceWorkspace.SealAsync(capture, preparer,
      intent with { Reviewed = true, ReviewBasis = glPreview.Value!.ReviewBasis });
    Assert.True(sealedGl.Succeeded, sealedGl.Message);
    return (f, source.DatasetId, sealedGl.Value!.ImportBatchId);
  }
}
