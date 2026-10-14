using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// An accepted, sealed general ledger for a reporting period and book, with two synthetic lines: a receivable of 100 and a
/// depreciation line of 25. Each call adds a new batch and accepts it, so the newest call becomes the current accepted ledger.
/// </summary>
internal static class CashFlowSourceSeed
{
  internal sealed record Source(Guid BatchId, Guid Receivable, Guid Depreciation, Guid CashAccount);

  internal static async Task<Source> SeedAcceptedGeneralLedgerAsync(PgTestSchema pg, Guid firmId, Guid clientId, Guid engagementId,
    Guid periodId, Guid bookId, Guid preparerId, ActorContext reviewer, string tag)
  {
    var batch = Guid.NewGuid();
    var journal = Guid.NewGuid();
    var receivable = Guid.NewGuid();
    var depreciation = Guid.NewGuid();
    var cashAccount = Guid.NewGuid();
    var batchHash = batch.ToString("N") + batch.ToString("N");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.SourceImportBatches.Add(new(){Id=batch,FirmId=firmId,ClientId=clientId,EngagementId=engagementId,PeriodId=periodId,BookId=bookId,SourceKind="GL",ProfileVersion="cash-flow-source-v1",ParserVersion="synthetic-parser-v1",RawFileSha256Hex=batchHash,NormalizedDatasetDigest=batchHash,LegalEntityKey="SYNTHETIC-CASH-FLOW",Currency="QAR",RowCount=2,Status="LOADING",ReceiptReference="Synthetic cash-flow source "+tag,CreatedByUserId=preparerId,CreatedAt=DateTimeOffset.UtcNow});
      db.GeneralLedgerTransactions.Add(new(){Id=journal,FirmId=firmId,ClientId=clientId,EngagementId=engagementId,ImportBatchId=batch,StableJournalId="SYN-CF-J-"+tag,DocumentNumber="SYN-CF-DOC-"+tag,PostingDate=new(2026,6,30),Currency="QAR",CreatedAt=DateTimeOffset.UtcNow});
      db.GeneralLedgerLines.Add(new(){Id=receivable,FirmId=firmId,ClientId=clientId,EngagementId=engagementId,ImportBatchId=batch,TransactionId=journal,StableLineId="SYN-CF-L1-"+tag,AccountCode="1100",Debit=100m,Credit=0m,FunctionalAmount=100m,OriginalCurrency="QAR",OriginalAmount=100m,CreatedAt=DateTimeOffset.UtcNow});
      db.GeneralLedgerLines.Add(new(){Id=cashAccount,FirmId=firmId,ClientId=clientId,EngagementId=engagementId,ImportBatchId=batch,TransactionId=journal,StableLineId="SYN-CF-L3-"+tag,AccountCode="1000",Debit=5m,Credit=0m,FunctionalAmount=5m,OriginalCurrency="QAR",OriginalAmount=5m,CreatedAt=DateTimeOffset.UtcNow});
      db.GeneralLedgerLines.Add(new(){Id=depreciation,FirmId=firmId,ClientId=clientId,EngagementId=engagementId,ImportBatchId=batch,TransactionId=journal,StableLineId="SYN-CF-L2-"+tag,AccountCode="1600",Debit=25m,Credit=0m,FunctionalAmount=25m,OriginalCurrency="QAR",OriginalAmount=25m,CreatedAt=DateTimeOffset.UtcNow});
      await db.SaveChangesAsync();
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.SourceImportBatches.Where(x => x.Id == batch).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SEALED"));
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
        new(clientId, engagementId, AccountingSourceKinds.GeneralLedger, null, batch, "Synthetic cash-flow source review " + tag));
      if (!accepted.Succeeded) throw new InvalidOperationException(accepted.Message);
    }
    return new(batch, receivable, depreciation, cashAccount);
  }
}
