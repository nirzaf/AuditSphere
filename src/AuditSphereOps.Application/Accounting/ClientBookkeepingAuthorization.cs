using AuditSphereOps.Application.Operations;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static class ClientBookkeepingAuthorization
{
  public static async Task<bool> IsCurrentDecisionAcceptedAsync(IClientAccountingDbContext db, Guid firmId,
    Guid clientId, string serviceRoute = "BOOKKEEPING", CancellationToken ct = default)
  {
    var current = await db.AcceptanceDecisions.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.PracticeClientId == clientId && x.EngagementId == null && x.ServiceRoute == serviceRoute)
      .OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id)
      .Select(x => new { x.Decision, x.Conditions }).FirstOrDefaultAsync(ct);
    return current is { Decision: "Accepted", Conditions: null or "" };
  }
}
