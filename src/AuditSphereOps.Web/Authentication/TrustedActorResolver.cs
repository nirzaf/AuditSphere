using System.Security.Claims;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Web.Authentication;

/// <summary>Maps only authenticated Entra claims to current local authorization state.</summary>
public sealed class TrustedActorResolver(IDbContextFactory<AuditSphereDbContext> factory)
{
  public async Task<ActorContext?> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default)
  {
    if (principal.Identity?.IsAuthenticated != true)
      return null;

    var subject = principal.FindFirstValue("oid") ??
                  principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                  principal.FindFirstValue("sub");
    var tenant = principal.FindFirstValue("tid");
    if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenant))
      return null;

    await using var db = await factory.CreateDbContextAsync(ct);
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Subject == subject && x.TenantId == tenant, ct);
    if (user is null || user.Disabled)
      return null;
    var roles = await db.RoleGrants.AsNoTracking()
      .Where(x => x.FirmId == user.FirmId && x.UserId == user.Id && x.RevokedAt == null)
      .Select(x => x.Role).Distinct().ToListAsync(ct);
    return new ActorContext(user.Id, user.FirmId, user.SessionEpoch, roles);
  }
}
