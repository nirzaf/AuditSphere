using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>Validates acceptance against the immutable receipt for the exact rendered offer.</summary>
public static class ProposalResponseEvidence
{
  public static async Task<bool> IsCurrentAcceptedAsync(IAuditSphereDbContext db, Guid firmId, Guid proposalId,
    CancellationToken ct = default)
  {
    var proposal = await db.Proposals.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == proposalId, ct);
    return proposal is not null && await IsCurrentAcceptedAsync(db, firmId, proposal, ct);
  }

  public static bool MatchesAcceptedResponse(Proposal proposal, CommercialNotification? dispatch,
    ProposalResponseRecord? response, QuotationVersion? quotation, CommercialDocument? document)
  {
    if (proposal.Status != CrmStates.ProposalAccepted || proposal.SentAt is null || proposal.ResponseAt is null ||
        dispatch is null || dispatch.Kind != CommercialNotificationKinds.Proposal || response is null ||
        response.Decision != CrmStates.ProposalAccepted || response.FirmId != proposal.FirmId ||
        response.ProposalId != proposal.Id || response.CommercialNotificationId != dispatch.Id ||
        response.RecordedByUserId == Guid.Empty || response.RecordedAt != proposal.ResponseAt ||
        string.IsNullOrWhiteSpace(response.RespondentName) || string.IsNullOrWhiteSpace(response.EvidenceReference) ||
        response.Channel is null || !ProposalResponseChannels.All.Contains(response.Channel) ||
        !string.Equals(response.DispatchRecipient, dispatch.Recipient, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(response.RespondentEmail, dispatch.Recipient, StringComparison.OrdinalIgnoreCase) ||
        response.AuthorityBasis != dispatch.RecipientAuthorityBasis ||
        response.RecipientContactId != dispatch.RecipientContactId || response.RecipientRoutingId != dispatch.RecipientRoutingId ||
        string.IsNullOrWhiteSpace(dispatch.OfferSha256) ||
        !string.Equals(response.OfferSha256, dispatch.OfferSha256, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(proposal.SentOfferSha256, dispatch.OfferSha256, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(proposal.ResponseOfferSha256, dispatch.OfferSha256, StringComparison.OrdinalIgnoreCase) ||
        quotation is null || quotation.Status != QuotationStates.Approved || quotation.Fee != proposal.Fee ||
        quotation.Id != dispatch.QuotationVersionId || dispatch.DocumentId is not { } documentId ||
        document is null || document.Id != documentId || document.ProposalId != proposal.Id ||
        document.QuotationVersionId != quotation.Id || document.Kind != CommercialDocumentKinds.Quotation ||
        document.Bytes.Length == 0 || !string.Equals(document.Sha256Hex, dispatch.OfferSha256, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(document.Sha256Hex, AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(document.Bytes), StringComparison.OrdinalIgnoreCase))
      return false;

    if (dispatch.RecipientAuthorityBasis is not { } basis || !ProposalRecipientAuthorityBases.All.Contains(basis) ||
        basis == ProposalRecipientAuthorityBases.ClientCommercialRouting &&
        (!dispatch.RecipientContactId.HasValue || !dispatch.RecipientRoutingId.HasValue) ||
        basis != ProposalRecipientAuthorityBases.ClientCommercialRouting &&
        (dispatch.RecipientContactId.HasValue || dispatch.RecipientRoutingId.HasValue))
      return false;

    // Legacy columns are a projection only. Reject divergence so a changed mutable row cannot shadow the receipt.
    return proposal.RespondentName == response.RespondentName &&
      string.Equals(proposal.RespondentEmail, response.RespondentEmail, StringComparison.OrdinalIgnoreCase) &&
      proposal.ResponseEvidenceReference == response.EvidenceReference && proposal.ResponseChannel == response.Channel &&
      proposal.ResponseReason == response.Reason;
  }

  public static async Task<bool> IsCurrentAcceptedAsync(IAuditSphereDbContext db, Guid firmId, Proposal proposal,
    CancellationToken ct = default)
  {
    if (proposal.FirmId != firmId) return false;
    var dispatch = await db.CommercialNotifications.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == firmId && x.ProposalId == proposal.Id && x.Kind == CommercialNotificationKinds.Proposal, ct);
    if (dispatch is null) return false;
    var response = await db.ProposalResponseRecords.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == firmId && x.ProposalId == proposal.Id, ct);
    var quotation = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == firmId && x.ProposalId == proposal.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (quotation is null || !await QuotationService.ApprovalsStandAsync(db, firmId, quotation.Id, ct)) return false;
    var document = dispatch.DocumentId is { } documentId
      ? await db.CommercialDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == documentId, ct)
      : null;
    return MatchesAcceptedResponse(proposal, dispatch, response, quotation, document);
  }
}
