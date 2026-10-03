using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Synthetic engagement history. Holds do not establish professional acceptance or Microsoft evidence.</summary>
internal static class EngagementProfileWorkspaceSeed
{
  internal static async Task PopulateAsync(AuditSphereDbContext db, PbcSeed.Fixture f, int count = 27)
  {
    var now = new DateTimeOffset(2026, 1, 2, 12, 30, 0, TimeSpan.Zero);
    var e = await db.Engagements.SingleAsync(e => e.Id == f.EngagementId);
    e.ServiceRoute = "Synthetic scoped audit"; e.ServiceProfileId = "Synthetic annual audit profile";
    e.PeriodStart = "2026-01-01"; e.PeriodEnd = "2026-12-31";
    e.CreatedAt = now; e.Generation = 9007199254740993;
    for (var i = 0; i < count; i++)
      db.EngagementHolds.Add(new EngagementHold { Id = Guid.NewGuid(), FirmId = f.FirmId, EngagementId = f.EngagementId,
        HoldKind = "Acceptance", Reason = $"Synthetic clearance {i:000}", CreatedAt = now.AddMinutes(count - i),
        Released = i % 2 == 0, ReleasedAt = i % 2 == 0 ? now.AddDays(1) : null });
    await db.SaveChangesAsync();
  }
}
