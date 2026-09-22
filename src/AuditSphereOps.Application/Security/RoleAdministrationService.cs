using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Security;

public sealed record EnsureStaffUserRequest(
  string TenantId,
  string Subject,
  string Email,
  string DisplayName,
  string Source);

public sealed record EnsureUserRequest(
  string TenantId,
  string Subject,
  string Email,
  string DisplayName,
  string UserKind,
  string Source);

public sealed record ApplyRoleGrantRequest(
  Guid UserId,
  string Role,
  string ScopeKind,
  Guid? ClientId = null,
  Guid? EngagementId = null);

public sealed record RevokeRoleGrantRequest(Guid GrantId, Guid? ReplacementAdministratorUserId = null);

public sealed record ApplyRoleGrantAndInvitationRequest(
  Guid UserId,
  string Role,
  string ScopeKind,
  Guid? ClientId = null,
  Guid? EngagementId = null);

public sealed record UserAccessAssignmentResult(Guid UserId, Guid RoleGrantId, Guid InvitationId,
  string DestinationPath, string DeliveryState);

/// <summary>Local role administration only; it never creates or changes Entra directory roles.</summary>
public static class RoleAdministrationService
{
  private static readonly string[] AllowedRoles =
    ["Administrator", "Partner", "Manager", "Staff", "RelationshipManager", "FinanceManager", "FinanceReviewer", "ClientUser"];

  public static IReadOnlyList<string> RoleCodes => AllowedRoles;

  public static async Task<CommandResult<Guid>> EnsureStaffUserAsync(
    IAuditSphereDbContext db, ActorContext actor, EnsureStaffUserRequest request, CancellationToken ct = default)
    => await EnsureUserAsync(db, actor,
      new(request.TenantId, request.Subject, request.Email, request.DisplayName, "Staff", request.Source), ct);

