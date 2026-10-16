using System.Text.Json.Serialization;

namespace AuditSphereOps.Application.Audit;

/// <summary>Administrative disposition actions; no independent reviewer or professional conclusion is implied.</summary>
public static class ReviewPointCommandActions
{
  public const string Clear = "CLEAR";
  public const string Reopen = "REOPEN";
}
/// <summary>Typed exact administrative intent.</summary>
public sealed record ReviewPointCommandFields(string Action);
/// <summary>Reviewed intent bound to the exact preview and disposition revision.</summary>
public sealed record ReviewPointCommandRequest(Guid RequestId, string ExpectedDispositionRevision, string ReviewBasis,
  ReviewPointCommandFields Fields, string? ExpectedRequestHash = null, bool Reviewed = false);
/// <summary>Immutable scope, target identity, content and disposition; revisions use exact strings.</summary>
public sealed record ReviewPointSnapshot(Guid ReviewPointId, Guid ClientId, Guid EngagementId, string DispositionRevision,
  string TargetKind, Guid TargetId, string TargetRevision, Guid RaisedByUserId, DateTimeOffset RaisedAt,
  bool Significant, bool Cleared, string Comment, string Status);
/// <summary>Current exact scoped authority and readable before-state.</summary>
public sealed record ReviewPointCommandState(ReviewPointSnapshot ReviewPoint, string ReviewBasis, bool CanChangeDisposition,
  IReadOnlyList<string> Blockers, [property: JsonIgnore] string StateJson);
/// <summary>Exact immutable before-state and intended outcome for explicit review.</summary>
public sealed record ReviewPointCommandPreview(Guid RequestId, string RequestHash, string ExpectedDispositionRevision,
  string ReviewBasis, ReviewPointCommandFields CanonicalInput, ReviewPointSnapshot Before, string NextDispositionRevision,
  bool Cleared, bool CanProceed, IReadOnlyList<string> Blockers, string Summary);
/// <summary>Original actor-owned disposition event, retained across subsequent amendments.</summary>
public sealed record ReviewPointDispositionEventView(Guid Id, Guid ActorId, string ActorEpoch, string Action,
  DateTimeOffset CreatedAt, ReviewPointSnapshot Before, ReviewPointSnapshot After);
/// <summary>Original reviewed publication; timestamps come from persisted rows.</summary>
public sealed record ReviewPointCommandReceiptView(Guid Id, Guid ReviewPointId, Guid ActorId, string ActorEpoch, Guid RequestId,
  string RequestHash, string ReviewBasis, string Action, DateTimeOffset CreatedAt, ReviewPointCommandPreview Preview,
  ReviewPointDispositionEventView DispositionEvent);
/// <summary>Opaque actor/request recovery result within the currently authorized exact scope.</summary>
public sealed record ReviewPointCommandLookup(bool Found, ReviewPointCommandReceiptView? Receipt);
/// <summary>Compatibility HTTP writes require an explicit disposition revision.</summary>
public sealed record ReviewPointDispositionInput(bool Cleared, string? ExpectedDispositionRevision = null);
