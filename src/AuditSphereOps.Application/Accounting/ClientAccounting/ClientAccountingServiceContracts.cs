using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientAccountingProfileRequest(
  Guid ClientId, string Jurisdiction, string FunctionalCurrency,
  int FiscalYearStartMonth, int FiscalYearStartDay, string SourceSystem,
  string SourceSystemIdentifier);

public sealed record ReportingPeriodRequest(
  Guid ClientId, string PeriodCode, DateOnly StartDate, DateOnly EndDate,
  string Basis, string Currency, Guid? PriorPeriodId = null);

public sealed record RollForwardPeriodRequest(
  Guid ClientId, Guid PriorPeriodId, string PeriodCode, DateOnly StartDate, DateOnly EndDate,
  string Basis, string Currency, string SourceHash, decimal PriorClosingAmount,
  decimal CurrentOpeningAmount, string EvidenceReference, Guid? SourcePackageId = null);

public sealed record ReportingBookRequest(
  Guid ClientId, Guid PeriodId, string Code, string Basis,
  string InclusionRule, string Currency);

public sealed record OpeningBalanceBridgeRequest(
  Guid ClientId, Guid CurrentPeriodId, Guid? PriorPeriodId, Guid? SourcePackageId,
  string SourceHash, decimal PriorClosingAmount, decimal CurrentOpeningAmount,
  string EvidenceReference);

public sealed record CreatePeriodRestatementRequest(
  Guid ClientId, Guid PeriodId, Guid OriginalPackageId, Guid RevisedPackageId,
  string RevisedBasis, string Reason, string EvidenceReference,
  string ChangeType = "", string AffectedPeriods = "");

/// <summary>Supported restatement treatments per the IAS 8 contract; estimate
/// changes use prospective treatment and never silently restate prior periods.</summary>
public static class PeriodRestatementChangeTypes
{
  public const string Reclassified = "RECLASSIFIED";
  public const string RestatedError = "RESTATED_ERROR";
  public const string PolicyTransition = "POLICY_TRANSITION";
  public const string ProspectiveEstimateChange = "PROSPECTIVE_ESTIMATE_CHANGE";
  public static readonly string[] All = [Reclassified, RestatedError, PolicyTransition, ProspectiveEstimateChange];
}

public sealed record ClientAccountInput(
  string StableIdentity, string AccountCode, string AccountName, string AccountType,
  string NormalBalance, bool IsPosting, string? ParentStableIdentity = null);

public sealed record SourceAccountAliasInput(
  Guid ClientAccountId, string SourceSystem, string AliasCode, string AliasName);

public sealed record ChartAccountViewDto(
  Guid Id, string StableIdentity, string AccountCode, string AccountName,
  string AccountType, string NormalBalance, bool IsPosting, Guid? ParentAccountId,
  string? ParentAccountCode, int ChildCount);

public sealed record ChartRevisionAccountsPage(
  IReadOnlyList<ChartAccountViewDto> Items, int TotalCount, int Page, int PageSize);

public sealed record AccountingDimensionInput(string DimensionType, string Code, string Name);

public sealed record TaxonomyNodeInput(
  string Code, string Name, string StatementSection, string DisplaySign,
  string NormalBalance, string DisclosureArea, bool IsPosting,
  string Applicability, string? ParentCode = null);

public sealed record TaxonomyOverlayRequest(
  Guid BaseTaxonomyVersionId, string Code, string Name, string OverlayScope, DateOnly EffectiveFrom);

public sealed record TaxonomyImpactItem(Guid Id, string Kind, string Status, string TaxonomyVersion);

public sealed record CapabilityProfileRequest(
  Guid? ClientId, Guid? GroupId, string ServiceKind, string Framework,
  string Edition, string PeriodRule, string ReportingCurrency,
  string AccountingMethod, string ConsolidationMethod, string ReviewHierarchy,
  string TemplateFamily, string ServiceRoute = "");

public sealed record GeneralLedgerLineInput(
  string StableLineId, string AccountCode, decimal Debit, decimal Credit,
  string OriginalCurrency, decimal OriginalAmount, decimal FunctionalAmount,
  string PartyIdentifier = "", string Branch = "", string CostCentre = "",
  string Department = "", string Project = "", string IntercompanyCounterparty = "");

public sealed record GeneralLedgerTransactionInput(
  string StableJournalId, string DocumentNumber, DateOnly PostingDate,
  DateOnly? DocumentDate, string SourceUser, string SourceSystem,
  string? ReversalReference, bool IsManual, bool IsYearEnd,
  IReadOnlyList<GeneralLedgerLineInput> Lines, DateOnly? ServiceDate = null);

public sealed record GeneralLedgerImportRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, Guid? BookId,
  string ProfileVersion, string ParserVersion, string RawFileSha256Hex,
  string LegalEntityKey, string Currency, string ReceiptReference,
  IReadOnlyList<GeneralLedgerTransactionInput> Transactions);

public sealed record GeneralLedgerImportStartRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, Guid? BookId,
  string ProfileVersion, string ParserVersion, string RawFileSha256Hex,
  string LegalEntityKey, string Currency, string ReceiptReference,
  int ExpectedChunkCount, int ExpectedTransactionCount, int ExpectedLineCount);

public sealed record GeneralLedgerImportChunkRequest(
  Guid ImportBatchId, int ChunkNumber, string ChunkDigest,
  IReadOnlyList<GeneralLedgerTransactionInput> Transactions, bool Finalize);

public sealed record GeneralLedgerImportBatchSummary(
  Guid ImportBatchId, string Status, int AcceptedChunkCount, int AcceptedTransactionCount,
  int AcceptedLineCount, int ExpectedChunkCount, int ExpectedTransactionCount,
  int ExpectedLineCount, string? NormalizedDatasetDigest);
