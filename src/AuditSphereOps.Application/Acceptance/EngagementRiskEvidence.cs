using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Engagements;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

/// <summary>Risk evidence for the exact engagement; previous-period decisions are reference evidence only.</summary>
internal static class EngagementRiskEvidence
{
  internal static async Task<AcceptanceDecision?> LatestAsync(IAuditSphereDbContext db, Guid firmId,
    Engagement engagement, CancellationToken ct)
  {
    if (engagement.FirmId != firmId) return null;
    var recurring = await db.Engagements.AsNoTracking().AnyAsync(x => x.FirmId == firmId &&
      x.PracticeClientId == engagement.PracticeClientId && x.ServiceRoute == engagement.ServiceRoute &&
      x.Id != engagement.Id && x.PeriodEnd != "" && string.Compare(x.PeriodEnd, engagement.PeriodStart) < 0, ct);
    var latest = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.PracticeClientId == engagement.PracticeClientId && x.ServiceRoute == engagement.ServiceRoute &&
      x.Decision != "Pending" && (x.EngagementId == engagement.Id || x.EngagementId == null))
      .OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id)
      .FirstOrDefaultAsync(ct);
    return recurring && latest is { EngagementId: null, Decision: "Accepted" } ? null : latest;
  }

  internal static async Task<AcceptanceDecision?> ForPeriodAsync(IAuditSphereDbContext db, Guid firmId,
    Guid clientId, string route, string start, string end, CancellationToken ct)
  {
    var matches = await db.Engagements.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.PracticeClientId == clientId && x.ServiceRoute == route && x.PeriodStart == start && x.PeriodEnd == end).Take(2).ToListAsync(ct);
    if (matches.Count > 1) return null;
    var engagement = matches.SingleOrDefault();
    if (engagement is not null) return await LatestAsync(db, firmId, engagement, ct);
    // Before the first engagement shell exists, the client-level new-client evaluation remains valid.
    // A recurring letter must wait for the exact shell and its fresh, engagement-scoped decision.
    if (await db.Engagements.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.PracticeClientId == clientId &&
      x.ServiceRoute == route && x.PeriodEnd != "" && string.Compare(x.PeriodEnd, start) < 0, ct)) return null;
    return await DualKeyEvidence.LatestRiskDecisionAsync(db, firmId, clientId, route, ct);
  }
}
