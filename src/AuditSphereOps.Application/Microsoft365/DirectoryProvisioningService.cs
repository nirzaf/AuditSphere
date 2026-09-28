using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record CreateTenantUserRequest(
  string IdempotencyKey,
  string DisplayName,
  string UserPrincipalName,
  string MailNickname,
  bool AccountEnabled,
  string Role,
  string ScopeKind,
  Guid? ClientId,
  Guid? EngagementId,
  string Reason);

public sealed record InviteGuestRequest(
  string IdempotencyKey,
  string Email,
  Guid ClientId,
  Guid? EngagementId,
  string Reason);

/// <summary>
/// Outcome of one external operation step. TemporaryPassword is populated only on the single call
/// that created the Microsoft user; it is never persisted, logged or returned again.
/// </summary>
public sealed record ExternalOperationResult(Guid OperationId, string State, string Message,
  Guid? BoundUserId = null, Guid? RoleGrantId = null, string? TemporaryPassword = null, string? CorrelationId = null);

/// <summary>
/// Microsoft user creation and B2B invitation (§§7, 9, 16). Microsoft effects are never treated as
/// atomic with local state: each step commits its own lifecycle state, and the local AppUser/RoleGrant
/// binding runs only after Microsoft accepted or reconciliation proved the exact immutable identity.
/// </summary>
public static partial class DirectoryProvisioningService
{
  private static readonly TimeSpan ReconcileNotFoundDelay = TimeSpan.FromMinutes(2);

