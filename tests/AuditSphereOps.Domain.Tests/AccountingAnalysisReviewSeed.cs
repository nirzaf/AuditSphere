using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Owned synthetic read evidence. It establishes no live, client or professional acceptance.</summary>
internal static class AccountingAnalysisReviewSeed
{
  internal sealed record Result(PbcSeed.Fixture Fixture, IReadOnlyDictionary<string,Guid> Evidence, Guid SourceId,
    Guid HiddenId, Guid ZeroAnalyticalId, IReadOnlyList<Guid> ProcedureResultIds);
  internal static async Task<Result> SeedAsync(ITestPostgresDatabase pg, bool retainReviews = true, bool linkEvidence = true)
  {
    var s = await ReconciliationReviewSeed.SeedAsync(pg); var f = s.Fixture;
    await using var db = new AuditSphereDbContext(pg.Options);
    var now = DateTimeOffset.UtcNow;
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner", f.ClientId, f.EngagementId));
    var mappingId = Guid.CreateVersion7();
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, DatasetId = s.SourceId,
      Version = 1, Generation = 1, TaxonomyVersion = "synthetic-analysis-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      Status = AccountingPackageStates.MappingApproved, CreatedByUserId = f.Staff.Id, ApprovedByUserId = f.Reviewer.Id,
      ApprovedAt = now, CreatedAt = now
    });
    db.MappingAllocations.AddRange(
      new MappingAllocation { Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        MappingVersionId = mappingId, SourceAccountCode = "1000", DestinationCode = "CASH", StatementSection = "ASSETS",
        Fraction = 1m, Rationale = "Synthetic current mapping", CreatedAt = now },
      new MappingAllocation { Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        MappingVersionId = mappingId, SourceAccountCode = "3000", DestinationCode = "EQUITY", StatementSection = "EQUITY",
        Fraction = 1m, Rationale = "Synthetic current mapping", CreatedAt = now });
    await db.SaveChangesAsync();
    var materiality = await MaterialityEngineService.CalculateAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
      new(f.EngagementId, MaterialityBenchmarks.TotalAssets, null, 1m, 75m, 5m, "Synthetic approved planning basis."));
    if (!materiality.Succeeded) throw new InvalidOperationException(materiality.Message);
    var approval = await AuditPlanningService.ApproveMaterialityAssessmentAsync(db,
      PbcSeed.Actor(f.Reviewer, "Partner"), materiality.Value!.AssessmentId);
    if (!approval.Succeeded) throw new InvalidOperationException(approval.Message);
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
    var results = new List<Guid>(); var evidenceAt=DateTimeOffset.UtcNow;
    for(var n=0;n<27;n++)
    {
      var procedure=Guid.NewGuid();var code=$"SYN-ANALYSIS-{n:D3}";
      db.AuditProcedures.Add(new(){Id=procedure,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,SourceProcedureId=code,
        SourceSectionNumber=1,SourceSectionTitle="Synthetic accounting evidence",SourceWording="Inspect synthetic retained evidence.",ApplicabilityStatus="APPLICABLE",
        Title=code,Status="PLANNED",CreatedAt=evidenceAt});
      await db.SaveChangesAsync();
      var generation=await db.ClientSafetyStates.AsNoTracking().Where(x=>x.FirmId==f.FirmId && x.Id==f.ClientId).Select(x=>x.InputGeneration).SingleAsync();
      var submitted=await AuditProgramService.SubmitResultAsync(db,PbcSeed.Actor(f.Staff,"Staff"),
        new(procedure,generation,"Synthetic fixture","{}",[],"Synthetic human conclusion"));
      Assert.True(submitted.Succeeded,submitted.Message);var resultId=submitted.Value!.AuditProcedureResultId;
      var reviewed=await AuditProgramService.ReviewResultAsync(db,PbcSeed.Actor(f.Reviewer,"Reviewer"),
        new(resultId,"REVIEWED","Synthetic retained review"));
      Assert.True(reviewed.Succeeded,reviewed.Message);
      var result=await db.AuditProcedureResults.AsNoTracking().SingleAsync(x=>x.Id==resultId);
      var paper=await db.Workpapers.AsNoTracking().SingleAsync(x=>x.Id==submitted.Value.WorkpaperId);
      var submission=await db.WorkpaperSubmissions.AsNoTracking().SingleAsync(x=>x.WorkpaperId==paper.Id);
      var review=await db.AuditProcedureReviews.AsNoTracking().SingleAsync(x=>x.AuditProcedureResultId==resultId);
      Assert.Equal(f.Staff.Id,result.PreparedByUserId);Assert.Equal(f.Staff.Id,paper.ActorId);Assert.Equal(f.Staff.Id,submission.ActorId);
      Assert.Equal(f.Reviewer.Id,result.ReviewedByUserId);Assert.Equal(f.Reviewer.Id,review.ReviewerUserId);Assert.NotEqual(result.PreparedByUserId,result.ReviewedByUserId);
      Assert.Equal(1L,result.Revision);Assert.Equal(1L,paper.Revision);Assert.Equal(1L,submission.Revision);Assert.Equal(1L,review.ResultRevision);
      Assert.NotNull(result.ReviewedAt);Assert.True(result.ReviewedAt>=result.SubmittedAt);Assert.True(result.ReviewedAt>=submission.SubmittedAt);
      Assert.True(review.CreatedAt>=result.SubmittedAt);Assert.True(review.CreatedAt>=submission.SubmittedAt);
      results.Add(resultId);
    }
    foreach(var (kind,id) in ids)
    {
      foreach(var result in linkEvidence ? (kind=="SPECIALIST" ? results : results.Take(1)) : [])
      {
        var linked=await AccountingAnalysisService.LinkAccountingEvidenceToProcedureAsync(db,actor,new(kind,id,result));
        if(!linked.Succeeded)throw new InvalidOperationException(linked.Message);
      }
      if (!retainReviews) continue;
      var reviewed=await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db,reviewer,new(kind,id,kind=="JOURNAL_RISK" ? "CLEARED" : "APPROVED",
        Disposition:"Synthetic reviewed disposition",Conclusion:"Synthetic retained human conclusion.",CorroborationReference:"SYNTHETIC-CORROBORATION"));
      if(!reviewed.Succeeded)throw new InvalidOperationException(reviewed.Message);
    }
    var hiddenRec=await db.AccountingReconciliations.AsNoTracking().SingleAsync(x=>x.Id==s.SiblingId);var hidden=Guid.NewGuid();
    db.SpecialistAccountingSchedules.Add(new(){Id=hidden,FirmId=f.FirmId,ClientId=hiddenRec.ClientId,EngagementId=hiddenRec.EngagementId,PeriodId=hiddenRec.PeriodId,
      Area="ASSETS",MethodologyVersion="HIDDEN SYNTHETIC",EvidenceReference="HIDDEN SYNTHETIC",DepreciationMethod="STRAIGHT_LINE",UsefulLifeMonths=120,
      AssumptionsHash=new('a',64),CreatedByUserId=f.Staff.Id,CreatedAt=evidenceAt});
    await db.SaveChangesAsync();
    return new(f,ids,source.Id,hidden,zero.Value,results);
  }
}
