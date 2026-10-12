using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record ManagerReviewBlocker(string Code, string Label, int Count);

/// <summary>Read-only manager handoff built from current audit evidence; it never writes an opinion or a review decision.</summary>
public sealed record ManagerReviewSummary(
  Guid EngagementId,
  bool CompletionReady,
  bool PartnerHandoffReady,
  int ApplicableProcedures,
  int ProceduresNeedingReview,
  int ItemTestsNeedingReview,
  int SelectionsNeedingReview,
  int UnresolvedReviewThreads,
  int OpenConfirmations,
  int BankReconciliationsNeedingApproval,
  int UnevaluatedDifferences,
  string SummaryReviewMemorandumState,
  int? SummaryReviewMemorandumVersion,
  DateTimeOffset? SummaryReviewMemorandumGeneratedAt,
  IReadOnlyList<ManagerReviewBlocker> Blockers);

public static class ManagerReviewSummaryQuery
{
  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<ManagerReviewSummary>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null)
      return CommandResult<ManagerReviewSummary>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, ReviewRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult<ManagerReviewSummary>.Fail(auth.ErrorCode!, auth.Message!);

    var evaluation = await AuditFieldworkService.EvaluateCompletionAsync(db, actor, engagementId, ct);
    if (!evaluation.Succeeded)
      return CommandResult<ManagerReviewSummary>.Fail(evaluation.ErrorCode!, evaluation.Message!);
    var rawBlockers = evaluation.Value!.Blockers;
    var blockers = rawBlockers.Select(Normalize).GroupBy(x => x.Code, StringComparer.Ordinal)
      .Select(group => new ManagerReviewBlocker(group.Key, group.First().Label, group.Count()))
      .OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();

    var latestSrm = await db.AuditDeliverables.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.EngagementId == engagementId && x.Kind == DeliverableKinds.SummaryReviewMemorandum && x.SignedFromDeliverableId == null)
      .OrderByDescending(x => x.Version).ThenByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
    var srmState = latestSrm is null ? "NOT_GENERATED" :
      await AuditDeliverableService.IsCurrentAsync(db, actor, latestSrm, ct) ? "CURRENT" : "STALE";

    var unreviewedItems = rawBlockers.Select(ParseUnreviewedItems).FirstOrDefault(x => x > 0);
    var unreviewedProcedures = Math.Max(0, evaluation.Value.ApplicableProcedureCount - evaluation.Value.ReviewedProcedureCount);
    var summary = new ManagerReviewSummary(
      engagementId,
      evaluation.Value.Ready,
      evaluation.Value.Ready && srmState == "CURRENT",
      evaluation.Value.ApplicableProcedureCount,
      unreviewedProcedures,
      unreviewedItems,
      rawBlockers.Count(x => x.StartsWith("selection:", StringComparison.Ordinal) && x.EndsWith(":unreviewed", StringComparison.Ordinal)),
      rawBlockers.Count(x => x.StartsWith("review-note:", StringComparison.Ordinal) && x.EndsWith(":unresolved", StringComparison.Ordinal)),
      rawBlockers.Count(x => x.StartsWith("confirmation:", StringComparison.Ordinal) && x.EndsWith(":open", StringComparison.Ordinal)),
      rawBlockers.Count(x => x.StartsWith("bank-reconciliation:", StringComparison.Ordinal) && x.EndsWith(":unapproved", StringComparison.Ordinal)),
      rawBlockers.Count(x => x.StartsWith("difference:", StringComparison.Ordinal) && x.EndsWith(":unevaluated", StringComparison.Ordinal)),
      srmState,
      latestSrm?.Version,
      latestSrm?.CreatedAt,
      blockers);
    return CommandResult<ManagerReviewSummary>.Ok(summary);
  }

  private static int ParseUnreviewedItems(string blocker)
  {
    var parts = blocker.Split(':');
    return parts.Length == 3 && parts[0] == "item-tests" && parts[2] == "unreviewed" &&
      int.TryParse(parts[1], out var count) && count > 0 ? count : 0;
  }

  private static (string Code, string Label) Normalize(string blocker)
  {
    if (blocker == "audit-program-not-adopted") return ("audit-program-not-adopted", "Audit program not adopted");
    if (blocker.StartsWith("applicability:", StringComparison.Ordinal)) return ("applicability-review-required", "Procedure applicability review required");
    if (blocker.StartsWith("procedure:", StringComparison.Ordinal))
    {
      if (blocker.EndsWith(":unreviewed", StringComparison.Ordinal)) return ("procedure-review-required", "Procedure review required");
      if (blocker.Contains(":risk-basis-stale", StringComparison.Ordinal)) return ("risk-basis-stale", "Procedure risk basis is stale");
      if (blocker.Contains(":risk-basis-blocked", StringComparison.Ordinal)) return ("risk-basis-blocked", "Procedure risk basis is blocked");
      if (blocker.Contains(":risk-basis-unavailable", StringComparison.Ordinal)) return ("risk-basis-unavailable", "Procedure risk basis is unavailable");
    }
    if (blocker.StartsWith("item-tests:", StringComparison.Ordinal)) return ("item-test-review-required", "Selected item tests need review");
    if (blocker.StartsWith("selection:", StringComparison.Ordinal)) return ("selection-review-required", "Selection review required");
    if (blocker.StartsWith("review-note:", StringComparison.Ordinal)) return ("review-thread-unresolved", "Review thread unresolved");
    if (blocker.StartsWith("confirmation:", StringComparison.Ordinal)) return ("confirmation-open", "Confirmation remains open");
    if (blocker.StartsWith("bank-reconciliation:", StringComparison.Ordinal)) return ("bank-reconciliation-unapproved", "Bank reconciliation needs approval");
    if (blocker.StartsWith("assessment:", StringComparison.Ordinal)) return ("area-assessment-review-required", "Area assessment needs review");
    if (blocker.StartsWith("difference:", StringComparison.Ordinal)) return ("difference-evaluation-required", "Difference evaluation required");
    if (blocker == "difference-aggregate:missing-stale-or-unreviewed") return ("difference-aggregate-not-current", "Difference aggregate needs current review");
    if (blocker == "materiality:stale") return ("materiality-stale", "Materiality is stale");
    if (blocker == "materiality:partner-approval-required") return ("materiality-partner-approval-required", "Current Partner materiality approval required");

    var parts = blocker.Split(':');
    if (parts.Length >= 3 && parts[0] == "risk") return ($"risk-{parts[^1]}", "Risk review prerequisite");
    if (blocker.StartsWith("standard-form:", StringComparison.Ordinal)) return ("standard-form-review-required", "Standard form review required");
    return ("completion-prerequisite", "Additional completion prerequisite");
  }
}
