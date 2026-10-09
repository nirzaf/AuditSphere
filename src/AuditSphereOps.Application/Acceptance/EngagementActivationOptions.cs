namespace AuditSphereOps.Application.Acceptance;

/// <summary>
/// Advance-payment enforcement modes for the Partner activation gate (STE v2.1 §4.1.5, mandatory control C-02).
/// </summary>
public static class AdvanceGateModes
{
  /// <summary>
  /// Requirement mode: a linked engagement fee agreement carrying a fully paid and allocated 50% advance is mandatory
  /// before activation. This is the fail-closed default and the only mode that satisfies control C-02.
  /// </summary>
  public const string Always = "ALWAYS";

  /// <summary>
  /// Recorded deviation: the advance is required only when a fee agreement is already linked to the engagement. A linked
  /// agreement always gates; an engagement with no agreement may activate.
  /// </summary>
  public const string WhenFeeAgreementLinked = "WHEN_FEE_AGREEMENT_LINKED";

  public static bool IsValid(string? mode) => mode is Always or WhenFeeAgreementLinked;
}

/// <summary>
/// The activation gate's advance-payment policy, bound from the <c>EngagementActivation</c> configuration section.
/// STE v2.1 §4.1.5 and control C-02 make the recorded 50% advance an unconditional hard block, so
/// <see cref="AdvanceGateModes.Always"/> is the default. Weakening the gate is an explicit, configured deviation, never
/// an implicit one: an unset, empty or unrecognised mode still enforces the requirement.
/// </summary>
public sealed class EngagementActivationOptions
{
  public const string SectionName = "EngagementActivation";

  public string AdvanceGateMode { get; set; } = AdvanceGateModes.Always;

  /// <summary>Refuses an unknown mode at startup rather than silently weakening or bypassing the gate.</summary>
  public void Validate()
  {
    if (!AdvanceGateModes.IsValid(AdvanceGateMode))
      throw new InvalidOperationException(
        $"{SectionName}:AdvanceGateMode must be '{AdvanceGateModes.Always}' or '{AdvanceGateModes.WhenFeeAgreementLinked}'.");
  }
}
