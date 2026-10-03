using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

internal static class AdjustmentPlanReviewSeed
{
  internal sealed record Result(Guid PlanId, Guid SourceId, Guid EligibleId);
  internal static async Task<Result> SeedAsync(AuditSphereDbContext db, PbcSeed.Fixture f)
  {
    var first = await JournalReviewSeed.SeedAsync(db, f);
    var source = (await db.AdjustmentJournals.AsNoTracking().SingleAsync(j => j.Id == first)).BaseDatasetId;
    var preparer = PbcSeed.Actor(f.Staff, "AccountingPreparer");
    var reviewer = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
    var inputs = new List<PlanLineInput>();
    foreach (var (number,state) in new[] { ("AJ-SYN",ReflectionStates.NotReflected),
      ("AJ-IN-SOURCE",ReflectionStates.Reflected),("AJ-PARTIAL",ReflectionStates.PartiallyReflected),("AJ-UNKNOWN",ReflectionStates.Unknown) })
    {
      var id = first;
      if (number != "AJ-SYN")
      {
        var created = await AdjustmentJournalService.CreateDraftAsync(db, preparer, source, number,
          [("1000", 10.123456m, 0m), ("3000", 0m, 10.123456m)],
          reason: "Synthetic plan treatment", evidenceReference: "Synthetic retained evidence");
        if (!created.Succeeded) throw new InvalidOperationException(created.Message);
        id = created.Value;
      }
      var posted = await AdjustmentJournalService.PostAsync(db, reviewer, id);
      if (!posted.Succeeded) throw new InvalidOperationException(posted.Message);
      if (state != ReflectionStates.Unknown)
      {
        var resolved = await SourceReconciliationService.ResolveAsync(db, reviewer, source, number, 1, state, "Synthetic exact source bridge");
        if (!resolved.Succeeded) throw new InvalidOperationException(resolved.Message);
      }
      inputs.Add(new(number,1));
    }
    var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparer, source, inputs);
    if (!plan.Succeeded) throw new InvalidOperationException(plan.Message);
    return new(plan.Value,source,first);
  }
}
