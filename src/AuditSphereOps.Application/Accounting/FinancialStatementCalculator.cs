using System.Globalization;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Accounting;

/// <summary>
/// Pure financial-package calculations. It accepts snapshots and value objects only;
/// it performs no EF, network, clock or random work. The command service owns loading,
/// authorization and the final transaction boundary.
/// </summary>
public static class FinancialStatementCalculator
{
  public const string CalculationEngineVersion = "auditsphere.fs-engine.v1";

  public sealed record PackageLine(
    string SourceAccountCode,
    string DestinationCode,
    string StatementSection,
    decimal Amount,
    decimal Fraction,
    string Currency,
    decimal RoundingResidual);

  public static IReadOnlyList<PackageLine> BuildPackageLines(
    IReadOnlyDictionary<string, decimal> balances,
    IReadOnlyCollection<MappingAllocationInput> allocations,
    string currency)
  {
    var lines = new List<PackageLine>();
    foreach (var balance in balances.OrderBy(x => x.Key, StringComparer.Ordinal))
    {
      var accountAllocations = allocations.Where(x => x.SourceAccountCode == balance.Key)
        .OrderBy(x => x.DestinationCode, StringComparer.Ordinal).ToArray();
      var allocated = 0m;
      for (var index = 0; index < accountAllocations.Length; index++)
      {
        var allocation = accountAllocations[index];
        var independentlyRounded = MoneyPolicy.Normalize(balance.Value * allocation.Fraction);
        var amount = index == accountAllocations.Length - 1
          ? MoneyPolicy.Normalize(balance.Value - allocated)
          : independentlyRounded;
        var residual = MoneyPolicy.Normalize(amount - independentlyRounded);
        lines.Add(new PackageLine(balance.Key, allocation.DestinationCode, allocation.StatementSection,
          amount, allocation.Fraction, currency, residual));
        allocated = MoneyPolicy.Normalize(allocated + amount);
      }
    }
    return lines;
  }

  public static string ComputePackageHash(
    BuildFinancialPackageRequest request,
    MappingVersion mapping,
    AdjustmentPlan plan,
    string adjustedHash,
    IReadOnlyCollection<PackageLine> lines,
    string? supplementaryHash,
    Guid? periodId = null,
    Guid? bookId = null,
    string? basis = null)
  {
    var hasReportingContext = periodId is not null || bookId is not null || !string.IsNullOrWhiteSpace(basis);
    var parts = new List<string>
    {
      hasReportingContext ? "financial-statement-package.v2" : "financial-statement-package.v1",
      mapping.Id.ToString("D"), mapping.Version.ToString(CultureInfo.InvariantCulture),
      plan.Id.ToString("D"), plan.ResultHash ?? string.Empty, adjustedHash, request.Framework.Trim(),
      request.PeriodStart.Trim(), request.PeriodEnd.Trim(), mapping.TaxonomyVersion,
      request.TemplateVersion.Trim(), CalculationEngineVersion, supplementaryHash ?? string.Empty
    };
    if (hasReportingContext)
      parts.AddRange([periodId?.ToString("D") ?? string.Empty, bookId?.ToString("D") ?? string.Empty,
        basis?.Trim().ToUpperInvariant() ?? string.Empty]);
    var canonical = string.Join('\n', parts.Concat(lines.OrderBy(x => x.SourceAccountCode, StringComparer.Ordinal)
      .ThenBy(x => x.DestinationCode, StringComparer.Ordinal)
      .Select(x => string.Join('|', x.SourceAccountCode, x.DestinationCode, x.StatementSection,
        x.Amount.ToString("0.000000", CultureInfo.InvariantCulture),
        x.Fraction.ToString("0.000000", CultureInfo.InvariantCulture), x.Currency,
        x.RoundingResidual.ToString("0.000000", CultureInfo.InvariantCulture)))));
    return Hashing.Sha256Hex(canonical);
  }

