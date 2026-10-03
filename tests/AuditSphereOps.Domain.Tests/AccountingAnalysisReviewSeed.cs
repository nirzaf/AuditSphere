using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Owned synthetic read evidence. It establishes no live, client or professional acceptance.</summary>
internal static class AccountingAnalysisReviewSeed
{
  internal sealed record Result(PbcSeed.Fixture Fixture, IReadOnlyDictionary<string,Guid> Evidence, Guid SourceId,
    Guid HiddenId, Guid ZeroAnalyticalId, IReadOnlyList<Guid> ProcedureResultIds);
  internal static async Task<Result> SeedAsync(ITestPostgresDatabase pg)
  {
    var s = await ReconciliationReviewSeed.SeedAsync(pg); var f = s.Fixture;
    await using var db = new AuditSphereDbContext(pg.Options);
    var source = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x=>x.Id==s.SourceId);
    var actor = PbcSeed.Actor(f.Staff,"AccountingPreparer"); var reviewer = PbcSeed.Actor(f.Reviewer,"AccountingReviewer");
    var rec = await AccountingAnalysisService.CreateReconciliationAsync(db,actor,
      new(f.ClientId,f.EngagementId,source.PeriodId!.Value,source.BookId,"CASH",source.Id,null,["1000"],new(2026,12,31)));
    if (!rec.Succeeded) throw new InvalidOperationException(rec.Message);
    var proof = await AccountingAnalysisService.CalculateReconciliationProofAsync(db,actor,rec.Value);
    if (!proof.Succeeded) throw new InvalidOperationException(proof.Message);
    var ecl = await AccountingAnalysisService.CreateEclAssessmentAsync(db,actor,new(rec.Value,new(2026,12,31),"PROVISION_MATRIX_V1","SYNTHETIC-ECL-V1",.1m,.5m,2m,8m,new('a',64),BookedAmount:8m));
    var inventory = await AccountingAnalysisService.CreateInventoryValuationAsync(db,actor,new(rec.Value,new(2026,12,31),10m,12m,11m,1m,100m,"SYNTHETIC-INVENTORY-V1",new('b',64)));
    var specialist = await AccountingAnalysisService.RecordSpecialistScheduleAsync(db,actor,new(f.ClientId,f.EngagementId,source.PeriodId.Value,"ASSETS","SYNTHETIC-ASSETS-V1",
      100.123456m,20m,0m,10m,0m,0m,0m,0m,0m,0m,0m,110m,new('c',64),"SYNTHETIC-ASSET-EVIDENCE",DepreciationMethod:"STRAIGHT_LINE",UsefulLifeMonths:120));
    var analytical = await AccountingAnalysisService.CreateAnalyticalReviewAsync(db,actor,new(f.ClientId,f.EngagementId,source.PeriodId.Value,null,"REVENUE","annual-movement",
      120.123456m,100m,110m,"prior-year-total","SYNTHETIC-ANALYTICAL-V1","Synthetic signed contract movement."));
    var zero = await AccountingAnalysisService.CreateAnalyticalReviewAsync(db,actor,new(f.ClientId,f.EngagementId,source.PeriodId.Value,null,"REVENUE","no-prior",
      100.123456m,0m,null,"prior-year-total","SYNTHETIC-ANALYTICAL-V1","Prior data unavailable."));
    var batch = await GeneralLedgerWorkspaceSeed.SeedAsync(db,f,1);
    var transaction = await db.GeneralLedgerTransactions.AsNoTracking().SingleAsync(x=>x.ImportBatchId==batch);
    var risk = await AccountingAnalysisService.AddJournalRiskFlagAsync(db,actor,new(f.ClientId,f.EngagementId,batch,transaction.Id,"MANUAL_ENTRY",
      "Synthetic review indicator; no fraud conclusion.",70m,"SYNTHETIC-RISK-EVIDENCE",true,"Synthetic management explanation.","SYNTHETIC-CORROBORATION"));
    foreach(var result in new[]{ecl,inventory,specialist,analytical,zero,risk}) if(!result.Succeeded)throw new InvalidOperationException(result.Message);
    var ids = new Dictionary<string,Guid>{{"ECL",ecl.Value},{"INVENTORY",inventory.Value},{"SPECIALIST",specialist.Value},{"ANALYTICAL",analytical.Value},{"JOURNAL_RISK",risk.Value}};
    var results = new List<Guid>(); var now=DateTimeOffset.UtcNow;
    for(var n=0;n<27;n++)
    {
      var procedure=Guid.NewGuid();var paper=Guid.NewGuid();var result=Guid.NewGuid();var code=$"SYN-ANALYSIS-{n:D3}";
      db.AuditProcedures.Add(new(){Id=procedure,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,SourceProcedureId=code,
        SourceSectionNumber=1,SourceSectionTitle="Synthetic accounting evidence",SourceWording="Inspect synthetic retained evidence.",ApplicabilityStatus="APPLICABLE",
        CurrentResultRevision=1,Title=code,Status="REVIEWED",CreatedAt=now});
      db.Workpapers.Add(new(){Id=paper,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,ProcedureId=procedure,ActorId=f.Staff.Id,
        Index=code,Title=code,Objective="Synthetic inspection",TemplateVersion="synthetic-v1",Procedure="Inspect synthetic evidence",WorkPerformed="Synthetic fixture",
        Conclusion="Synthetic human conclusion",Revision=1,Status=WorkpaperStatuses.SubmittedSnapshot,SubmittedAt=now,CreatedAt=now});
      db.WorkpaperSubmissions.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,WorkpaperId=paper,
        ActorId=f.Staff.Id,Revision=1,WorkPerformed="Synthetic fixture",Conclusion="Synthetic human conclusion",SubmittedAt=now});
      db.AuditProcedureResults.Add(new(){Id=result,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,AuditProcedureId=procedure,WorkpaperId=paper,
        Revision=1,InputGeneration=1,WorkPerformed="Synthetic fixture",StructuredResultJson="{}",EvidenceReferencesJson="[]",Conclusion="Synthetic human conclusion",
        Status="REVIEWED",PreparedByUserId=f.Staff.Id,ReviewedByUserId=f.Reviewer.Id,ReviewComment="Synthetic retained review",SubmittedAt=now,ReviewedAt=now});
      db.AuditProcedureReviews.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,AuditProcedureResultId=result,
        AuditProcedureId=procedure,ResultRevision=1,Decision="REVIEWED",Comment="Synthetic retained review",ReviewerUserId=f.Reviewer.Id,CreatedAt=now});results.Add(result);
    }
    await db.SaveChangesAsync();
    foreach(var (kind,id) in ids)
    {
      foreach(var result in kind=="SPECIALIST" ? results : results.Take(1))
      {
        var linked=await AccountingAnalysisService.LinkAccountingEvidenceToProcedureAsync(db,actor,new(kind,id,result));
        if(!linked.Succeeded)throw new InvalidOperationException(linked.Message);
      }
      var reviewed=await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db,reviewer,new(kind,id,kind=="JOURNAL_RISK" ? "CLEARED" : "APPROVED",
        Disposition:"Synthetic reviewed disposition",Conclusion:"Synthetic retained human conclusion.",CorroborationReference:"SYNTHETIC-CORROBORATION"));
      if(!reviewed.Succeeded)throw new InvalidOperationException(reviewed.Message);
    }
    var hiddenRec=await db.AccountingReconciliations.AsNoTracking().SingleAsync(x=>x.Id==s.SiblingId);var hidden=Guid.NewGuid();
    db.SpecialistAccountingSchedules.Add(new(){Id=hidden,FirmId=f.FirmId,ClientId=hiddenRec.ClientId,EngagementId=hiddenRec.EngagementId,PeriodId=hiddenRec.PeriodId,
      Area="ASSETS",MethodologyVersion="HIDDEN SYNTHETIC",EvidenceReference="HIDDEN SYNTHETIC",DepreciationMethod="STRAIGHT_LINE",UsefulLifeMonths=120,
      AssumptionsHash=new('a',64),CreatedByUserId=f.Staff.Id,CreatedAt=now});
    await db.SaveChangesAsync();
    return new(f,ids,source.Id,hidden,zero.Value,results);
  }
}
