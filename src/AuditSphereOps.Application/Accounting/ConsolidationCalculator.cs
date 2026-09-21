using System.Globalization;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Accounting;

public sealed record ConsolidationComponentBalance(
  Guid ComponentId,
  Guid ClientId,
  string TaxonomyCode,
  decimal Amount,
  string Currency,
  decimal OwnershipPercent,
  string ControlMethod,
  string PackageHash = "",
  string PeriodBasis = "",
  string TaxonomyVersion = "",
  string MappingVersion = "");

public sealed record ConsolidationElimination(
  Guid MatchId,
  string TaxonomyCode,
  decimal Amount,
  string Currency);

public sealed record ConsolidatedLine(
  Guid? ComponentId,
  Guid? MatchId,
  string TaxonomyCode,
  decimal ComponentAmount,
  decimal EliminationAmount,
  decimal ConsolidatedAmount,
  string Currency);

public sealed record ConsolidationCalculation(
  IReadOnlyList<ConsolidatedLine> Lines,
  IReadOnlyList<ConsolidatedLine> DetailLines,
  decimal SignedTotal,
  string InputManifest,
  string RunHash);

/// <summary>
/// Pure, deliberately restricted first-profile group calculation. It refuses mixed
/// currencies, partial ownership and unapproved methods instead of silently applying
/// full-consolidation arithmetic to an unsupported group.
/// </summary>
public static class ConsolidationCalculator
{
  public const string EngineVersion = "auditsphere.consolidation-engine.v1";
  public const string RestrictedMethod = "FULLY_OWNED_SAME_CURRENCY";

  public static ConsolidationCalculation Compute(
    string reportingCurrency,
    string method,
    string openingBasis,
    IReadOnlyCollection<ConsolidationComponentBalance> components,
    IReadOnlyCollection<ConsolidationElimination> eliminations)
  {
    reportingCurrency = reportingCurrency.Trim().ToUpperInvariant();
    method = method.Trim().ToUpperInvariant();
    openingBasis = openingBasis.Trim();
    if (reportingCurrency.Length != 3 || reportingCurrency.Any(c => c is < 'A' or > 'Z'))
      throw new InvalidOperationException("A three-letter reporting currency is required.");
    if (method != RestrictedMethod)
      throw new InvalidOperationException("The selected consolidation method is not enabled for this profile.");
    if (openingBasis.Length == 0)
      throw new InvalidOperationException("An approved opening consolidation basis is required.");
    if (components.Count == 0)
      throw new InvalidOperationException("At least one approved component package is required.");
    if (components.GroupBy(x => x.ComponentId).Any(x => x.Count() > 1))
      throw new InvalidOperationException("A component package may appear only once in a consolidation run.");
    if (components.Any(x => x.Currency != reportingCurrency || x.OwnershipPercent != 100m ||
        !x.ControlMethod.Equals("CONTROLLED", StringComparison.OrdinalIgnoreCase)))
      throw new InvalidOperationException("The restricted profile requires fully owned, controlled components in one currency.");
    if (components.Any(x => x.PackageHash.Length != 64 || x.PackageHash.Any(c => c is < '0' or > '9' and < 'a' or > 'f')))
      throw new InvalidOperationException("Every component must be bound to a valid package hash.");
    if (components.Any(x => string.IsNullOrWhiteSpace(x.PeriodBasis) || string.IsNullOrWhiteSpace(x.TaxonomyVersion) ||
        string.IsNullOrWhiteSpace(x.MappingVersion)))
      throw new InvalidOperationException("Every component must be bound to period, taxonomy and mapping versions.");
    if (components.Any(x => string.IsNullOrWhiteSpace(x.TaxonomyCode)))
      throw new InvalidOperationException("Every component balance needs an approved taxonomy code.");
    if (eliminations.Any(x => x.Currency != reportingCurrency || string.IsNullOrWhiteSpace(x.TaxonomyCode)))
      throw new InvalidOperationException("Eliminations must use the scope currency and an approved taxonomy code.");
    if (eliminations.GroupBy(x => x.MatchId).Any(x => x.Count() > 1))
      throw new InvalidOperationException("An intercompany match may be applied only once in a run.");

    var lines = components
      .OrderBy(x => x.TaxonomyCode, StringComparer.Ordinal)
      .ThenBy(x => x.ComponentId)
      .Select(x => new ConsolidatedLine(x.ComponentId, null, x.TaxonomyCode,
        MoneyPolicy.Normalize(x.Amount), 0m, MoneyPolicy.Normalize(x.Amount), reportingCurrency))
      .ToList();
    lines.AddRange(eliminations
      .OrderBy(x => x.TaxonomyCode, StringComparer.Ordinal)
      .ThenBy(x => x.MatchId)
      .Select(x => new ConsolidatedLine(null, x.MatchId, x.TaxonomyCode, 0m,
        MoneyPolicy.Normalize(x.Amount), MoneyPolicy.Normalize(x.Amount), reportingCurrency)));

    var totals = lines.GroupBy(x => x.TaxonomyCode, StringComparer.Ordinal)
      .Select(group =>
      {
        var componentAmount = MoneyPolicy.Normalize(group.Sum(x => x.ComponentAmount));
        var eliminationAmount = MoneyPolicy.Normalize(group.Sum(x => x.EliminationAmount));
        return new ConsolidatedLine(null, null, group.Key, componentAmount, eliminationAmount,
          MoneyPolicy.Normalize(componentAmount + eliminationAmount), reportingCurrency);
      })
      .OrderBy(x => x.TaxonomyCode, StringComparer.Ordinal)
      .ToList();
    var signedTotal = MoneyPolicy.Normalize(totals.Sum(x => x.ConsolidatedAmount));
    if (signedTotal != 0m)
      throw new InvalidOperationException("The consolidated working trial balance is not balanced.");

    var manifest = string.Join('\n', new[]
    {
      EngineVersion, method, openingBasis, reportingCurrency
    }.Concat(lines.OrderBy(x => x.TaxonomyCode, StringComparer.Ordinal)
      .ThenBy(x => x.ComponentId)
      .ThenBy(x => x.MatchId)
      .Select(x =>
      {
        var component = components.FirstOrDefault(c => c.ComponentId == x.ComponentId);
        return string.Join('|', x.ComponentId?.ToString("D") ?? string.Empty,
          x.MatchId?.ToString("D") ?? string.Empty, x.TaxonomyCode,
          x.ComponentAmount.ToString("0.000000", CultureInfo.InvariantCulture),
          x.EliminationAmount.ToString("0.000000", CultureInfo.InvariantCulture), x.Currency,
          component?.PackageHash ?? string.Empty, component?.PeriodBasis ?? string.Empty,
          component?.TaxonomyVersion ?? string.Empty, component?.MappingVersion ?? string.Empty);
      })));
    return new ConsolidationCalculation(totals, lines, signedTotal, manifest, Hashing.Sha256Hex(manifest));
  }
}
