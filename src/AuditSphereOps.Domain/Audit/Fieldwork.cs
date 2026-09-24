namespace AuditSphereOps.Domain.Audit;

/// <summary>Immutable source schedule version used by reconciliations and selections.</summary>
public sealed class AuditSchedule
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string ScheduleType { get; set; } = string.Empty;
  public string EntityIdentifier { get; set; } = string.Empty;
  public string SourceReceiptReference { get; set; } = string.Empty;
  public DateTimeOffset? AsOfDate { get; set; }
  public DateOnly? PeriodStart { get; set; }
  public DateOnly? PeriodEnd { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string SignConvention { get; set; } = string.Empty;
  public string SourceHash { get; set; } = string.Empty;
  public Guid? SourceImportBatchId { get; set; }
  /// <summary>The accepted R2R source revision this lead schedule was prepared against
  /// (a Module 21 source-acceptance decision). Null for a client-supplied schedule not
  /// tied to an accepted TB/GL revision.</summary>
  public Guid? AcceptedSourceDecisionId { get; set; }
  /// <summary>Identity hash of that accepted revision, captured at binding time.</summary>
  public string AcceptedSourceHash { get; set; } = string.Empty;
  public int RowCount { get; set; }
  public decimal SignedControlTotal { get; set; }
  public decimal GlControlTotal { get; set; }
  public decimal Residual { get; set; }
  public long InputGeneration { get; set; } = 1;
  public string CompletenessDecision { get; set; } = string.Empty;
  public string Status { get; set; } = AuditScheduleStatuses.PendingReview;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class AuditScheduleStatuses
{
  public const string PendingReview = "PENDING_REVIEW";
  public const string Reconciled = "RECONCILED";
  public const string Unreconciled = "UNRECONCILED";
  public const string Approved = "APPROVED";
  public const string Superseded = "SUPERSEDED";
}

public static class AuditBankReconciliationItemTypes
{
  public const string Ledger = "LEDGER";
  public const string Statement = "STATEMENT";
  public const string Timing = "TIMING";
  public const string ProposedCorrection = "PROPOSED_CORRECTION";

  public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
  {
    Ledger, Statement, Timing, ProposedCorrection
  };
}

public static class AuditBankReconciliationStatuses
{
  public const string Reconciled = "RECONCILED";
  public const string Unreconciled = "UNRECONCILED";
  public const string Approved = "APPROVED";
  public const string ChangesRequired = "CHANGES_REQUIRED";
}

public sealed class AuditBankReconciliation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid? ProcedureId { get; set; }
  public Guid LedgerScheduleId { get; set; }
  public Guid StatementScheduleId { get; set; }
  public DateOnly AsOfDate { get; set; }
  public string Currency { get; set; } = string.Empty;
  public decimal LedgerBalance { get; set; }
  public decimal StatementBalance { get; set; }
  public decimal TimingItemTotal { get; set; }
  public decimal ProposedCorrectionTotal { get; set; }
  public decimal Residual { get; set; }
  public long InputGeneration { get; set; } = 1;
  public string Status { get; set; } = AuditBankReconciliationStatuses.Unreconciled;
  public string? Conclusion { get; set; }
  public Guid CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class AuditBankReconciliationItem
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid BankReconciliationId { get; set; }
  public Guid? SourceScheduleId { get; set; }
  public Guid? ProposedJournalId { get; set; }
  public string StableItemId { get; set; } = string.Empty;
  public string ItemType { get; set; } = string.Empty;
  public decimal SignedAmount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public string SourceReference { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditScheduleRow
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ScheduleId { get; set; }
  public string StableRowId { get; set; } = string.Empty;
  public int SourceLineNumber { get; set; }
  public string AccountCode { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public decimal SignedAmount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public DateOnly? TransactionDate { get; set; }
  public DateOnly? PostingDate { get; set; }
  public DateOnly? DeliveryDate { get; set; }
  public DateOnly? ServiceDate { get; set; }
  public string OriginalValuesJson { get; set; } = "{}";
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditSelection
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid? ScheduleId { get; set; }
  public Guid? PopulationVersionId { get; set; }
  public Guid ProcedureId { get; set; }
  public string Method { get; set; } = string.Empty;
  public string Rationale { get; set; } = string.Empty;
  public int SelectedCount { get; set; }
  public decimal SelectedSignedTotal { get; set; }
  public string Status { get; set; } = AuditSelectionStatuses.Submitted;
  public long InputGeneration { get; set; } = 1;
  public Guid CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

public static class AuditSelectionStatuses
{
  public const string Submitted = "SUBMITTED";
  public const string Reviewed = "REVIEWED";
  public const string ChangesRequired = "CHANGES_REQUIRED";
}

public sealed class AuditSelectionItem
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid SelectionId { get; set; }
  public Guid? ScheduleRowId { get; set; }
  public string StableRowId { get; set; } = string.Empty;
  public decimal SignedAmount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string InclusionReason { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditItemTest
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid SelectionId { get; set; }
  public Guid SelectionItemId { get; set; }
  public Guid ProcedureId { get; set; }
  public long Revision { get; set; } = 1;
  public string WorkPerformed { get; set; } = string.Empty;
  public string EvidenceReferencesJson { get; set; } = "[]";
  public string Result { get; set; } = AuditItemTestResults.Pending;
  public decimal? ExceptionAmount { get; set; }
  public string? ContradictoryEvidence { get; set; }
  public string? FollowUp { get; set; }
  public long InputGeneration { get; set; } = 1;
  public Guid TestedByUserId { get; set; }
  public DateTimeOffset TestedAt { get; set; }
}

public sealed class AuditItemTestReview
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid SelectionItemId { get; set; }
  public Guid AuditItemTestId { get; set; }
  public long TestRevision { get; set; }
  public string Decision { get; set; } = AuditItemTestReviewDecisions.ChangesRequired;
  public string? Comment { get; set; }
  public Guid ReviewerUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class AuditItemTestReviewDecisions
{
  public const string Reviewed = "REVIEWED";
  public const string ChangesRequired = "CHANGES_REQUIRED";
}

public static class AuditItemTestResults
{
  public const string Pending = "PENDING";
  public const string Pass = "PASS";
  public const string Exception = "EXCEPTION";
  public const string Limitation = "LIMITATION";
}

/// <summary>Confirmation register shared by bank, customer, supplier, loan and related-party work.</summary>
public sealed class AuditConfirmationCase
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid? ProcedureId { get; set; }
  public string AreaCode { get; set; } = string.Empty;
  public string SourceRecordId { get; set; } = string.Empty;
  public decimal BookedAmount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public DateOnly ConfirmationDate { get; set; }
  public string Respondent { get; set; } = string.Empty;
  public string ContactValidationSource { get; set; } = string.Empty;
  public long InputGeneration { get; set; } = 1;
  public string Status { get; set; } = AuditConfirmationStatuses.Draft;
  public string? DispatchReference { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class AuditConfirmationStatuses
{
  public const string Draft = "DRAFT";
  public const string Approved = "APPROVED";
  public const string Dispatched = "DISPATCHED";
  public const string ResponseReceived = "RESPONSE_RECEIVED";
  public const string NoResponse = "NO_RESPONSE";
  public const string AlternativeRequired = "ALTERNATIVE_REQUIRED";
  public const string Closed = "CLOSED";
}

public sealed class AuditConfirmationResponse
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ConfirmationCaseId { get; set; }
  public long Revision { get; set; } = 1;
  public string Origin { get; set; } = string.Empty;
  public string Channel { get; set; } = string.Empty;
  public DateTimeOffset ReceivedAt { get; set; }
  public string ResponseReference { get; set; } = string.Empty;
  public decimal? ConfirmedAmount { get; set; }
  public decimal? DifferenceAmount { get; set; }
  public string AuthenticityAssessment { get; set; } = string.Empty;
  public string Decision { get; set; } = AuditConfirmationDecisions.Pending;
  public Guid CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class AuditConfirmationDecisions
{
  public const string Pending = "PENDING";
  public const string Agreed = "AGREED";
  public const string Difference = "DIFFERENCE";
  public const string NoResponse = "NO_RESPONSE";
  public const string AlternativeRequired = "ALTERNATIVE_REQUIRED";
}

public sealed class AuditAlternativeProcedure
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ConfirmationCaseId { get; set; }
  public string Purpose { get; set; } = string.Empty;
  public string EvidenceReferencesJson { get; set; } = "[]";
  public string Conclusion { get; set; } = string.Empty;
  public string Status { get; set; } = AuditAlternativeStatuses.Submitted;
  public Guid CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

public static class AuditAlternativeStatuses
{
  public const string Submitted = "SUBMITTED";
  public const string Reviewed = "REVIEWED";
}

/// <summary>
/// Typed area assessment envelope for ECL, analytics, going concern, subsequent events, disclosures,
/// valuation, depreciation, payroll, loans, tax, related parties and fraud work. The method-specific
/// input snapshot is retained as evidence; it is not an executable or user-defined form.
/// </summary>
public sealed class AuditAreaAssessment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid? ProcedureId { get; set; }
  public string AreaCode { get; set; } = string.Empty;
  public string AssessmentKind { get; set; } = string.Empty;
  public string MethodologyReference { get; set; } = string.Empty;
  public string InputSnapshotJson { get; set; } = "{}";
  public decimal? BookedAmount { get; set; }
  public decimal? AuditedAmount { get; set; }
  public decimal? ResidualAmount { get; set; }
  public decimal? VariancePercent { get; set; }
  public string? Currency { get; set; }
  public DateOnly? PeriodStart { get; set; }
  public DateOnly? PeriodEnd { get; set; }
  public string EvidenceReferencesJson { get; set; } = "[]";
  public long InputGeneration { get; set; } = 1;
  public long Revision { get; set; } = 1;
  public string Conclusion { get; set; } = string.Empty;
  public string Status { get; set; } = AuditAreaAssessmentStatuses.Submitted;
  public Guid CreatedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
}

public static class AuditAreaAssessmentStatuses
{
  public const string Submitted = "SUBMITTED";
  public const string Reviewed = "REVIEWED";
  public const string ChangesRequired = "CHANGES_REQUIRED";
}

public static class AuditAreaCodes
{
  public const string CashBank = "CASH_BANK";
  public const string Receivables = "RECEIVABLES";
  public const string Inventory = "INVENTORY";
  public const string Revenue = "REVENUE";
  public const string Payables = "PAYABLES";
  public const string FixedAssets = "FIXED_ASSETS";
  public const string Expenses = "EXPENSES";
  public const string Payroll = "PAYROLL";
  public const string Loans = "LOANS";
  public const string Equity = "EQUITY";
  public const string RelatedParties = "RELATED_PARTIES";
  public const string TaxStatutory = "TAX_STATUTORY";
  public const string JournalsFraud = "JOURNALS_FRAUD";
  public const string AnalyticalReview = "ANALYTICAL_REVIEW";
  public const string GoingConcern = "GOING_CONCERN";
  public const string SubsequentEvents = "SUBSEQUENT_EVENTS";
  public const string FinancialStatements = "FINANCIAL_STATEMENTS";
  public const string AuditDifferences = "AUDIT_DIFFERENCES";

  public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
  {
    CashBank, Receivables, Inventory, Revenue, Payables, FixedAssets, Expenses, Payroll, Loans,
    Equity, RelatedParties, TaxStatutory, JournalsFraud, AnalyticalReview, GoingConcern,
    SubsequentEvents, FinancialStatements, AuditDifferences
  };
}

public static class AuditAreaAssessmentKinds
{
  public const string AggregateDifferences = "AGGREGATE_DIFFERENCES";
}

public sealed class AuditDifference
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid? ProcedureId { get; set; }
  public string AccountArea { get; set; } = string.Empty;
  public string DifferenceType { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public string? MaterialityReference { get; set; }
  public string? QualitativeConcerns { get; set; }
  public decimal Amount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public bool Corrected { get; set; }
  public string? ManagementResponse { get; set; }
  public string? CorrectionReference { get; set; }
  public Guid? ProposedJournalId { get; set; }
  public long? ProposedJournalRevision { get; set; }
  public Guid? SourceReflectionReconciliationId { get; set; }
  public Guid? VerifiedAdjustedSnapshotId { get; set; }
  public string? CorrectionState { get; set; }
  public string? JournalImpactJson { get; set; }
  public string? JournalImpactHash { get; set; }
  public string? Evaluation { get; set; }
  public long InputGeneration { get; set; } = 1;
  public string Status { get; set; } = AuditDifferenceStatuses.Open;
  public Guid CreatedByUserId { get; set; }
  public Guid? EvaluatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? EvaluatedAt { get; set; }
}

public static class AuditDifferenceStatuses
{
  public const string Open = "OPEN";
  public const string ManagementResponded = "MANAGEMENT_RESPONDED";
  public const string Evaluated = "EVALUATED";
  public const string Corrected = "CORRECTED";
  public const string VerifiedReflected = "VERIFIED_REFLECTED";
}

public static class AuditDifferenceCorrectionStates
{
  public const string Proposed = "PROPOSED";
  public const string Agreed = "AGREED";
  public const string Rejected = "REJECTED";
  public const string AppliedInReporting = "APPLIED_IN_REPORTING";
  public const string ReportedPostedExternally = "REPORTED_POSTED_EXTERNALLY";
  public const string VerifiedReflected = "VERIFIED_REFLECTED";
}
