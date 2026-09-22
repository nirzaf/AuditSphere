// Microsoft 365 onboarding control-plane records. Provider effects remain outside the domain.
namespace AuditSphereOps.Domain.Microsoft365;

public static class Microsoft365SetupStates
{
  public const string Unclaimed = "UNCLAIMED";
  public const string Claimed = "CLAIMED";
  public const string Active = "ACTIVE";
  public const string Expired = "EXPIRED";
}

public static class Microsoft365RevisionStates
{
  public const string Draft = "DRAFT";
  public const string Validating = "VALIDATING";
  public const string Verified = "VERIFIED";
  public const string Active = "ACTIVE";
  public const string ConsentRequired = "CONSENT_REQUIRED";
  public const string Suspended = "SUSPENDED";
  public const string Blocked = "BLOCKED";
}

public static class Microsoft365AccessProfiles
{
  public const string AppMediated = "APP_MEDIATED";
  public const string DirectStaffCollaboration = "DIRECT_STAFF_COLLABORATION";
}

public sealed class Microsoft365SetupSession
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string InstallationId { get; set; } = string.Empty;
  public string BootstrapProofHash { get; set; } = string.Empty;
  public string CapabilityHash { get; set; } = string.Empty;
  public string State { get; set; } = Microsoft365SetupStates.Claimed;
  public long Revision { get; set; } = 1;
  public Guid? ClaimedByUserId { get; set; }
  public DateTimeOffset ClaimedAt { get; set; }
  public DateTimeOffset ExpiresAt { get; set; }
  public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class Microsoft365SetupDraft
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid SetupSessionId { get; set; }
  public long Revision { get; set; } = 1;
  public string State { get; set; } = Microsoft365RevisionStates.Draft;
  public string? ExpectedTenantId { get; set; }
  public string? TenantDisplayName { get; set; }
  public string? SiteUrl { get; set; }
  public string? SiteId { get; set; }
  public string? DriveId { get; set; }
  public string? RootFolderId { get; set; }
  public Guid? ConnectionRevisionId { get; set; }
  public string AccessProfile { get; set; } = Microsoft365AccessProfiles.AppMediated;
  public string MailState { get; set; } = "NOT_CONFIGURED";
  public string RecordsState { get; set; } = "NOT_CONFIGURED";
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Microsoft365ConnectionRevision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public long Revision { get; set; } = 1;
  public string TenantId { get; set; } = string.Empty;
  public string LoginClientIdReference { get; set; } = string.Empty;
  public string RuntimeCredentialReference { get; set; } = string.Empty;
  public string CloudProfile { get; set; } = "PUBLIC";
  public string State { get; set; } = Microsoft365RevisionStates.Draft;
  public string ConsentState { get; set; } = "REQUIRED";
  public Guid? CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? VerifiedAt { get; set; }
  public DateTimeOffset? ActivatedAt { get; set; }
}

public sealed class FirmWorkspaceConfiguration
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ConnectionRevisionId { get; set; }
  public string TenantId { get; set; } = string.Empty;
  public string SiteId { get; set; } = string.Empty;
  public string DriveId { get; set; } = string.Empty;
  public string RootFolderId { get; set; } = string.Empty;
  public string DisplayUrl { get; set; } = string.Empty;
  public string AccessProfile { get; set; } = Microsoft365AccessProfiles.AppMediated;
  public Guid FolderTemplateVersionId { get; set; }
  public bool DefaultForFutureClients { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FolderTemplateVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public long Version { get; set; } = 1;
  public string Purpose { get; set; } = "CLIENT_WORKSPACE";
  public string ManifestJson { get; set; } = "{}";
  public string ManifestDigest { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
}

public sealed class IntegrationVerificationEvidence
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid SetupDraftId { get; set; }
  public Guid? ConnectionRevisionId { get; set; }
  public string ResourceKind { get; set; } = string.Empty;
  public string ResourceId { get; set; } = string.Empty;
  public string Operation { get; set; } = string.Empty;
  public string IdentityReference { get; set; } = string.Empty;
  public string Result { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public DateTimeOffset ObservedAt { get; set; }
}

/// <summary>Bounded directory observation used by the administrator fallback roster and future Graph reader.</summary>
public sealed class DirectoryUserObservation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid? ConnectionRevisionId { get; set; }
  public string TenantId { get; set; } = string.Empty;
  public string ObjectId { get; set; } = string.Empty;
  public string DisplayName { get; set; } = string.Empty;
  public string? UserPrincipalName { get; set; }
  public string? Mail { get; set; }
  public string EnabledState { get; set; } = "UNKNOWN"; // ENABLED | DISABLED | UNKNOWN
  public string UserType { get; set; } = "UNKNOWN";
  public string Source { get; set; } = "APPROVED_ROSTER"; // APPROVED_ROSTER | VERIFIED_SIGN_IN | GRAPH
  public DateTimeOffset ObservedAt { get; set; }
}

public static class UserAccessInvitationStates
{
  public const string NotSent = "NOT_SENT";
  public const string Queued = "QUEUED";
  public const string ProviderAccepted = "PROVIDER_ACCEPTED";
  public const string Failed = "FAILED";
  public const string Unknown = "UNKNOWN";
  public const string Copied = "COPIED";
}

/// <summary>Notification intent for an existing Microsoft identity; never an authentication token.</summary>
public sealed class UserAccessInvitation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid UserId { get; set; }
  public Guid RoleGrantId { get; set; }
  public string RecipientEmail { get; set; } = string.Empty;
  public string DestinationPath { get; set; } = "/auth/landing";
  public string DeliveryState { get; set; } = UserAccessInvitationStates.NotSent;
  public int AttemptCount { get; set; }
  public string? ProviderCorrelationId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public DateTimeOffset? FirstAccessAt { get; set; }
  public DateTimeOffset? LastAttemptAt { get; set; }
}

public static class ClientWorkspaceStates
{
  public const string WaitingForIntegration = "WAITING_FOR_INTEGRATION";
  public const string Queued = "QUEUED";
  public const string Provisioning = "PROVISIONING";
  public const string Verifying = "VERIFYING";
  public const string Ready = "READY";
  public const string BlockedAcceptance = "BLOCKED_ACCEPTANCE";
  public const string BlockedConfiguration = "BLOCKED_CONFIGURATION";
  public const string ResultUncertain = "RESULT_UNCERTAIN";
  public const string ConflictRequiresReview = "CONFLICT_REQUIRES_REVIEW";
  public const string Suspended = "SUSPENDED";
}

public sealed class ClientWorkspace
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PracticeClientId { get; set; }
  public Guid AcceptanceDecisionId { get; set; }
  public string Purpose { get; set; } = "PRIMARY";
  public string LogicalKey { get; set; } = string.Empty;
  public string State { get; set; } = ClientWorkspaceStates.WaitingForIntegration;
  public long Revision { get; set; } = 1;
  public Guid? ConnectionRevisionId { get; set; }
  public Guid? FolderTemplateVersionId { get; set; }
  public string? TenantId { get; set; }
  public string? SiteId { get; set; }
  public string? DriveId { get; set; }
  public string? RootFolderId { get; set; }
  public string? RemoteItemId { get; set; }
  public string? LastErrorCode { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? LastVerifiedAt { get; set; }
}
