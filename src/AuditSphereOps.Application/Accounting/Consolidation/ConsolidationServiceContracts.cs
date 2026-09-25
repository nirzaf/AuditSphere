using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientGroupRequest(string Code, string Name);

public sealed record GroupMembershipRequest(
  Guid GroupId, Guid ClientId, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
  string ControlMethod, decimal OwnershipPercent, decimal EconomicInterestPercent,
  string EvidenceReference);

public sealed record OwnershipInterestRequest(
  Guid ScopeVersionId, Guid ParentClientId, Guid ChildClientId,
  DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal OwnershipPercent,
  decimal EconomicInterestPercent, string ControlAssessment, string Method,
  string EvidenceReference);

public sealed record ConsolidationScopeRequest(
  Guid GroupId, Guid PeriodId, string ReportingCurrency, string Method, string OpeningBasis,
  Guid? ExchangeRateSetVersionId = null, Guid? TranslationPolicyVersionId = null,
  DateOnly? TranslationRateDate = null, string TranslationRateType = "", Guid? PriorScopeVersionId = null);

public sealed record ConsolidationComponentRequest(
  Guid ScopeVersionId, Guid ClientId, Guid EngagementId, Guid PackageId,
  decimal OwnershipPercent, string ControlMethod, string PeriodBasis,
  string TaxonomyVersion, string MappingVersion);

public sealed record ExternalComponentPackLineRequest(
  string TaxonomyCode, decimal Amount, string Currency, string SourceLineReference);

public sealed record ExternalComponentPackRequest(
  Guid ScopeVersionId, Guid ClientId, Guid EngagementId, string PeriodStart, string PeriodEnd,
  string Framework, string ReportingCurrency, string PeriodBasis, string TaxonomyVersion,
  string MappingVersion, string SourceReference, string RawSourceHash, string NormalizedSourceDigest,
  decimal DeclaredSignedTotal, IReadOnlyList<ExternalComponentPackLineRequest> Lines,
  Guid? PriorPackId = null, string CompatibilityBridgeReference = "");

public sealed record ExternalComponentReconciliationRequest(Guid PackId, string EvidenceReference);

public sealed record ExternalComponentRequest(Guid ScopeVersionId, Guid ExternalComponentPackId,
  decimal OwnershipPercent = 100m, string ControlMethod = "CONTROLLED");

public sealed record IntercompanyMatchRequest(
  Guid ScopeVersionId, Guid SellerClientId, Guid BuyerClientId,
  string AccountNature, string PeriodCode, string Currency, string TransactionReference,
  decimal SellerAmount, decimal BuyerAmount, decimal MatchedAmount, string EvidenceReference,
  string SellerTaxonomyCode = "", string BuyerTaxonomyCode = "", string DifferenceReason = "",
  string MatchMode = IntercompanyMatchModes.OneToOne, string MatchGroupReference = "", bool OutsidePerimeterReview = false);

public sealed record ConsolidationJournalLineInput(
  Guid? IntercompanyMatchId, string TaxonomyCode, decimal Debit, decimal Credit, string Description);

public sealed record ConsolidationJournalRequest(
  Guid ScopeVersionId, string JournalNumber, string JournalType, string Currency,
  string EvidenceReference, IReadOnlyList<ConsolidationJournalLineInput> Lines);

public sealed record ConsolidationReportLine(
  string Component, string TaxonomyCode, decimal ComponentAmount, decimal AlignmentAmount,
  decimal EliminationAmount, decimal ConsolidatedAmount, string Currency);

public sealed record ConsolidationReport(
  Guid ScopeVersionId, Guid? RunId, string State, DateTimeOffset? ApprovedAt,
  IReadOnlyList<ConsolidationReportLine> Lines);

public sealed record AdvancedConsolidationMethodScheduleRequest(
  Guid ScopeVersionId, string Method, string Framework, string SourceManifestJson, string InputSnapshotJson);

public static class ConsolidationEliminationKinds
{
  public const string ReceivablePayable = "RECEIVABLE_PAYABLE";
  public const string RevenueExpense = "REVENUE_EXPENSE";
  public const string Dividend = "DIVIDEND";
  public const string InvestmentEquity = "INVESTMENT_EQUITY";
  public const string GroupJournal = "GROUP_JOURNAL";

  public static bool IsIntercompany(string value) => value is ReceivablePayable or RevenueExpense or Dividend or InvestmentEquity;
  public static bool IsRunKind(string value) => string.IsNullOrEmpty(value) || value == GroupJournal || IsIntercompany(value);
}


internal static class ConsolidationScopeGuards
{
  public static Task<bool> IsCurrentAsync(
    IClientAccountingDbContext db, Guid firmId, Guid groupId, long pinnedRevision, CancellationToken ct) =>
    db.ClientGroups.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.Id == groupId && x.Revision == pinnedRevision, ct);
}
