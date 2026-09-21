// Accounting: TB datasets, rows (immutable), mappings, journals, packages (§§16–18, 27.2, 42.1–42.6).
namespace AuditSphereOps.Domain.Accounting;

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
  public string Status { get; set; } = "Draft";             // Draft|Posted|ReflectedInSource|Void
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

public static class FinancialPackageArtifactVersions
{
  public const string Text = "financial-package-text.v1";
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
  public string Stage { get; set; } = string.Empty;
  public string Decision { get; set; } = string.Empty;
  public string EvidenceMode { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public string Comment { get; set; } = string.Empty;
  public Guid? DecidedByUserId { get; set; }
  public DateTimeOffset DecidedAt { get; set; }
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
  public string Currency { get; set; } = string.Empty;
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
