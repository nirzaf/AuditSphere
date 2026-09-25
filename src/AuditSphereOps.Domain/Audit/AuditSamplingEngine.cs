// Audit sampling engine (T058). Pure and deterministic: no EF Core, no system clock,
// no non-deterministic IDs. Selection is reproducible from the same population, method,
// parameters and seed, so an auditor can re-perform the selection exactly.
namespace AuditSphereOps.Domain.Audit;

public static class AuditSamplingMethods
{
  /// <summary>Monetary-unit sampling: interval selection over cumulative absolute value.</summary>
  public const string MonetaryUnit = "MUS";
  /// <summary>Every item at or above the key-item threshold is selected.</summary>
  public const string KeyItem = "KEY_ITEM";
  /// <summary>Seeded pseudo-random selection without replacement.</summary>
  public const string Random = "RANDOM";
  /// <summary>Key items first, then seeded random from the remainder up to the sample size.</summary>
  public const string Stratified = "STRATIFIED";
  public static readonly string[] All = [MonetaryUnit, KeyItem, Random, Stratified];
}

public sealed record SamplingPopulationItem(string StableRowId, decimal SignedAmount);

public sealed record SamplingPlan(
  string Method,
  /// <summary>Required for MUS: the monetary interval between selections.</summary>
  decimal? Interval = null,
  /// <summary>Required for KEY_ITEM and STRATIFIED: items at or above this are key items.</summary>
  decimal? KeyItemThreshold = null,
  /// <summary>Required for RANDOM and STRATIFIED: maximum items to select.</summary>
  int? SampleSize = null,
  /// <summary>Required for RANDOM and STRATIFIED: the seed that makes selection reproducible.</summary>
  int? Seed = null);

public sealed record SampledItem(
  string StableRowId, decimal SignedAmount, decimal AbsoluteAmount,
  string InclusionReason, decimal CumulativeAbsoluteAmount);

public sealed record SamplingOutcome(
  string Method, int PopulationCount, decimal PopulationSignedTotal, decimal PopulationAbsoluteTotal,
  int SelectedCount, decimal SelectedAbsoluteTotal, decimal CoveragePercent,
  int? Seed, decimal? Interval, decimal? KeyItemThreshold,
  IReadOnlyList<SampledItem> Items);

public static class AuditSamplingEngine
{
  /// <summary>
  /// Deterministic selection over one population. MUS walks the cumulative absolute value
  /// and takes each item whose cumulative total crosses a multiple of the interval.
  /// Stratification always includes key items, then fills from the seeded random stream.
  /// </summary>
  public static SamplingOutcome Select(IReadOnlyList<SamplingPopulationItem> population, SamplingPlan plan)
  {
    ArgumentNullException.ThrowIfNull(population);
    ArgumentNullException.ThrowIfNull(plan);
    if (!AuditSamplingMethods.All.Contains(plan.Method))
      throw new ArgumentException($"Unsupported sampling method '{plan.Method}'.", nameof(plan));
    if (population.Select(x => x.StableRowId).Distinct(StringComparer.Ordinal).Count() != population.Count)
      throw new ArgumentException("Population row identities must be unique.", nameof(population));

    // Zero-exposure rows carry no monetary exposure and are never selected by these
    // methods; the original signed amount is preserved for the auditor's test sheet.
    var ordered = population
      .Select(x => new SamplingPopulationItem(x.StableRowId.Trim(), x.SignedAmount))
      .Where(x => Abs(x.SignedAmount) > 0m)
      .ToList();
    var signedTotal = population.Sum(x => x.SignedAmount);
    var absoluteTotal = ordered.Sum(x => Abs(x.SignedAmount));

    var selected = plan.Method switch
    {
      AuditSamplingMethods.MonetaryUnit => SelectMonetaryUnit(ordered, Require(plan.Interval, "interval", plan.Method)),
      AuditSamplingMethods.KeyItem => SelectKeyItems(ordered, Require(plan.KeyItemThreshold, "key-item threshold", plan.Method)),
      AuditSamplingMethods.Random => SelectRandom(ordered, RequireSize(plan.SampleSize, plan.Method), RequireSeed(plan.Seed, plan.Method)),
      _ => SelectStratified(ordered, Require(plan.KeyItemThreshold, "key-item threshold", plan.Method),
        RequireSize(plan.SampleSize, plan.Method), RequireSeed(plan.Seed, plan.Method))
    };

    var selectedAbsolute = selected.Sum(x => x.AbsoluteAmount);
    return new SamplingOutcome(
      plan.Method, ordered.Count, Money(signedTotal), Money(absoluteTotal),
      selected.Count, Money(selectedAbsolute),
      absoluteTotal == 0m ? 0m : Money(selectedAbsolute / absoluteTotal * 100m),
      plan.Seed, plan.Interval, plan.KeyItemThreshold, selected);
  }

