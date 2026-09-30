using System.Globalization;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Audit;

/// <summary>One mapped trial-balance line: signed amount (debit positive) in its statement section.</summary>
public sealed record MappedBenchmarkLine(string SourceAccountCode, string DestinationCode, string StatementSection, decimal Amount);

public sealed record MaterialityPolicyRange(decimal MinRatePercent, decimal MaxRatePercent);

public sealed record MaterialityFigures(
  decimal BenchmarkAmount, int SourceLineCount, decimal PlanningMateriality, decimal TolerableError, decimal SadThreshold);

/// <summary>
/// Pure three-tier materiality: Planning Materiality = benchmark × rate, Tolerable Error = PM × performance
/// percentage, SAD (summary of adjusted/unadjusted differences) threshold = PM × trivial percentage. The benchmark is
/// derived from mapped trial-balance lines only; a benchmark that is not positive fails closed rather than producing
/// zero or a negative threshold. No EF, clock or random input.
/// </summary>
public static class MaterialityCalculator
{
  public const string PolicyVersion = "STE-MATERIALITY-2026.1";
  public static readonly (decimal Min, decimal Max) PerformanceRange = (50m, 75m);
  public static readonly (decimal Min, decimal Max) TrivialRange = (1m, 5m);

  /// <summary>Default methodology rate ranges per benchmark (percent). Firms change these through methodology approval.</summary>
  public static readonly IReadOnlyDictionary<string, MaterialityPolicyRange> RateRanges = new Dictionary<string, MaterialityPolicyRange>
  {
    [MaterialityBenchmarks.Revenue] = new(0.5m, 2m),
    [MaterialityBenchmarks.ProfitBeforeTax] = new(3m, 10m),
    [MaterialityBenchmarks.TotalAssets] = new(0.5m, 2m),
    [MaterialityBenchmarks.NetAssets] = new(1m, 5m),
    [MaterialityBenchmarks.TotalExpenses] = new(0.5m, 2m),
    [MaterialityBenchmarks.MappedLine] = new(0.5m, 10m)
  };

  private static readonly string[] IncomeSections = ["INCOME", "REVENUE", "P&L", "PROFIT_LOSS", "P_AND_L"];

  /// <summary>Selects the lines that make up the benchmark and returns the positive benchmark amount.</summary>
  public static (decimal Amount, int LineCount)? DeriveBenchmark(string kind, string? destinationCode, IReadOnlyCollection<MappedBenchmarkLine> lines)
  {
    static bool Section(MappedBenchmarkLine line, params string[] names) => names.Contains(line.StatementSection.Trim().ToUpperInvariant());
    static bool IsTax(MappedBenchmarkLine line) => line.DestinationCode.Contains("TAX", StringComparison.OrdinalIgnoreCase);
    IEnumerable<MappedBenchmarkLine> chosen;
    Func<decimal, decimal> sign;
    switch (kind)
    {
      case MaterialityBenchmarks.Revenue:
        chosen = lines.Where(x => Section(x, IncomeSections)); sign = x => -x; break;
      case MaterialityBenchmarks.ProfitBeforeTax:
        chosen = lines.Where(x => (Section(x, IncomeSections) || Section(x, "EXPENSE", "EXPENSES")) && !IsTax(x)); sign = x => -x; break;
      case MaterialityBenchmarks.TotalAssets:
        chosen = lines.Where(x => Section(x, "ASSETS", "ASSET")); sign = x => x; break;
      case MaterialityBenchmarks.NetAssets:
        chosen = lines.Where(x => Section(x, "ASSETS", "ASSET", "LIABILITIES", "LIABILITY")); sign = x => x; break;
      case MaterialityBenchmarks.TotalExpenses:
        chosen = lines.Where(x => Section(x, "EXPENSE", "EXPENSES")); sign = x => x; break;
      case MaterialityBenchmarks.MappedLine when !string.IsNullOrWhiteSpace(destinationCode):
        chosen = lines.Where(x => string.Equals(x.DestinationCode, destinationCode.Trim(), StringComparison.OrdinalIgnoreCase));
        sign = x => Math.Abs(x); break;
      default:
        return null;
    }
    var selected = chosen.ToList();
    if (selected.Count == 0) return null;
    return (MoneyPolicy.Normalize(sign(selected.Sum(x => x.Amount))), selected.Count);
  }

