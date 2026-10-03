using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class ResourcePlanningService
{
  // Firm publication lock precedes identity locks, matching staffing and role administration.
  // Standalone methods own a transaction; a composed command must roll back its entire caller transaction on failure.
  internal static async Task<bool> LockPublicationAsync(IAuditSphereDbContext db, ActorContext actor, Guid targetUser,
    AuthorizationRequest request, CancellationToken ct)
  {
    if (await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return false;
    if (request.ClientId is Guid client && await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE firm_id={actor.FirmId} AND id={client} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return false;
    if (await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null) return false;
    var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={targetUser} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    return user is { Disabled:false, UserKind:"Staff" } &&
      (await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded;
  }
}
