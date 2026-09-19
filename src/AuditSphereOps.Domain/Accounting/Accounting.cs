// Accounting: TB datasets, rows (immutable), mappings, journals, packages (§§16–18, 27.2, 42.1–42.6).
namespace AuditSphereOps.Domain.Accounting;

public sealed class TrialBalanceDataset
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string SourceKind { get; set; } = "Raw";     // Raw|Adjusted
  public long Revision { get; set; } = 1;
  public string Currency { get; set; } = string.Empty;
  public string Sha256Hex { get; set; } = string.Empty;
  public bool Balanced { get; set; }
  public string ValidationStatus { get; set; } = "Pending";
  public decimal ControlTotal { get; set; }
  public DateTimeOffset ImportedAt { get; set; }
  public Guid ImportedByUserId { get; set; }
}

public sealed class TrialBalanceRow
{
  public Guid Id { get; set; }
  public Guid DatasetId { get; set; }
  public string AccountCode { get; set; } = string.Empty;
  public string AccountName { get; set; } = string.Empty;
  public decimal Amount { get; set; }                 // signed, ≤6dp
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
  public string Status { get; set; } = "Draft";             // Draft|Posted|ReflectedInSource|Void
  public long Revision { get; set; } = 1;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
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
  public DateTimeOffset CreatedAt { get; set; }
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
