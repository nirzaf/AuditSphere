// Authorization decision enforced inside commands (§§8.1, 8.3, 8.6, 28.3).
// Presentation layers (Blazor/HTTP) call commands; they never authorize by themselves.
// Every protected command resolves the actor from the trusted session, re-reads current
// access/assignment/holds under the command transaction, and denies cross-firm/client
// access with a nondisclosing scope error. Workers declare an authority mode instead.
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Security;

public sealed record AuthorizationRequest(
  Guid FirmId,
  Guid? ClientId = null,
  Guid? EngagementId = null,
  string[]? RequiredRoles = null,
  bool InternalOnly = false,
  bool RequireProfessionalWork = false);

public static class AuthorizationDecision
{
  public static async Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    AuthorizationRequest request,
    CancellationToken ct = default)
  {
    if (actor.UserId == Guid.Empty || actor.FirmId == Guid.Empty || request.FirmId == Guid.Empty)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (actor.FirmId != request.FirmId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Cross-firm access denied.");

    var user = await db.Users.AsNoTracking()
      .SingleOrDefaultAsync(u => u.Id == actor.UserId, ct);
    if (user is null || user.FirmId != request.FirmId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (user.Disabled)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access disabled.");
    if (user.SessionEpoch != actor.SessionEpoch)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "Session is stale; sign in again.");

    // Scope linkage is resolved from stored records, never from a browser-supplied name/filter.
    Guid? effectiveClientId = request.ClientId;
    if (request.ClientId.HasValue)
    {
      var clientOk = await db.PracticeClients.AsNoTracking()
        .AnyAsync(c => c.Id == request.ClientId.Value && c.FirmId == request.FirmId, ct);
      if (!clientOk)
        return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    }
    if (request.EngagementId.HasValue)
    {
      var engagement = await db.Engagements.AsNoTracking()
        .SingleOrDefaultAsync(e => e.Id == request.EngagementId.Value && e.FirmId == request.FirmId, ct);
      if (engagement is null)
        return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      if (request.ClientId.HasValue && engagement.PracticeClientId != request.ClientId.Value)
        return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      effectiveClientId ??= engagement.PracticeClientId;
    }

    // Assignment: at least one active grant must cover the requested scope.
    var grants = await db.RoleGrants.AsNoTracking()
      .Where(g => g.UserId == actor.UserId && g.FirmId == request.FirmId && g.RevokedAt == null)
      .Select(g => new { g.Role, g.ClientId, g.EngagementId })
      .ToListAsync(ct);
    bool CoversScope(Guid? grantClient, Guid? grantEngagement)
    {
      if (request.EngagementId.HasValue)
        return (grantClient is null && grantEngagement is null)
          || (grantEngagement == request.EngagementId)
          || (grantClient.HasValue && grantClient == effectiveClientId && grantEngagement is null);
      if (effectiveClientId.HasValue)
        return (grantClient is null && grantEngagement is null)
          || (grantClient == effectiveClientId && grantEngagement is null);
      return true;
    }
    var covering = grants.Where(g => CoversScope(g.ClientId, g.EngagementId)).ToList();
    if (covering.Count == 0)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (request.RequiredRoles is { Length: > 0 })
    {
      var allowed = new HashSet<string>(request.RequiredRoles, StringComparer.OrdinalIgnoreCase);
      if (!covering.Any(g => allowed.Contains(g.Role)))
        return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    }

    // Client users never reach internal-only targets through any command path.
    if (request.InternalOnly &&
        (user.UserKind.Equals("Client", StringComparison.OrdinalIgnoreCase)
         || actor.Roles.Contains("ClientUser", StringComparer.OrdinalIgnoreCase)))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    if (request.RequireProfessionalWork)
    {
      if (!request.EngagementId.HasValue)
        return CommandResult.Fail(ErrorCodes.GateBlocked, "Professional work requires an engagement scope.");
      var engagement = await db.Engagements.AsNoTracking()
        .SingleOrDefaultAsync(e => e.Id == request.EngagementId.Value && e.FirmId == request.FirmId, ct);
      if (engagement is null)
        return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      if (engagement.ProfessionalWorkBlocked)
        return CommandResult.Fail(ErrorCodes.GateBlocked, "Professional work is blocked on this engagement.");
      var hold = await db.EngagementHolds.AsNoTracking()
        .AnyAsync(h => h.FirmId == request.FirmId && h.EngagementId == request.EngagementId.Value && !h.Released, ct);
      if (hold)
        return CommandResult.Fail(ErrorCodes.GateBlocked, "An unreleased hold blocks professional work.");
    }

    return CommandResult.Ok();
  }
}
