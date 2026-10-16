using System.Data;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

internal sealed record CurrentProcedureEvidence(bool Satisfied, string State, string Message,
  long? Revision = null, long? InputGeneration = null, Guid? ResultId = null, Guid? ReviewId = null, Guid? ReviewerUserId = null);

public static partial class AuditFieldworkService
{
  /// <summary>Evaluates current procedure evidence with bounded set reads shared across the workprogram.</summary>
  internal static async Task<IReadOnlyDictionary<Guid, CurrentProcedureEvidence>> EvaluateProcedureEvidenceBatchForAuthorizedScopeAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId,
    IReadOnlyCollection<AuditProcedure> procedures, CancellationToken ct)
  {
    var procedureIds = procedures.Where(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId)
      .Select(x => x.Id).Distinct().ToArray();
    if (procedureIds.Length == 0) return new Dictionary<Guid, CurrentProcedureEvidence>();
    await using var tx = db.Database.CurrentTransaction is null
      ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
      : null;
    var combined = new Dictionary<Guid, CurrentProcedureEvidence>(procedureIds.Length);
    foreach (var chunkIds in procedureIds.Chunk(1000))
    {
      var scoped = await db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId &&
        x.EngagementId == engagementId && chunkIds.Contains(x.Id)).ToListAsync(ct);
      // Chunked re-reads stay inside the repeatable-read snapshot, so every procedure is judged against the same current generation.
      foreach (var pair in await EvaluateProcedureEvidenceChunkForAuthorizedScopeAsync(db, firmId, clientId, engagementId, scoped, ct))
        combined[pair.Key] = pair.Value;
    }
    if (tx is not null) await tx.CommitAsync(ct);
    return combined;
  }

  private static async Task<IReadOnlyDictionary<Guid, CurrentProcedureEvidence>> EvaluateProcedureEvidenceChunkForAuthorizedScopeAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, IReadOnlyCollection<AuditProcedure> scoped,
    CancellationToken ct)
  {
    if (scoped.Count == 0) return new Dictionary<Guid, CurrentProcedureEvidence>();
    var ids = scoped.Select(x => x.Id).ToArray();
    var results = await db.AuditProcedureResults.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId &&
      x.EngagementId == engagementId && ids.Contains(x.AuditProcedureId)).OrderByDescending(x => x.Revision).ToListAsync(ct);
    var latest = results.GroupBy(x => x.AuditProcedureId).ToDictionary(x => x.Key, x => x.First());
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == clientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    var riskIds = scoped.Where(x => x.RiskId is not null && latest.ContainsKey(x.Id)).Select(x => x.RiskId!.Value).Distinct().ToArray();
    var riskEvaluations = await ProcedureRiskBandEvaluator.EvaluateManyAsync(db, firmId, engagementId, riskIds, ct);
    var hasUnlinked = scoped.Any(x => x.RiskId is null && latest.ContainsKey(x.Id));
    var unlinkedPlanningBlocker = hasUnlinked
      ? await ProcedureRiskBandEvaluator.ReadinessBlockerAsync(db, firmId, engagementId, ct)
      : null;
    var openProcedures = await ReviewNotesService.OpenProcedureIdsAsync(db, firmId, engagementId, ids, ct);
    var resultIds = latest.Values.Select(x => x.Id).ToArray();
    List<AuditProcedureReview> reviews = resultIds.Length == 0 ? [] : await db.AuditProcedureReviews.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && resultIds.Contains(x.AuditProcedureResultId))
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync(ct);
    var latestReviews = reviews.GroupBy(x => x.AuditProcedureResultId).ToDictionary(x => x.Key, x => x.First());
    var evaluated = new Dictionary<Guid, CurrentProcedureEvidence>(scoped.Length);
    foreach (var procedure in scoped)
    {
      if (!latest.TryGetValue(procedure.Id, out var result))
      {
        evaluated[procedure.Id] = new(false, "MISSING_RESULT", "Submit and independently review the current procedure result.");
        continue;
      }
      if (generation is null)
      {
        evaluated[procedure.Id] = new(false, "MISSING_GENERATION", "The current planning generation is unavailable.", result.Revision);
        continue;
      }
      var currentGeneration = Math.Max(1, generation.Value);
      if (result.InputGeneration != currentGeneration)
      {
        evaluated[procedure.Id] = new(false, "STALE_GENERATION", "Resubmit the procedure against the current planning generation.", result.Revision, currentGeneration);
        continue;
      }
      if (procedure.CurrentResultRevision != result.Revision)
      {
        evaluated[procedure.Id] = new(false, "STALE_RESULT", "The latest submitted result is not the procedure's current result revision.", result.Revision, currentGeneration, result.Id);
        continue;
      }
      ProcedureRiskBandEvaluation? risk = null;
      if (procedure.RiskId is { } riskId && !riskEvaluations.TryGetValue(riskId, out risk))
      {
        evaluated[procedure.Id] = new(false, "STALE_PLANNING_BASIS", "The linked risk is no longer available in this engagement.", result.Revision, currentGeneration);
        continue;
      }
      var blocker = procedure.RiskId is null ? unlinkedPlanningBlocker : risk?.Blocker;
      var currentBasis = risk?.Value;
      if (blocker is not null || !ProcedureRiskBandEvaluator.CapturedBasisMatches(result.StructuredResultJson, currentBasis))
      {
        evaluated[procedure.Id] = new(false, "STALE_PLANNING_BASIS", "Review the current planning and risk basis, then resubmit the procedure.", result.Revision, currentGeneration);
        continue;
      }
      var review = latestReviews.GetValueOrDefault(result.Id);
      var openNotes = openProcedures.Contains(procedure.Id);
      var satisfied = !openNotes && procedure.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable &&
        procedure.Status == AuditProcedureStatuses.Reviewed && result.Status == AuditProcedureResultStatuses.Reviewed &&
        review?.Decision == AuditProcedureReviewDecisions.Reviewed && review.ResultRevision == result.Revision &&
        review.ReviewerUserId != result.PreparedByUserId;
      var state = openNotes ? "OPEN_REVIEW_NOTES" : satisfied ? "REVIEWED_CURRENT" : review is null ? "MISSING_REVIEW" :
        review.ResultRevision != result.Revision || review.Decision != AuditProcedureReviewDecisions.Reviewed ? "STALE_REVIEW" :
        review.ReviewerUserId == result.PreparedByUserId ? "SELF_REVIEW" : "REVIEW_REQUIRED";
      evaluated[procedure.Id] = new(satisfied, state,
        openNotes ? "Resolve this procedure's open review notes and renew its independent review." :
        satisfied ? "The current procedure result has independent review against current inputs." :
        "Independently review the current submitted result in fieldwork.", result.Revision, currentGeneration, result.Id, review?.Id, review?.ReviewerUserId);
    }
    return evaluated;
  }

  // Caller must authorize this exact engagement. The live risk/planning evaluator remains the source of currency.
  internal static async Task<CurrentProcedureEvidence> EvaluateProcedureEvidenceForAuthorizedScopeAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, Guid procedureId, CancellationToken ct)
  {
    var procedure = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.Id == procedureId, ct);
    if (procedure is null) return new(false, "MISSING", "The exact procedure is unavailable.");
    var evaluated = await EvaluateProcedureEvidenceBatchForAuthorizedScopeAsync(db, firmId, clientId, engagementId, [procedure], ct);
    return evaluated.GetValueOrDefault(procedureId, new(false, "MISSING_RESULT", "Submit and independently review the current procedure result."));
  }
}
