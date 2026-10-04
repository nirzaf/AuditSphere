using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public sealed record AssessmentRoute(Guid ClientId, Guid? DecisionId);

/// <summary>
/// Resolves a legacy assessment link (a client ID or an acceptance decision ID) to the client whose acceptance workspace
/// it belongs to, only when the actor holds a current internal client grant; otherwise it is indistinguishable from missing.
/// </summary>
public static class AssessmentRouteQuery
{
  // Match the legacy AssessmentDetail staff gate. Senior/Reviewer do not gain access
  // merely because the Angular route catalogue can resolve their client IDs.
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "Staff", "EngagementLeader", "Auditor"];
  private static readonly string[] PartnerRole = ["Partner"];

  public static Task<CommandResult<AssessmentRoute>> ResolveAsync(IAuditSphereDbContext db, ActorContext actor, Guid id, CancellationToken ct = default) =>
    ResolveAsync(db, actor, id, Roles, ct);

  /// <summary>Resolves the legacy Partner decision deep link without disclosing the assessment to other staff roles.</summary>
  public static Task<CommandResult<AssessmentRoute>> ResolveDecisionAsync(IAuditSphereDbContext db, ActorContext actor, Guid id, CancellationToken ct = default) =>
    ResolveAsync(db, actor, id, PartnerRole, ct);

  private static async Task<CommandResult<AssessmentRoute>> ResolveAsync(IAuditSphereDbContext db, ActorContext actor, Guid id,
    string[] requiredRoles, CancellationToken ct)
  {
    var clientId = await db.PracticeClients.AsNoTracking().Where(c => c.Id == id && c.FirmId == actor.FirmId).Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct)
      ?? await db.AcceptanceDecisions.AsNoTracking().Where(d => d.Id == id && d.FirmId == actor.FirmId).Select(d => (Guid?)d.PracticeClientId).SingleOrDefaultAsync(ct);
    if (clientId is null) return CommandResult<AssessmentRoute>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var decisionId = clientId.Value == id ? (Guid?)null : id;
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: requiredRoles, InternalOnly: true), ct);
    return auth.Succeeded ? CommandResult<AssessmentRoute>.Ok(new(clientId.Value, decisionId)) : CommandResult<AssessmentRoute>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  }
}
