using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

internal static class JournalReviewSeed
{
  internal static async Task<Guid> SeedAsync(AuditSphereDbContext db, PbcSeed.Fixture f)
  {
    var c = await GeneralLedgerUploadSeed.SeedAsync(db, f);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "AccountingReviewer", f.ClientId, f.EngagementId));
    var id = Guid.NewGuid();
    db.TrialBalanceDatasets.Add(new() {
      Id=id,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,
      PeriodId=c.PeriodId,BookId=c.BookId,Basis="IFRS",SourceKind="Raw",Revision=1,
      LegalEntityKey="SYN-JOURNAL",Currency="QAR",RawFileSha256Hex=new('c',64),
      NormalizedDatasetDigest=new('d',64),Sha256Hex=new('d',64),Balanced=true,
      ValidationStatus="Accepted",ImportState=TrialBalanceImportStates.Loading,ControlTotal=0m,
      ImportedAt=DateTimeOffset.UtcNow,ImportedByUserId=f.Staff.Id
    });
    foreach(var(code,amount) in new[]{("1000",100.123456m),("3000",-100.123456m)})
      db.TrialBalanceRows.Add(new(){Id=Guid.NewGuid(),DatasetId=id,AccountCode=code,AccountName="Synthetic "+code,Amount=amount,Currency="QAR",Entity="SYN-JOURNAL"});
    await db.SaveChangesAsync();
    await db.TrialBalanceDatasets.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ImportState,TrialBalanceImportStates.Sealed));
    var created=await AdjustmentJournalService.CreateDraftAsync(db,PbcSeed.Actor(f.Staff,"AccountingPreparer"),id,"AJ-SYN",
      [("1000",100.123456m,0m),("3000",0m,100.123456m)],reason:"Synthetic exact treatment",evidenceReference:"Synthetic retained source");
    if(!created.Succeeded)throw new InvalidOperationException(created.Message);
    return created.Value;
  }
}
