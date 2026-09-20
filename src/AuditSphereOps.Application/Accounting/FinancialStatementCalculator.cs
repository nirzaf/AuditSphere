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
    string Currency);

  public static IReadOnlyList<PackageLine> BuildPackageLines(
    IReadOnlyDictionary<string, decimal> balances,
    IReadOnlyCollection<MappingAllocationInput> allocations,
    string currency)
  {
    var lines = new List<PackageLine>();
    foreach (var balance in balances.OrderBy(x => x.Key, StringComparer.Ordinal))
    {
      foreach (var allocation in allocations.Where(x => x.SourceAccountCode == balance.Key)
        .OrderBy(x => x.DestinationCode, StringComparer.Ordinal))
      {
        lines.Add(new PackageLine(balance.Key, allocation.DestinationCode, allocation.StatementSection,
          MoneyPolicy.Normalize(balance.Value * allocation.Fraction), allocation.Fraction, currency));
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
    string? supplementaryHash)
  {
    var canonical = string.Join('\n', new[]
    {
      "financial-statement-package.v1", mapping.Id.ToString("D"), mapping.Version.ToString(CultureInfo.InvariantCulture),
      plan.Id.ToString("D"), plan.ResultHash ?? string.Empty, adjustedHash, request.Framework.Trim(),
      request.PeriodStart.Trim(), request.PeriodEnd.Trim(), mapping.TaxonomyVersion,
      request.TemplateVersion.Trim(), CalculationEngineVersion, supplementaryHash ?? string.Empty
    }.Concat(lines.OrderBy(x => x.SourceAccountCode, StringComparer.Ordinal)
      .ThenBy(x => x.DestinationCode, StringComparer.Ordinal)
      .Select(x => string.Join('|', x.SourceAccountCode, x.DestinationCode, x.StatementSection,
        x.Amount.ToString("0.000000", CultureInfo.InvariantCulture),
        x.Fraction.ToString("0.000000", CultureInfo.InvariantCulture), x.Currency))));
    return Hashing.Sha256Hex(canonical);
  }

  public static string ComputeSupplementaryHash(
    FinancialSupplementaryInformation input,
    string currency)
  {
    var canonical = string.Join('\n', new[]
    {
      "financial-supplementary-information.v1", currency,
      input.CashBeginning.ToString("0.000000", CultureInfo.InvariantCulture),
      input.CashEnding.ToString("0.000000", CultureInfo.InvariantCulture)
    }.Concat(input.CashFlowLines.OrderBy(x => x.Section, StringComparer.OrdinalIgnoreCase)
      .ThenBy(x => x.Description, StringComparer.Ordinal)
      .Select(x => string.Join('|', x.Section.Trim().ToUpperInvariant(), x.Description.Trim(),
        x.Amount.ToString("0.000000", CultureInfo.InvariantCulture))))
     .Concat(input.Disclosures.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
       .Select(x => string.Join('|', x.Code.Trim().ToUpperInvariant(),
         x.NotApplicable ? "NA" : x.Response.Trim(), x.Rationale?.Trim() ?? string.Empty))));
    return Hashing.Sha256Hex(canonical);
  }
}
