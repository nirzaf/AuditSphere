using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

internal sealed record CurrentProcedureEvidence(bool Satisfied, string State, string Message,
  long? Revision = null, long? InputGeneration = null);

public static partial class AuditFieldworkService
{
  // Caller must authorize this exact engagement. The live risk/planning evaluator remains the source of currency.
  internal static async Task<CurrentProcedureEvidence> EvaluateProcedureEvidenceForAuthorizedScopeAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, Guid procedureId, CancellationToken ct)
  {
    var procedure = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.Id == procedureId, ct);
    if (procedure is null) return new(false, "MISSING", "The exact procedure is unavailable.");
    var result = await db.AuditProcedureResults.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.AuditProcedureId == procedureId)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (result is null) return new(false, "MISSING_RESULT", "Submit and independently review the current procedure result.");
    var storedGeneration = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == clientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    if (storedGeneration is null) return new(false, "MISSING_GENERATION", "The current planning generation is unavailable.", result.Revision);
    var generation = Math.Max(1, storedGeneration.Value);
    if (result.InputGeneration != generation || procedure.CurrentResultRevision != result.Revision)
      return new(false, "STALE_GENERATION", "Resubmit the procedure against the current planning generation.", result.Revision, generation);
    var risk = await ProcedureRiskBandEvaluator.EvaluateAsync(db, firmId, engagementId, procedure.RiskId, ct);
    if (risk.Blocker is not null || !ProcedureRiskBandEvaluator.CapturedBasisMatches(result.StructuredResultJson, risk.Value))
      return new(false, "STALE_PLANNING_BASIS", "Review the current planning and risk basis, then resubmit the procedure.", result.Revision, generation);
    var openNotes = await ReviewNotesService.OpenCountAsync(db, firmId, procedureId, ct) > 0;
    var review = await db.AuditProcedureReviews.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.AuditProcedureResultId == result.Id)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var satisfied = !openNotes && procedure.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable &&
      procedure.Status == AuditProcedureStatuses.Reviewed && result.Status == AuditProcedureResultStatuses.Reviewed &&
      review?.Decision == AuditProcedureReviewDecisions.Reviewed && review.ResultRevision == result.Revision &&
      review.ReviewerUserId != result.PreparedByUserId;
    return new(satisfied, openNotes ? "OPEN_REVIEW_NOTES" : satisfied ? "REVIEWED_CURRENT" : result.Status,
      openNotes ? "Resolve this procedure's open review notes and renew its independent review." :
      satisfied ? "The current procedure result has independent review against current inputs." :
      "Independently review the current submitted result in fieldwork.", result.Revision, generation);
  }
}
