using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Synthetic independent-approval draft; no professional or provider acceptance.</summary>
internal static class BudgetApprovalReviewSeed
{
  internal static async Task<Guid> PopulateAsync(AuditSphereDbContext db, PbcSeed.Fixture f)
  {
    await BudgetPreparationReviewSeed.PopulateAsync(db, f);
    var result = await PracticeTimeService.ReviseBudgetAsync(db, PbcSeed.Actor(f.Staff, "Manager"),
      new(f.EngagementId, "QAR", [new("Senior", "AUDIT", 480, "PLANNING", "Revenue")], 0));
    if (!result.Succeeded) throw new InvalidOperationException("Synthetic approval draft could not be prepared.");
    return result.Value;
  }
}
