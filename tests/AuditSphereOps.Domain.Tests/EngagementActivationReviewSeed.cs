using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Domain.Tests;
internal static class EngagementActivationReviewSeed
{
  internal static async Task<Guid> PopulateAsync(AuditSphereDbContext db,PbcSeed.Fixture f)
  {
    const long generation=9007199254740993;
    await db.Engagements.Where(e=>e.Id==f.EngagementId).ExecuteUpdateAsync(s=>s.SetProperty(e=>e.Status,"Draft")
      .SetProperty(e=>e.ProfessionalWorkBlocked,true).SetProperty(e=>e.Generation,generation).SetProperty(e=>e.ServiceRoute,"FinancialStatementAudit")
      .SetProperty(e=>e.ServiceProfileId,"Synthetic annual audit").SetProperty(e=>e.PeriodStart,"2026-01-01").SetProperty(e=>e.PeriodEnd,"2026-12-31"));
    await db.ClientSafetyStates.Where(c=>c.Id==f.ClientId).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.InputGeneration,generation));
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"Partner",f.ClientId,f.EngagementId));
    var id=Guid.NewGuid();db.AcceptanceDecisions.Add(new(){Id=id,FirmId=f.FirmId,PracticeClientId=f.ClientId,ServiceRoute="FinancialStatementAudit",
      Decision="Accepted",Path=AcceptancePaths.NewClient,Generation=generation,Rationale="Synthetic owner-approved test evidence",
      EvaluationTemplateVersion="synthetic.v1",EvaluationSnapshotDigest=new string('a',64),DecidedByUserId=f.Staff.Id,DecidedAt=DateTimeOffset.Parse("2026-10-01T12:00:00Z")});
    await db.SaveChangesAsync();return id;
  }
}
