// Audit: risks, procedures, populations, samples, workpapers, findings, misstatements (§§19–23, 27.2).
// Every row carries the (firm, client, engagement) scope triple; the DbContext binds it to composite
// foreign keys so a cross-scope link is rejected by PostgreSQL, not only by application code (§42.3).
namespace AuditSphereOps.Domain.Audit;

public sealed class MaterialityAssessment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public string BenchmarkSource { get; set; } = string.Empty;
  public string BenchmarkVersion { get; set; } = string.Empty;
  public string Rationale { get; set; } = string.Empty;
  public decimal BenchmarkAmount { get; set; }
  public decimal RateApplied { get; set; }
  public decimal OverallMateriality { get; set; }
  public decimal PerformanceMateriality { get; set; }
  public decimal ClearlyTrivialThreshold { get; set; }
  public string? QualitativeConsiderations { get; set; }
  public string Status { get; set; } = MaterialityStatuses.Draft;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MaterialityApproval
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid MaterialityAssessmentId { get; set; }
  public Guid ApprovedByUserId { get; set; }
  public DateTimeOffset ApprovedAt { get; set; }
}

public static class MaterialityStatuses
{
  public const string Draft = "DRAFT";
  public const string Approved = "APPROVED";
}

public sealed class AuditRisk
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public string AccountArea { get; set; } = string.Empty;
  public string Assertion { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public string Drivers { get; set; } = string.Empty;
  public string Severity { get; set; } = RiskSeverities.Normal;
  public string SignificanceDecision { get; set; } = string.Empty;
  public string? ControlsConsidered { get; set; }
  public string ResponseDescription { get; set; } = string.Empty;
  public string Status { get; set; } = RiskStatuses.Identified;
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Risk classification derived from the significance decision (§19.3); never a free-text field.</summary>
public static class RiskSeverities
{
  public const string Normal = "Normal";
  public const string Significant = "Significant";

  /// <summary>Maps the §19.3 significance decision onto the stored classification.</summary>
  public static string ForDecision(string significanceDecision) =>
    string.Equals(significanceDecision, SignificanceDecisions.Significant, StringComparison.Ordinal)
      ? Significant : Normal;
}

public static class SignificanceDecisions
{
  public const string Significant = "SIGNIFICANT";
  public const string Normal = "NORMAL";
}

public static class RiskStatuses
{
  public const string Identified = "IDENTIFIED";
  public const string Assessed = "ASSESSED";
  public const string Responded = "RESPONDED";
}

public sealed class PopulationVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public string Purpose { get; set; } = string.Empty;
  public string Assertion { get; set; } = string.Empty;
  public string SourceReceiptReference { get; set; } = string.Empty;
  public string ExtractionParameters { get; set; } = string.Empty;
  public int RowCount { get; set; }
  public decimal MonetaryControlTotal { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string? Exclusions { get; set; }
  public string Status { get; set; } = PopulationStatuses.PendingApproval;
  public DateTimeOffset CreatedAt { get; set; }
}

public static class PopulationStatuses
{
  public const string PendingApproval = "PENDING_APPROVAL";
  public const string Approved = "APPROVED";
  public const string Rejected = "REJECTED";
}

public sealed class AuditProcedure
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid? RiskId { get; set; }
  public Guid? EngagementProgramId { get; set; }
  public Guid? ProgramProcedureId { get; set; }
  public string SourceProcedureId { get; set; } = string.Empty;
  public int? SourceSectionNumber { get; set; }
  public string? SourceSectionTitle { get; set; }
  public string? SourceWording { get; set; }
  public string ApplicabilityStatus { get; set; } = AuditApplicabilityStatuses.Pending;
  public string? ApplicabilityRationale { get; set; }
  public Guid? ApplicabilityDecidedByUserId { get; set; }
  public DateTimeOffset? ApplicabilityDecidedAt { get; set; }
  public long CurrentResultRevision { get; set; }
  public string Title { get; set; } = string.Empty;
  public string Status { get; set; } = "Planned";
  public DateTimeOffset CreatedAt { get; set; }
}

