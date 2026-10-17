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
    return await EvaluateCompletionForAuthorizedScopeAsync(db, actor.FirmId, auth.ClientId, engagementId, ct);
  }

  // Internal use only after a caller has authorized this exact scope. Never returns internal facts to a client.
  internal static async Task<CommandResult<AuditCompletionEvaluation>> EvaluateCompletionForAuthorizedScopeAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, CancellationToken ct = default)
  {
    var blockers = new List<string>();
    var procedures = await db.AuditProcedures.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId).ToListAsync(ct);
    if (procedures.Count == 0)
      blockers.Add("audit-program-not-adopted");
    var applicability = await AuditProgramService.EffectiveApplicabilityStatusesAsync(
      db, firmId, clientId, engagementId, procedures, ct);
    var applicable = procedures.Where(x => applicability[x.Id] == AuditApplicabilityStatuses.Applicable).ToArray();
    blockers.AddRange(procedures.Where(x => applicability[x.Id] is AuditApplicabilityStatuses.Pending or
        AuditApplicabilityStatuses.NotApplicablePendingReview or AuditApplicabilityStatuses.NotApplicableUnverified)
      .Select(x => $"applicability:{x.SourceProcedureId}"));
    var evidence = await EvaluateProcedureEvidenceBatchForAuthorizedScopeAsync(db, firmId, clientId, engagementId, applicable, ct);
    var reviewed = 0;
    foreach (var procedure in applicable)
    {
      if (!evidence.TryGetValue(procedure.Id, out var current))
      {
        blockers.Add($"procedure:{procedure.SourceProcedureId}:evidence-unavailable");
        continue;
      }
      if (current.Satisfied) reviewed++;
      else
      {
        var reason = current.State switch
        {
          "STALE_GENERATION" => "stale-generation",
          "STALE_RESULT" => "stale-result",
          "STALE_REVIEW" => "stale-review",
          "OPEN_REVIEW_NOTES" => "open-review-notes",
          "MISSING_GENERATION" => "risk-basis-unavailable",
          "STALE_PLANNING_BASIS" => "risk-basis-stale",
          _ => "unreviewed"
        };
        blockers.Add($"procedure:{procedure.SourceProcedureId}:{reason}");
      }
    }
    // Every unresolved review thread blocks completion, including notes on earlier result revisions.
    // Read those threads by bounded procedure batches rather than materializing the whole engagement history.
    var openNoteIds = await ReviewNotesService.OpenNoteIdsForScopeAsync(db, firmId, engagementId,
      procedures.Select(x => x.Id).ToArray(), ct);
    blockers.AddRange(openNoteIds.Select(id => $"review-note:{id}:unresolved"));
    blockers.AddRange(await db.AuditSelections.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Status != AuditSelectionStatuses.Reviewed)
      .Select(x => $"selection:{x.Id}:unreviewed").ToListAsync(ct));
    var reviewedSelections = await db.AuditSelections.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Status == AuditSelectionStatuses.Reviewed)
      .Select(x => x.Id).ToArrayAsync(ct);
    var selectedItemIds = await db.AuditSelectionItems.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && reviewedSelections.Contains(x.SelectionId))
      .Select(x => x.Id).ToArrayAsync(ct);
    var currentGeneration = await CurrentGenerationAsync(db, clientId, firmId, ct);
    var testedItemIds = await db.AuditItemTests.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && selectedItemIds.Contains(x.SelectionItemId) &&
      x.InputGeneration == currentGeneration &&
      !db.AuditItemTests.Any(newer => newer.FirmId == x.FirmId && newer.SelectionItemId == x.SelectionItemId && newer.Revision > x.Revision))
      .Where(x => db.AuditItemTestReviews.Any(review => review.FirmId == firmId && review.AuditItemTestId == x.Id &&
        review.TestRevision == x.Revision && review.Decision == AuditItemTestReviewDecisions.Reviewed))
      .Select(x => x.SelectionItemId).Distinct().ToArrayAsync(ct);
    var unreviewedItemCount = selectedItemIds.Except(testedItemIds).Count();
    if (unreviewedItemCount > 0)
      blockers.Add($"item-tests:{unreviewedItemCount}:unreviewed");
    blockers.AddRange(await db.AuditConfirmationCases.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Status != AuditConfirmationStatuses.Closed)
      .Select(x => $"confirmation:{x.Id}:open").ToListAsync(ct));
    blockers.AddRange(await db.AuditBankReconciliations.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.Status != AuditBankReconciliationStatuses.Approved)
      .Select(x => $"bank-reconciliation:{x.Id}:unapproved").ToListAsync(ct));
    blockers.AddRange(await db.AuditAreaAssessments.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Status != AuditAreaAssessmentStatuses.Reviewed)
      .Select(x => $"assessment:{x.Id}:unreviewed").ToListAsync(ct));
    blockers.AddRange(await db.AuditDifferences.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
      (x.Status == AuditDifferenceStatuses.Open || x.Status == AuditDifferenceStatuses.ManagementResponded))
      .Select(x => $"difference:{x.Id}:unevaluated").ToListAsync(ct));
    var hasDifferences = await db.AuditDifferences.AsNoTracking().AnyAsync(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId, ct);
    if (hasDifferences)
    {
      var snapshot = await CurrentDifferenceSnapshotAsync(db, firmId, clientId, engagementId, ct);
      var reviewedAggregate = snapshot is not null && await db.AuditAreaAssessments.AsNoTracking().AnyAsync(x =>
        x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
        x.AreaCode == AuditAreaCodes.AuditDifferences && x.AssessmentKind == AuditAreaAssessmentKinds.AggregateDifferences &&
        x.MethodologyReference == "audit-differences-aggregate.v1" && x.InputSnapshotJson == snapshot &&
        x.Status == AuditAreaAssessmentStatuses.Reviewed, ct);
      if (!reviewedAggregate)
        blockers.Add("difference-aggregate:missing-stale-or-unreviewed");
    }
    blockers.AddRange(await RiskBandService.BlockersAsync(db, firmId, engagementId, ct));
    blockers.AddRange(await StandardFormBlockersAsync(db, firmId, engagementId, ct));
    var latestMateriality = await db.MaterialityAssessments.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == clientId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var latestCalculation = await MaterialityEngineService.GetLatestAsync(db, firmId, engagementId, ct);
    if (latestCalculation is { State: MaterialityCalculationStates.Stale })
      blockers.Add("materiality:stale");
    else if (latestMateriality is null || !await MaterialityEngineService.IsPartnerApprovedCurrentAsync(db, latestMateriality, ct))
      blockers.Add("materiality:partner-approval-required");
    var evaluation = new AuditCompletionEvaluation(blockers.Count == 0, procedures.Count, applicable.Length, reviewed, blockers);
    return CommandResult<AuditCompletionEvaluation>.Ok(evaluation);
  }
}
