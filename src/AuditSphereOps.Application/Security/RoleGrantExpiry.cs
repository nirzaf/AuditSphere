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
    // Authentication calls this on every request. Avoid serializing the ordinary
    // no-expiry path; the locked query below is authoritative when work exists.
    if (!await db.RoleGrants.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.UserId == userId &&
          x.RevokedAt == null && x.ExpiresAt != null && x.ExpiresAt <= now, ct))
      return false;
    await using var transaction = db.Database.CurrentTransaction is null
      ? await db.Database.BeginTransactionAsync(ct)
      : null;
    // Same root fence as access administration and protected commercial mutations.
    // Keep firm -> user -> grants order, including when staffing owns the transaction.
    if (await db.FirmSafetyStates.FromSqlInterpolated(
          $"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct) is null)
      throw new InvalidOperationException("The firm safety state is unavailable for grant expiry.");
    var user = await db.Users.FromSqlInterpolated(
      $"SELECT * FROM users WHERE firm_id = {firmId} AND id = {userId} FOR UPDATE").SingleAsync(ct);
    await db.Users.Attach(user).ReloadAsync(ct);
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
      if (transaction is not null) await transaction.CommitAsync(ct);
      return false;
    }

    foreach (var grant in expired)
    {
      await db.RoleGrants.Attach(grant).ReloadAsync(ct);
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
    if (transaction is not null) await transaction.CommitAsync(ct);
    return true;
  }
}