public static class AuditApplicabilityStatuses
{
  public const string Pending = "PENDING";
  public const string Applicable = "APPLICABLE";
  public const string NotApplicablePendingReview = "NA_PENDING_REVIEW";
  public const string NotApplicableApproved = "NA_APPROVED";
}

public static class AuditProcedureStatuses
{
  public const string Planned = "PLANNED";
  public const string InProgress = "IN_PROGRESS";
  public const string Submitted = "SUBMITTED";
  public const string InReview = "IN_REVIEW";
  public const string ChangesRequired = "CHANGES_REQUIRED";
  public const string Reviewed = "REVIEWED";
}

public sealed class Workpaper
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  /// <summary>Planned procedure this workpaper executes; null when the work is not procedure-driven (§42.4).</summary>
  public Guid? ProcedureId { get; set; }
  public Guid ActorId { get; set; }
  public string Index { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;
  public string Objective { get; set; } = string.Empty;
  public string TemplateVersion { get; set; } = string.Empty;
  public string Procedure { get; set; } = string.Empty;
  public string? WorkPerformed { get; set; }
  public string? Conclusion { get; set; }
  public long Revision { get; set; } = 1;
  public string Status { get; set; } = WorkpaperStatuses.Working;
  public DateTimeOffset? SubmittedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class WorkpaperStatuses
{
  public const string Working = "WORKING";
  public const string SubmittedSnapshot = "SUBMITTED_SNAPSHOT";
}

/// <summary>
/// Durable server-side working content for one owner and workpaper. Drafts are
/// disposable working state; the immutable submission remains the official record.
/// </summary>
public sealed class WorkpaperDraft
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid WorkpaperId { get; set; }
  public Guid OwnerUserId { get; set; }
  public long BaseWorkpaperRevision { get; set; } = 1;
  public long BaseInputGeneration { get; set; } = 1;
  public long BasePolicyGeneration { get; set; } = 1;
  public long DraftRevision { get; set; } = 1;
  public string WorkPerformed { get; set; } = string.Empty;
  public string Conclusion { get; set; } = string.Empty;
  public Guid LastSaveId { get; set; }
  public DateTimeOffset LastSavedAt { get; set; }
  public string Lifecycle { get; set; } = WorkpaperDraftLifecycles.Active;
}

public static class WorkpaperDraftLifecycles
{
  public const string Active = "ACTIVE";
  public const string Consumed = "CONSUMED";
  public const string Discarded = "DISCARDED";
}

/// <summary>Immutable submitted content; review and approval always target a frozen submission (§21.1, §22).</summary>
public sealed class WorkpaperSubmission
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid WorkpaperId { get; set; }
  public Guid ActorId { get; set; }
  public long Revision { get; set; }
  public string WorkPerformed { get; set; } = string.Empty;
  public string Conclusion { get; set; } = string.Empty;
  public DateTimeOffset SubmittedAt { get; set; }
}

public sealed class Finding
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public string FindingType { get; set; } = string.Empty;
  public string ImpactDescription { get; set; } = string.Empty;
  public bool Corrected { get; set; }
  public decimal? MonetaryAmount { get; set; }
  public string? ManagementResponse { get; set; }
  public string Status { get; set; } = FindingStatuses.Open;
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable firm methodology version sourced from the controlled audit program.</summary>
public sealed class AuditProgramVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string ProgramCode { get; set; } = "AUDIT";
  public string Version { get; set; } = string.Empty;
  public string SourceHash { get; set; } = string.Empty;
  public string Status { get; set; } = AuditProgramStatuses.Draft;
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
}

public static class AuditProgramStatuses
{
  public const string Draft = "DRAFT";
  public const string Published = "PUBLISHED";
  public const string Retired = "RETIRED";
}

