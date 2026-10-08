namespace AuditSphereOps.Application.Audit;

/// <summary>
/// Server-computed result of a manager's practical rounding. Deltas are derived here from the engine's stored
/// calculation, never from a UI-supplied percentage.
/// </summary>
public sealed record PracticalRoundingAdjustment(
  decimal PlanningMateriality, decimal TolerableError, decimal SadThreshold,
  decimal PlanningDeltaPercent, decimal TolerableDeltaPercent, decimal SadDeltaPercent);

/// <summary>
/// Pure STE 3.2 practical rounding of computed Planning Materiality, Tolerable Error and SAD thresholds. Each adjusted
/// threshold must stay within ±5% of its computed value, all three must be positive, and the hierarchy
/// SAD &lt;= TE &lt;= PM must hold. The ±5% test is exact decimal arithmetic (no floating point or display rounding), so
/// +5.0000% and -5.0000% pass while any amount beyond the boundary fails. No EF, clock or random input.
/// </summary>
public static class MaterialityPracticalRounding
{
  public const decimal LimitPercent = 5m;

  /// <summary>Returns the server-computed adjustment, or a human-readable error that fails closed.</summary>
  public static (PracticalRoundingAdjustment? Adjustment, string? Error) Evaluate(
    MaterialityFigures computed, decimal proposedPlanningMateriality, decimal proposedTolerableError, decimal proposedSadThreshold)
  {
    if (computed.PlanningMateriality <= 0 || computed.TolerableError <= 0 || computed.SadThreshold <= 0)
      return (null, "The computed thresholds must be positive before practical rounding can be applied.");

    // Thresholds are whole-currency amounts at 2dp, the precision the engine stores, so re-reading them compares equal.
    var pm = Math.Round(proposedPlanningMateriality, 2, MidpointRounding.ToEven);
    var te = Math.Round(proposedTolerableError, 2, MidpointRounding.ToEven);
    var sad = Math.Round(proposedSadThreshold, 2, MidpointRounding.ToEven);
    if (pm <= 0 || te <= 0 || sad <= 0)
      return (null, "Practical thresholds must be positive amounts.");
    if (!(sad <= te && te <= pm))
      return (null, "Practical thresholds must satisfy SAD <= Tolerable Error <= Planning Materiality.");

    var pmDelta = Check("Planning materiality", computed.PlanningMateriality, pm);
    if (pmDelta.Error is not null) return (null, pmDelta.Error);
    var teDelta = Check("Tolerable error", computed.TolerableError, te);
    if (teDelta.Error is not null) return (null, teDelta.Error);
    var sadDelta = Check("SAD threshold", computed.SadThreshold, sad);
    if (sadDelta.Error is not null) return (null, sadDelta.Error);

    return (new PracticalRoundingAdjustment(pm, te, sad, pmDelta.Percent, teDelta.Percent, sadDelta.Percent), null);
  }

  private static (decimal Percent, string? Error) Check(string label, decimal computed, decimal proposed)
  {
    var delta = proposed - computed;
    // Exact comparison: |delta| / computed <= 5% is |delta| * 100 <= 5 * computed.
    if (Math.Abs(delta) * 100m > LimitPercent * computed)
      return (0m, $"{label} may be rounded by at most ±{LimitPercent:0.####}% of the computed value ({computed:N2}); {proposed:N2} is outside that bound.");
    var percent = Math.Round(delta * 100m / computed, 4, MidpointRounding.AwayFromZero);
    return (percent, null);
  }
}
