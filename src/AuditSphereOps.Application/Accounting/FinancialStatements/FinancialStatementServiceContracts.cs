using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record MappingAllocationInput(
  string SourceAccountCode,
  string DestinationCode,
  string StatementSection,
  decimal Fraction,
  string Rationale,
  string? AuditArea = null,
  string ResidualPolicy = "LAST_DESTINATION");

public sealed record CreateMappingVersionRequest(
  Guid DatasetId,
  string TaxonomyVersion,
  string PeriodStart,
  string PeriodEnd,
  IReadOnlyList<MappingAllocationInput> Allocations,
  Guid? ClientChartVersionId = null);

public sealed record BuildFinancialPackageRequest(
  Guid AdjustmentPlanId,
  Guid MappingVersionId,
  string Framework,
  string PeriodStart,
  string PeriodEnd,
  string TemplateVersion,
  FinancialSupplementaryInformation? SupplementaryInformation = null);

public sealed record FinancialSupplementaryInformation(
  decimal CashBeginning,
  decimal CashEnding,
  IReadOnlyList<CashFlowLineInput> CashFlowLines,
  IReadOnlyList<DisclosureInput> Disclosures,
  IReadOnlyList<EquityLineInput>? EquityLines = null,
  FinancialComparativeInput? Comparative = null,
  IReadOnlyList<NoteLineInput>? NoteLines = null,
  IReadOnlyList<FxCashEffectInput>? FxCashEffects = null,
  IReadOnlyList<string>? CashAccountCodes = null,
  IReadOnlyList<string>? OpeningCashEvidenceReferences = null,
  IReadOnlyList<string>? ClosingCashEvidenceReferences = null);

/// <summary>
/// One manually prepared cash-flow movement. Activity and evidence are recorded verbatim as preparer inputs;
/// they are not an approved accounting policy or an independently verified source assertion.
/// </summary>
public sealed record CashFlowLineInput(
  string Section,
  string Description,
  decimal Amount,
  bool IsNonCash = false,
  DateOnly? MovementDate = null,
  string? ActivityCode = null,
  IReadOnlyList<string>? SourceReferences = null,
  string? ClassificationRationale = null,
  // Accepted general-ledger lines that make up this movement. Null or empty keeps the movement a draft without lineage.
  IReadOnlyList<CashFlowSourceAllocationInput>? SourceAllocations = null);

/// <summary>The part of one accepted general-ledger line that contributes to a cash-flow movement. The sign follows the ledger line (debit-positive); the statement effect is projected separately.</summary>
public sealed record CashFlowSourceAllocationInput(Guid GeneralLedgerLineId, decimal SignedAmount);

/// <summary>
/// A resolved contribution: the movement's position in the request, the exact accepted source line and its identity. SignedAmount keeps
/// the ledger sign. PopulationComplete is true when the cash event this contribution draws on is fully explained, or it is a noncash link.
/// </summary>
internal sealed record ResolvedCashFlowSourceLink(int MovementIndex, Guid GeneralLedgerLineId, Guid GeneralLedgerTransactionId,
  Guid SourceImportBatchId, string SourceLineHash, decimal SignedAmount, string Currency, bool PopulationComplete);

public sealed record FxCashEffectInput(string CurrencyPair, decimal Amount, string EvidenceReference);

public sealed record DisclosureInput(string Code, string Response, bool NotApplicable = false, string? Rationale = null);

public sealed record EquityLineInput(
  string LineCode,
  string Description,
  decimal OpeningAmount,
  decimal ProfitOrLossAmount,
  decimal OciAmount,
  decimal CapitalMovementAmount,
  decimal DividendsAmount,
  decimal ClosingAmount,
  string EvidenceReference);

public sealed record FinancialComparativeInput(Guid PackageId, string Basis, string EvidenceReference);

public sealed record NoteLineInput(string NoteCode, string FaceDestinationCode, decimal Amount, string EvidenceReference);

public sealed record FinancialPackageBuildResult(
  Guid PackageId,
  Guid AdjustedSnapshotId,
  string Status,
  string CalculationHash,
  IReadOnlyDictionary<string, decimal> StatementTotals);

public sealed record FinancialStatementPackageArtifact(
  Guid PackageId,
  string CalculationHash,
  string ArtifactSha256Hex,
  byte[] ArtifactBytes,
  string RenderedText,
  Guid ArtifactId = default);

/// <summary>
/// Versioned mapping and deterministic financial-statement calculation over the
/// accepted TB/adjustment inputs. Rendering, disclosures and provider delivery stay
/// separate; missing supplementary information is persisted as an explicit review gate.
/// </summary>
