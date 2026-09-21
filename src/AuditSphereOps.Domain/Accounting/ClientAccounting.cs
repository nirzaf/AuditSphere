namespace AuditSphereOps.Domain.Accounting;

// Typed client-accounting and group-reporting records. These are deliberately
// separate from FirmAccount/FirmPeriod: the firm's books are not a client ERP.
public static class AccountingDefaults
{
  public const string DefaultCurrency = "QAR";
}

public static class AccountingWorkflowStates
{
  public const string Draft = "DRAFT";
  public const string Submitted = "SUBMITTED";
  public const string Approved = "APPROVED";
  public const string Active = "ACTIVE";
  public const string Closed = "CLOSED";
  public const string Retired = "RETIRED";
  public const string Stale = "STALE";
  public const string Rejected = "REJECTED";
}

public static class AccountingCapabilityAcceptanceStages
{
  public const string LocalConstruction = "LOCAL_CONSTRUCTION";
  public const string MethodOwnerApproval = "METHOD_OWNER_APPROVAL";
  public const string LiveEvidence = "LIVE_EVIDENCE";
  public const string Released = "RELEASED";
}

public static class AccountingCapabilityServiceKinds
{
  public const string EntityReporting = "ENTITY_REPORTING";
  public const string GroupReporting = "GROUP_REPORTING";
  public const string AuditOnly = "AUDIT_ONLY";
}

