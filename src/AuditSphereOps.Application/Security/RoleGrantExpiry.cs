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
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    // Authentication can resolve several Angular/API requests for one browser session
    // concurrently. Lock the expired grants and re-check the predicate in PostgreSQL so
    // only the first request records revocation evidence and advances the session epoch.
    var expired = await db.RoleGrants.FromSqlInterpolated($"""
      SELECT * FROM role_grants
      WHERE firm_id = {firmId} AND user_id = {userId}
        AND revoked_at IS NULL AND expires_at IS NOT NULL AND expires_at <= {now}
      FOR UPDATE
      """).ToListAsync(ct);
    if (expired.Count == 0)
    {
      await transaction.CommitAsync(ct);
      return false;
    }

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
    await transaction.CommitAsync(ct);
    return true;
  }
}
