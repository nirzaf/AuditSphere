using AuditSphereOps.Application.Operations;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Security;

/// <summary>
/// Expiry is enforced by real revocation (append-only evidence + session-epoch bump), so every
/// consumer that reads active grants stays consistent without its own expiry filter.
/// </summary>
public static class RoleGrantExpiry
{
  public static readonly Guid SystemActor = Guid.Empty;

  /// <summary>Revokes this user's expired grants. Returns true when access changed (sessions are stale).</summary>
  public static async Task<bool> RevokeExpiredForUserAsync(IAuditSphereDbContext db, Guid firmId, Guid userId,
    DateTimeOffset now, CancellationToken ct = default)
  {
    var expired = await db.RoleGrants.Where(x => x.FirmId == firmId && x.UserId == userId &&
      x.RevokedAt == null && x.ExpiresAt != null && x.ExpiresAt <= now).ToListAsync(ct);
    if (expired.Count == 0) return false;
    var user = await db.Users.SingleAsync(x => x.Id == userId && x.FirmId == firmId, ct);
    foreach (var grant in expired)
    {
      grant.RevokedAt = now;
      db.RoleGrantChangeEvidences.Add(new Domain.Security.RoleGrantChangeEvidence
      {
        Id = Guid.CreateVersion7(), FirmId = firmId, TargetUserId = userId, RoleGrantId = grant.Id,
        Action = "REVOKED", PriorRole = grant.Role, PriorClientId = grant.ClientId,
        PriorEngagementId = grant.EngagementId, NewRole = "EXPIRED", Source = "EXPIRY",
        Reason = "Grant reached its approved expiry", ActorUserId = SystemActor, CreatedAt = now
      });
    }
    user.SessionEpoch++;
    await db.SaveChangesAsync(ct);
    return true;
  }
}
