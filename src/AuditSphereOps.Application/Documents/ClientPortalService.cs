using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record FirstSignInStatus(bool Completed, string IdentityPath, bool CanComplete, string Message);
public sealed record PbcDelegationRow(Guid Id, Guid DelegateUserId, string DelegateName, DateTimeOffset CreatedAt);
public sealed record PbcDelegateCandidate(Guid UserId, string Name);
public sealed record ClientPortalIntentView(string State, string RecipientEmail, DateTimeOffset UpdatedAt, bool ContactHasClientAccess);

/// <summary>
/// Client portal onboarding rules. The first-sign-in requirement is derived from directory evidence for the identity
/// path and gates uploads; delegation is limited to the request's owner who is the client's primary contact, to
/// client-only users already holding a grant for that exact scope, and is checked on every command so revocation is
/// immediate.
/// </summary>
public static class ClientPortalService
{
  public const string TermsVersion = "PORTAL-SECURITY-2026-1";

  // Existing clients without a conversion intent retain their earlier onboarding contract.
  // This gate does not authorize an identity: callers first verify its current RoleGrant and scope.
  internal static async Task<bool> CommercialOnboardingClearedAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid? engagementId, CancellationToken ct)
  {
    var intent = await db.ClientPortalIntents.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.PracticeClientId == clientId, ct);
    if (intent is null) return true;
    var target = engagementId ?? intent.ActivatedEngagementId;
    if (target is null || !await db.EngagementActivations.AnyAsync(x => x.FirmId == firmId && x.PracticeClientId == clientId && x.EngagementId == target, ct)) return false;
    var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.ProposalId == intent.SourceProposalId && x.PracticeClientId == clientId, ct);
    return agreement is not null && await AuditSphereOps.Application.Practice.AutomaticFeeInvoiceHandler.HasCurrentLetterAsync(db, agreement, ct) &&
      await db.Proposals.AnyAsync(x => x.FirmId == firmId && x.Id == intent.SourceProposalId && x.Status == "ACCEPTED" && x.ResponseAt != null, ct) &&
      await db.FeeMilestones.AnyAsync(x => x.FirmId == firmId && x.AgreementId == agreement.Id && x.Kind == "ADVANCE" && x.State == "PAID", ct);
  }

  internal static async Task RefreshCommercialIntentAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct)
  {
    var intent = await db.ClientPortalIntents.SingleOrDefaultAsync(x => x.FirmId == firmId && x.PracticeClientId == clientId, ct);
    if (intent is not null && intent.State == ClientPortalIntentStates.AwaitingAcceptance &&
        await CommercialOnboardingClearedAsync(db, firmId, clientId, intent.ActivatedEngagementId, ct))
    {
      intent.State = ClientPortalIntentStates.ReadyToInvite;
      intent.UpdatedAt = DateTimeOffset.UtcNow;
      await db.SaveChangesAsync(ct);
    }
  }

  public static async Task<IReadOnlyList<Guid>> AuthorizedPortalGrantIdsAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var now = DateTimeOffset.UtcNow;
    var grants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.Role == "ClientUser" &&
      x.ClientId != null && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now)).Take(500).ToListAsync(ct);
    var ids = new List<Guid>();
    foreach (var grant in grants)
      if ((await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, grant.ClientId, grant.EngagementId, ["ClientUser"]), ct)).Succeeded) ids.Add(grant.Id);
    return ids;
  }

  public static async Task<IReadOnlyList<Guid>> AuthorizedPortalEngagementIdsAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var now = DateTimeOffset.UtcNow;
    var grants = db.RoleGrants.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.Role == "ClientUser" &&
      x.ClientId != null && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now));
    var candidates = await db.Engagements.AsNoTracking().Where(e => e.FirmId == actor.FirmId && grants.Any(g => g.ClientId == e.PracticeClientId &&
      (g.EngagementId == null || g.EngagementId == e.Id))).OrderBy(e => e.Id).Take(500).Select(e => e.Id).ToListAsync(ct);
    var ids = new List<Guid>();
    foreach (var id in candidates)
      if ((await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, EngagementId: id, RequiredRoles: ["ClientUser"]), ct)).Succeeded) ids.Add(id);
    return ids;
  }

  public static async Task<bool> HasPendingCommercialOnboardingAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var now = DateTimeOffset.UtcNow;
    var grants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.Role == "ClientUser" && x.ClientId != null &&
      x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now)).Take(500).ToListAsync(ct);
    foreach (var grant in grants)
      if ((await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, grant.ClientId, grant.EngagementId, ["ClientUser"]), ct)).ErrorCode == ErrorCodes.GateBlocked) return true;
    return false;
  }

  public static async Task<FirstSignInStatus> GetFirstSignInStatusAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId, ct);
    if (user is null) return new(false, ClientIdentityPaths.Unobserved, false, "The identity is unavailable.");
    var (path, observedAt) = await DeriveIdentityPathAsync(db, user, ct);
    if (await db.ClientPortalFirstSignIns.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId, ct))
      return new(true, path, false, "First sign-in is complete.");
    return path switch
    {
      ClientIdentityPaths.ProvisionedMember when observedAt is null => new(false, path, false,
        "This account was created with a temporary password. Sign in through Microsoft and change it; uploads open after that sign-in is observed."),
      ClientIdentityPaths.ProvisionedMember => new(false, path, true,
        "Microsoft required a new password at your first sign-in. Confirm the portal security terms to open uploads."),
      ClientIdentityPaths.ExternalIdentity => new(false, path, true,
        "Your organisation manages your password and multi-factor sign-in. Confirm the portal security terms to open uploads."),
      _ => new(false, path, true, "No directory record exists for this identity. Confirm the portal security terms to open uploads.")
    };
  }

  public static async Task<CommandResult> CompleteFirstSignInAsync(
    IAuditSphereDbContext db, ActorContext actor, bool acknowledged, CancellationToken ct = default)
  {
    if (!acknowledged)
      return CommandResult.Fail("portal.first-sign-in.acknowledgement", "Confirm the portal security terms first.");
    if (!await HoldsClientGrantAsync(db, actor, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var status = await GetFirstSignInStatusAsync(db, actor, ct);
    if (status.Completed) return CommandResult.Ok();
    if (!status.CanComplete) return CommandResult.Fail("portal.first-sign-in.pending", status.Message);
    var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId, ct);
    var (path, observedAt) = await DeriveIdentityPathAsync(db, user, ct);
    db.ClientPortalFirstSignIns.Add(new ClientPortalFirstSignIn
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = actor.UserId, IdentityPath = path,
      SignInObservedAt = observedAt, TermsVersion = TermsVersion, CompletedAt = DateTimeOffset.UtcNow
    });
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { return CommandResult.Ok(); } // a concurrent completion already recorded it
    return CommandResult.Ok();
  }

  /// <summary>Upload gate for client identities. Staff uploads are governed by their own authorization.</summary>
  public static async Task<CommandResult> RequireFirstSignInAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    await db.ClientPortalFirstSignIns.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId, ct)
      ? CommandResult.Ok()
      : CommandResult.Fail("portal.first-sign-in.required", "Complete your first portal sign-in before uploading files.");

  /// <summary>Client transfer privileges close at final financial-package release or final bundle assembly.</summary>
  public static async Task<CommandResult> RequireUploadWindowAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, engagement.PracticeClientId, engagementId, ["ClientUser"]), ct);
    if (!auth.Succeeded) return auth;
    var released = await db.CommercialDeliverableBundles.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId, ct) ||
      await db.Releases.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && db.ReleaseCandidates.Any(c =>
        c.FirmId == actor.FirmId && c.Id == x.ReleaseCandidateId && c.TargetKind == "FINANCIAL_PACKAGE"), ct);
    return released ? CommandResult.Fail(ErrorCodes.ProtectedState, "Client uploads are frozen after final release. You can still download your released documents.") : CommandResult.Ok();
  }

  /// <summary>Owner of the request, or holder of an unrevoked delegation for it.</summary>
  public static async Task<bool> IsRequestParticipantAsync(IAuditSphereDbContext db, ActorContext actor, PbcRequest request, CancellationToken ct) =>
    actor.UserId == request.ClientOwnerUserId ||
    await db.PbcRequestDelegations.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.PbcRequestId == request.Id &&
      x.DelegateUserId == actor.UserId && x.RevokedAt == null, ct);

  /// <summary>Requests the client identity owns or was delegated; callers still apply the grant scope.</summary>
  public static IQueryable<PbcRequest> ParticipantRequests(IAuditSphereDbContext db, ActorContext actor) =>
    db.PbcRequests.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.State != PbcStates.Draft &&
      (x.ClientOwnerUserId == actor.UserId || db.PbcRequestDelegations.Any(d => d.FirmId == actor.FirmId &&
        d.PbcRequestId == x.Id && d.DelegateUserId == actor.UserId && d.RevokedAt == null)));

  public static async Task<bool> IsPrimaryContactAsync(IAuditSphereDbContext db, ActorContext actor, Guid clientId, CancellationToken ct = default)
  {
    var email = await db.Users.AsNoTracking().Where(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && x.UserKind == "Client" && !x.Disabled)
      .Select(x => x.Email).SingleOrDefaultAsync(ct);
    if (string.IsNullOrWhiteSpace(email)) return false;
    var upper = email.ToUpperInvariant();
    var now = DateTimeOffset.UtcNow;
    return await db.ClientContacts.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId && x.Primary &&
      x.Email.ToUpper() == upper && (x.ValidFrom == null || x.ValidFrom <= now) && (x.ValidTo == null || x.ValidTo >= now), ct);
  }

  public static async Task<CommandResult<Guid>> DelegateRequestAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid requestId, Guid delegateUserId, CancellationToken ct = default)
  {
    var request = await db.PbcRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId && x.FirmId == actor.FirmId, ct);
    if (request is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeDelegatorAsync(db, actor, request, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.State is PbcStates.Accepted or PbcStates.Closed)
      return CommandResult<Guid>.Fail("pbc.delegation.state", "A closed request cannot be delegated.");
    if (!(await DelegateCandidatesAsync(db, actor, request, ct)).Any(x => x.UserId == delegateUserId))
      return CommandResult<Guid>.Fail("pbc.delegation.ineligible",
        "Only another client user with access to this exact engagement, and no firm role, can receive a delegation.");
    var existing = await db.PbcRequestDelegations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.PbcRequestId == requestId && x.DelegateUserId == delegateUserId && x.RevokedAt == null, ct);
    if (existing is not null) return CommandResult<Guid>.Ok(existing.Id);
    var delegation = new PbcRequestDelegation
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PbcRequestId = requestId, DelegatorUserId = actor.UserId, DelegateUserId = delegateUserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.PbcRequestDelegations.Add(delegation);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(delegation.Id);
  }

  public static async Task<CommandResult> RevokeDelegationAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid delegationId, CancellationToken ct = default)
  {
    var delegation = await db.PbcRequestDelegations.SingleOrDefaultAsync(x => x.Id == delegationId && x.FirmId == actor.FirmId, ct);
    if (delegation is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var request = await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == delegation.PbcRequestId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeDelegatorAsync(db, actor, request, ct);
    if (!auth.Succeeded) return auth;
    if (delegation.RevokedAt is not null) return CommandResult.Ok();
    delegation.RevokedAt = DateTimeOffset.UtcNow;
    delegation.RevokedByUserId = actor.UserId;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<List<PbcDelegationRow>> DelegationsAsync(
    IAuditSphereDbContext db, ActorContext actor, PbcRequest request, CancellationToken ct = default)
  {
    if (!(await AuthorizeDelegatorAsync(db, actor, request, ct)).Succeeded) return [];
    var rows = await db.PbcRequestDelegations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PbcRequestId == request.Id && x.RevokedAt == null)
      .OrderBy(x => x.CreatedAt).ToListAsync(ct);
    var ids = rows.Select(x => x.DelegateUserId).ToArray();
    var names = await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && ids.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.Email : x.DisplayName, ct);
    return rows.Select(x => new PbcDelegationRow(x.Id, x.DelegateUserId, names.GetValueOrDefault(x.DelegateUserId, "User"), x.CreatedAt)).ToList();
  }

  /// <summary>Client-only users of the same client with a grant covering the request's engagement.</summary>
  public static async Task<List<PbcDelegateCandidate>> DelegateCandidatesAsync(
    IAuditSphereDbContext db, ActorContext actor, PbcRequest request, CancellationToken ct = default)
  {
    if (!(await AuthorizeDelegatorAsync(db, actor, request, ct)).Succeeded) return [];
    var now = DateTimeOffset.UtcNow;
    var active = db.RoleGrants.AsNoTracking().Where(g => g.FirmId == actor.FirmId && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now));
    return await db.Users.AsNoTracking()
      .Where(u => u.FirmId == actor.FirmId && u.Id != actor.UserId && u.UserKind == "Client" && !u.Disabled &&
        active.Any(g => g.UserId == u.Id && g.Role == "ClientUser" && g.ClientId == request.ClientId &&
          (g.EngagementId == null || g.EngagementId == request.EngagementId)) &&
        !active.Any(g => g.UserId == u.Id && g.Role != "ClientUser"))
      .OrderBy(u => u.DisplayName)
      .Select(u => new PbcDelegateCandidate(u.Id, u.DisplayName == "" ? u.Email : u.DisplayName))
      .ToListAsync(ct);
  }

  public static async Task<ClientPortalIntentView?> GetIntentAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct = default)
  {
    var intent = await db.ClientPortalIntents.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.PracticeClientId == clientId, ct);
    if (intent is null) return null;
    var upper = intent.RecipientEmail.ToUpperInvariant();
    var hasAccess = await db.Users.AsNoTracking().AnyAsync(u => u.FirmId == firmId && u.Email.ToUpper() == upper &&
      db.RoleGrants.Any(g => g.UserId == u.Id && g.Role == "ClientUser" && g.ClientId == clientId && g.RevokedAt == null), ct);
    var cleared = await CommercialOnboardingClearedAsync(db, firmId, clientId, intent.ActivatedEngagementId, ct);
    return new(!cleared ? ClientPortalIntentStates.AwaitingAcceptance : hasAccess ? ClientPortalIntentStates.Invited : ClientPortalIntentStates.ReadyToInvite,
      intent.RecipientEmail, intent.UpdatedAt, cleared && hasAccess);
  }

  private static async Task<CommandResult> AuthorizeDelegatorAsync(IAuditSphereDbContext db, ActorContext actor, PbcRequest request, CancellationToken ct)
  {
    if (actor.UserId != request.ClientOwnerUserId || !await IsPrimaryContactAsync(db, actor, request.ClientId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Only the client's primary contact who owns this request can delegate it.");
    return await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, ["ClientUser"]), ct);
  }

  private static async Task<bool> HoldsClientGrantAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    await db.RoleGrants.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.Role == "ClientUser" &&
      x.ClientId != null && x.RevokedAt == null, ct);

  private static async Task<(string Path, DateTimeOffset? ObservedAt)> DeriveIdentityPathAsync(IAuditSphereDbContext db, AppUser user, CancellationToken ct)
  {
    var observation = await db.DirectoryUserObservations.AsNoTracking()
      .Where(x => x.FirmId == user.FirmId && x.ObjectId == user.Subject && x.Source.StartsWith("GRAPH_"))
      .OrderByDescending(x => x.ObservedAt).FirstOrDefaultAsync(ct);
    if (observation is null) return (ClientIdentityPaths.Unobserved, null);
    if (observation.Source == "GRAPH_PROVISIONED")
      return (ClientIdentityPaths.ProvisionedMember,
        user.LastSignInAt is { } signIn && signIn > observation.ObservedAt ? signIn : null);
    return (ClientIdentityPaths.ExternalIdentity, null);
  }
}
