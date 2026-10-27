using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Security;

/// <summary>
/// Explicit opt-in for audited, nonmutating projections only. Never use this for commands,
/// action previews, private work or raw-document downloads. Operational authorizers stay separate.
/// </summary>
internal static class AdministratorOversightReadAuthorization
{
  internal const string ReadOnly = "READ_ONLY";
  internal const string Reason = "Administrator oversight";

  internal static async Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct)
  {
    if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId &&
        x.UserKind == "Staff" && !x.Disabled && x.SessionEpoch == actor.SessionEpoch, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, RequiredRoles: ["Administrator"], InternalOnly: true, RequireFirmWide: true), ct);
  }

  internal static async Task<CommandResult> AuthorizeGroupAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid groupId, CancellationToken ct)
  {
    if (!await db.ClientGroups.AsNoTracking().AnyAsync(x => x.Id == groupId && x.FirmId == actor.FirmId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return await AuthorizeAsync(db, actor, ct);
  }
}
