using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditFieldworkService
{
  public static async Task<CommandResult<AuditCompletionEvaluation>> EvaluateCompletionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var auth = await AuthorizeEngagementAsync(db, actor, engagementId, ReviewRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<AuditCompletionEvaluation>.Fail(auth.ErrorCode!, auth.Message!);
    var blockers = new List<string>();
    var procedures = await db.AuditProcedures.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId).ToListAsync(ct);
    if (procedures.Count == 0)
      blockers.Add("audit-program-not-adopted");
    var applicable = procedures.Where(x => x.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable).ToArray();
    blockers.AddRange(procedures.Where(x => x.ApplicabilityStatus is AuditApplicabilityStatuses.Pending or AuditApplicabilityStatuses.NotApplicablePendingReview)
      .Select(x => $"applicability:{x.SourceProcedureId}"));
    var resultRows = await db.AuditProcedureResults.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId).ToListAsync(ct);
    var reviews = await db.AuditProcedureReviews.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId).ToListAsync(ct);
    var reviewed = 0;
    foreach (var procedure in applicable)
    {
      var result = resultRows.Where(x => x.AuditProcedureId == procedure.Id).OrderByDescending(x => x.Revision).FirstOrDefault();
      var review = result is null ? null : reviews.Where(x => x.AuditProcedureResultId == result.Id).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
      if (result is not null && procedure.Status == AuditProcedureStatuses.Reviewed && review?.Decision == AuditProcedureReviewDecisions.Reviewed)
        reviewed++;
      else
        blockers.Add($"procedure:{procedure.SourceProcedureId}:unreviewed");
    }
    blockers.AddRange(await db.AuditSelections.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId && x.Status != AuditSelectionStatuses.Reviewed)
      .Select(x => $"selection:{x.Id}:unreviewed").ToListAsync(ct));
    var reviewedSelections = await db.AuditSelections.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId && x.Status == AuditSelectionStatuses.Reviewed)
      .Select(x => x.Id).ToArrayAsync(ct);
    var selectedItemIds = await db.AuditSelectionItems.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId && reviewedSelections.Contains(x.SelectionId))
      .Select(x => x.Id).ToArrayAsync(ct);
    var currentGeneration = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct);
    var testedItemIds = await db.AuditItemTests.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId && selectedItemIds.Contains(x.SelectionItemId) &&
      x.InputGeneration == currentGeneration &&
      !db.AuditItemTests.Any(newer => newer.FirmId == x.FirmId && newer.SelectionItemId == x.SelectionItemId && newer.Revision > x.Revision))
      .Where(x => db.AuditItemTestReviews.Any(review => review.FirmId == actor.FirmId && review.AuditItemTestId == x.Id &&
        review.TestRevision == x.Revision && review.Decision == AuditItemTestReviewDecisions.Reviewed))
      .Select(x => x.SelectionItemId).Distinct().ToArrayAsync(ct);
    var unreviewedItemCount = selectedItemIds.Except(testedItemIds).Count();
    if (unreviewedItemCount > 0)
      blockers.Add($"item-tests:{unreviewedItemCount}:unreviewed");
    blockers.AddRange(await db.AuditConfirmationCases.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId && x.Status != AuditConfirmationStatuses.Closed)
      .Select(x => $"confirmation:{x.Id}:open").ToListAsync(ct));
    blockers.AddRange(await db.AuditBankReconciliations.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == auth.ClientId && x.EngagementId == engagementId && x.Status != AuditBankReconciliationStatuses.Approved)
      .Select(x => $"bank-reconciliation:{x.Id}:unapproved").ToListAsync(ct));
    blockers.AddRange(await db.AuditAreaAssessments.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId && x.Status != AuditAreaAssessmentStatuses.Reviewed)
      .Select(x => $"assessment:{x.Id}:unreviewed").ToListAsync(ct));
    blockers.AddRange(await db.AuditDifferences.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId &&
      (x.Status == AuditDifferenceStatuses.Open || x.Status == AuditDifferenceStatuses.ManagementResponded))
      .Select(x => $"difference:{x.Id}:unevaluated").ToListAsync(ct));
    var hasDifferences = await db.AuditDifferences.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == auth.ClientId && x.EngagementId == engagementId, ct);
    if (hasDifferences)
    {
      var snapshot = await CurrentDifferenceSnapshotAsync(db, actor.FirmId, auth.ClientId, engagementId, ct);
      var reviewedAggregate = snapshot is not null && await db.AuditAreaAssessments.AsNoTracking().AnyAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId &&
        x.AreaCode == AuditAreaCodes.AuditDifferences && x.AssessmentKind == AuditAreaAssessmentKinds.AggregateDifferences &&
        x.MethodologyReference == "audit-differences-aggregate.v1" && x.InputSnapshotJson == snapshot &&
        x.Status == AuditAreaAssessmentStatuses.Reviewed, ct);
      if (!reviewedAggregate)
        blockers.Add("difference-aggregate:missing-stale-or-unreviewed");
    }
    var evaluation = new AuditCompletionEvaluation(blockers.Count == 0, procedures.Count, applicable.Length, reviewed, blockers);
    return CommandResult<AuditCompletionEvaluation>.Ok(evaluation);
  }
}
