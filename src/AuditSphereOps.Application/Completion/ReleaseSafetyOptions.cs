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
    bool allowSimulationAdapters,
    string environmentName,
    bool liveAuditRelease = false)
  {
    // A release checkpoint writes only to the explicitly configured local append-only store.
    // It is not permission to compose mail, M365 or another provider in the API host.
    if (liveAuditRelease && environmentName is not ("Acceptance" or "Production"))
      throw new InvalidOperationException("LiveAuditRelease requires an isolated Acceptance or approved Production host.");

    if (liveAuditRelease && allowSimulationAdapters)
      throw new InvalidOperationException("Live audit release cannot use simulation adapters.");

    if (environmentName == "Production" && allowSimulationAdapters)
      throw new InvalidOperationException("Simulation adapters are forbidden in production.");
  }
}
