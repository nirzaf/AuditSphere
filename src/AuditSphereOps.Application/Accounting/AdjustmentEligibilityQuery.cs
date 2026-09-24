using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

// Read-only eligibility report for one adjustment plan (Module 22). The report
// mirrors the authoritative finalize rules without mutating state: it names the
// eligible/excluded/blocked membership with reason codes and hashes the exact
// contribution set so downstream modules can pin the plan they consume.
public sealed record AdjustmentEligibilityRow(
  string JournalNumber, long JournalRevision, string Layer,
  string TechnicalStatus, string ReflectionState,
  string Classification, string ReasonCode);

public sealed record AdjustmentEligibilityReport(
  Guid AdjustmentPlanId, Guid BaseDatasetId, string PlanStatus, string? ResultHash,
  string MembershipDigest,
  int EligibleCount, int ExcludedCount, int BlockedCount,
  IReadOnlyList<AdjustmentEligibilityRow> Journals);

public static class AdjustmentEligibilityQuery
{
  public const string Eligible = "ELIGIBLE";
  public const string Excluded = "EXCLUDED";
  public const string Blocked = "BLOCKED";

  public static async Task<CommandResult<AdjustmentEligibilityReport>> GetEligibilityAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid adjustmentPlanId,
    CancellationToken ct = default)
  {
    if (adjustmentPlanId == Guid.Empty)
      return CommandResult<AdjustmentEligibilityReport>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An adjustment plan id is required.");
    var plan = await db.AdjustmentPlans.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == adjustmentPlanId && x.FirmId == actor.FirmId, ct);
    if (plan is null)
      return CommandResult<AdjustmentEligibilityReport>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(plan.FirmId, plan.ClientId, plan.EngagementId,
        ["AccountingPreparer", "Staff", "Partner", "Manager"], InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<AdjustmentEligibilityReport>.Fail(auth.ErrorCode!, auth.Message!);

    var lines = await db.AdjustmentPlanLines.AsNoTracking()
      .Where(l => l.PlanId == plan.Id).ToListAsync(ct);
    var live = await db.JournalSourceReconciliations.AsNoTracking()
      .Where(r => r.FirmId == plan.FirmId && r.EngagementId == plan.EngagementId &&
        r.BaseDatasetId == plan.BaseDatasetId)
      .ToDictionaryAsync(r => (r.LogicalJournalNumber, r.JournalRevision), ct);
    var journals = await db.AdjustmentJournals.AsNoTracking()
      .Where(j => j.FirmId == plan.FirmId && j.EngagementId == plan.EngagementId)
      .ToDictionaryAsync(j => (j.JournalNumber, j.Revision), ct);

    var rows = new List<AdjustmentEligibilityRow>(lines.Count);
    foreach (var line in lines.OrderBy(l => l.LogicalJournalNumber, StringComparer.Ordinal)
      .ThenBy(l => l.JournalRevision))
    {
      var reflection = live.GetValueOrDefault((line.LogicalJournalNumber, line.JournalRevision))?.State
        ?? line.ReflectionState;
      var technical = journals.GetValueOrDefault((line.LogicalJournalNumber, line.JournalRevision))?.Status
        ?? "MISSING";
      string classification, reason;
      if (technical != "Posted")
      {
        classification = Blocked;
        reason = "journal.not-posted";
      }
      else if (reflection is ReflectionStates.Unknown or ReflectionStates.PartiallyReflected)
      {
        classification = Blocked;
        reason = "reflection.unresolved";
      }
      else if (reflection == ReflectionStates.Reflected)
      {
        classification = Excluded;
        reason = "reflection.already-in-source";
      }
      else if (reflection == ReflectionStates.NotApplicable)
      {
        classification = Excluded;
        reason = "reflection.not-applicable";
      }
      else
      {
        classification = Eligible;
        reason = string.Empty;
      }
      rows.Add(new AdjustmentEligibilityRow(
        line.LogicalJournalNumber, line.JournalRevision, line.Layer,
        technical, reflection, classification, reason));
    }

    // The membership digest pins the exact contribution set, not only the adjusted balances.
    var digest = Hashing.Sha256Hex(string.Join('\n', rows
      .Select(r => string.Join('|', r.JournalNumber, r.JournalRevision,
        r.ReflectionState, r.Classification))));

    return CommandResult<AdjustmentEligibilityReport>.Ok(new AdjustmentEligibilityReport(
      plan.Id, plan.BaseDatasetId, plan.Status, plan.ResultHash, digest,
      rows.Count(r => r.Classification == Eligible),
      rows.Count(r => r.Classification == Excluded),
      rows.Count(r => r.Classification == Blocked),
      rows));
  }
}
