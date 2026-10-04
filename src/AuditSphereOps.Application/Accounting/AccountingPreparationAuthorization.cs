using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Accounting;

/// <summary>Shared scope gate for practitioner preparation workspaces and their engagement navigation hints.</summary>
internal static class AccountingPreparationAuthorization
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];

  internal static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid clientId, Guid engagementId, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, clientId, engagementId, Roles, InternalOnly: true), ct);
}
