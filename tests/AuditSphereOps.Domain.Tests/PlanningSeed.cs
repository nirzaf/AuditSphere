using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

internal sealed record PlanningScope(Guid FirmId, Guid ClientId, Guid EngagementId, ActorContext Actor);

internal sealed record PlanningFixture(PlanningScope Primary, PlanningScope Other);

/// <summary>
/// Synthetic planning scopes: two unrelated clients, each with its own engagement, plus a staff
/// actor holding a covering role grant. Roles alone never bypass the in-command authorization check.
/// </summary>
internal static class PlanningSeed
{
  public static async Task<PlanningFixture> CreateAsync(
    PgTestSchema pg, string role = "Senior", bool blocked = false, bool withHold = false)
  {
    var firmId = Guid.NewGuid();
    var (first, second) = (NewScope(), NewScope());
    var userId = Guid.NewGuid();

    await using var db = new AuditSphereDbContext(pg.Options);
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.PracticeClients.AddRange(
      new Practice.PracticeClient { Id = first.Client, FirmId = firmId, LegalName = "CLIENT A " + first.Client.ToString("N")[..8], CreatedAt = DateTimeOffset.UtcNow },
      new Practice.PracticeClient { Id = second.Client, FirmId = firmId, LegalName = "CLIENT B " + second.Client.ToString("N")[..8], CreatedAt = DateTimeOffset.UtcNow });
    db.ClientSafetyStates.AddRange(
      new ClientSafetyState { Id = first.Client, FirmId = firmId },
      new ClientSafetyState { Id = second.Client, FirmId = firmId });
    db.Engagements.AddRange(
      new Engagement { Id = first.Engagement, FirmId = firmId, PracticeClientId = first.Client, CreatedAt = DateTimeOffset.UtcNow, ProfessionalWorkBlocked = blocked },
      new Engagement { Id = second.Engagement, FirmId = firmId, PracticeClientId = second.Client, CreatedAt = DateTimeOffset.UtcNow });
    db.Users.Add(new AppUser
    {
      Id = userId, FirmId = firmId, Subject = "planning-" + userId.ToString("N"),
      TenantId = "tenant-planning", Email = "planner@example.test", DisplayName = "Audit Senior",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = firmId, UserId = userId, Role = role,
      ClientId = first.Client, EngagementId = first.Engagement,
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = userId
    });
    if (withHold)
    {
      db.EngagementHolds.Add(new EngagementHold
      {
        Id = Guid.NewGuid(), FirmId = firmId, EngagementId = first.Engagement,
        HoldKind = "Integrity", Reason = "Fixture hold",
        CreatedAt = DateTimeOffset.UtcNow, Released = false
      });
    }
    await db.SaveChangesAsync();

    var actor = new ActorContext(userId, firmId, 1, [role]);
    return new PlanningFixture(
      new PlanningScope(firmId, first.Client, first.Engagement, actor),
      new PlanningScope(firmId, second.Client, second.Engagement, actor));
  }

  private static (Guid Client, Guid Engagement) NewScope() => (Guid.NewGuid(), Guid.NewGuid());
}
