// Multi-tier review, confirmations policy and final deliverables (STE 3.3-02..04, 3.4-01..02, 4.1-01..04).
// Notes anchor to exact result revisions; generated documents bind to the digest of the inputs they were built from
// and become stale when those inputs change; decisions and signatures are append-only records.
namespace AuditSphereOps.Domain.Completion;

public static class ReviewNoteFields
{
  public const string WorkPerformed = "WORK_PERFORMED";
  public const string Conclusion = "CONCLUSION";
}

/// <summary>An inline note anchored to an excerpt of one exact procedure result revision.</summary>
public sealed class ProcedureReviewNote
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ProcedureId { get; set; }
  public Guid ResultId { get; set; }
  public long ResultRevision { get; set; }
  public string Field { get; set; } = ReviewNoteFields.WorkPerformed;
  public string Excerpt { get; set; } = string.Empty;
  public int StartOffset { get; set; }
  public string Body { get; set; } = string.Empty;
  public Guid AuthorUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class ReviewNoteEventKinds
{
  public const string Response = "RESPONSE";
  public const string Resolved = "RESOLVED";
  public const string Reopened = "REOPENED";
}

/// <summary>Append-only thread entry on a note; the latest RESOLVED/REOPENED entry decides whether it is open.</summary>
public sealed class ProcedureReviewNoteEvent
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid NoteId { get; set; }
  public string Kind { get; set; } = ReviewNoteEventKinds.Response;
  public string Body { get; set; } = string.Empty;
  public Guid AuthorUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class DeliverableKinds
{
  public const string SummaryReviewMemorandum = "SUMMARY_REVIEW_MEMORANDUM";
  public const string AuditFindingsReport = "AUDIT_FINDINGS_REPORT";
  public const string ManagementLetter = "MANAGEMENT_LETTER";
  public const string IndependentAuditorsReport = "INDEPENDENT_AUDITORS_REPORT";
  public const string HoldingLetter = "HOLDING_LETTER";
  public const string RepresentationLetter = "REPRESENTATION_LETTER";

  public static readonly string[] All = [SummaryReviewMemorandum, AuditFindingsReport, ManagementLetter, IndependentAuditorsReport, HoldingLetter, RepresentationLetter];

  public static string Title(string kind) => kind switch
  {
    SummaryReviewMemorandum => "Summary Review Memorandum",
    AuditFindingsReport => "Audit Findings Report",
    ManagementLetter => "Management Letter",
    IndependentAuditorsReport => "Independent Auditor's Report",
    HoldingLetter => "Pending Confirmation / Holding Letter",
    RepresentationLetter => "Management Representation Letter",
    _ => kind
  };
}

/// <summary>One immutable, versioned generated document with the digest of the facts it was built from.</summary>
public sealed class AuditDeliverable
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string Kind { get; set; } = DeliverableKinds.SummaryReviewMemorandum;
  public int Version { get; set; } = 1;
  public string TemplateVersion { get; set; } = string.Empty;
  public string InputDigest { get; set; } = string.Empty;
  public string InputSummaryJson { get; set; } = "{}";
  public string FileName { get; set; } = string.Empty;
  public string ContentType { get; set; } = string.Empty;
  public byte[] Content { get; set; } = [];
  public string ContentSha256 { get; set; } = string.Empty;
  /// <summary>For a signed rendering: the unsigned deliverable it was produced from.</summary>
  public Guid? SignedFromDeliverableId { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Mandatory Engagement Partner clearance over key risk areas and financial-statement notes for one SRM.</summary>
public sealed class PartnerCompletionClearance
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid SummaryReviewMemorandumId { get; set; }
  public string KeyRiskAreasComment { get; set; } = string.Empty;
  public string FinancialStatementNotesComment { get; set; } = string.Empty;
  public Guid PartnerUserId { get; set; }
  public DateTimeOffset ClearedAt { get; set; }
}

public static class AuditOpinionTypes
{
  public const string Unmodified = "UNMODIFIED";
  public const string Qualified = "QUALIFIED";
  public const string Adverse = "ADVERSE";
  public const string Disclaimer = "DISCLAIMER";
  public static readonly string[] All = [Unmodified, Qualified, Adverse, Disclaimer];

  public static string Label(string type) => type switch
  {
    Unmodified => "Clean (unmodified)",
    Qualified => "Qualified",
    Adverse => "Adverse",
    Disclaimer => "Disclaimer of opinion",
    _ => type
  };
}

/// <summary>The Engagement Partner's opinion selection, bound to the partner clearance it follows.</summary>
public sealed class AuditOpinionDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PartnerClearanceId { get; set; }
  public string OpinionType { get; set; } = AuditOpinionTypes.Unmodified;
  public string? FocusArea { get; set; }
  public string? BasisText { get; set; }
  public Guid DecidedByUserId { get; set; }
  public DateTimeOffset DecidedAt { get; set; }
}

/// <summary>A signatory's registered PNG signature specimen; applying it is recorded per exact deliverable.</summary>
public sealed class SignatureSpecimen
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid UserId { get; set; }
  public byte[] PngContent { get; set; } = [];
  public string Sha256 { get; set; } = string.Empty;
  public int WidthPixels { get; set; }
  public int HeightPixels { get; set; }
  public DateTimeOffset RegisteredAt { get; set; }
  public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class SignatureApplication
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid SpecimenId { get; set; }
  public Guid SourceDeliverableId { get; set; }
  public Guid SignedDeliverableId { get; set; }
  public Guid SignedByUserId { get; set; }
  public DateTimeOffset SignedAt { get; set; }
}

public static class ClientReviewStates
{
  public const string Shared = "SHARED";
  public const string CommentsOpen = "COMMENTS_OPEN";
  public const string Acknowledged = "ACKNOWLEDGED";
}

/// <summary>A draft deliverable shared with client management for comment, or the representation letter to acknowledge.</summary>
public sealed class ClientDeliverableReview
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid DeliverableId { get; set; }
  public Guid SharedByUserId { get; set; }
  public DateTimeOffset SharedAt { get; set; }
  public Guid? AcknowledgedByUserId { get; set; }
  public DateTimeOffset? AcknowledgedAt { get; set; }
  public string? AcknowledgedSha256 { get; set; }
}

public sealed class ClientDeliverableComment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ReviewId { get; set; }
  public string Body { get; set; } = string.Empty;
  public Guid AuthorUserId { get; set; }
  public bool FromClient { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public Guid? ResolvedByUserId { get; set; }
  public DateTimeOffset? ResolvedAt { get; set; }
  public string? Resolution { get; set; }
}
