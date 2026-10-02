using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record Microsoft365InstallationOptions(Guid FirmId, string InstallationId, string ProofHash,
  string AdministratorTenantId, string AdministratorObjectId);
public sealed record InstallationMicrosoftIdentity(string TenantId, string ObjectId, string Email, string DisplayName);
public sealed record InstallationBootstrapStatus(bool Bound, bool CanBootstrap);
public sealed record PrepareInstallationTenantRequest(Guid DraftId, long ExpectedRevision, bool Reviewed);

/// <summary>
/// Initial installation only. The HTTP host supplies the authenticated immutable Microsoft identity and deployment
/// approval; no caller-selected firm/role is accepted. The existing proof/session and local binding transactions
/// remain authoritative. A partial/unknown response requires a fresh sign-in, never an automatic write retry.
/// </summary>
public static class Microsoft365InstallationService
{
  public static async Task<CommandResult<InstallationBootstrapStatus>> StatusAsync(IAuditSphereDbContext db,
    Microsoft365InstallationOptions options, string tenantId, string objectId, CancellationToken ct = default)
  {
    if (!Approved(options, tenantId, objectId)) return Denied<InstallationBootstrapStatus>();
    var bound = await db.Users.AsNoTracking().AnyAsync(user => user.FirmId == options.FirmId &&
      user.TenantId == tenantId && user.Subject == objectId, ct);
    if (await db.Users.AsNoTracking().AnyAsync(user => user.FirmId != options.FirmId && user.TenantId == tenantId && user.Subject == objectId, ct))
      return Denied<InstallationBootstrapStatus>();
    var hasAdministrator = await db.RoleGrants.AsNoTracking().AnyAsync(grant => grant.FirmId == options.FirmId &&
      grant.Role == "Administrator" && grant.ClientId == null && grant.EngagementId == null, ct);
    return CommandResult<InstallationBootstrapStatus>.Ok(new(bound, !bound && !hasAdministrator));
  }

  /// <summary>Records the deployment tenant and prepares an immutable connection revision. It grants no Microsoft capability.</summary>
  public static async Task<CommandResult<Guid>> PrepareTenantAsync(IAuditSphereDbContext db, ActorContext actor,
    Microsoft365InstallationOptions options, PrepareInstallationTenantRequest request, string configuredTenantId,
    string loginReference, string runtimeReference, DateTimeOffset now, CancellationToken ct = default)
  {
    if (actor.FirmId != options.FirmId || !await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return Denied<Guid>();
    var user = await db.Users.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.Id == actor.UserId, ct);
    if (!Approved(options, user.TenantId, user.Subject) ||
        !Guid.TryParse(configuredTenantId, out var configuredTenant) || !Guid.TryParse(user.TenantId, out var authenticatedTenant) || configuredTenant != authenticatedTenant)
      return Denied<Guid>();
    if (!request.Reviewed || request.ExpectedRevision < 1)
      return CommandResult<Guid>.Fail("review.required", "Review the configured tenant and setup revision before preparing it.");
    var draft = await db.Microsoft365SetupDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.DraftId && x.FirmId == actor.FirmId, ct);
    if (draft is null) return Denied<Guid>();
    if (draft.Revision != request.ExpectedRevision) return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The setup draft changed. Refresh and review it again.");
    if (draft.State is Microsoft365RevisionStates.Active or Microsoft365RevisionStates.Blocked or Microsoft365RevisionStates.Suspended)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "This setup revision is protected. A separately reviewed configuration draft is required.");
    if (!string.IsNullOrWhiteSpace(draft.ExpectedTenantId) && !string.Equals(draft.ExpectedTenantId, configuredTenantId, StringComparison.OrdinalIgnoreCase)) return Denied<Guid>();
    var claim = await Microsoft365OnboardingService.ClaimAsync(db, options.FirmId, options.InstallationId, string.Empty, options.ProofHash,
      now, ct, user.TenantId, user.Subject, actor);
    if (!claim.Succeeded) return CommandResult<Guid>.Fail(claim.ErrorCode!, claim.Message!);
    var saved = await Microsoft365OnboardingService.SaveDraftAsync(db, new(claim.Value!.SessionId, claim.Value.Capability, draft.Revision,
      configuredTenantId, draft.TenantDisplayName, draft.SiteUrl, draft.SiteId, draft.DriveId, draft.RootFolderId, draft.AccessProfile,
      draft.MailState, draft.RecordsState), now, ct);
    if (!saved.Succeeded) return CommandResult<Guid>.Fail(saved.ErrorCode!, saved.Message!);
    return await Microsoft365ConfigurationService.PrepareConnectionRevisionAsync(db, actor,
      new(saved.Value, draft.Revision + 1, loginReference, runtimeReference), now, ct);
  }

  public static async Task<CommandResult> BootstrapAsync(IAuditSphereDbContext db, Microsoft365InstallationOptions options,
    InstallationMicrosoftIdentity identity, string proof, bool reviewed, DateTimeOffset now, CancellationToken ct = default)
  {
    var status = await StatusAsync(db, options, identity.TenantId, identity.ObjectId, ct);
    if (!status.Succeeded) return CommandResult.Fail(status.ErrorCode!, status.Message!);
    if (!status.Value!.CanBootstrap)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Initial administrator setup is already closed. Sign in again or contact an existing administrator.");
    if (!reviewed || string.IsNullOrWhiteSpace(proof) || proof.Length > 4096 ||
        string.IsNullOrWhiteSpace(identity.Email) || identity.Email.Length > 320 ||
        string.IsNullOrWhiteSpace(identity.DisplayName) || identity.DisplayName.Length > 200)
      return CommandResult.Fail("m365.bootstrap.invalid", "Review the approved identity and provide the installation proof.");
    var claim = await Microsoft365OnboardingService.ClaimAsync(db, options.FirmId, options.InstallationId,
      proof, options.ProofHash, now, ct, authenticatedTenantId: identity.TenantId, authenticatedObjectId: identity.ObjectId);
    if (!claim.Succeeded) return CommandResult.Fail(claim.ErrorCode!, claim.Message!);
    var result = await Microsoft365OnboardingService.CompleteInitialAdministratorBootstrapAsync(db,
      new(claim.Value!.SessionId, claim.Value.Capability, identity.TenantId, identity.ObjectId, identity.Email, identity.DisplayName),
      options.AdministratorTenantId, options.AdministratorObjectId, now, ct);
    return result.Succeeded ? CommandResult.Ok() : CommandResult.Fail(result.ErrorCode!, result.Message!);
  }

  private static bool Approved(Microsoft365InstallationOptions options, string tenantId, string objectId) =>
    options.FirmId != Guid.Empty && !string.IsNullOrWhiteSpace(options.InstallationId) &&
    options.ProofHash is { Length: 64 } && options.ProofHash.All(Uri.IsHexDigit) &&
    Guid.TryParse(options.AdministratorTenantId, out var approvedTenant) &&
    Guid.TryParse(options.AdministratorObjectId, out var approvedObject) &&
    Guid.TryParse(tenantId, out var tenant) && Guid.TryParse(objectId, out var identity) &&
    tenant == approvedTenant && identity == approvedObject;

  private static CommandResult<T> Denied<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Initial installation setup is not available to this identity.");
}
