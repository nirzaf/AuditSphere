using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

/// <summary>Publication locks for assessment commands: firm, client, then actor. Callers own a joined transaction.</summary>
internal static class AcceptanceCommandAuthority
{
  public static async Task<bool> LockFirmAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    await db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is not null;

  // The caller has already acquired the client row lock. A stale identity cannot publish after waiting for either lock.
  public static async Task<CommandResult> CheckAsync(IAuditSphereDbContext db, ActorContext actor,
    AuthorizationRequest request, CancellationToken ct)
  {
    if (await db.Users.FromSqlInterpolated(
      $"SELECT * FROM users WHERE id = {actor.UserId} AND firm_id = {actor.FirmId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
  }
}
