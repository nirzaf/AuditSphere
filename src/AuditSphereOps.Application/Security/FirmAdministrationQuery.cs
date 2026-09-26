using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Security;

public sealed record FirmAdministrationSnapshot(
  FirmSafetyState? SafetyState,
  List<RoleGrant> Grants,
  Dictionary<Guid, AppUser> UsersById,
  Dictionary<Guid, UserAccessInvitation> InvitationsByGrant,
  Dictionary<(string TenantId, string Subject), DirectoryUserObservation> DirectoryByIdentity,
  Dictionary<Guid, List<RoleGrantChangeEvidence>> GrantHistoryByGrant);

/// <summary>Current firm-wide administration projection; every read rechecks the active grant.</summary>
public static class FirmAdministrationQuery
{
  public static async Task<CommandResult<UserAccessInvitation>> GetCopyableInvitationAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invitationId, CancellationToken ct = default)
  {
    if (invitationId == Guid.Empty)
      return CommandResult<UserAccessInvitation>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
      InternalOnly: true, RequireFirmWide: true);
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!authorization.Succeeded)
      return CommandResult<UserAccessInvitation>.Fail(authorization.ErrorCode!, authorization.Message!);
    var invitation = await db.UserAccessInvitations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == invitationId && x.FirmId == actor.FirmId, ct);
    if (invitation is null)
      return CommandResult<UserAccessInvitation>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var activeGrant = await db.RoleGrants.AsNoTracking().AnyAsync(x =>
      x.Id == invitation.RoleGrantId && x.FirmId == actor.FirmId &&
      x.UserId == invitation.UserId && x.RevokedAt == null, ct);
    if (!activeGrant)
      return CommandResult<UserAccessInvitation>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    authorization = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    return authorization.Succeeded
      ? CommandResult<UserAccessInvitation>.Ok(invitation)
      : CommandResult<UserAccessInvitation>.Fail(authorization.ErrorCode!, authorization.Message!);
  }

  public static async Task<CommandResult<FirmAdministrationSnapshot>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
      InternalOnly: true, RequireFirmWide: true);
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!authorization.Succeeded)
      return CommandResult<FirmAdministrationSnapshot>.Fail(authorization.ErrorCode!, authorization.Message!);

    var safetyState = await db.FirmSafetyStates.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == actor.FirmId, ct);
    var grants = await db.RoleGrants.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderBy(x => x.Role).ThenBy(x => x.UserId).ToListAsync(ct);
    var userIds = grants.Select(x => x.UserId).Distinct().ToArray();
    var usersById = await db.Users.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var grantIds = grants.Select(x => x.Id).ToArray();
    var invitationsByGrant = await db.UserAccessInvitations.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && grantIds.Contains(x.RoleGrantId))
      .ToDictionaryAsync(x => x.RoleGrantId, ct);
    var observations = await db.DirectoryUserObservations.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.ObservedAt).ToListAsync(ct);
    var directoryByIdentity = observations.GroupBy(x => (x.TenantId, x.ObjectId))
      .ToDictionary(x => x.Key, x => x.First());
    var grantHistory = await db.RoleGrantChangeEvidences.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.RoleGrantId.HasValue && grantIds.Contains(x.RoleGrantId.Value))
      .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    var historyByGrant = grantHistory.GroupBy(x => x.RoleGrantId!.Value)
      .ToDictionary(x => x.Key, x => x.ToList());

    authorization = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!authorization.Succeeded)
      return CommandResult<FirmAdministrationSnapshot>.Fail(authorization.ErrorCode!, authorization.Message!);
    return CommandResult<FirmAdministrationSnapshot>.Ok(new(safetyState, grants, usersById,
      invitationsByGrant, directoryByIdentity, historyByGrant));
  }
}