  public static string ComputeSupplementaryHash(
    FinancialSupplementaryInformation input,
    string currency)
  {
    var parts = new List<string>
    {
      "financial-supplementary-information.v1", currency,
      input.CashBeginning.ToString("0.000000", CultureInfo.InvariantCulture),
      input.CashEnding.ToString("0.000000", CultureInfo.InvariantCulture)
    };
    parts.AddRange(input.CashFlowLines.OrderBy(x => x.Section, StringComparer.OrdinalIgnoreCase)
      .ThenBy(x => x.Description, StringComparer.Ordinal)
      .Select(x => string.Join('|', x.Section.Trim().ToUpperInvariant(), x.Description.Trim(),
        x.Amount.ToString("0.000000", CultureInfo.InvariantCulture))));
    parts.AddRange(input.Disclosures.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
      .Select(x => string.Join('|', x.Code.Trim().ToUpperInvariant(),
        x.NotApplicable ? "NA" : x.Response.Trim(), x.Rationale?.Trim() ?? string.Empty)));
    parts.AddRange((input.EquityLines ?? [])
      .OrderBy(x => x.LineCode, StringComparer.OrdinalIgnoreCase)
      .Select(x => string.Join('|', "EQUITY", x.LineCode.Trim().ToUpperInvariant(), x.Description.Trim(),
        x.OpeningAmount.ToString("0.000000", CultureInfo.InvariantCulture),
        x.ProfitOrLossAmount.ToString("0.000000", CultureInfo.InvariantCulture),
        x.OciAmount.ToString("0.000000", CultureInfo.InvariantCulture),
        x.CapitalMovementAmount.ToString("0.000000", CultureInfo.InvariantCulture),
        x.DividendsAmount.ToString("0.000000", CultureInfo.InvariantCulture),
        x.ClosingAmount.ToString("0.000000", CultureInfo.InvariantCulture), x.EvidenceReference.Trim())));
    parts.AddRange((input.NoteLines ?? [])
      .OrderBy(x => x.NoteCode, StringComparer.OrdinalIgnoreCase)
      .ThenBy(x => x.FaceDestinationCode, StringComparer.OrdinalIgnoreCase)
      .Select(x => string.Join('|', "NOTE", x.NoteCode.Trim().ToUpperInvariant(),
        x.FaceDestinationCode.Trim().ToUpperInvariant(), x.Amount.ToString("0.000000", CultureInfo.InvariantCulture),
        x.EvidenceReference.Trim())));
    if (input.Comparative is { } comparative)
      parts.Add(string.Join('|', "COMPARATIVE", comparative.PackageId.ToString("D"),
        comparative.Basis.Trim(), comparative.EvidenceReference.Trim()));
    return Hashing.Sha256Hex(string.Join('\n', parts));
  }

  public static string ComputeEquityHash(
    IReadOnlyCollection<EquityLineInput> lines,
    string currency)
  {
    var parts = new List<string> { "financial-equity-rollforward.v1", currency };
    parts.AddRange(lines.OrderBy(x => x.LineCode, StringComparer.OrdinalIgnoreCase).Select(x => string.Join('|',
      x.LineCode.Trim().ToUpperInvariant(), x.Description.Trim(),
      x.OpeningAmount.ToString("0.000000", CultureInfo.InvariantCulture),
      x.ProfitOrLossAmount.ToString("0.000000", CultureInfo.InvariantCulture),
      x.OciAmount.ToString("0.000000", CultureInfo.InvariantCulture),
      x.CapitalMovementAmount.ToString("0.000000", CultureInfo.InvariantCulture),
      x.DividendsAmount.ToString("0.000000", CultureInfo.InvariantCulture),
      x.ClosingAmount.ToString("0.000000", CultureInfo.InvariantCulture), x.EvidenceReference.Trim())));
    return Hashing.Sha256Hex(string.Join('\n', parts));
  }
}
