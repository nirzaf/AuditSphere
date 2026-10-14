using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// An accepted, sealed general ledger for a reporting period and book. Every journal balances (debit-positive ledger signs) and
/// every cash event carries its cash leg, the same shape the product imports. Each call adds a new batch and accepts it, so the
/// newest call becomes the current accepted ledger.
/// </summary>
internal static class CashFlowSourceSeed
{
  /// <summary>
  /// Named lines. Receivable is the credit side of a customer receipt (negative), Depreciation a debit with no cash leg,
  /// CashAccount a cash-to-cash transfer leg, Equipment a debit paid in cash, NoncashEquipment a debit bought on credit, and
  /// CompoundEquipment and CompoundFee the two debits of a compound payment whose single cash credit is in the same journal.
  /// </summary>
  internal sealed record Source(Guid BatchId, Guid Receivable, Guid Depreciation, Guid CashAccount, Guid Equipment,
    Guid NoncashEquipment, Guid CompoundEquipment, Guid CompoundFee);

  internal static async Task<Source> SeedAcceptedGeneralLedgerAsync(PgTestSchema pg, Guid firmId, Guid clientId, Guid engagementId,
    Guid periodId, Guid bookId, Guid preparerId, ActorContext reviewer, string tag) =>
    await SeedAcceptedGeneralLedgerAsync(pg.Options, firmId, clientId, engagementId, periodId, bookId, preparerId, reviewer, tag);

  internal static async Task<Source> SeedAcceptedGeneralLedgerAsync(DbContextOptions<AuditSphereDbContext> options,
    Guid firmId, Guid clientId, Guid engagementId, Guid periodId, Guid bookId, Guid preparerId, ActorContext reviewer,
    string tag, decimal amount = 100m, DateOnly? postingDate = null)
  {
    var batch = Guid.NewGuid();
    var batchHash = batch.ToString("N") + batch.ToString("N");
    var now = DateTimeOffset.UtcNow;
    var source = new Source(batch, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
      Guid.NewGuid(), Guid.NewGuid());
    // Journal J1 cash receipt, J2 noncash depreciation, J3 internal cash transfer, J4 cash equipment, J5 noncash equipment on
    // credit, J6 compound payment with a fee. Account 1000 is the package cash account; each journal balances to zero.
    var journals = Enumerable.Range(1, 6).ToDictionary(i => i, _ => Guid.NewGuid());
    var lines = new (int Journal, Guid Id, string Account, string Stable, decimal Amount)[]
    {
      (1, Guid.NewGuid(), "1000", "L1-cash", amount),
      (1, source.Receivable, "1100", "L1-receivable", -amount),
      (2, source.Depreciation, "1600", "L2-depreciation", 25m),
      (2, Guid.NewGuid(), "1650", "L2-accumulated-depreciation", -25m),
      (3, Guid.NewGuid(), "1010", "L3-cash-2", 5m),
      (3, source.CashAccount, "1000", "L3-cash", -5m),
      (4, source.Equipment, "1500", "L4-equipment", 25m),
      (4, Guid.NewGuid(), "1000", "L4-cash", -25m),
      (5, source.NoncashEquipment, "1500", "L5-equipment-on-credit", 500m),
      (5, Guid.NewGuid(), "2000", "L5-payables", -500m),
      (6, source.CompoundEquipment, "1500", "L6-equipment", 20m),
      (6, source.CompoundFee, "6000", "L6-fee", 5m),
      (6, Guid.NewGuid(), "1000", "L6-cash", -25m),
    };
    await using (var db = new AuditSphereDbContext(options))
    {
      db.SourceImportBatches.Add(new(){Id=batch,FirmId=firmId,ClientId=clientId,EngagementId=engagementId,PeriodId=periodId,BookId=bookId,SourceKind="GL",ProfileVersion="cash-flow-source-v1",ParserVersion="synthetic-parser-v1",RawFileSha256Hex=batchHash,NormalizedDatasetDigest=batchHash,LegalEntityKey="SYNTHETIC-CASH-FLOW",Currency="QAR",RowCount=lines.Length,Status="LOADING",ReceiptReference="Synthetic cash-flow source "+tag,CreatedByUserId=preparerId,CreatedAt=now});
      foreach (var (journal, id) in journals)
        db.GeneralLedgerTransactions.Add(new(){Id=id,FirmId=firmId,ClientId=clientId,EngagementId=engagementId,ImportBatchId=batch,StableJournalId="SYN-CF-J"+journal+"-"+tag,DocumentNumber="SYN-CF-DOC"+journal+"-"+tag,PostingDate=postingDate ?? new(2026,6,30),Currency="QAR",CreatedAt=now});
      foreach (var line in lines)
        db.GeneralLedgerLines.Add(new(){Id=line.Id,FirmId=firmId,ClientId=clientId,EngagementId=engagementId,ImportBatchId=batch,TransactionId=journals[line.Journal],StableLineId=line.Stable+"-"+tag,AccountCode=line.Account,Debit=line.Amount > 0m ? line.Amount : 0m,Credit=line.Amount < 0m ? -line.Amount : 0m,FunctionalAmount=line.Amount,OriginalCurrency="QAR",OriginalAmount=line.Amount,CreatedAt=now});
      await db.SaveChangesAsync();
    }
    await using (var db = new AuditSphereDbContext(options))
      await db.SourceImportBatches.Where(x => x.Id == batch).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SEALED"));
    await using (var db = new AuditSphereDbContext(options))
    {
      var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
        new(clientId, engagementId, AccountingSourceKinds.GeneralLedger, null, batch, "Synthetic cash-flow source review " + tag));
      if (!accepted.Succeeded) throw new InvalidOperationException(accepted.Message);
    }
    return source;
  }
}
