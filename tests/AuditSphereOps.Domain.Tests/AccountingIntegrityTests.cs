using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

// ND-03: persistence-level integrity. These controls live in the database, not only in
// application code, so they hold for any writer sharing the database.
public sealed class AccountingIntegrityTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task OrphanTrialBalanceRow_IsRejectedByForeignKey()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.TrialBalanceRows.Add(new TrialBalanceRow
    {
      Id = Guid.NewGuid(), DatasetId = Guid.NewGuid(),
      AccountCode = "001", Amount = 1m, Currency = "QAR"
    });
    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task Dataset_ReferencingUnknownEngagement_IsRejectedByScopeForeignKey()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, _) = await pg.SeedScopeAsync();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      // Engagement does not exist -> composite (firm, engagement) FK must fail.
      Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId,
      EngagementId = Guid.NewGuid(), Currency = "QAR", ImportedAt = DateTimeOffset.UtcNow
    });
    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task Dataset_ReferencingClientOfAnotherFirm_IsRejectedByScopeForeignKey()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, _) = await pg.SeedScopeAsync();
    // A second firm owns a valid client/engagement, but the dataset client belongs to the first firm.
    var otherEngagementId = Guid.NewGuid();
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      var otherFirmId = Guid.NewGuid();
      var otherClientId = Guid.NewGuid();
      seed.PracticeClients.Add(new AuditSphereOps.Domain.Practice.PracticeClient
      {
        Id = otherClientId, FirmId = otherFirmId,
        LegalName = "OTHER FIRM CLIENT " + otherClientId.ToString("N")[..8],
        CreatedAt = DateTimeOffset.UtcNow
      });
      seed.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = otherEngagementId, FirmId = otherFirmId,
        PracticeClientId = otherClientId, CreatedAt = DateTimeOffset.UtcNow
      });
      await seed.SaveChangesAsync();
    }
    await using var db = new AuditSphereDbContext(pg.Options);
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId,
      EngagementId = otherEngagementId, Currency = "QAR", ImportedAt = DateTimeOffset.UtcNow
    });
    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task TrialBalanceRows_AreAppendOnly_UpdateAndDeleteRejected()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, engagementId) = await pg.SeedScopeAsync();
    var id = Guid.NewGuid();
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = id, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        Currency = "QAR", ImportedAt = DateTimeOffset.UtcNow
      });
      seed.TrialBalanceRows.Add(new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = id, AccountCode = "001", Amount = 10m, Currency = "QAR"
      });
      await seed.SaveChangesAsync();
      await seed.Database.ExecuteSqlInterpolatedAsync($"UPDATE trial_balance_datasets SET import_state = {TrialBalanceImportStates.Sealed} WHERE id = {id}");
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var row = await db.TrialBalanceRows.SingleAsync(x => x.DatasetId == id);
      row.Amount = 99m;
      await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
      db.Entry(row).State = EntityState.Detached;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var row = await db.TrialBalanceRows.SingleAsync(x => x.DatasetId == id);
      db.TrialBalanceRows.Remove(row);
      await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // The original value is untouched by the failed attempts.
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(10m, (await verify.TrialBalanceRows.SingleAsync(x => x.DatasetId == id)).Amount);

    var rawInsert = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO trial_balance_rows (id, dataset_id, account_code, account_name, amount, currency, entity)
      VALUES ({Guid.NewGuid()}, {id}, '002', 'sealed insert', 1, 'QAR', 'DEFAULT')
      """));
    Assert.Equal("55000", rawInsert.SqlState);

    var reopen = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE trial_balance_datasets SET import_state = {TrialBalanceImportStates.Loading} WHERE id = {id}
      """));
    Assert.Equal("55000", reopen.SqlState);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AdjustmentLines_FreezeOnceJournalLeavesDraft()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, engagementId) = await pg.SeedScopeAsync();
    var datasetId = Guid.NewGuid();
    var journalId = Guid.NewGuid();
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        Currency = "QAR", ImportedAt = DateTimeOffset.UtcNow
      });
      seed.AdjustmentJournals.Add(new AdjustmentJournal
      {
        Id = journalId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        BaseDatasetId = datasetId, JournalNumber = "AJ-001", Status = "Draft",
        CreatedAt = DateTimeOffset.UtcNow
      });
      seed.AdjustmentLines.Add(new AdjustmentLine
      { Id = Guid.NewGuid(), JournalId = journalId, AccountCode = "520100", Debit = 5000m });
      await seed.SaveChangesAsync();
      await seed.Database.ExecuteSqlInterpolatedAsync($"UPDATE trial_balance_datasets SET import_state = {TrialBalanceImportStates.Sealed} WHERE id = {datasetId}");
    }

    // While Draft, editing lines is allowed.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var line = await db.AdjustmentLines.SingleAsync(x => x.JournalId == journalId);
      line.Credit = 5000m; // now balanced
      await db.SaveChangesAsync();
    }

    // Post the journal.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      (await db.AdjustmentJournals.SingleAsync(x => x.Id == journalId)).Status = "Posted";
      await db.SaveChangesAsync();
    }

    // Frozen: edit, add, and delete are all rejected once posted.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var line = await db.AdjustmentLines.SingleAsync(x => x.JournalId == journalId);
      line.Debit = 1m;
      await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
      db.Entry(line).State = EntityState.Detached;
      db.AdjustmentLines.Add(new AdjustmentLine
      { Id = Guid.NewGuid(), JournalId = journalId, AccountCode = "159100", Credit = 1m });
      await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
      db.Entry(line).State = EntityState.Detached;
      db.AdjustmentLines.Remove(line);
      await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var frozen = await verify.AdjustmentLines.SingleAsync(x => x.JournalId == journalId);
    Assert.Equal(5000m, frozen.Debit);
    Assert.Equal(5000m, frozen.Credit);
  }
}
