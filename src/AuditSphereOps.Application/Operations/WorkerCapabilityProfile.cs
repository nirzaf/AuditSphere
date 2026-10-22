using AuditSphereOps.Application.Documents;

namespace AuditSphereOps.Application.Operations;

/// <summary>The worker composition a host may build, with explicit reasons when it may not.</summary>
public sealed record WorkerCompositionDecision(
  bool DevelopmentOrTest, bool SafeCore, bool LiveMail, bool LivePbc, bool ClientSitesWorker, IReadOnlyList<string> Blockers)
{
  public bool Approved => Blockers.Count == 0 && (DevelopmentOrTest || SafeCore || LiveMail || LivePbc || ClientSitesWorker);
  public bool ExternalEffectsComposed => LiveMail || LivePbc || ClientSitesWorker;
}

/// <summary>
/// Pure environment/group decision for the worker host (WU-007). Acceptance keeps its narrow live groups.
/// Production composes a live group only when that group is individually approved and simulation adapters are
/// off; core groups never carry external effects. Provider credential and resource checks stay with each provider.
/// </summary>
public static class WorkerCapabilityProfile
{
  public const string MailGroup = "mail";
  private static readonly string[] CoreGroups = ["general", "processing", "records"];
  private static readonly string[] LiveGroups = [MailGroup, PbcDocumentTransferHandler.LiveGroup, ClientSharePointSiteHandler.Group];

  public static WorkerCompositionDecision Evaluate(string environment, string group, bool externalEffects,
    bool allowSimulationAdapters, IReadOnlySet<string> approvedLiveGroups)
  {
    if (environment is "Development" or "Test")
      return new(true, false, false, false, false, []);

    var blockers = new List<string>();
    var core = CoreGroups.Contains(group, StringComparer.Ordinal);
    var live = LiveGroups.Contains(group, StringComparer.Ordinal);
    var acceptance = environment == "Acceptance";
    var production = environment == "Production";
    if (!acceptance && !production) blockers.Add($"environment-not-approved:{environment}");
    if (!core && !live) blockers.Add($"worker-group-unknown:{group}");
    if (core && externalEffects) blockers.Add($"external-effects-not-approved-for-group:{group}");
    if (production && live)
    {
      if (!approvedLiveGroups.Contains(group)) blockers.Add($"production-live-group-not-approved:{group}");
      if (allowSimulationAdapters) blockers.Add("simulation-adapters-forbidden-in-production");
    }
    // The mail and pbc groups exist only to carry their external effect.
    if (live && group != ClientSharePointSiteHandler.Group && !externalEffects) blockers.Add($"live-group-requires-external-effects:{group}");
    if (blockers.Count > 0) return new(false, false, false, false, false, blockers);

    var safeCore = core && !externalEffects;
    return new(false, safeCore, group == MailGroup, group == PbcDocumentTransferHandler.LiveGroup,
      group == ClientSharePointSiteHandler.Group, []);
  }
}