  public static async Task<CommandResult<ExternalOperationResult>> CreateTenantUserAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftDirectoryUserProvisioner provisioner,
    TenantAdministrationOptions options, CreateTenantUserRequest request, DateTimeOffset now,
    CancellationToken ct = default)
  {
    var upn = (request.UserPrincipalName ?? string.Empty).Trim();
    var nickname = (request.MailNickname ?? string.Empty).Trim();
    var displayName = (request.DisplayName ?? string.Empty).Trim();
    var scopeKind = (request.ScopeKind ?? string.Empty).Trim().ToUpperInvariant();
    var role = RoleAdministrationService.CanonicalRole(request.Role);
    if (!ValidKey(request.IdempotencyKey) || !UpnPattern().IsMatch(upn) || upn.Length > 113 ||
        !NicknamePattern().IsMatch(nickname) || displayName.Length is < 1 or > 256 ||
        string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 5 or > 1000)
      return Fail("m365.user.invalid", "Display name, a valid user principal name, mail nickname and a reason are required.");
    if (role is null || role == "ClientUser" || !ValidStaffScope(scopeKind, request.ClientId, request.EngagementId))
      return Fail("m365.user.invalid", "Choose a staff role and an explicit firm, client or engagement scope.");
    if (!request.AccountEnabled)
      return Fail("m365.user.disabled-access", "A disabled Microsoft account cannot receive AuditSphere access. Create it enabled, or create it in Microsoft Entra and assign access after it is enabled.");
    if (!Guid.TryParse(options.TenantId, out var tenant))
      return Fail(ErrorCodes.GateBlocked, "The tenant is not configured.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<ExternalOperationResult>();
    var gate = await TenantCapabilityService.RequireVerifiedAsync(db, actor.FirmId, options,
      Microsoft365Capabilities.TenantUserProvisioning, now, ct);
    if (!gate.Succeeded || !provisioner.IsConfigured)
      return Fail(ErrorCodes.GateBlocked, gate.Message ?? "Microsoft user provisioning is not configured.");
    if (!await ScopeExistsAsync(db, actor.FirmId, request.ClientId, request.EngagementId, ct))
      return Fail(ErrorCodes.ScopeDenied, "The selected scope is unavailable.");

    var tenantId = tenant.ToString("D");
    var fingerprint = TenantAdministration.Fingerprint(ExternalOperationKinds.CreateTenantUser, tenantId,
      upn.ToLowerInvariant(), nickname, displayName, request.AccountEnabled.ToString(), role, scopeKind,
      request.ClientId?.ToString("D"), request.EngagementId?.ToString("D"));
    var start = await StartOperationAsync(db, actor, request.IdempotencyKey, ExternalOperationKinds.CreateTenantUser,
      fingerprint, tenantId, upn, displayName, request.Reason.Trim(), role, scopeKind, request.ClientId,
      request.EngagementId, null, now, ct, duplicateCheck: async () =>
      {
        var upper = upn.ToUpperInvariant();
        if (await db.Users.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Email.ToUpper() == upper, ct))
          return "An AuditSphere identity already uses this address. Assign access to the existing user instead.";
        if (await db.Microsoft365ExternalOperations.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
              x.Kind == ExternalOperationKinds.CreateTenantUser && x.TargetDescriptor.ToUpper() == upper &&
              x.State != ExternalOperationStates.Failed, ct))
          return "A create request for this user principal name already exists. Open it from the operations list.";
        return null;
      });
    if (!start.Succeeded) return Fail(start.ErrorCode!, start.Message!);
    var operation = start.Value!;
    if (operation.State != ExternalOperationStates.Authorized)
      return await ResumeAsync(db, actor, operation.Id, new(provisioner, null, null), options, now, ct);

    // Pre-dispatch duplicate check at Microsoft. A lookup failure blocks dispatch (fail closed).
    DirectoryUserRecord? existing;
    try { existing = await provisioner.FindByUserPrincipalNameAsync(tenantId, upn, ct); }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      await RecordOutcomeAsync(db, actor, operation.Id, ExternalOperationStates.Failed, null, null,
        "precheck-unavailable", null, now, ct);
      return Fail(ErrorCodes.GateBlocked, "Microsoft could not confirm the user principal name is unused. Nothing was created.");
    }
    if (existing is not null)
    {
      await RecordOutcomeAsync(db, actor, operation.Id, ExternalOperationStates.Failed, null, null,
        "duplicate-upn", null, now, ct);
      return Fail("m365.user.duplicate", "A Microsoft user with this user principal name already exists. Use the existing directory user workflow.");
    }

    if (!await MarkDispatchingAsync(db, actor, operation.Id, now, ct))
      return TenantAdministration.Denied<ExternalOperationResult>();
    // Generated in memory, sent once over TLS to Microsoft, returned once to the caller, never stored.
    var password = TemporaryPassword();
    ProviderMutationResult outcome;
    try
    {
      outcome = await provisioner.CreateUserAsync(tenantId,
        new NewDirectoryUser(displayName, upn, nickname, request.AccountEnabled, password, ForceChangePasswordNextSignIn: true), ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      outcome = ProviderMutationResult.Unknown();
    }
    var state = OutcomeState(outcome);
    await RecordOutcomeAsync(db, actor, operation.Id, state, outcome.ObjectId, outcome.CorrelationId,
      outcome.ErrorCode, null, now, ct);
    if (state != ExternalOperationStates.Accepted)
      return CommandResult<ExternalOperationResult>.Ok(new(operation.Id, state, state == ExternalOperationStates.Unknown
        ? "Microsoft did not confirm the result. Reconcile before any retry; a blind retry could duplicate the user."
        : "Microsoft rejected the user creation. No AuditSphere access was granted.", CorrelationId: outcome.CorrelationId));
    var bound = await BindAsync(db, actor, operation.Id, now, ct);
    return bound.Succeeded
      ? CommandResult<ExternalOperationResult>.Ok(bound.Value! with { TemporaryPassword = password })
      : CommandResult<ExternalOperationResult>.Ok(new(operation.Id, ExternalOperationStates.Accepted,
          "The Microsoft user was created but local binding needs review: " + bound.Message,
          TemporaryPassword: password, CorrelationId: outcome.CorrelationId));
  }

  public static async Task<CommandResult<ExternalOperationResult>> InviteGuestAsync(
    IAuditSphereDbContext db, ActorContext actor, IMicrosoftGuestInvitationProvider invitations,
    TenantAdministrationOptions options, InviteGuestRequest request, DateTimeOffset now,
    CancellationToken ct = default)
  {
    var email = (request.Email ?? string.Empty).Trim();
    if (!ValidKey(request.IdempotencyKey) || !RoleAdministrationService.EmailLike(email) || email.Length > 320 ||
        request.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 5 or > 1000)
      return Fail("m365.guest.invalid", "An approved email, a client scope and a reason are required.");
    if (!Guid.TryParse(options.TenantId, out var tenant) ||
        !Uri.TryCreate(options.GuestRedirectUrl, UriKind.Absolute, out var redirect) || redirect.Scheme != Uri.UriSchemeHttps &&
        !(redirect.IsLoopback && redirect.Scheme == Uri.UriSchemeHttp))
      return Fail(ErrorCodes.GateBlocked, "Guest invitation redirect is not configured.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<ExternalOperationResult>();
    var gate = await TenantCapabilityService.RequireVerifiedAsync(db, actor.FirmId, options,
      Microsoft365Capabilities.GuestInvitation, now, ct);
    if (!gate.Succeeded || !invitations.IsConfigured)
      return Fail(ErrorCodes.GateBlocked, gate.Message ?? "Guest invitations are not configured.");
    if (!await ScopeExistsAsync(db, actor.FirmId, request.ClientId, request.EngagementId, ct))
      return Fail(ErrorCodes.ScopeDenied, "The selected client scope is unavailable.");

    var tenantId = tenant.ToString("D");
    var scopeKind = request.EngagementId.HasValue ? "ENGAGEMENT" : "CLIENT";
    var fingerprint = TenantAdministration.Fingerprint(ExternalOperationKinds.InviteGuest, tenantId,
      email.ToLowerInvariant(), scopeKind, request.ClientId.ToString("D"), request.EngagementId?.ToString("D"));
    var start = await StartOperationAsync(db, actor, request.IdempotencyKey, ExternalOperationKinds.InviteGuest,
      fingerprint, tenantId, email, null, request.Reason.Trim(), "ClientUser", scopeKind, request.ClientId,
      request.EngagementId, null, now, ct, duplicateCheck: async () =>
      {
        var upper = email.ToUpperInvariant();
        if (await db.Users.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Email.ToUpper() == upper, ct))
          return "An AuditSphere identity already uses this email. Assign access to the existing user instead.";
        if (await db.Microsoft365ExternalOperations.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
              x.Kind == ExternalOperationKinds.InviteGuest && x.TargetDescriptor.ToUpper() == upper &&
              x.State != ExternalOperationStates.Failed, ct))
          return "An invitation for this email already exists. Open it from the operations list.";
        return null;
      });
    if (!start.Succeeded) return Fail(start.ErrorCode!, start.Message!);
    var operation = start.Value!;
    if (operation.State != ExternalOperationStates.Authorized)
      return await ResumeAsync(db, actor, operation.Id, new(null, invitations, null), options, now, ct);

    IReadOnlyList<DirectoryUserRecord> existingGuests;
    try { existingGuests = await invitations.FindGuestsByEmailAsync(tenantId, email, ct); }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      await RecordOutcomeAsync(db, actor, operation.Id, ExternalOperationStates.Failed, null, null, "precheck-unavailable", null, now, ct);
      return Fail(ErrorCodes.GateBlocked, "Microsoft could not confirm whether this guest already exists. Nothing was sent.");
    }
    if (existingGuests.Count > 0)
    {
      await RecordOutcomeAsync(db, actor, operation.Id, ExternalOperationStates.Failed, null, null, "guest-exists", null, now, ct);
      return Fail("m365.guest.exists", "A guest for this email already exists in the tenant. Use Existing Guest instead of inviting again.");
    }
    if (!await MarkDispatchingAsync(db, actor, operation.Id, now, ct)) return TenantAdministration.Denied<ExternalOperationResult>();
    ProviderMutationResult outcome;
    try { outcome = await invitations.InviteAsync(tenantId, email, redirect.AbsoluteUri, ct); }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      outcome = ProviderMutationResult.Unknown();
    }
    var state = OutcomeState(outcome);
    await RecordOutcomeAsync(db, actor, operation.Id, state, outcome.ObjectId, outcome.CorrelationId, outcome.ErrorCode, null, now, ct);
    if (state != ExternalOperationStates.Accepted)
      return CommandResult<ExternalOperationResult>.Ok(new(operation.Id, state, state == ExternalOperationStates.Unknown
        ? "Microsoft did not confirm the invitation. Reconcile before any retry."
        : "Microsoft rejected the invitation. No AuditSphere access was granted.", CorrelationId: outcome.CorrelationId));
    var bound = await BindAsync(db, actor, operation.Id, now, ct);
    return bound.Succeeded ? bound : CommandResult<ExternalOperationResult>.Ok(new(operation.Id,
      ExternalOperationStates.Accepted, "The invitation was accepted by Microsoft but local binding needs review: " + bound.Message));
  }

  public sealed record ProviderSet(IMicrosoftDirectoryUserProvisioner? Provisioner,
    IMicrosoftGuestInvitationProvider? Invitations, IMicrosoftGroupMembershipProvider? Groups);

  /// <summary>
  /// Continues an operation from its persisted state. UNKNOWN (or an interrupted DISPATCHING) is
  /// reconciled by immutable Microsoft identity before any retry; it is never blindly re-sent.
  /// </summary>
  public static async Task<CommandResult<ExternalOperationResult>> ResumeAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid operationId, ProviderSet providers,
    TenantAdministrationOptions options, DateTimeOffset now, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<ExternalOperationResult>();
    var operation = await db.Microsoft365ExternalOperations.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == operationId && x.FirmId == actor.FirmId, ct);
    if (operation is null) return TenantAdministration.Denied<ExternalOperationResult>();
    switch (operation.State)
    {
      case ExternalOperationStates.Bound:
        return CommandResult<ExternalOperationResult>.Ok(new(operation.Id, operation.State,
          "Already completed.", operation.BoundUserId, operation.BoundRoleGrantId, CorrelationId: operation.ProviderCorrelationId));
      case ExternalOperationStates.Accepted or ExternalOperationStates.Reconciled
        when operation.Kind is ExternalOperationKinds.CreateTenantUser or ExternalOperationKinds.InviteGuest:
        return await BindAsync(db, actor, operation.Id, now, ct);
      case ExternalOperationStates.Failed or ExternalOperationStates.ConflictRequiresReview
        or ExternalOperationStates.Accepted or ExternalOperationStates.Reconciled:
        return CommandResult<ExternalOperationResult>.Ok(new(operation.Id, operation.State,
          operation.State == ExternalOperationStates.ConflictRequiresReview
            ? "Reconciliation found a conflicting Microsoft object. Review it in Microsoft Entra before acting."
            : $"Operation finished: {operation.ResultCode ?? operation.State}.", CorrelationId: operation.ProviderCorrelationId));
      case ExternalOperationStates.Requested or ExternalOperationStates.Authorized:
        return CommandResult<ExternalOperationResult>.Ok(new(operation.Id, operation.State,
          "The operation was not dispatched. Submit the request again with the same idempotency key."));
    }
    // DISPATCHING or UNKNOWN: reconcile.
    return await ReconcileAsync(db, actor, operation, providers, now, ct);
  }

  private static async Task<CommandResult<ExternalOperationResult>> ReconcileAsync(
    IAuditSphereDbContext db, ActorContext actor, Microsoft365ExternalOperation operation, ProviderSet providers,
    DateTimeOffset now, CancellationToken ct)
  {
    var dispatchedAt = operation.DispatchedAt ?? operation.CreatedAt;
    string resultState;
    string? objectId = null;
    string reconciliation;
    try
    {
      switch (operation.Kind)
      {
        case ExternalOperationKinds.CreateTenantUser when providers.Provisioner is { IsConfigured: true } provisioner:
        {
          var found = await provisioner.FindByUserPrincipalNameAsync(operation.TenantId, operation.TargetDescriptor, ct);
          (resultState, objectId, reconciliation) = found switch
          {
            null when now - dispatchedAt < ReconcileNotFoundDelay => (ExternalOperationStates.Unknown, null, "not-yet-visible"),
            null => (ExternalOperationStates.Failed, null, "reconciled-not-created"),
            { CreatedAt: { } created } when created >= dispatchedAt.AddMinutes(-5) =>
              (ExternalOperationStates.Reconciled, found.ObjectId, "found-created-after-dispatch"),
            _ => (ExternalOperationStates.ConflictRequiresReview, null, "existing-object-predates-request")
          };
          break;
        }
        case ExternalOperationKinds.InviteGuest when providers.Invitations is { IsConfigured: true } invitations:
        {
          var found = await invitations.FindGuestsByEmailAsync(operation.TenantId, operation.TargetDescriptor, ct);
          var recent = found.Where(x => x.CreatedAt is { } created && created >= dispatchedAt.AddMinutes(-5)).ToList();
          (resultState, objectId, reconciliation) =
            found.Count == 0 && now - dispatchedAt < ReconcileNotFoundDelay ? (ExternalOperationStates.Unknown, null, "not-yet-visible") :
            found.Count == 0 ? (ExternalOperationStates.Failed, null, "reconciled-not-created") :
            recent.Count == 1 && found.Count == 1 ? (ExternalOperationStates.Reconciled, recent[0].ObjectId, "found-created-after-dispatch") :
            (ExternalOperationStates.ConflictRequiresReview, null, "ambiguous-guest-match");
          break;
        }
        case ExternalOperationKinds.AddGroupMember or ExternalOperationKinds.RemoveGroupMember
          when providers.Groups is { IsConfigured: true } groups:
        {
          var group = await db.ManagedDirectoryGroups.AsNoTracking().SingleAsync(x =>
            x.FirmId == actor.FirmId && x.Id == operation.ManagedGroupId, ct);
          var member = operation.ResultObjectId!;
          var isMember = await groups.IsMemberAsync(operation.TenantId, group.GroupObjectId, member, ct);
          var wanted = operation.Kind == ExternalOperationKinds.AddGroupMember;
          (resultState, objectId, reconciliation) = isMember == wanted
            ? (ExternalOperationStates.Reconciled, member, "membership-matches-request")
            : (ExternalOperationStates.Failed, member, "membership-unchanged");
          break;
        }
        default:
          return Fail(ErrorCodes.GateBlocked, "The provider for this operation is not configured; it stays UNKNOWN.");
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      return Fail(ErrorCodes.GateBlocked, "Microsoft could not be reached to reconcile. The operation stays UNKNOWN; try again later.");
    }
    await RecordOutcomeAsync(db, actor, operation.Id, resultState, objectId ?? operation.ResultObjectId,
      operation.ProviderCorrelationId, operation.ResultCode, reconciliation, now, ct);
    if (resultState == ExternalOperationStates.Reconciled &&
        operation.Kind is ExternalOperationKinds.CreateTenantUser or ExternalOperationKinds.InviteGuest)
    {
      var bound = await BindAsync(db, actor, operation.Id, now, ct);
      return bound.Succeeded ? CommandResult<ExternalOperationResult>.Ok(bound.Value! with
        { Message = "Reconciled by immutable Microsoft identity and bound. " +
          (operation.Kind == ExternalOperationKinds.CreateTenantUser
            ? "The initial password is not recoverable; reset it in Microsoft Entra." : string.Empty) })
        : bound;
    }
    return CommandResult<ExternalOperationResult>.Ok(new(operation.Id, resultState, reconciliation switch
    {
      "not-yet-visible" => "Microsoft has not yet shown the result. Wait a few minutes and reconcile again.",
      "reconciled-not-created" => "Microsoft did not create the object. Start a new request if still needed.",
      "membership-matches-request" => "Group membership matches the request.",
      "membership-unchanged" => "Microsoft did not apply the membership change.",
      _ => "Reconciliation found a conflict that requires review in Microsoft Entra."
    }, CorrelationId: operation.ProviderCorrelationId));
  }

  /// <summary>Local binding transaction: AppUser by tid+oid, reviewed RoleGrant, evidence, epoch, event.</summary>
  private static async Task<CommandResult<ExternalOperationResult>> BindAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid operationId, DateTimeOffset now, CancellationToken ct)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var operation = await db.Microsoft365ExternalOperations.FromSqlInterpolated($"""
      SELECT * FROM m365_external_operations WHERE firm_id = {actor.FirmId} AND id = {operationId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (operation is null || !await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<ExternalOperationResult>();
    if (operation.State == ExternalOperationStates.Bound)
      return CommandResult<ExternalOperationResult>.Ok(new(operation.Id, operation.State, "Already completed.",
        operation.BoundUserId, operation.BoundRoleGrantId));
    if (operation.State is not (ExternalOperationStates.Accepted or ExternalOperationStates.Reconciled) ||
        !Guid.TryParse(operation.ResultObjectId, out var objectGuid))
      return Fail(ErrorCodes.GateBlocked, "Microsoft has not confirmed an immutable identity for this operation.");
    var objectId = objectGuid.ToString("D");
    var guest = operation.Kind == ExternalOperationKinds.InviteGuest;
    var userKind = guest ? "Client" : "Staff";

    var user = await db.Users.SingleOrDefaultAsync(x => x.TenantId == operation.TenantId && x.Subject == objectId, ct);
    if (user is not null && (user.FirmId != actor.FirmId || user.UserKind != userKind || user.Disabled))
      return await ConflictAsync(db, tx, actor, operation, "identity-bound-elsewhere", now, ct);
    var upperEmail = operation.TargetDescriptor.ToUpperInvariant();
    if (user is null && await db.Users.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
          x.Email.ToUpper() == upperEmail, ct))
      return await ConflictAsync(db, tx, actor, operation, "email-bound-to-other-identity", now, ct);
    if (user?.Id == actor.UserId)
      return await ConflictAsync(db, tx, actor, operation, "self-elevation", now, ct);
    var role = operation.RequestedRole;
    if (role is not null && RoleAdministrationService.ValidateRoleForUser(
          new AppUser { UserKind = userKind }, role, operation.RequestedScopeKind!) is { } invalid)
      return await ConflictAsync(db, tx, actor, operation, "role-invalid", now, ct, invalid);

    if (user is null)
    {
      user = new AppUser
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TenantId = operation.TenantId, Subject = objectId,
        Email = operation.TargetDescriptor, DisplayName = operation.DisplayName ?? operation.TargetDescriptor,
        UserKind = userKind, CreatedAt = now, CreatedByUserId = actor.UserId
      };
      db.Users.Add(user);
    }
    db.DirectoryUserObservations.Add(new DirectoryUserObservation
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TenantId = operation.TenantId, ObjectId = objectId,
      DisplayName = user.DisplayName, UserPrincipalName = guest ? null : operation.TargetDescriptor,
      Mail = operation.TargetDescriptor, EnabledState = "ENABLED", UserType = guest ? "Guest" : "Member",
      Source = guest ? "GRAPH_INVITED" : "GRAPH_PROVISIONED", ObservedAt = now
    });

    RoleGrant? grant = null;
    if (role is not null)
    {
      grant = await db.RoleGrants.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.UserId == user.Id &&
        x.RevokedAt == null && x.Role == role && x.ClientId == operation.RequestedClientId &&
        x.EngagementId == operation.RequestedEngagementId, ct);
      if (grant is null)
      {
        grant = new RoleGrant
        {
          Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = user.Id, Role = role,
          ClientId = operation.RequestedClientId, EngagementId = operation.RequestedEngagementId,
          GrantedAt = now, GrantedByUserId = actor.UserId, Reason = operation.Reason
        };
        db.RoleGrants.Add(grant);
        db.RoleGrantChangeEvidences.Add(RoleAssignmentService.Evidence(actor, user.Id, grant.Id, "GRANTED",
          string.Empty, null, null, role, grant.ClientId, grant.EngagementId,
          guest ? "GRAPH_INVITED" : "GRAPH_PROVISIONED", operation.Reason, now));
        db.UserAccessInvitations.Add(new UserAccessInvitation
        {
          Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = user.Id, RoleGrantId = grant.Id,
          RecipientEmail = operation.TargetDescriptor, DestinationPath = "/auth/landing",
          DeliveryState = guest ? UserAccessInvitationStates.ProviderAccepted : UserAccessInvitationStates.NotSent,
          ProviderCorrelationId = operation.ProviderCorrelationId, CreatedAt = now, UpdatedAt = now
        });
        user.SessionEpoch++;
      }
    }
    var previous = operation.State;
    operation.State = ExternalOperationStates.Bound;
    operation.BoundUserId = user.Id;
    operation.BoundRoleGrantId = grant?.Id;
    operation.CompletedAt = now;
    operation.UpdatedAt = now;
    TenantAdministration.AddEvent(db, actor, guest ? "GUEST_BOUND" : "TENANT_USER_BOUND", now,
      oldState: previous, newState: ExternalOperationStates.Bound, reason: operation.Reason, result: "BOUND",
      targetTenantId: operation.TenantId, targetObjectId: objectId, targetUserId: user.Id,
      roleScopeChange: grant is null ? null : $"NONE -> {grant.Role}@{operation.RequestedScopeKind}:{grant.EngagementId?.ToString("D") ?? grant.ClientId?.ToString("D") ?? "firm"}",
      externalOperationId: operation.Id, correlationId: operation.ProviderCorrelationId);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ExternalOperationResult>.Ok(new(operation.Id, operation.State,
      guest ? "Guest invited and bound. Access applies only to the selected client scope." :
        "Microsoft user created and bound with the reviewed AuditSphere role.",
      user.Id, grant?.Id, CorrelationId: operation.ProviderCorrelationId));
  }

  private static async Task<CommandResult<ExternalOperationResult>> ConflictAsync(IAuditSphereDbContext db,
    Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, ActorContext actor,
    Microsoft365ExternalOperation operation, string code, DateTimeOffset now, CancellationToken ct, string? message = null)
  {
    var previous = operation.State;
    operation.State = ExternalOperationStates.ConflictRequiresReview;
    operation.ReconciliationResult = code;
    operation.UpdatedAt = now;
    TenantAdministration.AddEvent(db, actor, "EXTERNAL_OPERATION_CONFLICT", now, oldState: previous,
      newState: operation.State, reason: operation.Reason, result: code, targetTenantId: operation.TenantId,
      targetObjectId: operation.ResultObjectId, externalOperationId: operation.Id, correlationId: operation.ProviderCorrelationId);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return Fail("m365.binding-conflict", message ?? "The Microsoft identity cannot be bound automatically; review it before granting access.");
  }

  internal static async Task<CommandResult<Microsoft365ExternalOperation>> StartOperationAsync(
    IAuditSphereDbContext db, ActorContext actor, string key, string kind, string fingerprint, string tenantId,
    string target, string? displayName, string reason, string? role, string? scopeKind, Guid? clientId,
    Guid? engagementId, Guid? managedGroupId, DateTimeOffset now, CancellationToken ct,
    Func<Task<string?>>? duplicateCheck = null, string? memberObjectId = null)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<Microsoft365ExternalOperation>();
    var existing = await db.Microsoft365ExternalOperations.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.IdempotencyKey == key.Trim(), ct);
    if (existing is not null)
    {
      await tx.CommitAsync(ct);
      return existing.RequestFingerprint == fingerprint && existing.Kind == kind
        ? CommandResult<Microsoft365ExternalOperation>.Ok(existing)
        : CommandResult<Microsoft365ExternalOperation>.Fail(ErrorCodes.IdempotencyConflict,
            "This request key was already used for a different request.");
    }
    if (duplicateCheck is not null && await duplicateCheck() is { } duplicate)
      return CommandResult<Microsoft365ExternalOperation>.Fail("m365.duplicate", duplicate);
    var operation = new Microsoft365ExternalOperation
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, IdempotencyKey = key.Trim(), Kind = kind,
      State = ExternalOperationStates.Authorized, RequestFingerprint = fingerprint, TenantId = tenantId,
      TargetDescriptor = target, DisplayName = displayName, Reason = reason, RequestedRole = role,
      RequestedScopeKind = scopeKind, RequestedClientId = clientId, RequestedEngagementId = engagementId,
      ManagedGroupId = managedGroupId, ResultObjectId = memberObjectId, RequestedByUserId = actor.UserId,
      CreatedAt = now, UpdatedAt = now
    };
    db.Microsoft365ExternalOperations.Add(operation);
    TenantAdministration.AddEvent(db, actor, kind + "_REQUESTED", now, oldState: ExternalOperationStates.Requested,
      newState: ExternalOperationStates.Authorized, reason: reason, result: "AUTHORIZED", targetTenantId: tenantId,
      targetObjectId: memberObjectId, roleScopeChange: role is null ? null : $"NONE -> {role}@{scopeKind}",
      externalOperationId: operation.Id);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Microsoft365ExternalOperation>.Ok(operation);
  }

  internal static async Task<bool> MarkDispatchingAsync(IAuditSphereDbContext db, ActorContext actor, Guid operationId,
    DateTimeOffset now, CancellationToken ct)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var operation = await db.Microsoft365ExternalOperations.FromSqlInterpolated($"""
      SELECT * FROM m365_external_operations WHERE firm_id = {actor.FirmId} AND id = {operationId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    // Re-check authority immediately before the external side effect.
    if (operation is null || operation.State != ExternalOperationStates.Authorized ||
        !await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return false;
    operation.State = ExternalOperationStates.Dispatching;
    operation.DispatchedAt = now;
    operation.AttemptCount++;
    operation.UpdatedAt = now;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return true;
  }

  internal static async Task RecordOutcomeAsync(IAuditSphereDbContext db, ActorContext actor, Guid operationId,
    string state, string? objectId, string? correlationId, string? resultCode, string? reconciliation,
    DateTimeOffset now, CancellationToken ct)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var operation = await db.Microsoft365ExternalOperations.FromSqlInterpolated($"""
      SELECT * FROM m365_external_operations WHERE firm_id = {actor.FirmId} AND id = {operationId} FOR UPDATE
      """).SingleAsync(ct);
    var previous = operation.State;
    operation.State = state;
    if (Guid.TryParse(objectId, out var parsed)) operation.ResultObjectId = parsed.ToString("D");
    operation.ProviderCorrelationId = Clip(correlationId, 200) ?? operation.ProviderCorrelationId;
    operation.ResultCode = Clip(resultCode, 100) ?? operation.ResultCode;
    operation.ReconciliationResult = Clip(reconciliation, 100) ?? operation.ReconciliationResult;
    operation.UpdatedAt = now;
    if (state is ExternalOperationStates.Failed or ExternalOperationStates.Reconciled or ExternalOperationStates.Accepted)
      operation.CompletedAt ??= now;
    TenantAdministration.AddEvent(db, actor, operation.Kind + (reconciliation is null ? "_OUTCOME" : "_RECONCILED"), now,
      oldState: previous, newState: state, reason: operation.Reason, result: operation.ResultCode ?? state,
      targetTenantId: operation.TenantId, targetObjectId: operation.ResultObjectId,
      externalOperationId: operation.Id, correlationId: operation.ProviderCorrelationId);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
  }

  internal static string OutcomeState(ProviderMutationResult outcome) => outcome.Outcome switch
  {
    ProviderOutcomes.Accepted when Guid.TryParse(outcome.ObjectId, out _) => ExternalOperationStates.Accepted,
    ProviderOutcomes.Failed => ExternalOperationStates.Failed,
    _ => ExternalOperationStates.Unknown
  };

  internal static bool ValidKey(string? key) =>
    !string.IsNullOrWhiteSpace(key) && key.Trim().Length is >= 16 and <= 100 &&
    key.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

  private static bool ValidStaffScope(string scopeKind, Guid? clientId, Guid? engagementId) =>
    scopeKind == "FIRM_WIDE" && clientId is null && engagementId is null ||
    scopeKind == "CLIENT" && clientId.HasValue && engagementId is null ||
    scopeKind == "ENGAGEMENT" && clientId.HasValue && engagementId.HasValue;

  private static async Task<bool> ScopeExistsAsync(IAuditSphereDbContext db, Guid firmId, Guid? clientId,
    Guid? engagementId, CancellationToken ct) =>
    (!clientId.HasValue || await db.PracticeClients.AsNoTracking().AnyAsync(x => x.Id == clientId && x.FirmId == firmId, ct)) &&
    (!engagementId.HasValue || await db.Engagements.AsNoTracking().AnyAsync(x =>
      x.Id == engagementId && x.FirmId == firmId && x.PracticeClientId == clientId, ct));

  /// <summary>16 random characters from four classes; satisfies Microsoft Entra complexity rules.</summary>
  public static string TemporaryPassword()
  {
    const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnopqrstuvwxyz", digits = "23456789", symbols = "!#$%*+-=?@";
    const string all = upper + lower + digits + symbols;
    var chars = new char[16];
    chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
    chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
    chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
    chars[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
    for (var i = 4; i < chars.Length; i++) chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
    RandomNumberGenerator.Shuffle(chars.AsSpan());
    return new string(chars);
  }

  private static string? Clip(string? value, int max) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];

  private static CommandResult<ExternalOperationResult> Fail(string code, string message) =>
    CommandResult<ExternalOperationResult>.Fail(code, message);

  [GeneratedRegex(@"^[A-Za-z0-9'._!#^~-]{1,64}@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$")]
  private static partial Regex UpnPattern();

  [GeneratedRegex(@"^[A-Za-z0-9'._!#^~-]{1,64}$")]
  private static partial Regex NicknamePattern();
}
