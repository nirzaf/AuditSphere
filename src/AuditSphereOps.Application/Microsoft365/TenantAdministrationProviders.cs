using AuditSphereOps.Domain.Microsoft365;

namespace AuditSphereOps.Application.Microsoft365;

// Narrow provider boundaries for Microsoft 365 tenant administration (§15). Infrastructure owns
// the Graph calls; Application owns authorization, validation, idempotency and local mutations.
// No implementation may return, log or persist access tokens, refresh tokens or passwords.

/// <summary>Immutable identity of the person who completed the Microsoft consent sign-in.</summary>
public sealed record ConsentingAdministrator(string TenantId, string ObjectId, string Nonce, bool ExternalIdentity);

public sealed record CapabilityProbe(string Capability, string Permission);

public sealed record CapabilityProbeResult(string Capability, string State, string DiagnosticCode,
  string? ProviderCorrelationId = null);

public interface IMicrosoftTenantConsentVerifier
{
  /// <summary>True only when the deployment configured a separate consent application and callback.</summary>
  bool IsConfigured { get; }

  /// <summary>Builds the fixed-tenant authorization request that authenticates the consenting administrator.</summary>
  Uri BuildIdentityChallenge(string tenantId, string state, string nonce);

  /// <summary>Redeems a one-use authorization code server-side and returns validated immutable claims.</summary>
  Task<ConsentingAdministrator> RedeemIdentityAsync(string tenantId, string code, CancellationToken ct);

  /// <summary>Checks each requested capability with its own credential and bounded Graph read.</summary>
  Task<IReadOnlyList<CapabilityProbeResult>> VerifyCapabilitiesAsync(string tenantId,
    IReadOnlyList<CapabilityProbe> capabilities, CancellationToken ct);
}

public static class ProviderOutcomes
{
  public const string Accepted = "ACCEPTED";
  public const string Failed = "FAILED";
  public const string Unknown = "UNKNOWN";
}

/// <summary>Result of one Microsoft mutation. ErrorCode is a safe typed code, never a raw exception.</summary>
public sealed record ProviderMutationResult(string Outcome, string? ObjectId, string? CorrelationId,
  string? ErrorCode = null)
{
  public static ProviderMutationResult Accepted(string objectId, string? correlationId) =>
    new(ProviderOutcomes.Accepted, objectId, correlationId);
  public static ProviderMutationResult Failed(string code, string? correlationId = null) =>
    new(ProviderOutcomes.Failed, null, correlationId, code);
  public static ProviderMutationResult Unknown(string? correlationId = null) =>
    new(ProviderOutcomes.Unknown, null, correlationId, "provider-outcome-unknown");
}

/// <summary>A directory object observed during reconciliation. CreatedAt is Microsoft's createdDateTime.</summary>
public sealed record DirectoryUserRecord(string TenantId, string ObjectId, string DisplayName,
  string UserPrincipalName, string? Mail, bool AccountEnabled, string UserType, DateTimeOffset? CreatedAt);

/// <summary>Initial password is created in memory, sent once to Microsoft and never persisted.</summary>
public sealed record NewDirectoryUser(string DisplayName, string UserPrincipalName, string MailNickname,
  bool AccountEnabled, string TemporaryPassword, bool ForceChangePasswordNextSignIn);

public interface IMicrosoftDirectoryUserProvisioner
{
  bool IsConfigured { get; }
  Task<ProviderMutationResult> CreateUserAsync(string tenantId, NewDirectoryUser user, CancellationToken ct);
  Task<DirectoryUserRecord?> FindByUserPrincipalNameAsync(string tenantId, string userPrincipalName, CancellationToken ct);
}

public interface IMicrosoftGuestInvitationProvider
{
  bool IsConfigured { get; }
  Task<ProviderMutationResult> InviteAsync(string tenantId, string email, string redirectUrl, CancellationToken ct);
  Task<IReadOnlyList<DirectoryUserRecord>> FindGuestsByEmailAsync(string tenantId, string email, CancellationToken ct);
}

public sealed record DirectoryGroupRecord(string TenantId, string ObjectId, string DisplayName,
  bool SecurityEnabled, bool IsAssignableToRole, bool DynamicMembership);

public sealed record DirectoryGroupMember(string ObjectId, string DisplayName, string UserPrincipalName);

