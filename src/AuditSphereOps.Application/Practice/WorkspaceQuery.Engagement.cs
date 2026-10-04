using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class WorkspaceQuery
{
  public static async Task<CommandResult<EngagementWorkspace>> EngagementAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid id, CancellationToken ct = default, EngagementWorkspacePaging? paging = null)
  {
    // Older retained UI builds use the original bounded window; native paging stays deliberately small.
    var legacyWindow = paging is null;
    paging ??= new(0, 100);
    if (paging.HoldPage is < 0 or > 10000 || !legacyWindow && paging.HoldPageSize is not (10 or 25 or 50))
      return CommandResult<EngagementWorkspace>.Fail("request.invalid", "Invalid hold page.");
    var request = new AuthorizationRequest(actor.FirmId, EngagementId: id, RequiredRoles: EngagementRoles, InternalOnly: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded) return UnavailableEngagement();
    var row = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(e => e.FirmId == actor.FirmId && e.Id == id, ct);
    if (row is null) return UnavailableEngagement();
    var clientName = await db.PracticeClients.AsNoTracking().Where(c => c.FirmId == actor.FirmId && c.Id == row.PracticeClientId)
      .Select(c => c.LegalName).SingleOrDefaultAsync(ct);
    if (clientName is null) return UnavailableEngagement();
    var query = db.EngagementHolds.AsNoTracking().Where(h => h.FirmId == actor.FirmId && h.EngagementId == id);
    var counts = await query.GroupBy(h => 1).Select(g => new EngagementHoldMetrics(g.Count(),
      g.Count(h => !h.Released), g.Count(h => h.Released))).SingleOrDefaultAsync(ct) ?? new(0, 0, 0);
    var holds = await query.OrderByDescending(h => h.CreatedAt).ThenBy(h => h.Id)
      .Skip(paging.HoldPage * paging.HoldPageSize).Take(paging.HoldPageSize)
      .Select(h => new WorkspaceHold(h.HoldKind, h.Reason, h.Released, h.CreatedAt, h.ReleasedAt, h.Id)).ToListAsync(ct);
    var activation = new AuthorizationRequest(actor.FirmId, EngagementId: id, RequiredRoles: ["Partner"], InternalOnly: true);
    var canActivate = row.Status == "Draft" && (await AuthorizationDecision.AuthorizeAsync(db, actor, activation, ct)).Succeeded;
    var clientProfile = new AuthorizationRequest(actor.FirmId, ClientId: row.PracticeClientId, RequiredRoles: ClientRoles, InternalOnly: true);
    var canViewClient = (await AuthorizationDecision.AuthorizeAsync(db, actor, clientProfile, ct)).Succeeded;
    var canPrepareAccounting = (await AccountingPreparationAuthorization.AuthorizeAsync(db, actor,
      row.PracticeClientId, row.Id, ct)).Succeeded;
    // Every advertised authority is rechecked after the projection; client navigation never derives from engagement scope.
    if (canActivate && !(await AuthorizationDecision.AuthorizeAsync(db, actor, activation, ct)).Succeeded ||
        canViewClient && !(await AuthorizationDecision.AuthorizeAsync(db, actor, clientProfile, ct)).Succeeded ||
        canPrepareAccounting && !(await AccountingPreparationAuthorization.AuthorizeAsync(db, actor,
          row.PracticeClientId, row.Id, ct)).Succeeded ||
        !(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded) return UnavailableEngagement();
    return CommandResult<EngagementWorkspace>.Ok(new(row.Id, row.PracticeClientId, clientName, row.ServiceRoute,
      row.Status, row.PeriodStart, row.PeriodEnd, row.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
      row.ProfessionalWorkBlocked, holds, canActivate, row.ServiceProfileId, row.CreatedAt, canViewClient,
      canPrepareAccounting, counts, paging));
  }

  private static CommandResult<EngagementWorkspace> UnavailableEngagement() =>
    CommandResult<EngagementWorkspace>.Fail(ErrorCodes.ScopeDenied, "Engagement unavailable.");
}
