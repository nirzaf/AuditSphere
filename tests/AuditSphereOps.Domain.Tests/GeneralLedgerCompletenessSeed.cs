using WorkerHost = AuditSphereOps.Worker.Worker;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Synthetic immutable source pairs. No external or professional acceptance is inferred.</summary>
internal static class GeneralLedgerCompletenessSeed
{
  internal sealed record Pair(Guid BatchId, Guid ClosingId, Guid OpeningId, Guid PeriodId, Guid BookId);
  internal static async Task<Pair> SeedAsync(AuditSphereDbContext db, PbcSeed.Fixture f, bool missingOpeningAccount=false, int accounts=2)
  {
    var prior=Guid.NewGuid();var period=Guid.NewGuid();var oldBook=Guid.NewGuid();var book=Guid.NewGuid();var closing=Guid.NewGuid();var opening=Guid.NewGuid();var batch=Guid.NewGuid();var journal=Guid.NewGuid();
    db.ClientReportingPeriods.AddRange(
      new(){Id=prior,FirmId=f.FirmId,ClientId=f.ClientId,PeriodCode=prior.ToString("N"),StartDate=new(2025,1,1),EndDate=new(2025,12,31),Currency="QAR",Basis="IFRS",Status="ACTIVE",CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow},
      new(){Id=period,FirmId=f.FirmId,ClientId=f.ClientId,PeriodCode=period.ToString("N"),StartDate=new(2026,1,1),EndDate=new(2026,12,31),Currency="QAR",Basis="IFRS",Status="ACTIVE",PriorPeriodId=prior,CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow});
    foreach(var pair in new[]{(oldBook,prior),(book,period)}) db.ClientReportingBooks.Add(new(){Id=pair.Item1,FirmId=f.FirmId,ClientId=f.ClientId,PeriodId=pair.Item2,Code="STAT",InclusionRule="STATUTORY_ONLY",Basis="IFRS",Currency="QAR",Status="ACTIVE",CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow});
    foreach(var pair in new[]{(opening,prior,oldBook),(closing,period,book)}){
      var hash=pair.Item1.ToString("N")+pair.Item1.ToString("N");
      db.TrialBalanceDatasets.Add(new(){Id=pair.Item1,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,PeriodId=pair.Item2,BookId=pair.Item3,Basis="IFRS",Currency="QAR",LegalEntityKey="SYNTHETIC-COMPLETENESS",SourceKind="Raw",RawFileSha256Hex=hash,NormalizedDatasetDigest=hash,Sha256Hex=hash,Balanced=true,ValidationStatus="Pending",ImportState="LOADING",ImportedByUserId=f.Staff.Id,ImportedAt=DateTimeOffset.UtcNow});
      for(var i=0;i<accounts;i++){
        if(pair.Item1==opening && missingOpeningAccount && i>=accounts-2)continue;
        var amount=pair.Item1==opening?0m:(i%2==0?100.123456m:-100.123456m);
        db.TrialBalanceRows.Add(new(){Id=Guid.NewGuid(),DatasetId=pair.Item1,AccountCode=$"{1000+i:D4}",AccountName=$"Synthetic account {i}",Amount=amount,Currency="QAR",Entity="SYNTHETIC-COMPLETENESS"});
      }
    }
    var batchHash=batch.ToString("N")+batch.ToString("N");
    db.SourceImportBatches.Add(new(){Id=batch,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,PeriodId=period,BookId=book,SourceKind="GL",ProfileVersion="synthetic-completeness-v1",ParserVersion="synthetic-parser-v1",RawFileSha256Hex=batchHash,NormalizedDatasetDigest=batchHash,LegalEntityKey="SYNTHETIC-COMPLETENESS",Currency="QAR",RowCount=accounts,Status="LOADING",ReceiptReference="Synthetic completeness source",CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow});
    db.GeneralLedgerTransactions.Add(new(){Id=journal,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,ImportBatchId=batch,StableJournalId="SYN-J-1",DocumentNumber="SYN-DOC",PostingDate=new(2026,6,30),Currency="QAR",CreatedAt=DateTimeOffset.UtcNow});
    for(var i=0;i<accounts;i++){var amount=i%2==0?100.123456m:-100.123456m;db.GeneralLedgerLines.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,ImportBatchId=batch,TransactionId=journal,StableLineId=$"SYN-L-{i}",AccountCode=$"{1000+i:D4}",Debit=amount>0?amount:0,Credit=amount<0?-amount:0,FunctionalAmount=amount,OriginalCurrency="QAR",OriginalAmount=amount,CreatedAt=DateTimeOffset.UtcNow});}
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"AccountingPreparer",f.ClientId,f.EngagementId));
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"AccountingReviewer",f.ClientId,f.EngagementId));
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Reviewer,"AccountingReviewer",f.ClientId,f.EngagementId));
    await db.SaveChangesAsync();
    await db.TrialBalanceDatasets.Where(x=>x.Id==closing||x.Id==opening).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ValidationStatus,"Accepted").SetProperty(x=>x.ImportState,"SEALED"));
    await db.SourceImportBatches.Where(x=>x.Id==batch).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"SEALED"));
    return new(batch,closing,opening,period,book);
  }
  internal static async Task<bool> RunWorkerAsync(DbContextOptions<AuditSphereDbContext> options, Guid firmId)
  {
    var store=new PostgresOperationStore(new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(options)));
    var workerOptions=new WorkerOptions(firmId,"Test");
    var worker=new WorkerHost(new OperationDispatcher(store,new DurableOperationRegistry([new GeneralLedgerCompletenessHandler()],workerOptions),workerOptions),Array.Empty<IPendingOperationDiscovery>(),NullLogger<WorkerHost>.Instance);
    return await worker.ProcessNextAsync();
  }
}
