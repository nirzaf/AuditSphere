using System.Globalization;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Practice;

public static partial class PracticeCrmService
{
  /// <summary>
  /// The exact offer identity queued for dispatch (STE 4.1.2–3): the generated artifact hash when a reviewed
  /// quotation document exists, otherwise a deterministic digest of the reviewed proposal revision with its
  /// approved quotation. Dispatch and client acceptance bind to this hash, so a revised, superseded, or
  /// materially changed offer can never be accepted under an earlier identity.
  /// </summary>
  internal static string ProposalOfferSha256(Proposal proposal, QuotationVersion? quotation, CommercialDocument? document) =>
    document is not null ? document.Sha256Hex
    : Hashing.Sha256Hex(string.Join('|', "proposal-offer.v1", proposal.FirmId.ToString("D"), proposal.Id.ToString("D"),
        proposal.Revision.ToString(CultureInfo.InvariantCulture), proposal.CreateRequestHash ?? string.Empty,
        quotation?.Id.ToString("D") ?? string.Empty,
        quotation?.Fee.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        proposal.Fee.ToString(CultureInfo.InvariantCulture), proposal.Currency));
}
