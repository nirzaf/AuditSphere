using AuditSphereOps.Application.Audit;

namespace AuditSphereOps.Domain.Tests;

public sealed class MaterialityPracticalRoundingTests
{
  private static readonly MaterialityFigures Computed = new(100_000m, 10, 1_000m, 750m, 50m);

  [Fact]
  public void UnchangedThresholdsAreAcceptedWithZeroDelta()
  {
    var (adjustment, error) = MaterialityPracticalRounding.Evaluate(Computed, 1_000m, 750m, 50m);

    Assert.Null(error);
    Assert.NotNull(adjustment);
    Assert.Equal((0m, 0m, 0m), (adjustment.PlanningDeltaPercent, adjustment.TolerableDeltaPercent, adjustment.SadDeltaPercent));
  }

  [Theory]
  [InlineData(1050.00, 5.0)]   // +5.0000% exactly on the boundary
  [InlineData(950.00, -5.0)]   // -5.0000% exactly on the boundary
  public void BoundaryAdjustmentsOfExactlyFivePercentAreAccepted(double proposedPm, double expectedDeltaPercent)
  {
    var (adjustment, error) = MaterialityPracticalRounding.Evaluate(Computed, (decimal)proposedPm, 750m, 50m);

    Assert.Null(error);
    Assert.NotNull(adjustment);
    Assert.Equal((decimal)expectedDeltaPercent, adjustment.PlanningDeltaPercent);
  }

  [Theory]
  [InlineData(1_050.01)]  // +5.001%
  [InlineData(949.99)]    // -5.001%
  public void AdjustmentsJustBeyondFivePercentAreRejected(decimal proposedPm)
  {
    var (adjustment, error) = MaterialityPracticalRounding.Evaluate(Computed, proposedPm, 750m, 50m);

    Assert.Null(adjustment);
    Assert.Contains("±5%", error);
  }

  [Fact]
  public void ToleranceAndSadAreBoundedIndependently()
  {
    // Tolerable error 750 -> 787.50 is exactly +5%; 750 -> 787.51 is over the limit.
    Assert.Null(MaterialityPracticalRounding.Evaluate(Computed, 1_000m, 787.50m, 50m).Error);
    Assert.NotNull(MaterialityPracticalRounding.Evaluate(Computed, 1_000m, 787.51m, 50m).Error);
    // SAD 50 -> 47.50 is exactly -5%; 50 -> 47.49 is over the limit.
    Assert.Null(MaterialityPracticalRounding.Evaluate(Computed, 1_000m, 750m, 47.50m).Error);
    Assert.NotNull(MaterialityPracticalRounding.Evaluate(Computed, 1_000m, 750m, 47.49m).Error);
  }

  [Fact]
  public void RejectsBreakingTheHierarchyEvenWhenEachAmountIsWithinBounds()
  {
    // TE 990 is within bounds of 990, but PM 950 (-5%) is below it.
    var pmBelowTe = MaterialityPracticalRounding.Evaluate(new MaterialityFigures(100_000m, 10, 1_000m, 990m, 50m), 950m, 990m, 50m);
    Assert.Null(pmBelowTe.Adjustment);
    Assert.Contains("SAD <= Tolerable Error <= Planning Materiality", pmBelowTe.Error);

    // SAD 51 is within 5% of 49 but exceeds TE 50.
    var sadAboveTe = MaterialityPracticalRounding.Evaluate(new MaterialityFigures(100_000m, 10, 1_000m, 50m, 49m), 1_000m, 50m, 51m);
    Assert.Null(sadAboveTe.Adjustment);
    Assert.Contains("SAD <= Tolerable Error <= Planning Materiality", sadAboveTe.Error);
  }

  [Theory]
  [InlineData(0, 750, 50)]
  [InlineData(1_000, -750, 50)]
  [InlineData(1_000, 750, 0)]
  public void RejectsZeroOrNegativeThresholds(decimal pm, decimal te, decimal sad)
  {
    var (adjustment, error) = MaterialityPracticalRounding.Evaluate(Computed, pm, te, sad);

    Assert.Null(adjustment);
    Assert.Contains("positive", error);
  }

  [Fact]
  public void FailsClosedWhenTheComputedBasisIsNotPositive()
  {
    var (adjustment, error) = MaterialityPracticalRounding.Evaluate(new MaterialityFigures(0m, 0, 0m, 0m, 0m), 1m, 1m, 1m);

    Assert.Null(adjustment);
    Assert.Contains("computed thresholds must be positive", error);
  }

  [Fact]
  public void DeltaPercentagesAreServerComputedFromTheStoredCalculation()
  {
    var (adjustment, error) = MaterialityPracticalRounding.Evaluate(Computed, 1_020m, 735m, 49m);

    Assert.Null(error);
    Assert.NotNull(adjustment);
    Assert.Equal(2m, adjustment.PlanningDeltaPercent);
    Assert.Equal(-2m, adjustment.TolerableDeltaPercent);
    Assert.Equal(-2m, adjustment.SadDeltaPercent);
  }
}
