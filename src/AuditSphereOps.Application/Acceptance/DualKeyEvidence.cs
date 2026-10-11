using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

/// <summary>
/// Read-side evidence for the two Dual-Key approval keys (WF-01 and WF-03). These predicates mirror the checks the protected
/// engagement-letter and fee-agreement guards apply to the same records, so a read never reports a key as cleared when the
/// protected command would refuse it. The protected commands keep their own checks, error codes and locks; this class only
/// reads. Callers that need the exact letter-identity or activation semantics must still go through those commands.
/// </summary>
internal static class DualKeyEvidence
{
  /// <summary>The newest non-pending client-level decision for the service route, before any current-generation check.</summary>
  public static Task<AcceptanceDecision?> LatestRiskDecisionAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, string serviceRoute, CancellationToken ct) =>
    db.AcceptanceDecisions.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.PracticeClientId == clientId && x.EngagementId == null &&
        x.ServiceRoute == serviceRoute && x.Decision != "Pending")
      .OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).FirstOrDefaultAsync(ct);

  /// <summary>
  /// The proposal an engagement is bound to. The fee agreement linked to the engagement is authoritative, because activation reads
  /// that link; without one, the client's newest proposal is used, as the lifecycle always did.
  /// </summary>
  public static async Task<Proposal?> ResolveEngagementProposalAsync(
    IAuditSphereDbContext db, Guid firmId, Guid engagementId, Guid clientId, CancellationToken ct)
  {
    var linkedProposalId = await db.EngagementFeeAgreements.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.EngagementId == engagementId)
      .Select(x => (Guid?)x.ProposalId).FirstOrDefaultAsync(ct);
    if (linkedProposalId is { } proposalId)
      return await db.Proposals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == proposalId, ct);
    return await db.Proposals.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.PracticeClientId == clientId)
      .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
  }

  /// <summary>The client's current input generation; null when its safety state is missing, which fails closed.</summary>
  public static async Task<long?> InputGenerationAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct) =>
    (await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == clientId, ct))?.InputGeneration;

  /// <summary>
  /// The Partner risk key: the newest non-pending client-level decision for the service route. It counts only when it is an
  /// unconditional Accepted decision with a recorded decider and time, and its generation matches the client's current input
  /// generation. A missing safety state fails closed.
  /// </summary>
  public static async Task<AcceptanceDecision?> CurrentUnconditionalRiskClearanceAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, string serviceRoute, CancellationToken ct)
  {
    var latest = await LatestRiskDecisionAsync(db, firmId, clientId, serviceRoute, ct);
    if (latest is not { Decision: "Accepted", DecidedByUserId: not null, DecidedAt: not null }) return null;
    return await InputGenerationAsync(db, firmId, clientId, ct) == latest.Generation ? latest : null;
  }

  /// <summary>
  /// The client commercial key: an accepted proposal whose response cites the exact offer hash it was sent with. When a dispatch
  /// record exists it must also bind the newest approved quotation revision and that same offer hash. The sent-versus-response
  /// hash check is stricter than the protected engagement-letter guard, which applies identity only when a dispatch record
  /// exists; a response that answers a different offer never counts as consent here.
  /// </summary>
  public static async Task<bool> CommercialAcceptanceStandsAsync(
    IAuditSphereDbContext db, Guid firmId, Proposal proposal, CancellationToken ct)
  {
    if (proposal.Status != CrmStates.ProposalAccepted || proposal.ResponseAt is null || proposal.PracticeClientId is null) return false;
    if (string.IsNullOrWhiteSpace(proposal.SentOfferSha256) || string.IsNullOrWhiteSpace(proposal.ResponseOfferSha256) ||
      !string.Equals(proposal.SentOfferSha256, proposal.ResponseOfferSha256, StringComparison.OrdinalIgnoreCase))
      return false;
    // The protected guard refuses unless the newest quotation revision is approved and matches the proposal fee, before any identity check.
    var newestQuotation = await db.QuotationVersions.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.ProposalId == proposal.Id)
      .OrderByDescending(x => x.Revision).Select(x => new { x.Id, x.Status, x.Fee }).FirstOrDefaultAsync(ct);
    if (newestQuotation is null || newestQuotation.Status != QuotationStates.Approved || newestQuotation.Fee != proposal.Fee) return false;
    var dispatch = await db.CommercialNotifications.AsNoTracking().SingleOrDefaultAsync(
      x => x.FirmId == firmId && x.ProposalId == proposal.Id && x.Kind == CommercialNotificationKinds.Proposal, ct);
    if (dispatch is null) return true;
    return dispatch.QuotationVersionId == newestQuotation.Id &&
      string.Equals(dispatch.OfferSha256, proposal.ResponseOfferSha256, StringComparison.OrdinalIgnoreCase);
  }
}