  private static List<SampledItem> SelectMonetaryUnit(List<SamplingPopulationItem> ordered, decimal interval)
  {
    var selected = new List<SampledItem>();
    var cumulative = 0m;
    var nextSelectionPoint = interval;
    foreach (var item in ordered)
    {
      var exposure = Abs(item.SignedAmount);
      cumulative += exposure;
      if (cumulative >= nextSelectionPoint)
      {
        selected.Add(new SampledItem(item.StableRowId, item.SignedAmount, exposure,
          $"Monetary unit: cumulative exposure {Money(cumulative)} crossed the interval at {Money(nextSelectionPoint)}.",
          Money(cumulative)));
        // Advance beyond the crossed point; a single large item can cover several
        // intervals, and each crossing selects that row only once.
        while (cumulative >= nextSelectionPoint) nextSelectionPoint += interval;
      }
    }
    return selected;
  }

  private static List<SampledItem> SelectKeyItems(List<SamplingPopulationItem> ordered, decimal threshold)
  {
    var selected = new List<SampledItem>();
    var cumulative = 0m;
    foreach (var item in ordered)
    {
      var exposure = Abs(item.SignedAmount);
      cumulative += exposure;
      if (exposure >= threshold)
        selected.Add(new SampledItem(item.StableRowId, item.SignedAmount, exposure,
          $"Key item: exposure {Money(exposure)} is at or above the threshold {Money(threshold)}.",
          Money(cumulative)));
    }
    return selected;
  }

  private static List<SampledItem> SelectRandom(List<SamplingPopulationItem> ordered, int sampleSize, int seed)
  {
    var selection = DrawIndexes(ordered.Count, sampleSize, seed);
    var cumulativeByRow = CumulativeIndex(ordered);
    return selection
      .Select(index =>
      {
        var item = ordered[index];
        return new SampledItem(item.StableRowId, item.SignedAmount, Abs(item.SignedAmount),
          $"Random: deterministic draw with seed {seed}.", cumulativeByRow[index]);
      })
      .ToList();
  }

  private static List<SampledItem> SelectStratified(
    List<SamplingPopulationItem> ordered, decimal threshold, int sampleSize, int seed)
  {
    var cumulativeByRow = CumulativeIndex(ordered);
    var keyIndexes = new List<int>();
    for (var i = 0; i < ordered.Count; i++)
      if (Abs(ordered[i].SignedAmount) >= threshold) keyIndexes.Add(i);

    var selected = keyIndexes.Select(index => new SampledItem(
      ordered[index].StableRowId, ordered[index].SignedAmount, Abs(ordered[index].SignedAmount),
      $"Key item: exposure {Money(Abs(ordered[index].SignedAmount))} is at or above the threshold {Money(threshold)}.",
      cumulativeByRow[index])).ToList();

    var remaining = sampleSize - selected.Count;
    if (remaining > 0)
    {
      var keySet = keyIndexes.ToHashSet();
      var candidates = Enumerable.Range(0, ordered.Count).Where(i => !keySet.Contains(i)).ToList();
      foreach (var index in DrawIndexesFrom(candidates, remaining, seed))
      {
        var item = ordered[index];
        selected.Add(new SampledItem(item.StableRowId, item.SignedAmount, Abs(item.SignedAmount),
          $"Stratified random: deterministic draw with seed {seed} from the non-key stratum.",
          cumulativeByRow[index]));
      }
    }
    return selected;
  }

  private static List<decimal> CumulativeIndex(List<SamplingPopulationItem> ordered)
  {
    var result = new List<decimal>(ordered.Count);
    var running = 0m;
    foreach (var item in ordered)
    {
      running += Abs(item.SignedAmount);
      result.Add(Money(running));
    }
    return result;
  }

  private static List<int> DrawIndexes(int count, int sampleSize, int seed) =>
    DrawIndexesFrom(Enumerable.Range(0, count).ToList(), sampleSize, seed);

  /// <summary>Deterministic without-replacement draw. Uses a fixed splitmix64-style
  /// generator so the same seed always yields the same selection on every runtime.</summary>
  private static List<int> DrawIndexesFrom(List<int> candidates, int take, int seed)
  {
    var pool = new List<int>(candidates);
    var drawn = new List<int>(Math.Min(take, pool.Count));
    var state = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 0xBF58476D1CE4E5B9UL);
    while (drawn.Count < take && pool.Count > 0)
    {
      state = unchecked(state + 0x9E3779B97F4A7C15UL);
      var mixed = state;
      mixed = unchecked((mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL);
      mixed = unchecked((mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL);
      mixed ^= mixed >> 31;
      var index = (int)(mixed % (ulong)pool.Count);
      drawn.Add(pool[index]);
      pool.RemoveAt(index);
    }
    // Preserve population order for a stable, readable test sheet.
    drawn.Sort();
    return drawn;
  }

  private static decimal Require(decimal? value, string name, string method) =>
    value is > 0m ? value.Value
      : throw new ArgumentException($"The {name} must be greater than zero for {method} sampling.", nameof(value));

  private static int RequireSize(int? value, string method) =>
    value is > 0 ? value.Value
      : throw new ArgumentException($"A positive sample size is required for {method} sampling.", nameof(value));

  private static int RequireSeed(int? value, string method) =>
    value ?? throw new ArgumentException($"A seed is required for {method} sampling.", nameof(value));

  private static decimal Abs(decimal value) => value < 0m ? -value : value;

  private static decimal Money(decimal value) => decimal.Round(value, 6, MidpointRounding.ToEven);
}
