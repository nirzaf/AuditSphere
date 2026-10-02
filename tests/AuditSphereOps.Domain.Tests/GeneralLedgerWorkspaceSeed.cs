using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Synthetic sealed source records for read-side tests, not worker/professional acceptance.</summary>
internal static class GeneralLedgerWorkspaceSeed
{
  internal static async Task<Guid> SeedAsync(AuditSphereDbContext db, PbcSeed.Fixture f,
    int journals = 75, bool oversized = false, bool seal = true, string kind = "GL")
  {
    var period = Guid.NewGuid(); var id = Guid.NewGuid(); var hash = id.ToString("N") + id.ToString("N");
    db.ClientReportingPeriods.Add(new() { Id = period, FirmId = f.FirmId, ClientId = f.ClientId,
      PeriodCode = period.ToString("N"), StartDate = new(2026,1,1), EndDate = new(2026,12,31),
      Currency = "QAR", Basis = "IFRS", Status = "ACTIVE", CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow });
    db.SourceImportBatches.Add(new() { Id = id, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      PeriodId = period, SourceKind = kind, ProfileVersion = "synthetic-gl-v1", ParserVersion = "synthetic-parser-v1",
      RawFileSha256Hex = hash, NormalizedDatasetDigest = hash, LegalEntityKey = "SYNTHETIC-GL", Currency = "QAR",
      Status = "LOADING", RowCount = oversized ? 1001 : journals * 2, ReceiptReference = "Synthetic read-side fixture",
      CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow });
    for (var n = 0; n < journals; n++)
    {
      var transaction = Guid.NewGuid();
      db.GeneralLedgerTransactions.Add(new() { Id = transaction, FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, ImportBatchId = id, StableJournalId = $"J-{n:D3}", DocumentNumber = $"DOC-{n:D3}",
        PostingDate = new(2026,n == 0 ? 6 : 9,30), Currency = "QAR", SourceSystem = "Synthetic test source",
        IsManual = n == 0, IsYearEnd = false, CreatedAt = DateTimeOffset.UtcNow });
      for (var i = 0; i < (oversized ? 1001 : 2); i++)
      {
        var debit = i % 2 == 0; var amount = debit ? 100.123456m : -100.123456m;
        db.GeneralLedgerLines.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
          EngagementId = f.EngagementId, ImportBatchId = id, TransactionId = transaction,
          StableLineId = $"J-{n:D3}-L{i:D4}", AccountCode = debit ? "1000" : "4000",
          Debit = debit ? amount : 0m, Credit = debit ? 0m : -amount, FunctionalAmount = amount,
          OriginalCurrency = "QAR", OriginalAmount = amount, IntercompanyCounterparty = n == 0 ? "SYN-PARTNER" : "",
          Branch = "SYN-BRANCH", CreatedAt = DateTimeOffset.UtcNow });
      }
    }
    await db.SaveChangesAsync();
    if (seal) await db.SourceImportBatches.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status,"SEALED"));
    return id;
  }
}
