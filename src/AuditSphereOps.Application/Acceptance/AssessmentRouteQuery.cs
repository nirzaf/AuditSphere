using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public sealed record AssessmentRoute(Guid ClientId);

/// <summary>
/// Resolves a legacy assessment link (a client ID or an acceptance decision ID) to the client whose acceptance workspace
/// it belongs to, only when the actor holds a current internal client grant; otherwise it is indistinguishable from missing.
/// </summary>
public static class AssessmentRouteQuery
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "Reviewer", "Senior", "Staff"];

  public static async Task<CommandResult<AssessmentRoute>> ResolveAsync(IAuditSphereDbContext db, ActorContext actor, Guid id, CancellationToken ct = default)
  {
    var clientId = await db.PracticeClients.AsNoTracking().Where(c => c.Id == id && c.FirmId == actor.FirmId).Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct)
      ?? await db.AcceptanceDecisions.AsNoTracking().Where(d => d.Id == id && d.FirmId == actor.FirmId).Select(d => (Guid?)d.PracticeClientId).SingleOrDefaultAsync(ct);
    if (clientId is null) return CommandResult<AssessmentRoute>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: Roles, InternalOnly: true), ct);
    return auth.Succeeded ? CommandResult<AssessmentRoute>.Ok(new(clientId.Value)) : CommandResult<AssessmentRoute>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  }
}
