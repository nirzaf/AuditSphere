using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class PortfolioQuery
{
  public const int ProgressPageSize = 10;

  /// <summary>
  /// A bounded page of engagement progress summaries across the actor's portfolio scope, newest period first. Each engagement
  /// is also checked against the progress read roles; the total counts the portfolio-visible engagements.
  /// </summary>
  public static async Task<CommandResult<EngagementProgressList>> ProgressAsync(IAuditSphereDbContext db, ActorContext actor,
    int page = 0, CancellationToken ct = default)
  {
    if (page is < 0 or > 10000) return CommandResult<EngagementProgressList>.Fail("request.invalid", "Choose a valid progress page.");
    var scope = await ScopeAsync(db, actor, ct);
    if (scope is null) return CommandResult<EngagementProgressList>.Fail(ErrorCodes.ScopeDenied, "Portfolio progress unavailable.");
    var visible = VisibleEngagements(db, actor, scope);
    var total = await visible.CountAsync(ct);
    var candidates = await visible.OrderByDescending(x => x.PeriodEnd).ThenBy(x => x.Id)
      .Skip(page * ProgressPageSize).Take(ProgressPageSize).ToListAsync(ct);
    var permitted = await EngagementProgressQuery.PermittedAsync(db, actor, candidates, ct);
    var views = await EngagementProgressQuery.BuildAsync(db, actor, permitted, includeSteps: false, ct);
    if (!await CurrentAsync(db, actor, scope, ct))
      return CommandResult<EngagementProgressList>.Fail(ErrorCodes.ScopeDenied, "Portfolio progress unavailable.");
    return CommandResult<EngagementProgressList>.Ok(new(views, total, page, ProgressPageSize));
  }
}
