using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
namespace AuditSphereOps.Domain.Tests;
internal static class BudgetPreparationReviewSeed
{
  internal static async Task PopulateAsync(AuditSphereDbContext db,PbcSeed.Fixture f)
  {
    await EngagementCreationReviewSeed.PopulateAsync(db,f);
    db.RateCardVersions.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,Role="Senior",Activity="AUDIT",Currency="QAR",RatePerHour=100.125m,Status=PracticeTimeStates.RateApproved,CreatedByUserId=f.Admin.Id,ApprovedByUserId=f.Staff.Id,ApprovedAt=DateTimeOffset.UtcNow,CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();
  }
}