public sealed record DirectoryGroupMemberPage(IReadOnlyList<DirectoryGroupMember> Members, string? NextPageToken);

public interface IMicrosoftGroupMembershipProvider
{
  bool IsConfigured { get; }
  Task<DirectoryGroupRecord> GetGroupAsync(string tenantId, string groupObjectId, CancellationToken ct);
  Task<DirectoryGroupMemberPage> ListMembersAsync(string tenantId, string groupObjectId, string? pageToken, CancellationToken ct);
  Task<bool> IsMemberAsync(string tenantId, string groupObjectId, string memberObjectId, CancellationToken ct);
  Task<ProviderMutationResult> AddMemberAsync(string tenantId, string groupObjectId, string memberObjectId, CancellationToken ct);
  Task<ProviderMutationResult> RemoveMemberAsync(string tenantId, string groupObjectId, string memberObjectId, CancellationToken ct);
}

/// <summary>One row of the documented permission matrix (§21). No capability ships without a row.</summary>
public sealed record CapabilityPermission(
  string Capability, string DisplayName, string GraphEndpoint, string Permission, string PermissionType,
  string WhyRequired, string AdminConsent, string RequiredEntraRole, bool Optional);

public static class Microsoft365PermissionMatrix
{
  public static readonly IReadOnlyList<CapabilityPermission> Rows =
  [
    new(Microsoft365Capabilities.SignIn, "Sign-in", "Microsoft identity platform OIDC", "openid profile email",
      "Delegated", "Authenticate one work/school identity and bind immutable tid + oid.",
      "Not required for sign-in scopes", "None", Optional: false),
    new(Microsoft365Capabilities.DirectoryRead, "Microsoft directory", "GET /users, GET /users/{id}", "User.Read.All",
      "Application (separate reader identity)", "Bounded search of existing users and exact enabled/userType checks.",
      "Required", "Privileged Role Administrator or Global Administrator to consent", Optional: true),
    new(Microsoft365Capabilities.SelectedSite, "Selected SharePoint site", "GET/PUT /sites/{id}, /drives/{id}", "Sites.Selected",
      "Application (document-worker identity)", "Read/write only the explicitly granted working site.",
      "Required, plus a separate per-site grant", "Privileged Role Administrator; SharePoint Administrator for the site grant", Optional: false),
    new(Microsoft365Capabilities.OutboundMail, "Outbound mail", "POST /users/{sender}/sendMail", "Mail.Send",
      "Application (existing mail provider, restricted to one sender mailbox)", "Send AuditSphere notifications from the approved sender only.",
      "Required; restrict with an Exchange application access policy", "Privileged Role Administrator; Exchange Administrator for the access policy", Optional: true),
    new(Microsoft365Capabilities.TenantUserProvisioning, "Create Microsoft 365 users", "POST /users, GET /users?$filter=userPrincipalName eq", "User.Create",
      "Application (separate provisioner identity)", "Create a new workforce user only when explicitly enabled; reconcile by UPN before retry.",
      "Required", "Privileged Role Administrator or Global Administrator to consent", Optional: true),
    new(Microsoft365Capabilities.GuestInvitation, "Guest invitations", "POST /invitations", "User.Invite.All",
      "Application (separate inviter identity)", "Invite an explicitly approved external client identity; never automatic.",
      "Required; tenant B2B policy must allow invitations", "Privileged Role Administrator or Global Administrator to consent", Optional: true),
    new(Microsoft365Capabilities.GroupMembership, "Managed group membership", "GET /groups/{id}, GET/POST/DELETE /groups/{id}/members", "GroupMember.ReadWrite.All",
      "Application (separate group-manager identity)", "Add/remove users only in allowlisted AuditSphere groups; role-assignable groups refused.",
      "Required", "Privileged Role Administrator or Global Administrator to consent", Optional: true),
  ];

  /// <summary>Permissions no AuditSphere runtime may request (§2).</summary>
  public static readonly IReadOnlyList<string> Prohibited =
    ["Directory.ReadWrite.All", "User.ReadWrite.All", "RoleManagement.ReadWrite.Directory",
     "Sites.ReadWrite.All", "Sites.FullControl.All", "Files.ReadWrite.All", "Mail.ReadWrite", "Mail.Read"];

  public static CapabilityPermission For(string capability) =>
    Rows.Single(x => x.Capability == capability);
}
