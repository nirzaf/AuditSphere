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
  /// <summary>Seeded random start with equal spacing across the ordered row population.</summary>
  public const string Systematic = "SYSTEMATIC";
  /// <summary>Key items first, then seeded random from the remainder up to the sample size.</summary>
  public const string Stratified = "STRATIFIED";
  /// <summary>Reviewer-defined attribute strata: one seeded draw per reviewer-defined stratum.</summary>
  public const string AttributeStrata = "ATTRIBUTE_STRATA";
  public static readonly string[] All = [MonetaryUnit, KeyItem, Random, Systematic, Stratified, AttributeStrata];
}

/// <summary>Reviewer-selectable, deterministic stratification fields derived from imported schedule rows.</summary>
public static class SamplingAttributeFields
{
  public const string Account = "ACCOUNT";
  public const string Currency = "CURRENCY";
  public const string Direction = "DIRECTION";
  public const string Month = "MONTH";
  public static readonly string[] All = [Account, Currency, Direction, Month];
}

/// <summary>Row attributes carried from the imported schedule; they feed reviewer-defined attribute strata.</summary>
public sealed record SamplingRowAttributes(string AccountCode, string Currency, DateOnly? TransactionDate, DateOnly? PostingDate);

public sealed record SamplingPopulationItem(string StableRowId, decimal SignedAmount, SamplingRowAttributes? Attributes = null);

public sealed record SamplingPlan(
  string Method,
  /// <summary>Required for MUS: the monetary interval between selections.</summary>
  decimal? Interval = null,
  /// <summary>Required for KEY_ITEM and STRATIFIED: items at or above this are key items.</summary>
  decimal? KeyItemThreshold = null,
  /// <summary>Required for RANDOM, SYSTEMATIC and STRATIFIED: maximum items to select.</summary>
  int? SampleSize = null,
  /// <summary>Required for RANDOM, SYSTEMATIC and STRATIFIED: reproducible selection seed.</summary>
  int? Seed = null,
  /// <summary>Required for ATTRIBUTE_STRATA: reviewer-defined stratification fields from SamplingAttributeFields.</summary>
  IReadOnlyList<string>? AttributeFields = null);

public sealed record SampledItem(
  string StableRowId, decimal SignedAmount, decimal AbsoluteAmount,
  string InclusionReason, decimal CumulativeAbsoluteAmount);

public sealed record AttributeStratumSummary(
  string StratumKey, int PopulationCount, int Slots, int SelectedCount);

