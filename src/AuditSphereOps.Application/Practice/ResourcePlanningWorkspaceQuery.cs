using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record ResourceEngagementOption(Guid Id, string Label);
public sealed record ResourcePlanningWorkspace(ResourceGridView Grid, IReadOnlyList<ResourceEngagementOption> Engagements);

/// <summary>Resource grid plus the active engagements offered for allocation; both behind the grid's planning authorization.</summary>
public static class ResourcePlanningWorkspaceQuery
{
  public static async Task<CommandResult<ResourcePlanningWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor, DateOnly firstWeek, int weeks, CancellationToken ct = default)
  {
    var grid = await ResourcePlanningService.GetGridAsync(db, actor, firstWeek, weeks, ct);
    if (!grid.Succeeded) return CommandResult<ResourcePlanningWorkspace>.Fail(grid.ErrorCode!, grid.Message!);
    var engagements = await db.Engagements.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Status == "Active")
      .Join(db.PracticeClients.AsNoTracking(), e => e.PracticeClientId, c => c.Id, (e, c) => new { e.Id, Label = (c.CommercialName ?? c.LegalName) + " · " + e.ServiceRoute + " " + e.PeriodEnd })
      .OrderBy(x => x.Label).Take(200).ToListAsync(ct);
    return CommandResult<ResourcePlanningWorkspace>.Ok(new(grid.Value!, engagements.Select(x => new ResourceEngagementOption(x.Id, x.Label)).ToList()));
  }
}