/// <summary>Source procedure template. SourceProcedureId is stable across program versions.</summary>
public sealed class AuditProgramProcedure
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ProgramVersionId { get; set; }
  public string SourceProcedureId { get; set; } = string.Empty;
  public int SectionNumber { get; set; }
  public string SectionTitle { get; set; } = string.Empty;
  public int Ordinal { get; set; }
  public string SourceWording { get; set; } = string.Empty;
  public string? ApplicabilityCondition { get; set; }
  public string? ExpectedEvidence { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable adoption of a published methodology version by one engagement.</summary>
public sealed class EngagementAuditProgram
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ProgramVersionId { get; set; }
  public string Status { get; set; } = EngagementAuditProgramStatuses.Adopted;
  public Guid AdoptedByUserId { get; set; }
  public DateTimeOffset AdoptedAt { get; set; }
}

public static class EngagementAuditProgramStatuses
{
  public const string Adopted = "ADOPTED";
  public const string Superseded = "SUPERSEDED";
}

/// <summary>Immutable structured result for one procedure revision.</summary>
public sealed class AuditProcedureResult
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid AuditProcedureId { get; set; }
  public Guid? WorkpaperId { get; set; }
  public long Revision { get; set; }
  public long InputGeneration { get; set; }
  public string WorkPerformed { get; set; } = string.Empty;
  public string StructuredResultJson { get; set; } = string.Empty;
  public string EvidenceReferencesJson { get; set; } = "[]";
  public string Conclusion { get; set; } = string.Empty;
  public string Status { get; set; } = AuditProcedureResultStatuses.Submitted;
  public Guid PreparedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public string? ReviewComment { get; set; }
  public DateTimeOffset SubmittedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

public static class AuditProcedureResultStatuses
{
  public const string Submitted = "SUBMITTED";
  public const string ChangesRequired = "CHANGES_REQUIRED";
  public const string Reviewed = "REVIEWED";
}

/// <summary>Append-only reviewer decision for an immutable procedure result.</summary>
public sealed class AuditProcedureReview
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid AuditProcedureResultId { get; set; }
  public Guid AuditProcedureId { get; set; }
  public long ResultRevision { get; set; }
  public string Decision { get; set; } = AuditProcedureReviewDecisions.ChangesRequired;
  public string? Comment { get; set; }
  public Guid ReviewerUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class AuditProcedureReviewDecisions
{
  public const string Reviewed = "REVIEWED";
  public const string ChangesRequired = "CHANGES_REQUIRED";
}

public static class FindingStatuses
{
  public const string Open = "OPEN";
  public const string Corrected = "CORRECTED";
  public const string Evaluated = "EVALUATED";
}

public static class OpeningBalanceVerificationConclusions
{
  public const string Agreed = "AGREED";
  public const string DifferencesResolved = "DIFFERENCES_RESOLVED";
  public const string DifferencesUnresolved = "DIFFERENCES_UNRESOLVED";
  public const string NotVerifiable = "NOT_VERIFIABLE";
}

/// <summary>
/// ISA 510 opening-balance verification: the prior-year closing figures, the consistency
/// of accounting policies and the resolution of any difference are recorded with the
/// evidence that supports the conclusion. An unresolved difference stays visible and
/// blocks reliance rather than being absorbed silently.
/// </summary>
public sealed class OpeningBalanceVerification
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid? ProcedureId { get; set; }
  /// <summary>Prior reporting period whose closing balances become the opening balances.</summary>
  public Guid? PriorPeriodId { get; set; }
  public string PriorReference { get; set; } = string.Empty;
  public DateOnly AsOfDate { get; set; }
  public string Currency { get; set; } = string.Empty;
  public decimal OpeningSignedTotal { get; set; }
  public decimal AgreedSignedTotal { get; set; }
  public decimal DifferenceAmount { get; set; }
  public bool AccountingPoliciesConsistent { get; set; }
  public string Conclusion { get; set; } = OpeningBalanceVerificationConclusions.NotVerifiable;
  public string Rationale { get; set; } = string.Empty;
  public string EvidenceReferencesJson { get; set; } = "[]";
  public long Revision { get; set; } = 1;
  public Guid RecordedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset RecordedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}
