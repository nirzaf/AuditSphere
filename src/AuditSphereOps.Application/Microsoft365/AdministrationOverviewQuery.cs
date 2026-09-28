using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public static class ProgressStates
{
  public const string Done = "DONE";
  public const string Partial = "PARTIAL";
  public const string Pending = "PENDING";
  public const string Blocked = "BLOCKED";
}

/// <summary>A setup step derived from persisted verified state; never hard-coded.</summary>
public sealed record SetupProgressStep(string Key, string Title, string State, string Detail,
  string? WhyBlocked, string? RequiredAction, string? RequiredAuthority, DateTimeOffset? LastVerification);

public sealed record StatusCard(string Key, string Title, string State, string Value, string Detail);

public sealed record SharePointStatus(string? SiteUrl, string? SiteId, string? DriveId, string? RootFolderId,
  string AccessProfile, string TenantAuthentication, string ApplicationConsent, string SelectedSitePermission,
  string LibraryAccess, string WorkspaceConfiguration, string ClientTemplate, int ReadyClientWorkspaces,
  int BlockedClientWorkspaces, DateTimeOffset? LastVerification);

public sealed record MailStatus(string SenderIdentity, string TransportState, string PermissionState,
  DateTimeOffset? LastVerification, string? LastFailure);

public sealed record AdministrationOverview(
  string? TenantId,
  IReadOnlyList<StatusCard> Cards,
  IReadOnlyList<SetupProgressStep> Progress,
  IReadOnlyList<CapabilityStatus> Capabilities,
  SharePointStatus SharePoint,
  MailStatus Mail,
  IReadOnlyList<string> SecurityWarnings,
  IReadOnlyList<CapabilityPermission> PermissionMatrix);

/// <summary>Firm-wide Administrator overview. Counts are never returned to unauthorized callers.</summary>
public static class AdministrationOverviewQuery
{
  public static async Task<CommandResult<AdministrationOverview>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, TenantAdministrationOptions options, string? mailSender,
    DateTimeOffset now, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<AdministrationOverview>();
    var tenantId = Guid.TryParse(options.TenantId, out var tenant) ? tenant.ToString("D") : null;
    var firmId = actor.FirmId;
    var capabilities = await TenantCapabilityService.StatusesAsync(db, firmId, options, now, ct);
    CapabilityStatus Cap(string key) => capabilities.Single(x => x.Capability == key);

