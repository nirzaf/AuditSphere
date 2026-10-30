using System.Text.RegularExpressions;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using System.Data;

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

public sealed record EngagementWorkflowPhaseState(int Index, string Code, string Label, string Status, DateOnly? OperationalDueDate);

public sealed record EngagementWorkflowNextAction(string Key, string Label, string Href);

public sealed record EngagementWorkflowArchiveState(
  string? LocalArchiveState, string? ProviderProtectionState, string? ComplianceWarning, int? ComplianceCountdownDays);

/// <summary>
/// Seven-phase engagement projection (WF-01). It carries the exact canonical stage for audit traceability, the display phase
/// derived from it, blockers from the authoritative evaluator, open-task due dates, and at most one role-authorized
/// allowlisted internal next action.
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
  IReadOnlyList<string> ResponsibleUsers,
  IReadOnlyList<string> Blockers,
  IReadOnlyList<EngagementWorkflowNextAction> NextActions,
  EngagementWorkflowArchiveState? Archive,
  DateTimeOffset EvaluatedAtUtc,
  IReadOnlyList<EngagementLifecycleBlockerDetail>? BlockerDetails = null);

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
    if (AuthorizationDecision.HasCallerTransaction(db))
      return CommandResult<EngagementWorkflowReport>.Fail(ErrorCodes.GateBlocked,
        "Workflow requires an independent authorization snapshot.");
    var lifecycle = await EngagementLifecycleQuery.GetAsync(db, actor, engagementId, ct);
    if (!lifecycle.Succeeded) return CommandResult<EngagementWorkflowReport>.Fail(lifecycle.ErrorCode!, lifecycle.Message!);
    var report = lifecycle.Value!;
    var summary = report.Summary;

    if (!EngagementDisplayPhaseMapper.TryResolve(summary.CanonicalStage, summary.StageIndex, out var phase))
      return CommandResult<EngagementWorkflowReport>.Fail(ErrorCodes.GateBlocked,
        "The engagement stage could not be projected to a workflow phase.");

    var hasBlockers = summary.BlockedReasons.Count > 0;
    var blockers = summary.BlockedReasons;
    var operationalDueDates = await OperationalDueDatesAsync(db, actor.FirmId, report.EngagementId, ct);
    var phases = EngagementDisplayPhaseMapper.Phases
      .Select(p => new EngagementWorkflowPhaseState(p.Index, p.Code, p.Label,
        PhaseStatus(p.Index, phase.Index, summary.CanonicalStage, hasBlockers), operationalDueDates.GetValueOrDefault(p.Code)))
      .ToList();
    var current = phases[phase.Index - 1];

    var archive = summary.LocalArchiveState is null && summary.ProviderProtectionState is null && summary.ComplianceWarning is null
      ? null
      : new EngagementWorkflowArchiveState(summary.LocalArchiveState, summary.ProviderProtectionState,
        summary.ComplianceWarning, summary.ComplianceCountdownDays);

    // Preliminary checks retain existing admission behavior; only the final shared snapshot selects output.
    if (hasBlockers) await EngagementLifecycleQuery.AuthorizeDetailedBlockersAsync(db, actor, engagementId, ct);
    if (InternalRoute.IsMatch(summary.DeepLink))
      await CanOpenNextActionAsync(db, actor, report.ClientId, report.EngagementId, summary.ResponsibleRole, ct);
    var read = await EngagementLifecycleQuery.AuthorizeReadAsync(db, actor, engagementId, ct);
    if (!read.Succeeded) return CommandResult<EngagementWorkflowReport>.Fail(read.ErrorCode!, read.Message!);
    var (actionRequest, staffingLevel) = ActionRequest(actor, report.ClientId, report.EngagementId, summary.ResponsibleRole);
    if (!InternalRoute.IsMatch(summary.DeepLink)) actionRequest = null;
    AuthorizationDecision.EngagementSnapshot authority;
    IReadOnlyList<string> responsibleUsers;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      authority = await EngagementLifecycleQuery.CaptureProjectionAsync(db, actor, report.ClientId, report.EngagementId,
        actionRequest, staffingLevel, ct);
      responsibleUsers = await ResponsibleUsersAsync(db, actor.FirmId, report.EngagementId, summary.ResponsibleRole, ct);
      await snapshot.CommitAsync(ct);
    }
    var final = authority.Evaluate(DateTimeOffset.UtcNow);
    if (!final.Read.Succeeded) return CommandResult<EngagementWorkflowReport>.Fail(final.Read.ErrorCode!, final.Read.Message!);
    var detailStillAuthorized = hasBlockers && final.CanViewDetails;
    var canOpenNextAction = final.CanOpenAction;
    var nextActions = canOpenNextAction
      ? new List<EngagementWorkflowNextAction>
      {
        new("OPEN_CANONICAL_ACTION", $"Open: {summary.StageLabel}", summary.DeepLink),
      }
      : new List<EngagementWorkflowNextAction>();
    var blockerDetails = summary.BlockerDetails;
    if (hasBlockers && !detailStillAuthorized)
    {
      blockers = [EngagementLifecycleQuery.RestrictedBlockers];
      blockerDetails = null;
    }

    return CommandResult<EngagementWorkflowReport>.Ok(new EngagementWorkflowReport(
      report.EngagementId, report.ClientId, report.ServiceRoute,
      summary.CanonicalStage, summary.StageIndex, summary.StageLabel,
      current, phases, summary.ResponsibleRole, responsibleUsers, blockers, nextActions, archive, report.EvaluatedAtUtc,
      blockerDetails));
  }

  /// <summary>
  /// A phase date is the earliest due date on a current, unfinished operational task. It is deliberately labelled as an
  /// operational date; it does not invent a statutory deadline or satisfy a canonical lifecycle gate.
  /// </summary>
  private static async Task<Dictionary<string, DateOnly?>> OperationalDueDatesAsync(
    IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct)
  {
    var dates = await (from binding in db.WorkflowTaskBindings.AsNoTracking()
      join task in db.WorkTasks.AsNoTracking()
        on new { binding.FirmId, WorkTaskId = binding.WorkTaskId }
        equals new { task.FirmId, WorkTaskId = task.Id }
      where binding.FirmId == firmId && binding.EngagementId == engagementId && binding.SupersededAt == null &&
        task.DueDate != null && (task.Status == PracticeTimeStates.TaskOpen || task.Status == PracticeTimeStates.TaskInProgress)
      group task by binding.Phase into phaseTasks
      select new { Phase = phaseTasks.Key, DueDate = phaseTasks.Min(task => task.DueDate) })
      .ToDictionaryAsync(x => x.Phase, x => x.DueDate, ct);
    return dates;
  }

  /// <summary>
  /// The canonical stage proposes a route; a current grant and, for staffed engagement roles, the matching live assignment
  /// determine whether this caller gets it. Downstream commands remain authoritative and reauthorize on every action.
  /// </summary>
  private static async Task<bool> CanOpenNextActionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId, string responsibleRole, CancellationToken ct)
  {
    var (request, staffingLevel) = ActionRequest(actor, clientId, engagementId, responsibleRole);
    if (request is null) return false;
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!auth.Succeeded) return false;
    return staffingLevel is null || await db.EngagementStaffAssignments.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId &&
      x.UserId == actor.UserId && x.StaffingLevel == staffingLevel && x.RevokedAt == null, ct);
  }

  private static (AuthorizationRequest? Request, string? StaffingLevel) ActionRequest(
    ActorContext actor, Guid clientId, Guid engagementId, string responsibleRole)
  {
    var staffingLevel = responsibleRole switch
    {
      "EngagementPartner" => StaffingLevels.EngagementPartner,
      "AuditManager" => StaffingLevels.AuditManager,
      "SeniorAuditor" => StaffingLevels.SeniorAuditor,
      _ => null,
    };
    if (staffingLevel is not null)
      return (new(actor.FirmId, clientId, engagementId, [StaffingLevels.AuthorizationRole(staffingLevel)], InternalOnly: true), staffingLevel);

    if (responsibleRole is not ("CommercialManager" or "FinanceManager")) return (null, null);
    return (new(actor.FirmId, clientId, engagementId, [responsibleRole], InternalOnly: true,
      RequireFirmWide: responsibleRole == "FinanceManager"), null);
  }

  /// <summary>
  /// The people currently staffed on this engagement at the level that owns the current step. It is a display fact taken from
  /// live staffing: it grants nothing, and it is empty when the step belongs to a firm role outside engagement staffing
  /// (commercial or finance) or when nobody is staffed at that level.
  /// </summary>
  private static async Task<IReadOnlyList<string>> ResponsibleUsersAsync(
    IAuditSphereDbContext db, Guid firmId, Guid engagementId, string responsibleRole, CancellationToken ct)
  {
    var level = responsibleRole switch
    {
      "EngagementPartner" => StaffingLevels.EngagementPartner,
      "AuditManager" => StaffingLevels.AuditManager,
      "SeniorAuditor" => StaffingLevels.SeniorAuditor,
      _ => null,
    };
    if (level is null) return [];
    return await (from assignment in db.EngagementStaffAssignments.AsNoTracking()
      join user in db.Users.AsNoTracking() on new { assignment.FirmId, assignment.UserId } equals new { user.FirmId, UserId = user.Id }
      where assignment.FirmId == firmId && assignment.EngagementId == engagementId && assignment.RevokedAt == null &&
        assignment.StaffingLevel == level && !user.Disabled
      orderby user.DisplayName, user.Id
      select user.DisplayName).Take(20).ToListAsync(ct);
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
