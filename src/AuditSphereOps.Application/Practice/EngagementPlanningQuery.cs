using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Application.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record PlanningBudgetRow(string Phase, string RiskArea, int ForecastMinutes,
  string ForecastCost, int ActualMinutes, string ActualCost);
public sealed record PlanningBudget(string Id, string Version, string Currency, IReadOnlyList<PlanningBudgetRow> Rows,
  int ForecastMinutes, string ForecastCost, int ActualMinutes, string ActualCost);
public sealed record EngagementPlanning(IReadOnlyList<StaffAssignmentRow> Team, PlanningBudget? Budget, string BudgetState,
  bool CanManageStaffing, IReadOnlyList<StaffCandidate> Candidates, string LatestBudgetVersion, PlanningDraft? Draft);
public sealed record PlanningDraft(Guid Id, string Version, string Currency, bool CanApprove,
  IReadOnlyList<PlanningDraftLine> Lines);
public sealed record PlanningDraftLine(string Role, string Activity, string Phase, string? RiskArea, int Minutes, string Cost);

/// <summary>Browser projection of existing planning services; financial quantities remain exact decimal strings.</summary>
public static class EngagementPlanningQuery
{
  public static async Task<CommandResult<EngagementPlanning>> GetAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var workspace = await WorkspaceQuery.EngagementAsync(db, actor, engagementId, ct);
    if (!workspace.Succeeded) return CommandResult<EngagementPlanning>.Fail(workspace.ErrorCode!, "Planning unavailable.");
    var team = await StaffingService.ListAsync(db, actor, engagementId, ct);
    if (!team.Succeeded) return CommandResult<EngagementPlanning>.Fail(team.ErrorCode!, "Planning unavailable.");
    var budget = await PracticeTimeService.GetBudgetBreakdownAsync(db, actor, engagementId, ct);
    if (!budget.Succeeded && budget.ErrorCode != ErrorCodes.GateBlocked)
      return CommandResult<EngagementPlanning>.Fail(budget.ErrorCode!, "Planning unavailable.");
    PlanningBudget? projection = null;
    if (budget.Succeeded)
    {
      var b = budget.Value!;
      static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);
      var currency = await db.EngagementBudgets.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == b.BudgetId)
        .Select(x => x.Currency).SingleAsync(ct);
      projection = new(b.BudgetId.ToString(), b.BudgetVersion.ToString(CultureInfo.InvariantCulture), currency,
        b.Rows.Select(r => new PlanningBudgetRow(r.Phase, r.RiskArea, r.ForecastMinutes,
          Exact(r.ForecastCost), r.ActualMinutes, Exact(r.ActualCost))).ToArray(),
        b.ForecastMinutes, Exact(b.ForecastCost), b.ActualMinutes, Exact(b.ActualCost));
    }
    var final = await WorkspaceQuery.EngagementAsync(db, actor, engagementId, ct);
    if (!final.Succeeded) return CommandResult<EngagementPlanning>.Fail(final.ErrorCode!, "Planning unavailable.");
    var canManage = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: engagementId,
        RequiredRoles: ["Partner", "Manager", "Administrator"], InternalOnly: true), ct)).Succeeded;
    var candidates = canManage ? (await StaffingService.CandidatesAsync(db, actor, ct, limit: 100))
      .Where(c => team.Value!.All(t => t.UserId != c.UserId)).Take(100).ToArray() : [];
    var latest = await db.EngagementBudgets.AsNoTracking().Where(b => b.FirmId == actor.FirmId && b.EngagementId == engagementId)
      .OrderByDescending(b => b.Version).FirstOrDefaultAsync(ct);
    PlanningDraft? draft = null;
    if (canManage && latest?.Status == AuditSphereOps.Domain.Practice.PracticeTimeStates.BudgetDraft)
    {
      var lines = await db.BudgetLines.AsNoTracking().Where(l => l.FirmId == actor.FirmId && l.EngagementBudgetId == latest.Id)
        .OrderBy(l => l.Phase).ThenBy(l => l.Role).Take(200).ToListAsync(ct);
      draft = new(latest.Id, latest.Version.ToString(CultureInfo.InvariantCulture), latest.Currency,
        latest.CreatedByUserId != actor.UserId, lines.Select(l => new PlanningDraftLine(l.Role, l.Activity, l.Phase,
          l.RiskArea, l.ForecastMinutes, l.ForecastCost.ToString(CultureInfo.InvariantCulture))).ToArray());
    }
    if (!(await WorkspaceQuery.EngagementAsync(db, actor, engagementId, ct)).Succeeded)
      return CommandResult<EngagementPlanning>.Fail(ErrorCodes.ScopeDenied, "Planning unavailable.");
    return CommandResult<EngagementPlanning>.Ok(new(team.Value!, projection, budget.Succeeded ? "APPROVED" : "UNAVAILABLE", canManage,
      candidates, (latest?.Version ?? 0).ToString(CultureInfo.InvariantCulture), draft));
  }

  public static async Task<CommandResult> ApproveBudgetAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid engagementId, Guid budgetId, CancellationToken ct = default)
  {
    if (!await db.EngagementBudgets.AsNoTracking().AnyAsync(b => b.FirmId == actor.FirmId && b.EngagementId == engagementId && b.Id == budgetId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Budget unavailable.");
    return await PracticeTimeService.ApproveBudgetAsync(db, actor, budgetId, ct);
  }

  public static async Task<CommandResult> RevokeAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid engagementId, Guid assignmentId, CancellationToken ct = default)
  {
    if (!await db.EngagementStaffAssignments.AsNoTracking().AnyAsync(a => a.FirmId == actor.FirmId &&
      a.EngagementId == engagementId && a.Id == assignmentId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Assignment unavailable.");
    return await StaffingService.RevokeAsync(db, actor, assignmentId, ct);
  }
}