    var draft = await db.Microsoft365SetupDrafts.AsNoTracking().Where(x => x.FirmId == firmId)
      .OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
    var connection = draft?.ConnectionRevisionId is { } connectionId
      ? await db.Microsoft365ConnectionRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == connectionId, ct)
      : null;
    var consent = await db.TenantConsentAttempts.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.ExpectedTenantId == tenantId)
      .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
    var verifiedConsent = await db.TenantConsentAttempts.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.ExpectedTenantId == tenantId && x.State == TenantConsentAttemptStates.ConsentVerified)
      .OrderByDescending(x => x.ConsentVerifiedAt).FirstOrDefaultAsync(ct);
    var evidence = connection is null ? [] : await db.IntegrationVerificationEvidences.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.ConnectionRevisionId == connection.Id)
      .OrderByDescending(x => x.ObservedAt).ToListAsync(ct);
    bool Passed(string kind, string? id) => !string.IsNullOrWhiteSpace(id) &&
      evidence.Any(x => x.ResourceKind == kind && x.ResourceId == id && x.Result == "PASS");
    var workspace = connection is null ? null : await db.FirmWorkspaceConfigurations.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.ConnectionRevisionId == connection.Id).FirstOrDefaultAsync(ct);
    var templateApproved = await db.FolderTemplateVersions.AsNoTracking().AnyAsync(x => x.FirmId == firmId &&
      x.Purpose == FolderTemplatePurposes.ClientWorkspace && x.ApprovedAt != null, ct);
    var clientWorkspaces = await db.ClientWorkspaces.AsNoTracking().Where(x => x.FirmId == firmId)
      .GroupBy(x => x.State).Select(x => new { x.Key, Count = x.Count() }).ToListAsync(ct);

    var activeUsers = await db.Users.AsNoTracking().CountAsync(x => x.FirmId == firmId && !x.Disabled, ct);
    var disabledUsers = await db.Users.AsNoTracking().CountAsync(x => x.FirmId == firmId && x.Disabled, ct);
    var activeGrants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == firmId && x.RevokedAt == null)
      .Select(x => new { x.UserId, x.Role, x.ClientId, x.EngagementId, x.ExpiresAt }).ToListAsync(ct);
    var administrators = activeGrants.Where(x => x.Role == "Administrator" && x.ClientId == null && x.EngagementId == null)
      .Select(x => x.UserId).Distinct().Count();
    var pendingInvitations = await db.UserAccessInvitations.AsNoTracking().CountAsync(x => x.FirmId == firmId &&
      x.FirstAccessAt == null && db.RoleGrants.Any(g => g.Id == x.RoleGrantId && g.RevokedAt == null), ct);
    var managedGroups = await db.ManagedDirectoryGroups.AsNoTracking().CountAsync(x => x.FirmId == firmId && x.RetiredAt == null, ct);
    var openOperations = await db.Microsoft365ExternalOperations.AsNoTracking().Where(x => x.FirmId == firmId &&
        (x.State == ExternalOperationStates.Unknown || x.State == ExternalOperationStates.Dispatching ||
         x.State == ExternalOperationStates.ConflictRequiresReview ||
         x.State == ExternalOperationStates.Accepted && (x.Kind == ExternalOperationKinds.CreateTenantUser || x.Kind == ExternalOperationKinds.InviteGuest)))
      .Select(x => x.State).ToListAsync(ct);
    var disabledWithGrants = await db.Users.AsNoTracking().CountAsync(x => x.FirmId == firmId && x.Disabled &&
      db.RoleGrants.Any(g => g.UserId == x.Id && g.RevokedAt == null), ct);
    var observations = await db.DirectoryUserObservations.AsNoTracking().Where(x => x.FirmId == firmId)
      .Select(x => new { x.TenantId, x.ObjectId, x.EnabledState, x.ObservedAt }).ToListAsync(ct);
    var disabledInMicrosoft = observations.GroupBy(x => (x.TenantId, x.ObjectId))
      .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
      .Where(x => x.EnabledState == "DISABLED").Select(x => (x.TenantId, x.ObjectId)).ToHashSet();
    var usersWithAccess = await db.Users.AsNoTracking()
      .Where(x => x.FirmId == firmId && !x.Disabled && db.RoleGrants.Any(g => g.UserId == x.Id && g.RevokedAt == null))
      .Select(x => new { x.TenantId, x.Subject }).ToListAsync(ct);
    var disabledInMicrosoftWithAccess = usersWithAccess.Count(u => disabledInMicrosoft.Contains((u.TenantId, u.Subject)));
    var mailRows = await db.TenantCapabilityVerifications.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.TenantId == tenantId && x.Capability == Microsoft365Capabilities.OutboundMail)
      .OrderByDescending(x => x.ObservedAt).Take(20).ToListAsync(ct);

    // Re-check before returning any counts (a grant may have been revoked during the read).
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<AdministrationOverview>();

    var sitePassed = Passed("SITE", draft?.SiteId);
    var libraryPassed = Passed("DRIVE", draft?.DriveId) && Passed("ROOT", draft?.RootFolderId);
    var consentVerified = verifiedConsent is not null;
    var siteCap = Cap(Microsoft365Capabilities.SelectedSite);
    var sharePoint = new SharePointStatus(draft?.SiteUrl, draft?.SiteId, draft?.DriveId, draft?.RootFolderId,
      workspace?.AccessProfile ?? draft?.AccessProfile ?? Microsoft365AccessProfiles.AppMediated,
      TenantAuthentication: tenantId is null ? CapabilityVerificationStates.BlockedExternal : CapabilityVerificationStates.Verified,
      ApplicationConsent: consentVerified ? CapabilityVerificationStates.Verified : TenantCapabilityService.NotVerified,
      SelectedSitePermission: sitePassed || siteCap.State == CapabilityVerificationStates.Verified ? CapabilityVerificationStates.Verified : siteCap.State,
      LibraryAccess: libraryPassed ? CapabilityVerificationStates.Verified : TenantCapabilityService.NotVerified,
      WorkspaceConfiguration: workspace is not null && connection?.State == Microsoft365RevisionStates.Active ? "ACTIVE" :
        draft is null ? "NOT_CONFIGURED" : "DRAFT",
      ClientTemplate: templateApproved ? "APPROVED" : "NOT_APPROVED",
      ReadyClientWorkspaces: clientWorkspaces.Where(x => x.Key == ClientWorkspaceStates.Ready).Sum(x => x.Count),
      BlockedClientWorkspaces: clientWorkspaces.Where(x => x.Key != ClientWorkspaceStates.Ready).Sum(x => x.Count),
      LastVerification: evidence.Where(x => x.ResourceKind is "SITE" or "DRIVE" or "ROOT").Select(x => (DateTimeOffset?)x.ObservedAt).FirstOrDefault());

    var mailCap = Cap(Microsoft365Capabilities.OutboundMail);
    var mail = new MailStatus(
      string.IsNullOrWhiteSpace(mailSender) ? "Not configured" : mailSender,
      TransportState: !options.OutboundMailEnabled ? "DISABLED" :
        string.IsNullOrWhiteSpace(mailSender) ? "NOT_CONFIGURED" :
        draft?.MailState == "CONFIGURED" ? "CONFIGURED" : "NOT_CONFIGURED",
      PermissionState: mailCap.State,
      LastVerification: mailRows.Select(x => (DateTimeOffset?)x.ObservedAt).FirstOrDefault(),
      LastFailure: mailRows.FirstOrDefault(x => x.State != CapabilityVerificationStates.Verified) is { } failure
        ? $"{failure.State} ({failure.DiagnosticCode}) at {failure.ObservedAt:yyyy-MM-dd HH:mm} UTC" : null);

    var directoryCap = Cap(Microsoft365Capabilities.DirectoryRead);
    var nonAdminAssignments = activeGrants.Count(x => x.Role != "Administrator");
    var progress = new List<SetupProgressStep>
    {
      new("tenant", "Tenant connected",
        tenantId is null ? ProgressStates.Blocked : connection is not null ? ProgressStates.Done : ProgressStates.Pending,
        tenantId is null ? "No tenant is configured." : connection is not null ? $"Connection revision {connection.Revision} for tenant {tenantId}." : "Prepare the connection revision for the configured tenant.",
        tenantId is null ? "Identity:TenantId is not configured for this deployment." : null,
        tenantId is null ? "Configure the workforce tenant and sign in with Microsoft Entra." : connection is null ? "Open Microsoft 365 setup and prepare the connection." : null,
        tenantId is null ? "Deployment operator" : null, connection?.CreatedAt),
      new("consent", "Consent verified",
        consentVerified ? ProgressStates.Done : consent?.State is TenantConsentAttemptStates.Denied or TenantConsentAttemptStates.Expired ? ProgressStates.Blocked : ProgressStates.Pending,
        consentVerified ? $"Consent completed by Microsoft administrator object {verifiedConsent!.ConsentingObjectId}." : $"Last attempt: {consent?.State ?? "none"}.",
        consentVerified ? null : consent?.State is TenantConsentAttemptStates.Denied or TenantConsentAttemptStates.Expired ? "The last consent attempt was denied, expired or returned for a different tenant." : null,
        consentVerified ? null : "Select Connect Microsoft 365 tenant and have a tenant administrator grant consent.",
        consentVerified ? null : "Privileged Role Administrator or Global Administrator", verifiedConsent?.ConsentVerifiedAt),
      new("administrator", "Initial administrator", administrators > 0 ? ProgressStates.Done : ProgressStates.Blocked,
        $"{administrators} firm-wide AuditSphere administrator(s).", administrators > 0 ? null : "No active firm-wide AuditSphere Administrator.",
        administrators > 0 ? null : "Complete the proof-backed initial administrator bootstrap.", administrators > 0 ? null : "Deployment operator", null),
      Step("directory", "Directory access", directoryCap, "Verify capabilities after consent.", "Privileged Role Administrator to consent User.Read.All"),
      new("sharepoint", "SharePoint access",
        sitePassed && libraryPassed ? ProgressStates.Done : sitePassed || siteCap.State == CapabilityVerificationStates.Verified ? ProgressStates.Partial :
          siteCap.State is CapabilityVerificationStates.NotGranted or CapabilityVerificationStates.Failed ? ProgressStates.Blocked : ProgressStates.Pending,
        $"Site {(sitePassed ? "verified" : "not verified")}; library/root {(libraryPassed ? "verified" : "not verified")}.",
        sitePassed && libraryPassed ? null : siteCap.State is CapabilityVerificationStates.NotGranted or CapabilityVerificationStates.Failed ? $"Selected-site capability is {siteCap.State}." : null,
        sitePassed && libraryPassed ? null : "Grant Sites.Selected for the approved site, then test the selected site connection.",
        sitePassed && libraryPassed ? null : "SharePoint Administrator (site grant) and Privileged Role Administrator (consent)",
        sharePoint.LastVerification),
      new("mail", "Mail configuration",
        !options.OutboundMailEnabled ? ProgressStates.Pending : mailCap.State == CapabilityVerificationStates.Verified && mail.TransportState == "CONFIGURED" ? ProgressStates.Done :
          mailCap.State == CapabilityVerificationStates.Verified || mail.TransportState == "CONFIGURED" ? ProgressStates.Partial :
          mailCap.State is CapabilityVerificationStates.NotGranted or CapabilityVerificationStates.Failed ? ProgressStates.Blocked : ProgressStates.Pending,
        $"Transport {mail.TransportState}; permission {mail.PermissionState}.",
        mailCap.State is CapabilityVerificationStates.NotGranted or CapabilityVerificationStates.Failed ? $"Mail.Send is {mailCap.State}." : null,
        mailCap.State == CapabilityVerificationStates.Verified && mail.TransportState == "CONFIGURED" ? null : "Configure the approved sender mailbox and verify Mail.Send.",
        "Exchange Administrator (application access policy)", mail.LastVerification),
      new("assignment", "User assignment", nonAdminAssignments > 0 ? ProgressStates.Done : ProgressStates.Pending,
        $"{nonAdminAssignments} active non-administrator role grant(s).", null,
        nonAdminAssignments > 0 ? null : "Assign AuditSphere roles and scope to users.", null, null),
    };

    var warnings = new List<string>();
    if (administrators == 1) warnings.Add("Only one firm-wide AuditSphere Administrator exists. Add a second administrator to avoid lock-out.");
    if (openOperations.Count(x => x is ExternalOperationStates.Unknown or ExternalOperationStates.Dispatching) is > 0 and var unknown)
      warnings.Add($"{unknown} Microsoft operation(s) have an unknown result and must be reconciled before retrying.");
    if (openOperations.Count(x => x == ExternalOperationStates.ConflictRequiresReview) is > 0 and var conflicts)
      warnings.Add($"{conflicts} Microsoft operation(s) require manual conflict review.");
    if (disabledWithGrants > 0) warnings.Add($"{disabledWithGrants} disabled AuditSphere identity(ies) still hold active grants.");
    if (disabledInMicrosoftWithAccess > 0) warnings.Add($"{disabledInMicrosoftWithAccess} user(s) observed as disabled in Microsoft still hold AuditSphere access. Revoke or reverify.");
    var expiring = activeGrants.Count(x => x.ExpiresAt is { } e && e <= now.AddDays(7));
    if (expiring > 0) warnings.Add($"{expiring} role grant(s) expire within seven days.");
    foreach (var cap in capabilities.Where(x => x.Enabled && x.Stale))
      warnings.Add($"{cap.DisplayName} verification is stale; verify capabilities again.");
    foreach (var cap in capabilities.Where(x => x.Enabled && x.State is CapabilityVerificationStates.NotGranted or CapabilityVerificationStates.Failed))
      warnings.Add($"{cap.DisplayName} is enabled but {cap.State}.");
    if (!consentVerified && tenantId is not null) warnings.Add("Tenant administrator consent has not been verified.");

    var externalHealth = capabilities.Where(x => x.Enabled && x.Capability != Microsoft365Capabilities.SignIn).ToList();
    var cards = new List<StatusCard>
    {
      new("tenant", "Microsoft Tenant", consentVerified ? "VERIFIED" : connection is not null ? "PARTIAL" : "NOT_CONNECTED",
        tenantId ?? "Not configured", connection is null ? "No connection revision." : $"Connection {connection.State}; consent {connection.ConsentState}."),
      new("users", "Users", "INFO", activeUsers.ToString(), $"{disabledUsers} disabled."),
      new("invitations", "Pending Invitations", pendingInvitations > 0 ? "ATTENTION" : "INFO", pendingInvitations.ToString(), "Granted access not yet used."),
      new("administrators", "AuditSphere Administrators", administrators >= 2 ? "OK" : "ATTENTION", administrators.ToString(), "Firm-wide application administrators (not Entra roles)."),
      new("grants", "Role Grants", "INFO", activeGrants.Count.ToString(), $"{expiring} expiring within 7 days."),
      new("groups", "Microsoft Groups", Cap(Microsoft365Capabilities.GroupMembership).State, managedGroups.ToString(), "Allowlisted AuditSphere-managed groups."),
      new("site", "Selected SharePoint Site", sharePoint.SelectedSitePermission, draft?.SiteUrl ?? "Not configured", $"Library {sharePoint.LibraryAccess}; workspace {sharePoint.WorkspaceConfiguration}."),
      new("mail", "Mail", mail.PermissionState, mail.SenderIdentity, $"Transport {mail.TransportState}."),
      new("health", "External Capability Health",
        externalHealth.Count == 0 ? "NOT_CONFIGURED" : externalHealth.All(x => x.State == CapabilityVerificationStates.Verified) ? "VERIFIED" : "PARTIAL",
        $"{externalHealth.Count(x => x.State == CapabilityVerificationStates.Verified)}/{externalHealth.Count}", "Enabled capabilities verified and fresh."),
      new("warnings", "Security Warnings", warnings.Count == 0 ? "OK" : "ATTENTION", warnings.Count.ToString(), warnings.FirstOrDefault() ?? "No warnings."),
    };
    return CommandResult<AdministrationOverview>.Ok(new(tenantId, cards, progress, capabilities, sharePoint, mail,
      warnings, Microsoft365PermissionMatrix.Rows));
  }

  private static SetupProgressStep Step(string key, string title, CapabilityStatus status, string action, string authority) =>
    new(key, title,
      status.State == CapabilityVerificationStates.Verified ? ProgressStates.Done :
      status.State is CapabilityVerificationStates.NotGranted or CapabilityVerificationStates.Failed or CapabilityVerificationStates.BlockedExternal ? ProgressStates.Blocked :
      ProgressStates.Pending,
      $"{status.Permission}: {status.State}.",
      status.State is CapabilityVerificationStates.NotGranted or CapabilityVerificationStates.Failed or CapabilityVerificationStates.BlockedExternal
        ? $"{status.Permission} is {status.State} ({status.DiagnosticCode})." : status.State == TenantCapabilityService.Stale ? "Verification is stale." : null,
      status.State == CapabilityVerificationStates.Verified ? null : status.Enabled ? action : "Enable the capability in deployment configuration, then verify.",
      status.State == CapabilityVerificationStates.Verified ? null : authority, status.LastVerifiedAt);
}
