using System.Text.Json.Serialization;

namespace AuditSphereOps.Application.Audit;

public static class FindingCommandActions
{
  public const string RecordResponse = "RECORD_RESPONSE";
  public const string Designate = "DESIGNATE_MANAGEMENT_LETTER";
  public const string Undesignate = "UNDESIGNATE_MANAGEMENT_LETTER";
}
public sealed record FindingResponseFields(string ManagementResponse, bool Corrected);
public sealed record FindingDesignationFields(string Recommendation);
public sealed record FindingCommandFields(string Action, FindingResponseFields? Response = null, FindingDesignationFields? Designation = null);
public sealed record FindingCommandRequest(Guid RequestId, string ExpectedRevision, string ReviewBasis, FindingCommandFields Fields,
  string? ExpectedRequestHash = null, bool Reviewed = false);
public sealed record FindingSnapshot(Guid FindingId, Guid ClientId, Guid EngagementId, string Revision, Guid CreatedByUserId,
  string FindingType, string ImpactDescription, string? MonetaryAmount, bool Corrected, string? ManagementResponse, string Status,
  DateTimeOffset? LetterDesignatedAt, Guid? LetterDesignatedByUserId, string? LetterRecommendation, DateTimeOffset CreatedAt);
public sealed record FindingCommandState(FindingSnapshot Finding, string ReviewBasis, bool CanRecordResponse, bool CanDesignate,
  IReadOnlyList<string> ResponseBlockers, IReadOnlyList<string> DesignationBlockers, [property: JsonIgnore] string StateJson);
public sealed record FindingCommandPreview(Guid RequestId, string RequestHash, string ExpectedRevision, string ReviewBasis,
  FindingCommandFields CanonicalInput, FindingSnapshot Before, string NextRevision, bool CanProceed,
  IReadOnlyList<string> Blockers, string Summary);
public sealed record FindingAmendmentView(Guid Id, Guid ActorId, string ActorEpoch, string Action, DateTimeOffset CreatedAt,
  FindingSnapshot Before, FindingSnapshot After);
public sealed record FindingCommandReceiptView(Guid Id, Guid FindingId, Guid ActorId, string ActorEpoch, Guid RequestId,
  string RequestHash, string ReviewBasis, string Action, DateTimeOffset CreatedAt, FindingCommandPreview Preview, FindingAmendmentView Amendment);
public sealed record FindingCommandLookup(bool Found, FindingCommandReceiptView? Receipt);