public sealed record SamplingOutcome(
  string Method, int PopulationCount, decimal PopulationSignedTotal, decimal PopulationAbsoluteTotal,
  int SelectedCount, decimal SelectedAbsoluteTotal, decimal CoveragePercent,
  int? Seed, decimal? Interval, decimal? KeyItemThreshold,
  IReadOnlyList<SampledItem> Items,
  /// <summary>Only for ATTRIBUTE_STRATA: the reviewer-defined strata and their allocation.</summary>
  IReadOnlyList<AttributeStratumSummary>? Strata = null);

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
    var normalizedPopulation = population.Select(x =>
    {
      if (x is null || string.IsNullOrWhiteSpace(x.StableRowId))
        throw new ArgumentException("Population rows require a stable, non-empty identity.", nameof(population));
      return x with { StableRowId = x.StableRowId.Trim() };
    }).ToList();
    if (normalizedPopulation.Select(x => x.StableRowId).Distinct(StringComparer.Ordinal).Count() != normalizedPopulation.Count)
      throw new ArgumentException("Population row identities must be unique.", nameof(population));

    // Systematic sampling and attribute strata select row positions including zero-amount
    // transactions (a zero-value row still carries attributes worth stratifying). Existing
    // monetary-exposure methods preserve their original population contract.
    var ordered = normalizedPopulation
      .Where(x => plan.Method == AuditSamplingMethods.Systematic || plan.Method == AuditSamplingMethods.AttributeStrata || Abs(x.SignedAmount) > 0m)
      .ToList();
    var signedTotal = normalizedPopulation.Sum(x => x.SignedAmount);
    var absoluteTotal = ordered.Sum(x => Abs(x.SignedAmount));

    List<SampledItem> selected;
    IReadOnlyList<AttributeStratumSummary>? strata = null;
    switch (plan.Method)
    {
      case AuditSamplingMethods.MonetaryUnit:
        selected = SelectMonetaryUnit(ordered, Require(plan.Interval, "interval", plan.Method));
        break;
      case AuditSamplingMethods.KeyItem:
        selected = SelectKeyItems(ordered, Require(plan.KeyItemThreshold, "key-item threshold", plan.Method));
        break;
      case AuditSamplingMethods.Random:
        selected = SelectRandom(ordered, RequireSize(plan.SampleSize, plan.Method), RequireSeed(plan.Seed, plan.Method));
        break;
      case AuditSamplingMethods.Systematic:
        selected = SelectSystematic(ordered, RequireSize(plan.SampleSize, plan.Method), RequireSeed(plan.Seed, plan.Method));
        break;
      case AuditSamplingMethods.AttributeStrata:
        (selected, strata) = SelectAttributeStrata(ordered, plan.AttributeFields,
          RequireSize(plan.SampleSize, plan.Method), RequireSeed(plan.Seed, plan.Method));
        break;
      default:
        selected = SelectStratified(ordered, Require(plan.KeyItemThreshold, "key-item threshold", plan.Method),
          RequireSize(plan.SampleSize, plan.Method), RequireSeed(plan.Seed, plan.Method));
        break;
    }

    var selectedAbsolute = selected.Sum(x => x.AbsoluteAmount);
    return new SamplingOutcome(
      plan.Method, ordered.Count, Money(signedTotal), Money(absoluteTotal),
      selected.Count, Money(selectedAbsolute),
      absoluteTotal == 0m ? 0m : Money(selectedAbsolute / absoluteTotal * 100m),
      plan.Seed, plan.Interval, plan.KeyItemThreshold, selected, strata);
  }

  /// <summary>
  /// Reviewer-defined attribute strata (R07/T20): rows are grouped by the reviewer-selected
  /// deterministic fields (account, currency, direction, month), the sample size is allocated one slot
  /// per stratum first and the remainder in stable stratum order, and each stratum is filled by a
  /// derived seeded draw without replacement. A size below the stratum count cannot cover every
  /// required stratum and is refused instead of silently dropping strata.
  /// </summary>
  private static (List<SampledItem> Items, IReadOnlyList<AttributeStratumSummary> Strata) SelectAttributeStrata(
    List<SamplingPopulationItem> ordered, IReadOnlyList<string>? fields, int sampleSize, int seed)
  {
    if (fields is null || fields.Count == 0)
      throw new ArgumentException("Attribute strata sampling requires reviewer-defined attribute fields.", nameof(fields));
    var keys = fields.Select(f => f?.Trim().ToUpperInvariant() ?? string.Empty).ToArray();
    if (keys.Length != keys.Distinct(StringComparer.Ordinal).Count())
      throw new ArgumentException("Choose each attribute field only once.", nameof(fields));
    if (keys.Any(f => !SamplingAttributeFields.All.Contains(f)))
      throw new ArgumentException("Attribute strata fields must be chosen from the supported reviewer-defined attribute set.", nameof(fields));
    if (ordered.Any(x => x.Attributes is null))
      throw new ArgumentException("Attribute strata sampling requires population rows that carry the imported row attributes.", nameof(ordered));
    if ((keys.Contains(SamplingAttributeFields.Account, StringComparer.Ordinal) && ordered.Any(x => string.IsNullOrWhiteSpace(x.Attributes!.AccountCode))) ||
        (keys.Contains(SamplingAttributeFields.Currency, StringComparer.Ordinal) && ordered.Any(x => string.IsNullOrWhiteSpace(x.Attributes!.Currency))) ||
        (keys.Contains(SamplingAttributeFields.Month, StringComparer.Ordinal) && ordered.Any(x => x.Attributes!.TransactionDate is null && x.Attributes.PostingDate is null)))
      throw new ArgumentException("Every population row must contain a value for each selected attribute field.", nameof(ordered));
    if (sampleSize > ordered.Count)
      throw new ArgumentException("The sample size cannot exceed the number of rows in the approved population.", nameof(sampleSize));

    var groups = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
    for (var i = 0; i < ordered.Count; i++)
    {
      var key = string.Join('|', keys.Select(f => StratumFieldValue(f, ordered[i])));
      if (!groups.TryGetValue(key, out var bucket)) groups[key] = bucket = [];
      bucket.Add(i);
    }
    if (sampleSize < groups.Count)
      throw new ArgumentException(
        $"The sample size must provide at least one slot for each of the {groups.Count} strata present in the population.", nameof(fields));

    var cumulativeByRow = CumulativeIndex(ordered);
    var selected = new List<SampledItem>(sampleSize);
    var strata = new List<AttributeStratumSummary>(groups.Count);
    var allocations = groups.ToDictionary(x => x.Key, _ => 1, StringComparer.Ordinal);
    var slotsRemaining = sampleSize - groups.Count;
    while (slotsRemaining > 0)
    {
      var allocatedThisRound = 0;
      foreach (var (key, indexes) in groups)
      {
        if (slotsRemaining == 0) break;
        if (allocations[key] >= indexes.Count) continue;
        allocations[key]++;
        slotsRemaining--;
        allocatedThisRound++;
      }
      if (allocatedThisRound == 0)
        throw new ArgumentException("The requested sample cannot be allocated across the available strata.", nameof(sampleSize));
    }

    var ordinal = 0;
    foreach (var (key, indexes) in groups)
    {
      var slots = allocations[key];
      var stratumSeed = unchecked(seed + ordinal);
      foreach (var index in DrawIndexesFrom(indexes, slots, stratumSeed))
      {
        var item = ordered[index];
        selected.Add(new SampledItem(item.StableRowId, item.SignedAmount, Abs(item.SignedAmount),
          $"Attribute strata '{key}': {slots} of {indexes.Count} rows drawn with per-stratum seed {stratumSeed}.",
          cumulativeByRow[index]));
      }
      strata.Add(new AttributeStratumSummary(key, indexes.Count, slots, slots));
      ordinal++;
    }
    return (selected, strata);
  }

  private static string StratumFieldValue(string field, SamplingPopulationItem item) => field switch
  {
    SamplingAttributeFields.Account => item.Attributes!.AccountCode.Trim().ToUpperInvariant(),
    SamplingAttributeFields.Currency => item.Attributes!.Currency.Trim().ToUpperInvariant(),
    SamplingAttributeFields.Direction => item.SignedAmount > 0m ? "DEBIT" : item.SignedAmount < 0m ? "CREDIT" : "ZERO",
    _ => (item.Attributes!.TransactionDate ?? item.Attributes.PostingDate) is { } month
      ? $"{month.Year:D4}-{month.Month:D2}"
      : "NOT_STATED",
  };

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

  private static List<SampledItem> SelectSystematic(List<SamplingPopulationItem> ordered, int sampleSize, int seed)
  {
    var take = Math.Min(sampleSize, ordered.Count);
    if (take == 0) return [];
    // Integer rational positions avoid rounding drift for non-divisible populations.
    // The seeded numerator gives a random start within the first N/n interval.
    var startNumerator = DrawIndexes(ordered.Count, 1, seed)[0];
    var cumulative = CumulativeIndex(ordered);
    var selected = new List<SampledItem>(take);
    for (var k = 0; k < take; k++)
    {
      var index = (int)(((long)k * ordered.Count + startNumerator) / take);
      var item = ordered[index];
      selected.Add(new SampledItem(item.StableRowId, item.SignedAmount, Abs(item.SignedAmount),
        $"Systematic random: row {index + 1} of {ordered.Count}; interval {ordered.Count}/{take}; start {startNumerator}/{take}; seed {seed}.",
        cumulative[index]));
    }
    return selected;
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
