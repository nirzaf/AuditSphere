using AuditSphereOps.Application.Operations;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

/// <summary>Shared period check after client authorization; creation also calls it under the client safety lock.</summary>
internal static class EngagementPeriodConflictQuery
{
  internal const string ErrorCode = "engagement.period-overlap";
  internal const string Message = "An engagement for this client and service overlaps the requested period. Inspect the existing engagement and resolve any amendment explicitly before creating another.";

  internal static Task<bool> HasOverlapAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId,
    string serviceRoute, string start, string end, CancellationToken ct) =>
    db.Engagements.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.PracticeClientId == clientId &&
      x.ServiceRoute == serviceRoute && string.Compare(x.PeriodStart, end) <= 0 &&
      string.Compare(x.PeriodEnd, start) >= 0, ct);
}
