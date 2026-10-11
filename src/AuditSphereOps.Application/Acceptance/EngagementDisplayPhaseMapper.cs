using System.Diagnostics.CodeAnalysis;

namespace AuditSphereOps.Application.Acceptance;

/// <summary>
/// One ordinary-language workflow phase. Phases are presentation only: a phase never advances an engagement and never
/// satisfies a protected gate. Each phase owns a fixed set of canonical stages (ADR-0004 remains the sole stage authority).
/// </summary>
public sealed record EngagementDisplayPhase(int Index, string Code, string Label, IReadOnlyList<string> CanonicalStages);

/// <summary>
/// Pure, deterministic mapping from the eleven canonical lifecycle stages to the seven visible workflow phases
/// (WF-01). It performs no I/O and reads no clock. Unknown stage codes, or an index that disagrees with the canonical
/// table, fail closed: callers must not invent a phase for them.
/// </summary>
public static class EngagementDisplayPhaseMapper
{
  public const int PhaseCount = 7;

  private static readonly IReadOnlyDictionary<string, int> CanonicalIndexes = new Dictionary<string, int>(StringComparer.Ordinal)
  {
    [CanonicalEngagementStages.LeadIngestion] = 1,
    [CanonicalEngagementStages.ProposalGeneration] = 2,
    [CanonicalEngagementStages.DualKeyPending] = 3,
    [CanonicalEngagementStages.AdvanceBilling] = 4,
    [CanonicalEngagementStages.PortalActivePlanning] = 5,
    [CanonicalEngagementStages.FieldworkExecution] = 6,
    [CanonicalEngagementStages.ManagerialReview] = 7,
    [CanonicalEngagementStages.PartnerApproval] = 8,
    [CanonicalEngagementStages.DeliverableRelease] = 9,
    [CanonicalEngagementStages.ComplianceCountdown] = 10,
    [CanonicalEngagementStages.ArchivedReadOnly] = 11,
  };

  /// <summary>The seven visible phases in order. Every canonical stage appears in exactly one phase.</summary>
  public static readonly IReadOnlyList<EngagementDisplayPhase> Phases =
  [
    new(1, "CLIENT_ONBOARDING", "Client Onboarding",
      [CanonicalEngagementStages.LeadIngestion, CanonicalEngagementStages.ProposalGeneration,
       CanonicalEngagementStages.DualKeyPending, CanonicalEngagementStages.AdvanceBilling]),
    new(2, "AUDIT_PLANNING", "Audit Planning", [CanonicalEngagementStages.PortalActivePlanning]),
    new(3, "AUDIT_FIELDWORK", "Audit Fieldwork", [CanonicalEngagementStages.FieldworkExecution]),
    new(4, "MANAGER_REVIEW", "Manager Review", [CanonicalEngagementStages.ManagerialReview]),
    new(5, "PARTNER_SIGNOFF", "Partner Sign-off", [CanonicalEngagementStages.PartnerApproval]),
    new(6, "REPORT_DELIVERY", "Report and Delivery", [CanonicalEngagementStages.DeliverableRelease]),
    new(7, "ARCHIVE_CLOSE", "Archive and Close",
      [CanonicalEngagementStages.ComplianceCountdown, CanonicalEngagementStages.ArchivedReadOnly]),
  ];

  private static readonly IReadOnlyDictionary<string, EngagementDisplayPhase> PhaseByStage = BuildPhaseByStage();

  /// <summary>
  /// Resolves a validated canonical stage and index to its visible phase. Returns false, with no phase, when the stage code is
  /// unknown or the index does not match the canonical table.
  /// </summary>
  public static bool TryResolve(string? canonicalStage, int canonicalIndex, [NotNullWhen(true)] out EngagementDisplayPhase? phase)
  {
    phase = null;
    if (canonicalStage is null || !CanonicalIndexes.TryGetValue(canonicalStage, out var expectedIndex) ||
      expectedIndex != canonicalIndex || !PhaseByStage.TryGetValue(canonicalStage, out var found))
      return false;
    phase = found;
    return true;
  }

  private static IReadOnlyDictionary<string, EngagementDisplayPhase> BuildPhaseByStage()
  {
    var map = new Dictionary<string, EngagementDisplayPhase>(StringComparer.Ordinal);
    foreach (var phase in Phases)
      foreach (var stage in phase.CanonicalStages)
        map.Add(stage, phase);
    return map;
  }
}
