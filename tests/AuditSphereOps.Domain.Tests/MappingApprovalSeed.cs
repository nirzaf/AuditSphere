using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

internal static class MappingApprovalSeed
{
  internal sealed record Context(Guid MappingId, Guid DatasetId, Guid TaxonomyId, Guid PeriodId, Guid BookId);
  internal static async Task<Context> SeedAsync(AuditSphereDbContext db, PbcSeed.Fixture f, bool seal = true)
  {
    var context = await GeneralLedgerUploadSeed.SeedAsync(db, f);
    var taxonomy = Guid.NewGuid();
    var dataset = Guid.NewGuid();
    db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, f.Reviewer, "AccountingReviewer", f.ClientId, f.EngagementId),
      PbcSeed.Grant(f.FirmId, f.Staff, "AccountingReviewer", f.ClientId, f.EngagementId));
    db.ReportingTaxonomyVersions.Add(new() {
      Id=taxonomy,FirmId=f.FirmId,Code="synthetic-map-v1",Framework="IFRS",Name="Synthetic mapping review",
      Status="APPROVED",EffectiveFrom=new(2026,1,1),CreatedByUserId=f.Staff.Id,ApprovedByUserId=f.Reviewer.Id,
      ApprovedAt=DateTimeOffset.UtcNow,CreatedAt=DateTimeOffset.UtcNow
    });
    foreach(var (code,section) in new[]{("CASH","ASSETS"),("REVENUE","INCOME")})
      db.ReportingTaxonomyNodes.Add(new() {Id=Guid.NewGuid(),FirmId=f.FirmId,TaxonomyVersionId=taxonomy,Code=code,Name=code,
        StatementSection=section,DisplaySign="SIGNED",NormalBalance="DEBIT",IsPosting=true,Applicability="ALL",CreatedAt=DateTimeOffset.UtcNow});
    db.TrialBalanceDatasets.Add(new() {Id=dataset,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,
      PeriodId=context.PeriodId,BookId=context.BookId,SourceKind="Raw",Revision=1,LegalEntityKey="SYN-MAPPING",Currency="QAR",
      RawFileSha256Hex=new('a',64),NormalizedDatasetDigest=new('b',64),Sha256Hex=new('b',64),Balanced=true,
      ValidationStatus="Accepted",ImportState=TrialBalanceImportStates.Loading,ControlTotal=0m,ImportedAt=DateTimeOffset.UtcNow,ImportedByUserId=f.Staff.Id});
    foreach(var (code,amount) in new[]{("1000",100.123456m),("3000",-100.123456m)})
      db.TrialBalanceRows.Add(new() {Id=Guid.NewGuid(),DatasetId=dataset,AccountCode=code,AccountName="Synthetic "+code,Amount=amount,Currency="QAR",Entity="SYN-MAPPING"});
    await db.SaveChangesAsync();
    if (seal) await db.TrialBalanceDatasets.Where(x => x.Id == dataset).ExecuteUpdateAsync(s => s.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
    var created=await FinancialStatementService.CreateMappingVersionAsync(db,PbcSeed.Actor(f.Staff,"AccountingPreparer"),
      new(dataset,"synthetic-map-v1","2026-01-01","2026-12-31",[
        new("1000","CASH","ASSETS",1m,"Synthetic exact cash allocation"),
        new("3000","REVENUE","INCOME",1m,"Synthetic exact income allocation")],context.ChartId));
    if(!created.Succeeded) throw new InvalidOperationException(created.Message);
    return new(created.Value,dataset,taxonomy,context.PeriodId,context.BookId);
  }
}
