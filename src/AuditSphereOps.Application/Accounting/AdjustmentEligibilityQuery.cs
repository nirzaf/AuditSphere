using System.Text.Json;
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
  string Classification, string ReasonCode,
  string PlannedReflectionState = ReflectionStates.Unknown, Guid? JournalId = null,
  string Evidence = "", Guid? ReviewedByUserId = null, DateTimeOffset? ReviewedAt = null);

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
  public const int MaximumMembership = 1000;
  internal static readonly string[] ReadRoles = ["Administrator", "AccountingPreparer", "AccountingReviewer", "Staff", "Partner", "Manager"];

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
        ReadRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult<AdjustmentEligibilityReport>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await db.TrialBalanceDatasets.AnyAsync(d => d.Id == plan.BaseDatasetId && d.FirmId == plan.FirmId &&
      d.ClientId == plan.ClientId && d.EngagementId == plan.EngagementId, ct))
      return CommandResult<AdjustmentEligibilityReport>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var lines = await db.AdjustmentPlanLines.AsNoTracking()
      .Where(l => l.PlanId == plan.Id).OrderBy(l => l.LogicalJournalNumber).ThenBy(l => l.JournalRevision)
      .ThenBy(l => l.Layer).Take(MaximumMembership + 1).ToListAsync(ct);
    if (lines.Count > MaximumMembership)
      return CommandResult<AdjustmentEligibilityReport>.Fail(ErrorCodes.GateBlocked,
        "This plan exceeds the bounded interactive review. Use an approved larger-plan review workflow.");
    var numbers = lines.Select(l => l.LogicalJournalNumber).Distinct().ToArray();
    var live = await db.JournalSourceReconciliations.AsNoTracking()
      .Where(r => r.FirmId == plan.FirmId && r.ClientId == plan.ClientId && r.EngagementId == plan.EngagementId &&
        r.BaseDatasetId == plan.BaseDatasetId && numbers.Contains(r.LogicalJournalNumber))
      .ToDictionaryAsync(r => (r.LogicalJournalNumber, r.JournalRevision), ct);
    var journalRows = await db.AdjustmentJournals.AsNoTracking()
      .Where(j => j.FirmId == plan.FirmId && j.ClientId == plan.ClientId && j.EngagementId == plan.EngagementId && numbers.Contains(j.JournalNumber))
      .OrderBy(j => j.Id).Take(MaximumMembership + 1).ToListAsync(ct);
    if (journalRows.Count > MaximumMembership)
      return CommandResult<AdjustmentEligibilityReport>.Fail(ErrorCodes.GateBlocked, "Journal membership exceeds the interactive review bound.");
    var journals = journalRows.GroupBy(j => (j.JournalNumber, j.Revision)).ToDictionary(g => g.Key, g => g.ToArray());

    var rows = new List<AdjustmentEligibilityRow>(lines.Count);
    foreach (var line in lines.OrderBy(l => l.LogicalJournalNumber, StringComparer.Ordinal)
      .ThenBy(l => l.JournalRevision).ThenBy(l => l.Layer, StringComparer.Ordinal))
    {
      var reconciliation = live.GetValueOrDefault((line.LogicalJournalNumber, line.JournalRevision));
      var reflection = reconciliation?.State ?? ReflectionStates.Unknown;
      var candidates = journals.GetValueOrDefault((line.LogicalJournalNumber, line.JournalRevision)) ?? [];
      var journal = candidates.Length == 1 ? candidates[0] : null;
      var technical = candidates.Length > 1 ? "AMBIGUOUS" : journal?.Status ?? "MISSING";
      string classification, reason;
      if (candidates.Length > 1)
      {
        classification = Blocked;
        reason = "journal.ambiguous-identity";
      }
      else if (journal?.Purpose == AdjustmentJournalPurposes.GroupOnlyElimination)
      {
        classification = Blocked;
        reason = "journal.group-only";
      }
      else if (technical != "Posted")
      {
        classification = Blocked;
        reason = "journal.not-posted";
      }
      else if (reflection is ReflectionStates.Unknown or ReflectionStates.PartiallyReflected)
      {
        classification = Blocked;
        reason = "reflection.unresolved";
      }
      else if (reflection != line.ReflectionState)
      {
        classification = Blocked;
        reason = "reflection.changed-since-plan";
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
      else if (reflection == ReflectionStates.NotReflected)
      {
        classification = Eligible;
        reason = string.Empty;
      }
      else
      {
        classification = Blocked;
        reason = "reflection.unsupported";
      }
      rows.Add(new AdjustmentEligibilityRow(
        line.LogicalJournalNumber, line.JournalRevision, line.Layer,
        technical, reflection, classification, reason, line.ReflectionState,
        journal?.Purpose == AdjustmentJournalPurposes.GroupOnlyElimination ? null : journal?.Id,
        reconciliation?.Evidence ?? "", reconciliation?.ReviewedByUserId, reconciliation?.ReviewedAt));
    }

    // The membership digest pins the exact contribution set, not only the adjusted balances.
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(rows.Select(r => new {
      r.JournalNumber, r.JournalRevision, r.Layer, r.TechnicalStatus, r.PlannedReflectionState,
      r.ReflectionState, r.Classification, r.ReasonCode
    })));

    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(plan.FirmId, plan.ClientId, plan.EngagementId, ReadRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult<AdjustmentEligibilityReport>.Fail(auth.ErrorCode!, auth.Message!);

    return CommandResult<AdjustmentEligibilityReport>.Ok(new AdjustmentEligibilityReport(
      plan.Id, plan.BaseDatasetId, plan.Status, plan.ResultHash, digest,
      rows.Count(r => r.Classification == Eligible),
      rows.Count(r => r.Classification == Excluded),
      rows.Count(r => r.Classification == Blocked),
      rows));
  }
}