public sealed class AccountingCapabilityProfile
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid? ClientId { get; set; }
  public Guid? GroupId { get; set; }
  public string ServiceKind { get; set; } = string.Empty; // ENTITY_REPORTING | GROUP_REPORTING | AUDIT_ONLY
  public string Framework { get; set; } = string.Empty;
  public string Edition { get; set; } = string.Empty;
  public string PeriodRule { get; set; } = string.Empty;
  public string ReportingCurrency { get; set; } = AccountingDefaults.DefaultCurrency;
  public string AccountingMethod { get; set; } = string.Empty;
  public string ConsolidationMethod { get; set; } = string.Empty;
  public string ReviewHierarchy { get; set; } = string.Empty;
  public string TemplateFamily { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public string AcceptanceState { get; set; } = "LOCAL_CONSTRUCTION";
  public long Revision { get; set; } = 1;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AccountingCapabilityAcceptance
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid CapabilityProfileId { get; set; }
  public string Stage { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Submitted;
  public string EvidenceReference { get; set; } = string.Empty;
  public Guid? DecidedByUserId { get; set; }
  public DateTimeOffset? DecidedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClientAccountingProfile
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public string Jurisdiction { get; set; } = string.Empty;
  public string FunctionalCurrency { get; set; } = AccountingDefaults.DefaultCurrency;
  public int FiscalYearStartMonth { get; set; } = 1;
  public int FiscalYearStartDay { get; set; } = 1;
  public string SourceSystem { get; set; } = string.Empty;
  public string SourceSystemIdentifier { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public long Revision { get; set; } = 1;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClientGroup
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string Code { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Active;
  public long Revision { get; set; } = 1;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClientGroupMembership
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid GroupId { get; set; }
  public Guid ClientId { get; set; }
  public DateOnly EffectiveFrom { get; set; }
  public DateOnly? EffectiveTo { get; set; }
  public string ControlMethod { get; set; } = string.Empty;
  public decimal OwnershipPercent { get; set; }
  public decimal EconomicInterestPercent { get; set; }
  public string EvidenceReference { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public long Revision { get; set; } = 1;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class GroupAccessGrant
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid GroupId { get; set; }
  public Guid UserId { get; set; }
  public string Role { get; set; } = string.Empty;
  public DateTimeOffset GrantedAt { get; set; }
  public Guid GrantedByUserId { get; set; }
  public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class ClientReportingPeriod
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public string PeriodCode { get; set; } = string.Empty;
  public DateOnly StartDate { get; set; }
  public DateOnly EndDate { get; set; }
  public string Basis { get; set; } = string.Empty;
  public string Currency { get; set; } = AccountingDefaults.DefaultCurrency;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public Guid? PriorPeriodId { get; set; }
  public long Revision { get; set; } = 1;
  public Guid? ClosedByUserId { get; set; }
  public DateTimeOffset? ClosedAt { get; set; }
  public string? CloseReason { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable record of an authorized reopen that creates a new period revision.</summary>
public sealed class ClientPeriodAmendment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid PeriodId { get; set; }
  public long PreviousRevision { get; set; }
  public long AmendmentRevision { get; set; }
  public string Reason { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClientReportingBook
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid PeriodId { get; set; }
  public string Code { get; set; } = string.Empty;
  public string Basis { get; set; } = string.Empty;
  public string InclusionRule { get; set; } = string.Empty;
  public string Currency { get; set; } = AccountingDefaults.DefaultCurrency;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public long Revision { get; set; } = 1;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OpeningBalanceBridge
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid CurrentPeriodId { get; set; }
  public Guid? PriorPeriodId { get; set; }
  public Guid? SourcePackageId { get; set; }
  public string SourceHash { get; set; } = string.Empty;
  public decimal PriorClosingAmount { get; set; }
  public decimal CurrentOpeningAmount { get; set; }
  public decimal Residual { get; set; }
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public string EvidenceReference { get; set; } = string.Empty;
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable link between an issued package and an independently approved restatement.</summary>
public sealed class ClientPeriodRestatement
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PeriodId { get; set; }
  public Guid OriginalPackageId { get; set; }
  public Guid RevisedPackageId { get; set; }
  public string OriginalPackageHash { get; set; } = string.Empty;
  public string RevisedPackageHash { get; set; } = string.Empty;
  public string RevisedBasis { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Submitted;
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClientChartVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public int Version { get; set; }
  public string SourceScope { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public DateOnly EffectiveFrom { get; set; }
  public DateOnly? EffectiveTo { get; set; }
  public Guid CreatedByUserId { get; set; }
  public Guid? PublishedByUserId { get; set; }
  public DateTimeOffset? PublishedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClientAccount
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid ChartVersionId { get; set; }
  public string StableIdentity { get; set; } = string.Empty;
  public string AccountCode { get; set; } = string.Empty;
  public string AccountName { get; set; } = string.Empty;
  public string AccountType { get; set; } = string.Empty;
  public string NormalBalance { get; set; } = string.Empty;
  public Guid? ParentAccountId { get; set; }
  public bool IsPosting { get; set; } = true;
  public string Status { get; set; } = AccountingWorkflowStates.Active;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SourceAccountAlias
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid ChartVersionId { get; set; }
  public Guid ClientAccountId { get; set; }
  public string SourceSystem { get; set; } = string.Empty;
  public string AliasCode { get; set; } = string.Empty;
  public string AliasName { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ReportingTaxonomyVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string Code { get; set; } = string.Empty;
  public string Framework { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public DateOnly EffectiveFrom { get; set; }
  public DateOnly? EffectiveTo { get; set; }
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ReportingTaxonomyNode
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid TaxonomyVersionId { get; set; }
  public string Code { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public Guid? ParentNodeId { get; set; }
  public string StatementSection { get; set; } = string.Empty;
  public string DisplaySign { get; set; } = string.Empty;
  public string NormalBalance { get; set; } = string.Empty;
  public string DisclosureArea { get; set; } = string.Empty;
  public bool IsPosting { get; set; } = true;
  public string Applicability { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SourceImportBatch
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PeriodId { get; set; }
  public Guid? BookId { get; set; }
  public string SourceKind { get; set; } = string.Empty; // GL | TB | SCHEDULE
  public string ProfileVersion { get; set; } = string.Empty;
  public string ParserVersion { get; set; } = string.Empty;
  public string RawFileSha256Hex { get; set; } = string.Empty;
  public string NormalizedDatasetDigest { get; set; } = string.Empty;
  public string LegalEntityKey { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  public int RowCount { get; set; }
  public int ExpectedChunkCount { get; set; }
  public int ExpectedTransactionCount { get; set; }
  public int ExpectedLineCount { get; set; }
  public int AcceptedChunkCount { get; set; }
  public int AcceptedTransactionCount { get; set; }
  public int AcceptedLineCount { get; set; }
  public string Status { get; set; } = "LOADING";
  public string ReceiptReference { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class AccountingDimensionTypes
{
  public const string Branch = "BRANCH";
  public const string CostCentre = "COST_CENTRE";
  public const string Department = "DEPARTMENT";
  public const string Project = "PROJECT";
  public const string IntercompanyCounterparty = "INTERCOMPANY_COUNTERPARTY";

  public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
  {
    Branch, CostCentre, Department, Project, IntercompanyCounterparty
  };
}

public sealed class ClientAccountingDimensionDefinition
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public string DimensionType { get; set; } = string.Empty;
  public string Code { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Active;
  public long Revision { get; set; } = 1;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Idempotency receipt for one bounded GL chunk in a loading source batch.</summary>
public sealed class GeneralLedgerImportChunk
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ImportBatchId { get; set; }
  public int ChunkNumber { get; set; }
  public string ChunkDigest { get; set; } = string.Empty;
  public int TransactionCount { get; set; }
  public int LineCount { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class GeneralLedgerTransaction
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ImportBatchId { get; set; }
  public string StableJournalId { get; set; } = string.Empty;
  public string DocumentNumber { get; set; } = string.Empty;
  public DateOnly PostingDate { get; set; }
  public DateOnly? DocumentDate { get; set; }
  public DateOnly? ServiceDate { get; set; }
  public string SourceUser { get; set; } = string.Empty;
  public string SourceSystem { get; set; } = string.Empty;
  public string? ReversalReference { get; set; }
  public string Currency { get; set; } = string.Empty;
  public bool IsManual { get; set; }
  public bool IsYearEnd { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class GeneralLedgerLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ImportBatchId { get; set; }
  public Guid TransactionId { get; set; }
  public string StableLineId { get; set; } = string.Empty;
  public string AccountCode { get; set; } = string.Empty;
  public Guid? ClientAccountId { get; set; }
  public decimal Debit { get; set; }
  public decimal Credit { get; set; }
  public string OriginalCurrency { get; set; } = string.Empty;
  public decimal OriginalAmount { get; set; }
  public decimal FunctionalAmount { get; set; }
  public string PartyIdentifier { get; set; } = string.Empty;
  public string Branch { get; set; } = string.Empty;
  public string CostCentre { get; set; } = string.Empty;
  public string Department { get; set; } = string.Empty;
  public string Project { get; set; } = string.Empty;
  public string IntercompanyCounterparty { get; set; } = string.Empty;
  public bool IsManual { get; set; }
  public bool IsYearEnd { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class GeneralLedgerCompletenessBridge
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PeriodId { get; set; }
  public Guid? BookId { get; set; }
  public Guid TrialBalanceDatasetId { get; set; }
  public Guid ImportBatchId { get; set; }
  public Guid? OpeningTrialBalanceDatasetId { get; set; }
  public string TrialBalanceHash { get; set; } = string.Empty;
  public string GeneralLedgerHash { get; set; } = string.Empty;
  public string OpeningTrialBalanceHash { get; set; } = string.Empty;
  public string AccountResidualDigest { get; set; } = string.Empty;
  public string OpeningMovementResidualDigest { get; set; } = string.Empty;
  public int TrialBalanceAccountCount { get; set; }
  public int GeneralLedgerAccountCount { get; set; }
  public int MatchedAccountCount { get; set; }
  public int MismatchedAccountCount { get; set; }
  public int OpeningMovementMismatchedAccountCount { get; set; }
  public int JournalExceptionCount { get; set; }
  public decimal OpeningAmount { get; set; }
  public decimal MovementAmount { get; set; }
  public decimal ClosingAmount { get; set; }
  public decimal OpeningMovementResidual { get; set; }
  public decimal AbsoluteResidual { get; set; }
  public DateOnly CoverageStart { get; set; }
  public DateOnly CoverageEnd { get; set; }
  public string Status { get; set; } = "UNRECONCILED";
  public bool IncompleteExtract { get; set; }
  public string CompletenessDisclosure { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class AccountingReconciliation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PeriodId { get; set; }
  public Guid? BookId { get; set; }
  public string Area { get; set; } = string.Empty;
  public Guid? TrialBalanceDatasetId { get; set; }
  public Guid? ImportBatchId { get; set; }
  public string AccountSelection { get; set; } = string.Empty;
  public DateOnly AsOfDate { get; set; }
  public decimal SourceTotal { get; set; }
  public decimal GlTotal { get; set; }
  public decimal Residual { get; set; }
  public string SourceHash { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public long InputGeneration { get; set; } = 1;
  public Guid CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class AccountingReconciliationItem
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ReconciliationId { get; set; }
  public string StableItemId { get; set; } = string.Empty;
  public decimal SignedAmount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public DateOnly? ItemDate { get; set; }
  public int? AgeDays { get; set; }
  public string Reason { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public string Disposition { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EclAssessment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ReconciliationId { get; set; }
  public string ReconciliationSourceHash { get; set; } = string.Empty;
  public long InputGeneration { get; set; } = 1;
  public int Version { get; set; } = 1;
  public DateOnly AsOfDate { get; set; }
  public string Method { get; set; } = string.Empty;
  public string MethodologyVersion { get; set; } = string.Empty;
  public decimal EligibleExposure { get; set; }
  public decimal ProbabilityOfDefault { get; set; }
  public decimal LossGivenDefault { get; set; }
  public decimal ManagementOverlay { get; set; }
  public decimal CalculatedExpectedLoss { get; set; }
  public decimal ManagementExpectedLoss { get; set; }
  public decimal Difference { get; set; }
  public string AssumptionsHash { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public Guid CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class InventoryValuationAssessment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ReconciliationId { get; set; }
  public string ReconciliationSourceHash { get; set; } = string.Empty;
  public long InputGeneration { get; set; } = 1;
  public int Version { get; set; } = 1;
  public DateOnly AsOfDate { get; set; }
  public decimal Quantity { get; set; }
  public decimal UnitCost { get; set; }
  public decimal NrvPerUnit { get; set; }
  public decimal ObsolescenceReserve { get; set; }
  public decimal BookAmount { get; set; }
  public decimal CalculatedAmount { get; set; }
  public decimal Difference { get; set; }
  public string MethodologyVersion { get; set; } = string.Empty;
  public string AssumptionsHash { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public Guid CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class SpecialistAccountingSchedule
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PeriodId { get; set; }
  public long InputGeneration { get; set; } = 1;
  public string Area { get; set; } = string.Empty; // ASSETS | PAYROLL | LOANS | EQUITY | TAX | FORECAST
  public string MethodologyVersion { get; set; } = string.Empty;
  public string DepreciationMethod { get; set; } = string.Empty;
  public int? UsefulLifeMonths { get; set; }
  public decimal? PayrollGrossAmount { get; set; }
  public decimal? PayrollDeductionsAmount { get; set; }
  public decimal? PayrollNetAmount { get; set; }
  public string PayrollContractReference { get; set; } = string.Empty;
  public string PayrollBankPaymentReference { get; set; } = string.Empty;
  public decimal? LoanRepaymentAmount { get; set; }
  public DateOnly? LoanMaturityDate { get; set; }
  public string LoanCovenantReference { get; set; } = string.Empty;
  public decimal? EquityProfitOrLossAmount { get; set; }
  public decimal? EquityOciAmount { get; set; }
  public string RelatedPartyDisclosureReference { get; set; } = string.Empty;
  public string TaxJurisdiction { get; set; } = string.Empty;
  public string TaxRuleVersion { get; set; } = string.Empty;
  public decimal? TaxBaseAmount { get; set; }
  public decimal? TaxRate { get; set; }
  public string TaxReturnEvidenceReference { get; set; } = string.Empty;
  public string TaxPaymentEvidenceReference { get; set; } = string.Empty;
  public string TaxCorrespondenceReference { get; set; } = string.Empty;
  public string ForecastOwner { get; set; } = string.Empty;
  public DateOnly? ForecastHorizonEnd { get; set; }
  public decimal? ForecastCashInputAmount { get; set; }
  public decimal? ForecastDebtInputAmount { get; set; }
  public string ForecastSensitivityReference { get; set; } = string.Empty;
  public string ForecastSensitivityResult { get; set; } = string.Empty;
  public decimal OpeningAmount { get; set; }
  public decimal AdditionsAmount { get; set; }
  public decimal DisposalsAmount { get; set; }
  public decimal DepreciationAmount { get; set; }
  public decimal ImpairmentAmount { get; set; }
  public decimal InterestAmount { get; set; }
  public decimal CurrentPortion { get; set; }
  public decimal NonCurrentPortion { get; set; }
  public decimal CapitalMovement { get; set; }
  public decimal Dividends { get; set; }
  public decimal TaxPaid { get; set; }
  public decimal ManagementAmount { get; set; }
  public decimal CalculatedAmount { get; set; }
  public decimal ClosingAmount { get; set; }
  public decimal Difference { get; set; }
  public string AssumptionsHash { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public string ReviewConclusion { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public Guid CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class AnalyticalReview
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PeriodId { get; set; }
  public long InputGeneration { get; set; } = 1;
  public Guid? ComparisonPeriodId { get; set; }
  public string Area { get; set; } = string.Empty;
  public string Measure { get; set; } = string.Empty;
  public decimal CurrentAmount { get; set; }
  public decimal PriorAmount { get; set; }
  public decimal? BudgetAmount { get; set; }
  public decimal? Ratio { get; set; }
  public string Currency { get; set; } = AccountingDefaults.DefaultCurrency;
  public string DenominatorBasis { get; set; } = string.Empty;
  public string FormulaVersion { get; set; } = string.Empty;
  public string MovementFlags { get; set; } = string.Empty;
  public string SeasonalityExplanation { get; set; } = string.Empty;
  public string InputSnapshotJson { get; set; } = string.Empty;
  public string InputHash { get; set; } = string.Empty;
  public string Explanation { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public Guid CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class JournalRiskFlag
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ImportBatchId { get; set; }
  public Guid TransactionId { get; set; }
  public string RuleCode { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public decimal Score { get; set; }
  public bool SelectedForTesting { get; set; }
  public string ManagementExplanation { get; set; } = string.Empty;
  public string CorroborationReference { get; set; } = string.Empty;
  public string Status { get; set; } = "OPEN";
  public string Disposition { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public Guid? CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

/// <summary>Typed link from accounting evidence to the immutable audit-procedure result that supports it.</summary>
public sealed class AccountingEvidenceAuditLink
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string EvidenceKind { get; set; } = string.Empty;
  public Guid EvidenceId { get; set; }
  public Guid AuditProcedureResultId { get; set; }
  public Guid LinkedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ConsolidationScopeVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid GroupId { get; set; }
  public long GroupRevision { get; set; } = 1;
  public Guid PeriodId { get; set; }
  public Guid? PriorScopeVersionId { get; set; }
  public string OpeningRunHash { get; set; } = string.Empty;
  public string OpeningTranslationManifestHash { get; set; } = string.Empty;
  public decimal OpeningTranslationReserve { get; set; }
  public string RecurringEliminationManifest { get; set; } = string.Empty;
  public int Version { get; set; } = 1;
  public string ReportingCurrency { get; set; } = AccountingDefaults.DefaultCurrency;
  public string Method { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public string OpeningBasis { get; set; } = string.Empty;
  public Guid? ExchangeRateSetVersionId { get; set; }
  public Guid? TranslationPolicyVersionId { get; set; }
  public DateOnly? TranslationRateDate { get; set; }
  public string TranslationRateType { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ConsolidationComponent
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid GroupId { get; set; }
  public Guid ScopeVersionId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PackageId { get; set; }
  public string PackageHash { get; set; } = string.Empty;
  public string PeriodBasis { get; set; } = string.Empty;
  public string TaxonomyVersion { get; set; } = string.Empty;
  public string MappingVersion { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  public decimal OwnershipPercent { get; set; }
  public string ControlMethod { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Submitted;
  public Guid SubmittedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset SubmittedAt { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
}

public sealed class OwnershipInterestVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid GroupId { get; set; }
  public Guid ScopeVersionId { get; set; }
  public Guid ParentClientId { get; set; }
  public Guid ChildClientId { get; set; }
  public DateOnly EffectiveFrom { get; set; }
  public DateOnly? EffectiveTo { get; set; }
  public decimal OwnershipPercent { get; set; }
  public decimal EconomicInterestPercent { get; set; }
  public string ControlAssessment { get; set; } = string.Empty;
  public string Method { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IntercompanyMatch
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid GroupId { get; set; }
  public Guid ScopeVersionId { get; set; }
  public Guid SellerClientId { get; set; }
  public Guid BuyerClientId { get; set; }
  public string AccountNature { get; set; } = string.Empty;
  public string SellerTaxonomyCode { get; set; } = string.Empty;
  public string BuyerTaxonomyCode { get; set; } = string.Empty;
  public string PeriodCode { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  public string TransactionReference { get; set; } = string.Empty;
  public decimal SellerAmount { get; set; }
  public decimal BuyerAmount { get; set; }
  public decimal MatchedAmount { get; set; }
  public decimal Difference { get; set; }
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public string DifferenceReason { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class ConsolidationJournal
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid GroupId { get; set; }
  public Guid ScopeVersionId { get; set; }
  public string JournalNumber { get; set; } = string.Empty;
  public string JournalType { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  public decimal TotalDebits { get; set; }
  public decimal TotalCreditsAbs { get; set; }
  public string EvidenceReference { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
}

public sealed class ConsolidationJournalLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid GroupId { get; set; }
  public Guid ScopeVersionId { get; set; }
  public Guid ConsolidationJournalId { get; set; }
  public Guid? IntercompanyMatchId { get; set; }
  public string TaxonomyCode { get; set; } = string.Empty;
  public decimal Debit { get; set; }
  public decimal Credit { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ConsolidationRun
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid GroupId { get; set; }
  public Guid ScopeVersionId { get; set; }
  public string EngineVersion { get; set; } = string.Empty;
  public string InputManifest { get; set; } = string.Empty;
  public string RunHash { get; set; } = string.Empty;
  public string ReportingCurrency { get; set; } = string.Empty;
  public decimal SignedTotal { get; set; }
  public string Status { get; set; } = AccountingWorkflowStates.Submitted;
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
}

public sealed class ConsolidationRunLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid GroupId { get; set; }
  public Guid ScopeVersionId { get; set; }
  public Guid RunId { get; set; }
  public Guid? ComponentId { get; set; }
  public Guid? SourceLineId { get; set; }
  public Guid? IntercompanyMatchId { get; set; }
  public Guid? ConsolidationJournalId { get; set; }
  public string TaxonomyCode { get; set; } = string.Empty;
  public decimal ComponentAmount { get; set; }
  public decimal AlignmentAmount { get; set; }
  public decimal EliminationAmount { get; set; }
  public decimal ConsolidatedAmount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ExchangeRateSetVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string Code { get; set; } = string.Empty;
  public string Source { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
}

public sealed class ExchangeRate
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid RateSetVersionId { get; set; }
  public string FromCurrency { get; set; } = string.Empty;
  public string ToCurrency { get; set; } = string.Empty;
  public DateOnly RateDate { get; set; }
  public string RateType { get; set; } = string.Empty;
  public decimal Rate { get; set; }
  public string Direction { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TranslationPolicyVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string Code { get; set; } = string.Empty;
  public string FunctionalCurrency { get; set; } = string.Empty;
  public string PresentationCurrency { get; set; } = string.Empty;
  public string ClosingRateRule { get; set; } = string.Empty;
  public string AverageRateRule { get; set; } = string.Empty;
  public string HistoricalRateRule { get; set; } = string.Empty;
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
}

public sealed class TranslationResult
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid GroupId { get; set; }
  public Guid ScopeVersionId { get; set; }
  public Guid ComponentId { get; set; }
  public Guid RateSetVersionId { get; set; }
  public Guid TranslationPolicyVersionId { get; set; }
  public string? SourcePackageHash { get; set; }
  public DateOnly? RateDate { get; set; }
  public string RateType { get; set; } = string.Empty;
  public decimal? AppliedRate { get; set; }
  public string FromCurrency { get; set; } = string.Empty;
  public string ToCurrency { get; set; } = string.Empty;
  public decimal TranslatedAmount { get; set; }
  public decimal TranslationReserve { get; set; }
  public string Status { get; set; } = AccountingWorkflowStates.Draft;
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