  public static string? Validate(string kind, decimal ratePercent, decimal performancePercent, decimal trivialPercent)
  {
    if (!RateRanges.TryGetValue(kind, out var range)) return "Choose a supported benchmark.";
    if (ratePercent < range.MinRatePercent || ratePercent > range.MaxRatePercent)
      return $"The rate for {kind} must be between {range.MinRatePercent}% and {range.MaxRatePercent}% under {PolicyVersion}.";
    if (performancePercent < PerformanceRange.Min || performancePercent > PerformanceRange.Max)
      return $"Tolerable error must be {PerformanceRange.Min}%–{PerformanceRange.Max}% of planning materiality.";
    if (trivialPercent < TrivialRange.Min || trivialPercent > TrivialRange.Max)
      return $"The SAD threshold must be {TrivialRange.Min}%–{TrivialRange.Max}% of planning materiality.";
    return null;
  }

  public static MaterialityFigures Calculate(decimal benchmark, int lineCount, decimal ratePercent, decimal performancePercent, decimal trivialPercent)
  {
    if (benchmark <= 0) throw new ArgumentOutOfRangeException(nameof(benchmark), "The benchmark must be positive.");
    var pm = MoneyPolicy.Normalize(Math.Round(benchmark * ratePercent / 100m, 2, MidpointRounding.ToEven));
    var te = MoneyPolicy.Normalize(Math.Round(pm * performancePercent / 100m, 2, MidpointRounding.ToEven));
    var sad = MoneyPolicy.Normalize(Math.Round(pm * trivialPercent / 100m, 2, MidpointRounding.ToEven));
    return new(benchmark, lineCount, pm, te, sad);
  }

  public static string InputHash(Guid mappingVersionId, string datasetDigest, string kind, string? destinationCode,
    decimal benchmark, decimal ratePercent, decimal performancePercent, decimal trivialPercent) =>
    Hashing.Sha256Hex(string.Join('|', "materiality.v1", PolicyVersion, mappingVersionId.ToString("D"), datasetDigest, kind,
      destinationCode?.Trim().ToUpperInvariant() ?? string.Empty,
      benchmark.ToString("0.000000", CultureInfo.InvariantCulture), ratePercent.ToString("0.0000", CultureInfo.InvariantCulture),
      performancePercent.ToString("0.0000", CultureInfo.InvariantCulture), trivialPercent.ToString("0.0000", CultureInfo.InvariantCulture)));
}

/// <summary>
/// Pure green/amber/red rule. A significant or fraud risk is always red; otherwise likelihood × magnitude
/// (each 1–3) of 6 or more is red, 3–4 amber and 1–2 green. The colour routes work; it is not a professional judgment.
/// </summary>
public static class RiskBandRules
{
  public const string RuleVersion = "STE-RISK-BAND-2026.1";

  public static string Band(int likelihood, int magnitude, bool significant, bool fraudRisk)
  {
    if (likelihood is < 1 or > 3 || magnitude is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(likelihood));
    if (significant || fraudRisk) return RiskBands.Red;
    var score = likelihood * magnitude;
    return score >= 6 ? RiskBands.Red : score >= 3 ? RiskBands.Amber : RiskBands.Green;
  }

  /// <summary>Minimum staffing rank that may own the response: green any, amber senior, red manager.</summary>
  public static int MinimumOwnerRank(string band) => band switch { RiskBands.Red => 3, RiskBands.Amber => 2, _ => 1 };

  public static string Route(string band) => band switch
  {
    RiskBands.Red => "Audit Manager or above performs; Engagement Partner review is mandatory before planning completes.",
    RiskBands.Amber => "Senior Auditor or above performs; Audit Manager reviews.",
    _ => "Assignable to a Staff Associate; standard review."
  };
}
