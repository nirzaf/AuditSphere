using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ChartRevisionItem(Guid Id, string Version, string SourceScope, string Status, string EffectiveFrom,
  string? EffectiveTo, Guid PreparedBy, Guid? PublishedBy, string? PublishedAt);
public sealed record ChartWorkspace(Guid ClientId, IReadOnlyList<ChartRevisionItem> Items, bool HasMore);
public static class ChartWorkspaceQuery
{
  public static async Task<CommandResult<ChartWorkspace>> GetAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, clientId,
      RequiredRoles: ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"], InternalOnly: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<ChartWorkspace>.Fail(ErrorCodes.ScopeDenied, "Charts unavailable.");
    var charts = await db.ClientChartVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId)
      .OrderByDescending(x => x.Version).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<ChartWorkspace>.Fail(ErrorCodes.ScopeDenied, "Charts unavailable.");
    return CommandResult<ChartWorkspace>.Ok(new(clientId, charts.Take(100).Select(x => new ChartRevisionItem(x.Id,
      x.Version.ToString(CultureInfo.InvariantCulture), x.SourceScope, x.Status, x.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
      x.EffectiveTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.CreatedByUserId, x.PublishedByUserId,
      x.PublishedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))).ToArray(), charts.Count > 100));
  }
}
