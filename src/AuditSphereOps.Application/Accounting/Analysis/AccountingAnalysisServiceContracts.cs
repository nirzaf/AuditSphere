using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record AccountingReconciliationRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, Guid? BookId, string Area,
  Guid? TrialBalanceDatasetId, Guid? ImportBatchId, IReadOnlyList<string> AccountCodes,
  DateOnly AsOfDate, string AgingBasis = "", string AgingBucketRuleVersion = "");

public sealed record ReconciliationItemInput(
  string StableItemId, decimal SignedAmount, string Currency, DateOnly? ItemDate,
  string Reason, string EvidenceReference, string Disposition, string DateBasis = "",
  string AgingBucket = "", bool? IsCredit = null, DateOnly? SettlementDate = null,
  string SettlementReference = "");

public sealed record ReconciliationProofDto(
  Guid ProofId,
  Guid ReconciliationId,
  decimal SourceTotal,
  decimal GlTotal,
  decimal ItemsSignedTotal,
  decimal Residual,
  bool IsReconciled,
  int ItemCount,
  string Status);

public sealed record EclAssessmentRequest(
  Guid ReconciliationId, DateOnly AsOfDate, string Method, string MethodologyVersion,
  decimal ProbabilityOfDefault, decimal LossGivenDefault, decimal ManagementOverlay,
  decimal ManagementExpectedLoss, string AssumptionsHash, decimal? BookedAmount = null,
  Guid? ProposedJournalId = null);

public sealed record InventoryValuationRequest(
  Guid ReconciliationId, DateOnly AsOfDate, decimal Quantity, decimal UnitCost,
  decimal NrvPerUnit, decimal ObsolescenceReserve, decimal BookAmount,
  string MethodologyVersion, string AssumptionsHash, Guid? ProposedJournalId = null);

public sealed record SpecialistScheduleRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, string Area, string MethodologyVersion,
  decimal OpeningAmount, decimal AdditionsAmount, decimal DisposalsAmount,
  decimal DepreciationAmount, decimal ImpairmentAmount, decimal InterestAmount,
  decimal CurrentPortion, decimal NonCurrentPortion, decimal CapitalMovement,
  decimal Dividends, decimal TaxPaid, decimal ManagementAmount, string AssumptionsHash,
  string EvidenceReference, string? DepreciationMethod = null, int? UsefulLifeMonths = null,
  decimal? PayrollGrossAmount = null, decimal? PayrollDeductionsAmount = null, decimal? PayrollNetAmount = null,
  string? PayrollContractReference = null, string? PayrollBankPaymentReference = null,
  decimal? LoanRepaymentAmount = null, DateOnly? LoanMaturityDate = null, string? LoanCovenantReference = null,
  decimal? EquityProfitOrLossAmount = null, decimal? EquityOciAmount = null, string? RelatedPartyDisclosureReference = null,
  string? TaxJurisdiction = null, string? TaxRuleVersion = null, decimal? TaxBaseAmount = null, decimal? TaxRate = null,
  string? TaxReturnEvidenceReference = null, string? TaxPaymentEvidenceReference = null, string? TaxCorrespondenceReference = null,
  string? ForecastOwner = null, DateOnly? ForecastHorizonEnd = null, decimal? ForecastCashInputAmount = null,
  decimal? ForecastDebtInputAmount = null, string? ForecastSensitivityReference = null,
  string? ForecastSensitivityResult = null);

public sealed record AnalyticalReviewRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, Guid? ComparisonPeriodId,
  string Area, string Measure, decimal CurrentAmount, decimal PriorAmount,
  decimal? BudgetAmount, string DenominatorBasis, string FormulaVersion,
  string Explanation, string Currency = AccountingDefaults.DefaultCurrency,
  string SeasonalityExplanation = "");

public sealed record AnalyticalReviewAggregateSummary(
  Guid? GroupId, Guid? PeriodId, string Currency, int ReviewCount, int ClientCount,
  decimal CurrentTotal, decimal PriorTotal);

public sealed record JournalRiskFlagRequest(
  Guid ClientId, Guid EngagementId, Guid ImportBatchId, Guid TransactionId,
  string RuleCode, string Reason, decimal Score, string EvidenceReference,
  bool SelectedForTesting = false, string ManagementExplanation = "", string CorroborationReference = "");

public sealed record JournalRiskAnalysisRequest(
  Guid ClientId, Guid EngagementId, Guid ImportBatchId, DateOnly YearEnd,
  decimal HighValueThreshold, int YearEndWindowDays = 5);

public sealed record JournalRiskCandidate(
  string CriteriaVersion, Guid TransactionId, string StableJournalId, DateOnly PostingDate,
  string RuleCode, string Reason, decimal Score, bool SourceOriginAvailable, decimal AbsoluteAmount);

public sealed record ReviewAccountingEvidenceRequest(
  string Kind, Guid EvidenceId, string Decision, string? Disposition = null, string? Conclusion = null,
  string? CorroborationReference = null);

public sealed record LinkAccountingEvidenceRequest(
  string Kind, Guid EvidenceId, Guid AuditProcedureResultId);

public sealed record GeneralLedgerLineProjection(
  Guid LineId, string JournalId, DateOnly PostingDate, string AccountCode,
  decimal Debit, decimal Credit, decimal FunctionalAmount, string Currency,
  DateOnly? DocumentDate = null, DateOnly? ServiceDate = null, string ReceiptReference = "");

public sealed record GeneralLedgerPage(
  IReadOnlyList<GeneralLedgerLineProjection> Rows, bool HasNextPage);

public sealed record GeneralLedgerCompletenessRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, Guid? BookId,
  Guid TrialBalanceDatasetId, Guid ImportBatchId, string EvidenceReference,
  Guid? OpeningTrialBalanceDatasetId = null);

public static class AccountingEvidenceKinds
{
  public const string Ecl = "ECL";
  public const string Inventory = "INVENTORY";
  public const string Specialist = "SPECIALIST";
  public const string Analytical = "ANALYTICAL";
  public const string JournalRisk = "JOURNAL_RISK";
}

public static class AccountingEvidenceReviewDecisions
{
  public const string Approved = "APPROVED";
  public const string ChangesRequired = "CHANGES_REQUIRED";
  public const string Rejected = "REJECTED";
  public const string Cleared = "CLEARED";
  public const string Escalated = "ESCALATED";
  public const string NotAnIssue = "NOT_AN_ISSUE";
}
