// Accounting: TB datasets, rows (immutable), mappings, journals, packages (§§16–18, 27.2, 42.1–42.6).
namespace AuditSphereOps.Domain.Accounting;

public static class AccountingSourceKinds
{
  public const string TrialBalance = "TB";
  public const string GeneralLedger = "GL";
}

/// <summary>Append-only independent acceptance of a sealed source revision (M21): sealing
/// proves immutability, this decision selects the accepted source for downstream modules.</summary>
public sealed class SourceAcceptanceDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string SourceKind { get; set; } = AccountingSourceKinds.TrialBalance;
  public Guid? TrialBalanceDatasetId { get; set; }
  public Guid? ImportBatchId { get; set; }
  public string SourceIdentityHash { get; set; } = string.Empty;
  public string Decision { get; set; } = "ACCEPTED";
  public string EvidenceReference { get; set; } = string.Empty;
  public Guid AcceptedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Row-level validation issue persisted when a sealed TB dataset fails validation,
/// so reviewers can explain a rejection without access to the original upload.</summary>
public sealed class TrialBalanceValidationIssue
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid DatasetId { get; set; }
  public long Revision { get; set; }
  public string RowKey { get; set; } = string.Empty;
  public string Severity { get; set; } = "ERROR";
  public string Code { get; set; } = string.Empty;
  public string Message { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TrialBalanceDataset
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid? PeriodId { get; set; }
  public Guid? BookId { get; set; }
  public string? Basis { get; set; }
  public Guid? ImportBatchId { get; set; }
  public string SourceKind { get; set; } = "Raw";     // Raw|Adjusted
  public long Revision { get; set; } = 1;
  /// <summary>One canonical legal-entity label per accepted dataset.</summary>
  public string LegalEntityKey { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  /// <summary>Digest of the exact received upload bytes.</summary>
  public string RawFileSha256Hex { get; set; } = string.Empty;
  /// <summary>Versioned digest of normalized accounting rows.</summary>
  public string NormalizedDatasetDigest { get; set; } = string.Empty;
  /// <summary>Legacy normalized digest retained for existing records and callers.</summary>
  public string Sha256Hex { get; set; } = string.Empty;
  public string ImportProfileVersion { get; set; } = "tb-signed-net.v1";
  public string SourceLayout { get; set; } = TrialBalanceLayouts.SignedNet;
  public bool Balanced { get; set; }
  public string ValidationStatus { get; set; } = "Pending";
  /// <summary>
  /// Source membership lifecycle. New datasets start LOADING; the guarded importer
  /// promotes them to SEALED only after all source rows are present.
  /// </summary>
  public string ImportState { get; set; } = TrialBalanceImportStates.Loading;
  public decimal ControlTotal { get; set; }
  public DateTimeOffset ImportedAt { get; set; }
  public Guid ImportedByUserId { get; set; }
}

public static class TrialBalanceLayouts
{
  public const string SignedNet = "SIGNED_NET";
  public const string DebitCredit = "DEBIT_CREDIT";

  public static bool IsSupported(string value) => value is SignedNet or DebitCredit;
}

/// <summary>Immutable source receipt for one controlled multi-entity trial-balance upload.</summary>
public sealed class TrialBalanceImportBatch
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid? PeriodId { get; set; }
  public Guid? BookId { get; set; }
  public string? Basis { get; set; }
  public string RawFileSha256Hex { get; set; } = string.Empty;
  public string NormalizedDatasetDigest { get; set; } = string.Empty;
  public string ImportProfileVersion { get; set; } = string.Empty;
  public string SourceLayout { get; set; } = TrialBalanceLayouts.SignedNet;
  public int EntityCount { get; set; }
  public string Status { get; set; } = TrialBalanceImportStates.Loading;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class TrialBalanceImportStates
{
  public const string Loading = "LOADING";
  public const string Sealed = "SEALED";
}

public sealed class TrialBalanceRow
{
  public Guid Id { get; set; }
  public Guid DatasetId { get; set; }
  public string AccountCode { get; set; } = string.Empty;
  public string AccountName { get; set; } = string.Empty;
  public decimal Amount { get; set; }                 // signed, ≤6dp
  public decimal? SourceDebit { get; set; }
  public decimal? SourceCredit { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string Entity { get; set; } = string.Empty;
  public string? MappingCode { get; set; }
}

public sealed class MappingRule
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string MappingCode { get; set; } = string.Empty;
  public string SourcePattern { get; set; } = string.Empty;
  public long Revision { get; set; } = 1;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class AccountingPackageStates
{
  public const string MappingDraft = "DRAFT";
  public const string MappingApproved = "APPROVED";
  public const string PackageReviewRequired = "REVIEW_REQUIRED";
  public const string PackageValidated = "VALIDATED";
}

/// <summary>Versioned source-to-presentation mapping for one accepted dataset.</summary>
public sealed class MappingVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid DatasetId { get; set; }
  /// <summary>Approved client chart version used for this mapping; nullable for legacy mappings.</summary>
  public Guid? ClientChartVersionId { get; set; }
  /// <summary>Native batch creation receipt and exact reviewed parent; null on legacy versions.</summary>
  public Guid? BaseMappingVersionId { get; set; }
  public Guid? CreationRequestId { get; set; }
  public string? CreationRequestHash { get; set; }
  public string? CreationReviewRevision { get; set; }
  public long Version { get; set; } = 1;
  public long Generation { get; set; } = 1;
  public string TaxonomyVersion { get; set; } = string.Empty;
  public string PeriodStart { get; set; } = string.Empty;
  public string PeriodEnd { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingPackageStates.MappingDraft;
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable allocation; fractions for one source account must total 1.</summary>
public sealed class MappingAllocation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid MappingVersionId { get; set; }
  public string SourceAccountCode { get; set; } = string.Empty;
  public string DestinationCode { get; set; } = string.Empty;
  public string StatementSection { get; set; } = string.Empty;
  public string? AuditArea { get; set; }
  public decimal Fraction { get; set; }
  public string ResidualPolicy { get; set; } = "LAST_DESTINATION";
  public string Rationale { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Frozen adjusted-TB projection created from one immutable adjustment plan.</summary>
public sealed class AdjustedTrialBalanceSnapshot
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid BaseDatasetId { get; set; }
  public Guid AdjustmentPlanId { get; set; }
  public long Revision { get; set; } = 1;
  public string Currency { get; set; } = string.Empty;
  public string ResultHash { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AdjustedTrialBalanceRow
{
  public Guid Id { get; set; }
  public Guid SnapshotId { get; set; }
  public string AccountCode { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public string Currency { get; set; } = string.Empty;
}

/// <summary>Adjustment journal header: draft → posted immutable; AJ numbers unique per base TB (§17.4, VX-07).
/// The logical identity (firm, engagement, journal number) is stable across replacement
/// bases; each base carries its own revision row. CreatedByUserId enforces separation of
/// duties: the preparer can never post their own journal.</summary>
public sealed class AdjustmentJournal
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid BaseDatasetId { get; set; }
  public string JournalNumber { get; set; } = string.Empty; // AJ-001
  public string Purpose { get; set; } = AdjustmentJournalPurposes.ReportingAdjustment;
  public Guid? PeriodId { get; set; }
  public Guid? BookId { get; set; }
  public string? Basis { get; set; }
  public string? Currency { get; set; }
  public string Origin { get; set; } = AdjustmentJournalOrigins.AuditProposed;
  public string Reason { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public Guid? SupersedesJournalId { get; set; }
  public Guid? ReversalOfJournalId { get; set; }
  public string Status { get; set; } = "Draft";             // Draft|Submitted|Returned|Posted|ReflectedInSource|Void
  /// <summary>Mandatory reviewer rationale when a submitted revision is returned to the preparer.</summary>
  public string? ReturnReason { get; set; }
  public long Revision { get; set; } = 1;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class AdjustmentJournalPurposes
{
  public const string ClientBookCorrection = "CLIENT_BOOK_CORRECTION";
  public const string ReportingAdjustment = "REPORTING_ADJUSTMENT";
  public const string PresentationReclassification = "PRESENTATION_RECLASSIFICATION";
  public const string GroupOnlyElimination = "GROUP_ONLY_ELIMINATION";

  public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
  {
    ClientBookCorrection, ReportingAdjustment, PresentationReclassification, GroupOnlyElimination
  };
}

public static class AdjustmentJournalOrigins
{
  public const string AuditProposed = "AUDIT_PROPOSED";
  public const string ClientRequested = "CLIENT_REQUESTED";
  public const string ManagementProvided = "MANAGEMENT_PROVIDED";
  public const string Imported = "IMPORTED";

  public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
  {
    AuditProposed, ClientRequested, ManagementProvided, Imported
  };
}

public static class ManagementDecisionStates
{
  public const string Accepted = "ACCEPTED";
  public const string Rejected = "REJECTED";
  public const string Partial = "PARTIAL";

  public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
  {
    Accepted, Rejected, Partial
  };
}

public static class ManagementDecisionEvidenceModes
{
  public const string SignedIn = "SIGNED_IN";
  public const string Offline = "OFFLINE";
}

/// <summary>Immutable management disposition for one exact journal revision.</summary>
public sealed class AdjustmentJournalManagementDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid JournalId { get; set; }
  public long JournalRevision { get; set; }
  public string Decision { get; set; } = string.Empty;
  public string EvidenceMode { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public Guid? DecidedByUserId { get; set; }
  public DateTimeOffset DecidedAt { get; set; }
}

public sealed class AdjustmentLine
{
  public Guid Id { get; set; }
  public Guid JournalId { get; set; }
  public string AccountCode { get; set; } = string.Empty;
  public decimal Debit { get; set; }
  public decimal Credit { get; set; }
}

public sealed class FinancialPackage
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid AdjustedDatasetId { get; set; }
  public Guid MappingVersionId { get; set; }
  public Guid AdjustmentPlanId { get; set; }
  public Guid? PeriodId { get; set; }
  public Guid? BookId { get; set; }
  public string? Basis { get; set; }
  public string Framework { get; set; } = string.Empty;
  public string PeriodStart { get; set; } = string.Empty;
  public string PeriodEnd { get; set; } = string.Empty;
  public string TaxonomyVersion { get; set; } = string.Empty;
  public string TemplateVersion { get; set; } = string.Empty;
  public string CalculationEngineVersion { get; set; } = string.Empty;
  public string CalculationHash { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  public long Revision { get; set; } = 1;
  public long Generation { get; set; } = 1;
  public string Status { get; set; } = AccountingPackageStates.PackageReviewRequired;
  public decimal? CashBeginning { get; set; }
  public decimal? CashEnding { get; set; }
  /// <summary>Cash populations and evidence supplied for this package; empty for legacy packages.</summary>
  public string[] CashAccountCodes { get; set; } = [];
  public string[] OpeningCashEvidenceReferences { get; set; } = [];
  public string[] ClosingCashEvidenceReferences { get; set; } = [];
  public string? SupplementaryHash { get; set; }
  public string? EquityHash { get; set; }
  public Guid? ComparativePackageId { get; set; }
  public string? ComparativeBasis { get; set; }
  public string? ComparativeEvidenceReference { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable exact-byte export for one financial-package revision.</summary>
public sealed class FinancialPackageArtifact
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public long PackageRevision { get; set; }
  public long PackageGeneration { get; set; }
  public string PackageHash { get; set; } = string.Empty;
  public string ArtifactVersion { get; set; } = string.Empty;
  public string FrameworkVersion { get; set; } = string.Empty;
  public string TemplateVersion { get; set; } = string.Empty;
  public string ArtifactSha256Hex { get; set; } = string.Empty;
  public byte[] ArtifactBytes { get; set; } = [];
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An immutable, unapproved statement-presentation snapshot pinned to exact package and layout revisions.</summary>
public sealed class FinancialStatementPresentationRevision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public Guid? PeriodId { get; set; }
  public Guid? BookId { get; set; }
  public string? Basis { get; set; }
  public string Framework { get; set; } = string.Empty;
  public string TaxonomyVersion { get; set; } = string.Empty;
  public string TemplateVersion { get; set; } = string.Empty;
  public string CalculationEngineVersion { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  public string PeriodStart { get; set; } = string.Empty;
  public string PeriodEnd { get; set; } = string.Empty;
  public long PackageRevision { get; set; }
  public long PackageGeneration { get; set; }
  public string PackageCalculationHash { get; set; } = string.Empty;
  public Guid LayoutVersionId { get; set; }
  public string LayoutHash { get; set; } = string.Empty;
  public Guid? ComparativePackageId { get; set; }
  public long? ComparativePackageRevision { get; set; }
  public long? ComparativePackageGeneration { get; set; }
  public string? ComparativeCalculationHash { get; set; }
  public string? ComparativeBasis { get; set; }
  public string? ComparativeEvidenceReference { get; set; }
  public string NoteSourceManifestHash { get; set; } = string.Empty;
  public string BuilderVersion { get; set; } = string.Empty;
  public string RendererVersion { get; set; } = string.Empty;
  public string DisplayPolicy { get; set; } = string.Empty;
  public string Status { get; set; } = FinancialStatementPresentationRevisionStatuses.UnapprovedDraft;
  public Guid? PredecessorRevisionId { get; set; }
  public string RevisionHash { get; set; } = string.Empty;
  public string SnapshotSha256Hex { get; set; } = string.Empty;
  public byte[] SnapshotJson { get; set; } = [];
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class FinancialStatementPresentationRevisionStatuses
{
  public const string UnapprovedDraft = "UNAPPROVED_DRAFT";
  public const string Candidate = "CANDIDATE";
  public const string Approved = "APPROVED";
  public const string Superseded = "SUPERSEDED";

  public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
  {
    UnapprovedDraft, Candidate, Approved, Superseded
  };
}

/// <summary>Immutable presentation metadata linked to the exact-byte financial-package artifact covered by the package seal.</summary>
public sealed class FinancialStatementPresentationArtifact
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PresentationRevisionId { get; set; }
  public string Format { get; set; } = string.Empty;
  public string FileName { get; set; } = string.Empty;
  public string ContentType { get; set; } = string.Empty;
  public string RendererVersion { get; set; } = string.Empty;
}

public static class FinancialPackageArtifactVersions
{
  public const string Text = "financial-package-text.v1";
  public const string Workbook = "financial-package-xlsx.v1";
  public const string ControlledWorkbook = "financial-package-xlsx-controlled.v1";
  public const string Word = "financial-package-docx.v1";
  public const string Pdf = "financial-package-pdf.v1";

  public static string StatementPresentation(Guid revisionId, string format) =>
    $"statement-presentation.{format}.v1.{revisionId:N}";
}

public static class FinancialPackageReviewStages
{
  public const string ManagementApproval = "MANAGEMENT_APPROVAL";
  public const string AccountingReview = "ACCOUNTING_REVIEW";
  public const string PartnerApproval = "PARTNER_APPROVAL";

  public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
  {
    ManagementApproval, AccountingReview, PartnerApproval
  };
}

public static class FinancialPackageReviewDecisions
{
  public const string Approved = "APPROVED";
  public const string ChangesRequired = "CHANGES_REQUIRED";
  public const string Rejected = "REJECTED";

  public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
  {
    Approved, ChangesRequired, Rejected
  };
}

public static class FinancialPackageReviewEvidenceModes
{
  public const string SignedIn = "SIGNED_IN";
  public const string Offline = "OFFLINE";
}

/// <summary>Immutable stage decision for one exact financial package version.</summary>
public sealed class FinancialPackageReviewDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public long PackageRevision { get; set; }
  public long PackageGeneration { get; set; }
  public string PackageHash { get; set; } = string.Empty;
  public Guid FinancialPackageArtifactId { get; set; }
  public string ArtifactVersion { get; set; } = string.Empty;
  public string ArtifactSha256Hex { get; set; } = string.Empty;
  public Guid? PresentationRevisionId { get; set; }
  /// <summary>The immutable three-format composition covered by this decision; null only for historical or legacy decisions.</summary>
  public Guid? PresentationCompositionId { get; set; }
  public string? CompositionManifestDigest { get; set; }
  public string Stage { get; set; } = string.Empty;
  public string Decision { get; set; } = string.Empty;
  public string EvidenceMode { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public string Comment { get; set; } = string.Empty;
  public Guid? DecidedByUserId { get; set; }
  public DateTimeOffset DecidedAt { get; set; }
  /// <summary>Database-assigned order of recording. Ties on DecidedAt never decide which decision is latest.</summary>
  public long Sequence { get; set; }
  /// <summary>The caller's original intent. A retry with the same identity and payload returns this row; null only for legacy rows.</summary>
  public Guid? RequestId { get; set; }
  public string? RequestHash { get; set; }
  /// <summary>The latest decision on the same package version and composition that the decider saw, or null when there was none.</summary>
  public Guid? PreviousDecisionId { get; set; }
}

public sealed class FinancialPackageLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public string SourceAccountCode { get; set; } = string.Empty;
  public string DestinationCode { get; set; } = string.Empty;
  public string StatementSection { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public decimal RoundingResidual { get; set; }
  public decimal Fraction { get; set; }
  public string Currency { get; set; } = string.Empty;
  public Guid AdjustedSnapshotId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FinancialPackageValidation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public string Code { get; set; } = string.Empty;
  public bool Passed { get; set; }
  public string Detail { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FinancialPackageCashFlowLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public string Section { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  /// <summary>Noncash adjustments and investing/financing items are retained for disclosure and never enter the cash bridge.</summary>
  public bool IsNonCash { get; set; }
  /// <summary>Optional only for pre-lineage legacy rows; new package builds require all movement evidence fields.</summary>
  public DateOnly? MovementDate { get; set; }
  public string? ActivityCode { get; set; }
  public string[] SourceReferences { get; set; } = [];
  public string? ClassificationRationale { get; set; }
  public string Currency { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// An accepted general-ledger line that contributes part of its signed amount to one cash-flow movement of a sealed package.
/// Several movements may draw on one line, but their contributions may never exceed it. Append-only with the package.
/// </summary>
public sealed class FinancialPackageCashFlowAllocation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public Guid CashFlowLineId { get; set; }
  public Guid GeneralLedgerLineId { get; set; }
  public Guid GeneralLedgerTransactionId { get; set; }
  public Guid SourceImportBatchId { get; set; }
  /// <summary>Digest of the exact accepted source line this contribution was resolved against.</summary>
  public string SourceLineHash { get; set; } = string.Empty;
  public decimal SignedAmount { get; set; }
  public string Currency { get; set; } = string.Empty;
  /// <summary>The preparer who resolved this contribution. An independent reviewer can never be this user.</summary>
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Append-only marker closing source-allocation membership after the package build transaction.</summary>
public sealed class FinancialPackageCashFlowMembershipSeal
{
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public DateTimeOffset SealedAt { get; set; }
}

/// <summary>
/// An independent decision on the source-resolved cash-flow movements of one package. Append-only; the newest decision governs,
/// and an approval is valid only while the accepted general ledger it cites is still the current one.
/// </summary>
public sealed class FinancialPackageCashFlowSourceReview
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public string Decision { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public Guid SourceImportBatchId { get; set; }
  public Guid ReviewedByUserId { get; set; }
  public DateTimeOffset ReviewedAt { get; set; }
  /// <summary>The caller's original intent. Null only on rows written before intents were recorded; those rows never replay.</summary>
  public Guid? RequestId { get; set; }
  /// <summary>Digest of the decision payload, so the same intent with a different payload is refused.</summary>
  public string? RequestHash { get; set; }
  /// <summary>Digest of the exact resolved links the reviewer decided on.</summary>
  public string? LinkDigest { get; set; }
  /// <summary>The latest decision the reviewer saw when deciding. Null when this was the first decision.</summary>
  public Guid? PreviousReviewId { get; set; }
  /// <summary>Database-assigned order of commit. "Latest" decisions are ordered by this value, never by the application clock.</summary>
  public long Sequence { get; set; }
}

/// <summary>Exchange-rate effect on cash for one package; reconciles cash movements
/// to the opening/closing cash bridge without being folded into an activity section.</summary>
public sealed class FinancialPackageFxEffect
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public string CurrencyPair { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FinancialPackageDisclosure
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public string Code { get; set; } = string.Empty;
  public string Response { get; set; } = string.Empty;
  public bool NotApplicable { get; set; }
  public string? Rationale { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Typed statement-of-changes-in-equity line tied to one package.</summary>
public sealed class FinancialPackageEquityLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public string LineCode { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public decimal OpeningAmount { get; set; }
  public decimal ProfitOrLossAmount { get; set; }
  public decimal OciAmount { get; set; }
  public decimal CapitalMovementAmount { get; set; }
  public decimal DividendsAmount { get; set; }
  public decimal ClosingAmount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable identity of one paired, zero-sum transfer between two equity components in a package.</summary>
public sealed class FinancialPackageEquityTransfer
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public string TransferReference { get; set; } = string.Empty;
  public string SourceLineCode { get; set; } = string.Empty;
  public string DestinationLineCode { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Structured note amount used for note-to-face cross-casts.</summary>
public sealed class FinancialPackageNoteLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public string NoteCode { get; set; } = string.Empty;
  public string FaceDestinationCode { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// M25 two-digest seal: the content manifest hashes the immutable inputs before rendering,
/// and the artifact manifest hashes the completed ordered artifact identities afterwards.
/// The artifact digest is stored here rather than embedded in the artifacts themselves, so
/// no artifact ever claims to contain its own final hash.
/// </summary>
public sealed class FinancialPackageSeal
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public long PackageRevision { get; set; }
  public long PackageGeneration { get; set; }
  /// <summary>Package calculation hash captured at seal time.</summary>
  public string PackageHash { get; set; } = string.Empty;
  /// <summary>Digest over immutable source/statement/layout/note/policy/template inputs.</summary>
  public string ContentManifestDigest { get; set; } = string.Empty;
  /// <summary>Digest over the ordered artifact identities, byte hashes, sizes and renderer versions.</summary>
  public string ArtifactManifestDigest { get; set; } = string.Empty;
  public int ArtifactCount { get; set; }
  public long TotalByteLength { get; set; }
  /// <summary>Ordered artifact manifest JSON: version, byte length, SHA-256 and renderer versions.</summary>
  public string ArtifactManifestJson { get; set; } = "[]";
  public string SealVersion { get; set; } = "package-seal.v1";
  public Guid SealedByUserId { get; set; }
  public DateTimeOffset SealedAt { get; set; }
}
