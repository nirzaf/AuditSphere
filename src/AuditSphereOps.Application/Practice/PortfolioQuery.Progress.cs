using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class PortfolioQuery
{
  public const int ProgressPageSize = 10;
  private const int ClientProgressPageSize = 50;

  /// <summary>A bounded page over current progress-reading scope; broader portfolio-only roles do not affect its total.</summary>
  public static Task<CommandResult<EngagementProgressList>> ProgressAsync(IAuditSphereDbContext db, ActorContext actor,
    int page = 0, CancellationToken ct = default) => ReadProgressPageAsync(db, actor, null, page, ProgressPageSize, ct);

  internal static Task<CommandResult<EngagementProgressList>> ProgressForClientAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid clientId, int page, CancellationToken ct) => ReadProgressPageAsync(db, actor, clientId, page, ClientProgressPageSize, ct);

  private static async Task<CommandResult<EngagementProgressList>> ReadProgressPageAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid? clientId, int page, int pageSize, CancellationToken ct)
  {
    if (page is < 0 or > 10000) return CommandResult<EngagementProgressList>.Fail("request.invalid", "Choose a valid progress page.");
    var scope = await ScopeAsync(db, actor, ct, EngagementProgressQuery.ReadRoles);
    if (scope is null) return CommandResult<EngagementProgressList>.Fail(ErrorCodes.ScopeDenied, "Progress unavailable.");
    var visible = VisibleEngagements(db, actor, scope);
    if (clientId is { } client)
    {
      if (!await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == client, ct) ||
          (!scope.FirmWide && !scope.Clients.Contains(client) &&
            !await visible.AnyAsync(x => x.PracticeClientId == client, ct)))
        return CommandResult<EngagementProgressList>.Fail(ErrorCodes.ScopeDenied, "Client progress unavailable.");
      visible = visible.Where(x => x.PracticeClientId == client);
    }
    var total = await visible.CountAsync(ct);
    var candidates = await visible.OrderByDescending(x => x.PeriodEnd).ThenBy(x => x.Id)
      .Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
    var permitted = await EngagementProgressQuery.PermittedAsync(db, actor, candidates, ct);
    var views = await EngagementProgressQuery.BuildAsync(db, actor, permitted, includeSteps: false, ct);
    if ((await EngagementProgressQuery.PermittedAsync(db, actor, permitted, ct)).Count != permitted.Count ||
        !await CurrentAsync(db, actor, scope, ct))
      return CommandResult<EngagementProgressList>.Fail(ErrorCodes.ScopeDenied, "Progress unavailable.");
    return CommandResult<EngagementProgressList>.Ok(new(views, total, page, pageSize));
  }
}
