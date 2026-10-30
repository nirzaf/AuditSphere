using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class PortfolioQuery
{
  private sealed record Scope(bool FirmWide, Guid[] Clients, Guid[] Engagements, List<AuthorizationRequest> Requests);

  private static async Task<Scope?> ScopeAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct,
    string[]? requiredRoles = null, bool caseInsensitiveRoles = false)
  {
    var roles = requiredRoles ?? Roles;
    var grantsQuery = db.RoleGrants.AsNoTracking().Where(g => g.FirmId == actor.FirmId &&
      g.UserId == actor.UserId && g.RevokedAt == null);
    List<(Guid? ClientId, Guid? EngagementId)> grants;
    if (caseInsensitiveRoles)
    {
      var candidates = await grantsQuery.Select(g => new { g.Role, g.ClientId, g.EngagementId }).ToListAsync(ct);
      grants = candidates.Where(g => roles.Contains(g.Role, StringComparer.OrdinalIgnoreCase))
        .Select(g => (g.ClientId, g.EngagementId)).Distinct().ToList();
    }
    else
    {
      var candidates = await grantsQuery.Where(g => roles.Contains(g.Role))
        .Select(g => new { g.ClientId, g.EngagementId }).Distinct().ToListAsync(ct);
      grants = candidates.Select(g => (g.ClientId, g.EngagementId)).ToList();
    }
    var requests = new List<AuthorizationRequest>();
    foreach (var grant in grants)
    {
      var request = new AuthorizationRequest(actor.FirmId, grant.ClientId, grant.EngagementId, roles,
        InternalOnly: true, RequireFirmWide: grant.ClientId is null && grant.EngagementId is null);
      if ((await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded) requests.Add(request);
    }
    return requests.Count == 0 ? null : new(requests.Any(r => r.RequireFirmWide),
      requests.Where(r => r.ClientId.HasValue && !r.EngagementId.HasValue).Select(r => r.ClientId!.Value).ToArray(),
      requests.Where(r => r.EngagementId.HasValue).Select(r => r.EngagementId!.Value).ToArray(), requests);
  }

  internal static async Task<CommandResult> AuthorizeWorkspaceReadAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: Roles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;
    var now = DateTimeOffset.UtcNow;
    var exists = await db.RoleGrants.AsNoTracking().AnyAsync(g => g.FirmId == actor.FirmId && g.UserId == actor.UserId &&
      g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now) && Roles.Contains(g.Role) &&
      ((g.ClientId == null && g.EngagementId == null) ||
       (g.ClientId != null && g.EngagementId == null && db.PracticeClients.Any(c => c.FirmId == actor.FirmId && c.Id == g.ClientId)) ||
       (g.ClientId != null && g.EngagementId != null && db.Engagements.Any(e => e.FirmId == actor.FirmId &&
         e.Id == g.EngagementId && e.PracticeClientId == g.ClientId))), ct);
    return exists ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.ScopeDenied, "Access unavailable.");
  }

  private static IQueryable<Engagement> VisibleEngagements(IAuditSphereDbContext db, ActorContext actor, Scope scope) =>
    db.Engagements.AsNoTracking().Where(e => e.FirmId == actor.FirmId &&
      (scope.FirmWide || scope.Clients.Contains(e.PracticeClientId) || scope.Engagements.Contains(e.Id)));

  private static IQueryable<PracticeClient> VisibleClients(IAuditSphereDbContext db, ActorContext actor, Scope scope)
  {
    var engagements = VisibleEngagements(db, actor, scope);
    return db.PracticeClients.AsNoTracking().Where(c => c.FirmId == actor.FirmId &&
      (scope.FirmWide || scope.Clients.Contains(c.Id) || engagements.Any(e => e.PracticeClientId == c.Id)));
  }

  private static async Task<bool> CurrentAsync(IAuditSphereDbContext db, ActorContext actor, Scope scope, CancellationToken ct)
  {
    foreach (var request in scope.Requests)
      if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded) return false;
    return true;
  }
}
