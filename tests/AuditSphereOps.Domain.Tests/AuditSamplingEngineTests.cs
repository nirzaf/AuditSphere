using AuditSphereOps.Domain.Audit;

namespace AuditSphereOps.Domain.Tests;

/// <summary>T058 pure sampling engine: deterministic, reproducible selections with no
/// database, clock or runtime-dependent randomness.</summary>
public sealed class AuditSamplingEngineTests
{
  private static readonly SamplingPopulationItem[] Population =
  [
    new("row-01", 100m), new("row-02", 250m), new("row-03", 40m),
    new("row-04", 900m), new("row-05", 75m), new("row-06", 10m),
    new("row-07", 500m), new("row-08", 60m), new("row-09", 25m), new("row-10", 180m)
  ];

  [Fact(DisplayName = "MUS selects each interval crossing once and covers the expected exposures")]
  public void MonetaryUnit_SelectsIntervalCrossings()
  {
    var outcome = AuditSamplingEngine.Select(Population, new SamplingPlan(AuditSamplingMethods.MonetaryUnit, Interval: 300m));
    Assert.Equal(AuditSamplingMethods.MonetaryUnit, outcome.Method);
    Assert.Equal(10, outcome.PopulationCount);
    Assert.Equal(2140m, outcome.PopulationAbsoluteTotal);
    Assert.NotEmpty(outcome.Items);
    // No row may be selected twice even when a large item spans several intervals.
    Assert.Equal(outcome.Items.Count, outcome.Items.Select(x => x.StableRowId).Distinct(StringComparer.Ordinal).Count());
    Assert.True(outcome.SelectedAbsoluteTotal > 0m);
    Assert.True(outcome.CoveragePercent is > 0m and <= 100m);
    Assert.All(outcome.Items, item => Assert.Contains("Monetary unit", item.InclusionReason));
  }

  [Fact(DisplayName = "Key-item sampling selects every exposure at or above the threshold")]
  public void KeyItem_SelectsAboveThreshold()
  {
    var outcome = AuditSamplingEngine.Select(Population,
      new SamplingPlan(AuditSamplingMethods.KeyItem, KeyItemThreshold: 250m));
    Assert.Equal(["row-02", "row-04", "row-07"], outcome.Items.Select(x => x.StableRowId).ToArray());
    Assert.Equal(1650m, outcome.SelectedAbsoluteTotal);
  }

  [Fact(DisplayName = "Random sampling is reproducible from the seed and bounded by the sample size")]
  public void Random_IsReproducibleForTheSameSeed()
  {
    var plan = new SamplingPlan(AuditSamplingMethods.Random, SampleSize: 4, Seed: 20260925);
    var first = AuditSamplingEngine.Select(Population, plan);
    var second = AuditSamplingEngine.Select(Population, plan);
    Assert.Equal(4, first.Items.Count);
    Assert.Equal(first.Items.Select(x => x.StableRowId), second.Items.Select(x => x.StableRowId));
    Assert.Equal(first.SelectedAbsoluteTotal, second.SelectedAbsoluteTotal);

    // A different seed produces a different selection; the generator is not a fixed order.
    var other = AuditSamplingEngine.Select(Population,
      new SamplingPlan(AuditSamplingMethods.Random, SampleSize: 4, Seed: 7));
    Assert.Equal(4, other.Items.Count);
    Assert.NotEqual(first.Items.Select(x => x.StableRowId), other.Items.Select(x => x.StableRowId));
  }

  [Fact(DisplayName = "Random sampling cannot select more items than the population holds")]
  public void Random_ClampsToPopulationSize()
  {
    var outcome = AuditSamplingEngine.Select(Population,
      new SamplingPlan(AuditSamplingMethods.Random, SampleSize: 99, Seed: 1));
    Assert.Equal(10, outcome.Items.Count);
    Assert.Equal(100m, outcome.CoveragePercent);
  }

  [Fact(DisplayName = "Stratified sampling includes all key items then fills from the seeded remainder")]
  public void Stratified_CombinesKeyItemsAndSeededDraw()
  {
    var plan = new SamplingPlan(AuditSamplingMethods.Stratified,
      KeyItemThreshold: 250m, SampleSize: 5, Seed: 42);
    var outcome = AuditSamplingEngine.Select(Population, plan);
    Assert.Equal(5, outcome.Items.Count);
    // All three key items are always present.
    foreach (var key in new[] { "row-02", "row-04", "row-07" })
      Assert.Contains(outcome.Items, x => x.StableRowId == key && x.InclusionReason.StartsWith("Key item"));
    // The remaining two come from the non-key stratum.
    Assert.Equal(2, outcome.Items.Count(x => x.InclusionReason.StartsWith("Stratified random")));
    var repeat = AuditSamplingEngine.Select(Population, plan);
    Assert.Equal(outcome.Items.Select(x => x.StableRowId), repeat.Items.Select(x => x.StableRowId));
  }

  [Fact(DisplayName = "Zero-value and settled rows never inflate coverage and bad plans are rejected")]
  public void ZeroRowsIgnoredAndInvalidPlansRejected()
  {
    var withZero = Population.Append(new SamplingPopulationItem("row-00", 0m)).ToArray();
    var outcome = AuditSamplingEngine.Select(withZero,
      new SamplingPlan(AuditSamplingMethods.KeyItem, KeyItemThreshold: 1000m));
    Assert.Empty(outcome.Items);
    Assert.Equal(0m, outcome.CoveragePercent);
    Assert.Equal(10, outcome.PopulationCount); // the zero row is excluded from the tested population

    Assert.Throws<ArgumentException>(() => AuditSamplingEngine.Select(Population,
      new SamplingPlan("NOT_A_METHOD")));
    Assert.Throws<ArgumentException>(() => AuditSamplingEngine.Select(Population,
      new SamplingPlan(AuditSamplingMethods.MonetaryUnit)));
    Assert.Throws<ArgumentException>(() => AuditSamplingEngine.Select(Population,
      new SamplingPlan(AuditSamplingMethods.Random, SampleSize: 3)));
    Assert.Throws<ArgumentException>(() => AuditSamplingEngine.Select(Population,
      new SamplingPlan(AuditSamplingMethods.KeyItem)));
    var duplicated = new[] { new SamplingPopulationItem("dup", 10m), new SamplingPopulationItem("dup", 20m) };
    Assert.Throws<ArgumentException>(() => AuditSamplingEngine.Select(duplicated,
      new SamplingPlan(AuditSamplingMethods.KeyItem, KeyItemThreshold: 5m)));
  }

  [Fact(DisplayName = "Negative adjustments are selected on absolute exposure and totals stay signed")]
  public void NegativeAmountsUseAbsoluteExposure()
  {
    var population = new[]
    {
      new SamplingPopulationItem("credit-1", -800m),
      new SamplingPopulationItem("debit-1", 300m),
      new SamplingPopulationItem("debit-2", 100m)
    };
    var outcome = AuditSamplingEngine.Select(population,
      new SamplingPlan(AuditSamplingMethods.KeyItem, KeyItemThreshold: 500m));
    var item = Assert.Single(outcome.Items);
    Assert.Equal("credit-1", item.StableRowId);
    Assert.Equal(-800m, item.SignedAmount);       // signed value is preserved for the auditor
    Assert.Equal(800m, item.AbsoluteAmount);      // selection works on absolute exposure
    Assert.Equal(-400m, outcome.PopulationSignedTotal);
    Assert.Equal(1200m, outcome.PopulationAbsoluteTotal);
  }
}
