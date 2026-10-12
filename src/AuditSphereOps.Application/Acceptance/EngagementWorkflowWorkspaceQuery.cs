using System.Text.RegularExpressions;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Acceptance;

/// <summary>Projected status of one visible phase. Labels only: they never satisfy a protected gate.</summary>
public static class EngagementWorkflowPhaseStatuses
{
  public const string NotStarted = "NOT_STARTED";
  public const string InProgress = "IN_PROGRESS";
  public const string Blocked = "BLOCKED";
  public const string Completed = "COMPLETED";
  public const string Archived = "ARCHIVED";
}

public sealed record EngagementWorkflowPhaseState(int Index, string Code, string Label, string Status);

public sealed record EngagementWorkflowNextAction(string Key, string Label, string Href);

public sealed record EngagementWorkflowArchiveState(
  string? LocalArchiveState, string? ProviderProtectionState, string? ComplianceWarning, int? ComplianceCountdownDays);

/// <summary>
/// Seven-phase engagement projection (WF-01). It carries the exact canonical stage for audit traceability, the display phase
/// derived from it, blockers from the authoritative evaluator, and at most one allowlisted internal next action.
/// </summary>
public sealed record EngagementWorkflowReport(
  Guid EngagementId,
  Guid ClientId,
  string ServiceRoute,
  string CanonicalStage,
  int CanonicalIndex,
  string CanonicalLabel,
  EngagementWorkflowPhaseState CurrentPhase,
  IReadOnlyList<EngagementWorkflowPhaseState> Phases,
  string ResponsibleRole,
  IReadOnlyList<string> Blockers,
  IReadOnlyList<EngagementWorkflowNextAction> NextActions,
  EngagementWorkflowArchiveState? Archive,
  DateTimeOffset EvaluatedAtUtc);

/// <summary>
/// Read-only seven-phase workflow projection. It composes <see cref="EngagementLifecycleQuery"/>, which keeps authorization,
/// engagement scope and the canonical evaluation. It persists no stage and never mutates a protected record.
/// </summary>
public static class EngagementWorkflowWorkspaceQuery
{
  // Only internal application routes may leave the projection. Dots are excluded so no "." or ".." segment can climb out of /app.
  private static readonly Regex InternalRoute = new(@"^/app(?:/[A-Za-z0-9_~-]{1,100}){0,8}\z", RegexOptions.CultureInvariant);

  public static async Task<CommandResult<EngagementWorkflowReport>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var lifecycle = await EngagementLifecycleQuery.GetAsync(db, actor, engagementId, ct);
    if (!lifecycle.Succeeded) return CommandResult<EngagementWorkflowReport>.Fail(lifecycle.ErrorCode!, lifecycle.Message!);
    var report = lifecycle.Value!;
    var summary = report.Summary;

    if (!EngagementDisplayPhaseMapper.TryResolve(summary.CanonicalStage, summary.StageIndex, out var phase))
      return CommandResult<EngagementWorkflowReport>.Fail(ErrorCodes.GateBlocked,
        "The engagement stage could not be projected to a workflow phase.");

    var hasBlockers = summary.BlockedReasons.Count > 0;
    var blockers = summary.BlockedReasons;
    var phases = EngagementDisplayPhaseMapper.Phases
      .Select(p => new EngagementWorkflowPhaseState(p.Index, p.Code, p.Label,
        PhaseStatus(p.Index, phase.Index, summary.CanonicalStage, hasBlockers)))
      .ToList();
    var current = phases[phase.Index - 1];

    var archive = summary.LocalArchiveState is null && summary.ProviderProtectionState is null && summary.ComplianceWarning is null
      ? null
      : new EngagementWorkflowArchiveState(summary.LocalArchiveState, summary.ProviderProtectionState,
        summary.ComplianceWarning, summary.ComplianceCountdownDays);

    var nextActions = InternalRoute.IsMatch(summary.DeepLink)
      ? new List<EngagementWorkflowNextAction>
      {
        new("OPEN_CANONICAL_ACTION", $"Open: {summary.StageLabel}", summary.DeepLink),
      }
      : new List<EngagementWorkflowNextAction>();

    return CommandResult<EngagementWorkflowReport>.Ok(new EngagementWorkflowReport(
      report.EngagementId, report.ClientId, report.ServiceRoute,
      summary.CanonicalStage, summary.StageIndex, summary.StageLabel,
      current, phases, summary.ResponsibleRole, blockers, nextActions, archive, report.EvaluatedAtUtc));
  }

  /// <summary>
  /// Earlier phases are complete and later phases have not started. The current phase is blocked while the canonical evaluator
  /// reports any blocker, even when every operational task is done. Archive is shown only for a committed local freeze.
  /// </summary>
  private static string PhaseStatus(int phaseIndex, int currentIndex, string canonicalStage, bool hasBlockers)
  {
    if (phaseIndex < currentIndex) return EngagementWorkflowPhaseStatuses.Completed;
    if (phaseIndex > currentIndex) return EngagementWorkflowPhaseStatuses.NotStarted;
    if (canonicalStage == CanonicalEngagementStages.ArchivedReadOnly) return EngagementWorkflowPhaseStatuses.Archived;
    return hasBlockers ? EngagementWorkflowPhaseStatuses.Blocked : EngagementWorkflowPhaseStatuses.InProgress;
  }
}
