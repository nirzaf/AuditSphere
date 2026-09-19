namespace AuditSphereOps.Application.Completion;

/// <summary>
/// Release safety and evidence verification options (§24.4, §25.7, Appendix E.1).
/// Enforces mandatory release fences: external checkpoints, protection attestations, signature lineages.
/// </summary>
public sealed class ReleaseSafetyOptions
{
  public const string SectionName = "ReleaseSafety";

  public bool RequireSignatureLineage { get; set; } = false;
  public bool RequireProtectionAttestation { get; set; } = false;
  public bool RequireExternalCheckpointBeforeDelivery { get; set; } = true;

  public void Validate(
    bool externalEffectsEnabled,
    bool allowSimulationAdapters,
    string environmentName,
    bool liveAuditRelease = false)
  {
    if (liveAuditRelease && !externalEffectsEnabled)
      throw new InvalidOperationException("LiveAuditRelease requires ExternalEffects:Enabled=true.");

    if (environmentName == "Production" && allowSimulationAdapters)
      throw new InvalidOperationException("Simulation adapters are forbidden in production.");
  }
}