  public static async Task<CommandResult<Guid>> EnsureUserAsync(
    IAuditSphereDbContext db, ActorContext actor, EnsureUserRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.Subject) ||
        string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName) ||
        request.UserKind is not ("Staff" or "Client") ||
        request.Source is not ("APPROVED_ROSTER" or "VERIFIED_SIGN_IN" or "BOOTSTRAP"))
      return CommandResult<Guid>.Fail("roles.invalid", "Tenant, immutable subject, email, user kind and approved identity source are required.");
    if (!EmailLike(request.Email)) return CommandResult<Guid>.Fail("roles.invalid", "The staff email is invalid.");
    var auth = await FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var existing = await db.Users.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.TenantId == request.TenantId.Trim() && x.Subject == request.Subject.Trim(), ct);
    var emailConflict = await db.Users.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.Email.ToUpper() == request.Email.Trim().ToUpper() &&
      (existing == null || x.Id != existing.Id), ct);
    if (emailConflict)
      return CommandResult<Guid>.Fail("roles.identity-conflict", "The email is already bound to a different immutable identity.");
    if (existing is not null)
    {
      if (!string.Equals(existing.UserKind, request.UserKind, StringComparison.Ordinal))
        return CommandResult<Guid>.Fail("roles.identity-kind-conflict", "The immutable identity is already classified differently; review it before changing classification.");
      existing.Email = request.Email.Trim();
      existing.DisplayName = request.DisplayName.Trim();
      db.DirectoryUserObservations.Add(Observation(actor.FirmId, request));
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(existing.Id);
    }

    var user = new AppUser
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TenantId = request.TenantId.Trim(),
      Subject = request.Subject.Trim(), Email = request.Email.Trim(), DisplayName = request.DisplayName.Trim(),
      UserKind = request.UserKind, CreatedAt = DateTimeOffset.UtcNow
    };
    db.Users.Add(user);
    db.RoleGrantChangeEvidences.Add(new RoleGrantChangeEvidence
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TargetUserId = user.Id,
      Action = "GRANTED", PriorRole = string.Empty, NewRole = "NONE",
      Source = request.Source, ActorUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    db.DirectoryUserObservations.Add(Observation(actor.FirmId, request));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(user.Id);
  }

  public static async Task<CommandResult<Guid>> ApplyRoleGrantAsync(
    IAuditSphereDbContext db, ActorContext actor, ApplyRoleGrantRequest request, CancellationToken ct = default)
  {
    var scopeKind = (request.ScopeKind ?? string.Empty).Trim().ToUpperInvariant();
    var role = CanonicalRole(request.Role);
    if (request.UserId == Guid.Empty || role is null)
      return CommandResult<Guid>.Fail("roles.invalid", "The target user and supported application role are required.");
    if (scopeKind is not ("FIRM_WIDE" or "CLIENT" or "ENGAGEMENT"))
      return CommandResult<Guid>.Fail("roles.invalid", "Choose an explicit firm, client or engagement scope.");
    if (scopeKind == "FIRM_WIDE" && (request.ClientId.HasValue || request.EngagementId.HasValue) ||
        scopeKind == "CLIENT" && (!request.ClientId.HasValue || request.EngagementId.HasValue) ||
        scopeKind == "ENGAGEMENT" && (!request.ClientId.HasValue || !request.EngagementId.HasValue))
      return CommandResult<Guid>.Fail("roles.invalid", "The selected scope and identifiers do not match.");
    var auth = await FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var target = await db.Users.SingleOrDefaultAsync(x => x.Id == request.UserId && x.FirmId == actor.FirmId && x.UserKind == "Staff", ct);
    if (target is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The target identity is unavailable.");
    var roleValidation = ValidateRoleForUser(target, role, scopeKind);
    if (roleValidation is not null) return CommandResult<Guid>.Fail("roles.invalid", roleValidation);
    if (request.ClientId.HasValue && !await db.PracticeClients.AsNoTracking().AnyAsync(x => x.Id == request.ClientId && x.FirmId == actor.FirmId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The client scope is unavailable.");
    if (request.EngagementId.HasValue && !await db.Engagements.AsNoTracking().AnyAsync(x =>
      x.Id == request.EngagementId && x.FirmId == actor.FirmId && x.PracticeClientId == request.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The engagement scope is unavailable.");

    var existing = await db.RoleGrants.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.UserId == target.Id && x.RevokedAt == null && x.Role == role &&
      x.ClientId == request.ClientId && x.EngagementId == request.EngagementId, ct);
    if (existing is not null) return CommandResult<Guid>.Ok(existing.Id);
    if (target.Id == actor.UserId)
      return CommandResult<Guid>.Fail("roles.self-elevation", "Another authorized administrator must approve a new role or scope for the acting administrator.");
    var grant = new RoleGrant
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = target.Id, Role = role,
      ClientId = request.ClientId, EngagementId = request.EngagementId,
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = actor.UserId
    };
    db.RoleGrants.Add(grant);
    db.RoleGrantChangeEvidences.Add(new RoleGrantChangeEvidence
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TargetUserId = target.Id, RoleGrantId = grant.Id,
      Action = "GRANTED", NewRole = role, NewClientId = request.ClientId, NewEngagementId = request.EngagementId,
      Source = "ADMIN_ACTION", ActorUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    target.SessionEpoch++;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(grant.Id);
  }

  /// <summary>Atomically records a real local grant and its copy-link invitation intent.</summary>
  public static async Task<CommandResult<UserAccessAssignmentResult>> ApplyRoleGrantAndInvitationAsync(
    IAuditSphereDbContext db, ActorContext actor, ApplyRoleGrantAndInvitationRequest request,
    CancellationToken ct = default)
  {
    var scopeKind = (request.ScopeKind ?? string.Empty).Trim().ToUpperInvariant();
    var role = CanonicalRole(request.Role);
    if (request.UserId == Guid.Empty || role is null)
      return CommandResult<UserAccessAssignmentResult>.Fail("roles.invalid", "The target user and supported application role are required.");
    if (!ValidScope(request, scopeKind))
      return CommandResult<UserAccessAssignmentResult>.Fail("roles.invalid", "The selected scope and identifiers do not match.");
    var auth = await FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<UserAccessAssignmentResult>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var target = await db.Users.SingleOrDefaultAsync(x => x.Id == request.UserId && x.FirmId == actor.FirmId, ct);
    if (target is null || target.Disabled)
      return CommandResult<UserAccessAssignmentResult>.Fail(ErrorCodes.ScopeDenied, "The target identity is unavailable.");
    var roleValidation = ValidateRoleForUser(target, role, scopeKind);
    if (roleValidation is not null)
      return CommandResult<UserAccessAssignmentResult>.Fail("roles.invalid", roleValidation);
    if (request.ClientId.HasValue && !await db.PracticeClients.AsNoTracking().AnyAsync(x => x.Id == request.ClientId && x.FirmId == actor.FirmId, ct))
      return CommandResult<UserAccessAssignmentResult>.Fail(ErrorCodes.ScopeDenied, "The client scope is unavailable.");
    if (request.EngagementId.HasValue && !await db.Engagements.AsNoTracking().AnyAsync(x =>
      x.Id == request.EngagementId && x.FirmId == actor.FirmId && x.PracticeClientId == request.ClientId, ct))
      return CommandResult<UserAccessAssignmentResult>.Fail(ErrorCodes.ScopeDenied, "The engagement scope is unavailable.");

    var grant = await db.RoleGrants.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.UserId == target.Id && x.RevokedAt == null && x.Role == role &&
      x.ClientId == request.ClientId && x.EngagementId == request.EngagementId, ct);
    if (grant is null && target.Id == actor.UserId)
      return CommandResult<UserAccessAssignmentResult>.Fail("roles.self-elevation", "Another authorized administrator must approve a new role or scope for the acting administrator.");
    if (grant is null)
    {
      grant = new RoleGrant
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = target.Id, Role = role,
        ClientId = request.ClientId, EngagementId = request.EngagementId,
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = actor.UserId
      };
      db.RoleGrants.Add(grant);
      db.RoleGrantChangeEvidences.Add(new RoleGrantChangeEvidence
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TargetUserId = target.Id, RoleGrantId = grant.Id,
        Action = "GRANTED", NewRole = role, NewClientId = request.ClientId, NewEngagementId = request.EngagementId,
        Source = "ADMIN_ACTION", ActorUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      });
      target.SessionEpoch++;
    }

    var invitation = await db.UserAccessInvitations.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.RoleGrantId == grant.Id, ct);
    if (invitation is null)
    {
      invitation = new UserAccessInvitation
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = target.Id, RoleGrantId = grant.Id,
        RecipientEmail = target.Email.Trim(), DestinationPath = "/auth/landing",
        DeliveryState = UserAccessInvitationStates.NotSent,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
      };
      db.UserAccessInvitations.Add(invitation);
    }
    else if (!string.Equals(invitation.RecipientEmail, target.Email.Trim(), StringComparison.OrdinalIgnoreCase))
    {
      invitation.RecipientEmail = target.Email.Trim();
      invitation.UpdatedAt = DateTimeOffset.UtcNow;
    }
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<UserAccessAssignmentResult>.Ok(new(target.Id, grant.Id, invitation.Id,
      invitation.DestinationPath, invitation.DeliveryState));
  }

  public static async Task<CommandResult> RevokeRoleGrantAsync(
    IAuditSphereDbContext db, ActorContext actor, RevokeRoleGrantRequest request, CancellationToken ct = default)
  {
    var auth = await FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var grant = await db.RoleGrants.SingleOrDefaultAsync(x => x.Id == request.GrantId && x.FirmId == actor.FirmId, ct);
    if (grant is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "The role grant is unavailable.");
    if (grant.RevokedAt is not null) return CommandResult.Ok();
    if (grant.Role.Equals("Administrator", StringComparison.OrdinalIgnoreCase) && grant.ClientId is null && grant.EngagementId is null)
    {
      var remaining = await db.RoleGrants.AsNoTracking().CountAsync(x =>
        x.FirmId == actor.FirmId && x.Role == "Administrator" && x.ClientId == null && x.EngagementId == null &&
        x.RevokedAt == null && x.Id != grant.Id, ct);
      if (remaining == 0)
      {
        var replacement = request.ReplacementAdministratorUserId.HasValue && await db.RoleGrants.AsNoTracking().AnyAsync(x =>
          x.FirmId == actor.FirmId && x.UserId == request.ReplacementAdministratorUserId && x.Role == "Administrator" &&
          x.ClientId == null && x.EngagementId == null && x.RevokedAt == null, ct);
        if (!replacement) return CommandResult.Fail("roles.last-admin", "A verified replacement firm administrator is required before revocation.");
      }
      if (grant.UserId == actor.UserId &&
          (!request.ReplacementAdministratorUserId.HasValue || request.ReplacementAdministratorUserId == actor.UserId))
        return CommandResult.Fail("roles.self-admin-change", "Another authorized administrator must perform this privileged self-change.");
    }
    var target = await db.Users.SingleAsync(x => x.Id == grant.UserId && x.FirmId == actor.FirmId, ct);
    grant.RevokedAt = DateTimeOffset.UtcNow;
    db.RoleGrantChangeEvidences.Add(new RoleGrantChangeEvidence
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TargetUserId = target.Id, RoleGrantId = grant.Id,
      Action = "REVOKED", PriorRole = grant.Role, PriorClientId = grant.ClientId, PriorEngagementId = grant.EngagementId,
      NewRole = "REVOKED", Source = "ADMIN_ACTION", ActorUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    target.SessionEpoch++;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> MarkInvitationCopiedAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invitationId, CancellationToken ct = default)
  {
    if (invitationId == Guid.Empty) return CommandResult.Fail("invitation.invalid", "The invitation is required.");
    var auth = await FirmAdministratorAsync(db, actor, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var invitation = await db.UserAccessInvitations.SingleOrDefaultAsync(x =>
      x.Id == invitationId && x.FirmId == actor.FirmId, ct);
    if (invitation is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "The invitation is unavailable.");
    var active = await db.RoleGrants.AsNoTracking().AnyAsync(x => x.Id == invitation.RoleGrantId &&
      x.FirmId == actor.FirmId && x.UserId == invitation.UserId && x.RevokedAt == null, ct);
    if (!active) return CommandResult.Fail(ErrorCodes.ScopeDenied, "The invitation no longer has an active access grant.");
    invitation.DeliveryState = UserAccessInvitationStates.Copied;
    invitation.AttemptCount++;
    invitation.LastAttemptAt = DateTimeOffset.UtcNow;
    invitation.UpdatedAt = invitation.LastAttemptAt.Value;
    db.RoleGrantChangeEvidences.Add(new RoleGrantChangeEvidence
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TargetUserId = invitation.UserId,
      RoleGrantId = invitation.RoleGrantId, Action = "INVITATION_COPIED", NewRole = "NOTIFICATION",
      Source = "ADMIN_ACTION", ActorUserId = actor.UserId, CreatedAt = invitation.LastAttemptAt.Value
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  private static Task<CommandResult> FirmAdministratorAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"], InternalOnly: true, RequireFirmWide: true), ct);

  private static bool EmailLike(string value)
  {
    var trimmed = value.Trim();
    var at = trimmed.IndexOf('@');
    return at > 0 && at == trimmed.LastIndexOf('@') && at < trimmed.Length - 1 && !trimmed.Any(char.IsWhiteSpace);
  }

  private static bool ValidScope(ApplyRoleGrantRequest request, string scopeKind) =>
    scopeKind is "FIRM_WIDE" or "CLIENT" or "ENGAGEMENT" &&
    (scopeKind == "FIRM_WIDE" && request.ClientId is null && request.EngagementId is null ||
     scopeKind == "CLIENT" && request.ClientId.HasValue && request.EngagementId is null ||
     scopeKind == "ENGAGEMENT" && request.ClientId.HasValue && request.EngagementId.HasValue);

  private static bool ValidScope(ApplyRoleGrantAndInvitationRequest request, string scopeKind) =>
    scopeKind is "FIRM_WIDE" or "CLIENT" or "ENGAGEMENT" &&
    (scopeKind == "FIRM_WIDE" && request.ClientId is null && request.EngagementId is null ||
     scopeKind == "CLIENT" && request.ClientId.HasValue && request.EngagementId is null ||
     scopeKind == "ENGAGEMENT" && request.ClientId.HasValue && request.EngagementId.HasValue);

  private static string? ValidateRoleForUser(AppUser target, string role, string scopeKind)
  {
    var clientRole = role.Equals("ClientUser", StringComparison.OrdinalIgnoreCase);
    if (clientRole != target.UserKind.Equals("Client", StringComparison.OrdinalIgnoreCase))
      return clientRole ? "ClientUser must be assigned to a client identity." : "Staff roles must be assigned to a staff identity.";
    if (clientRole && scopeKind == "FIRM_WIDE")
      return "Client access must be scoped to a client or engagement.";
    return null;
  }

  private static string? CanonicalRole(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : AllowedRoles.SingleOrDefault(x => x.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

  private static DirectoryUserObservation Observation(Guid firmId, EnsureUserRequest request) => new()
  {
    Id = Guid.CreateVersion7(), FirmId = firmId, TenantId = request.TenantId.Trim(), ObjectId = request.Subject.Trim(),
    DisplayName = request.DisplayName.Trim(), Mail = request.Email.Trim(), UserPrincipalName = request.Email.Trim(),
    EnabledState = request.Source == "VERIFIED_SIGN_IN" ? "ENABLED" : "UNKNOWN", Source = request.Source,
    ObservedAt = DateTimeOffset.UtcNow
  };
}
