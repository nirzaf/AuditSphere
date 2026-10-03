// Microsoft 365 tenant administration records. Provider effects stay outside the domain;
// these rows hold only identifiers, outcomes and evidence — never credentials or tokens.
namespace AuditSphereOps.Domain.Microsoft365;

/// <summary>Independently consented Microsoft capabilities. None implies another.</summary>
public static class Microsoft365Capabilities
{
  public const string SignIn = "SIGN_IN";
  public const string DirectoryRead = "DIRECTORY_READ";
  public const string SelectedSite = "SELECTED_SITE";
  public const string OutboundMail = "OUTBOUND_MAIL";
  public const string TenantUserProvisioning = "TENANT_USER_PROVISIONING";
  public const string GuestInvitation = "GUEST_INVITATION";
  public const string GroupMembership = "GROUP_MEMBERSHIP";

  /// <summary>Entra directory-role administration is deliberately not a supported capability.</summary>
  public const string PrivilegedRoleAdministration = "PRIVILEGED_ROLE_ADMINISTRATION";

  public static readonly IReadOnlyList<string> All =
    [SignIn, DirectoryRead, SelectedSite, OutboundMail, TenantUserProvisioning, GuestInvitation, GroupMembership];
}

public static class CapabilityVerificationStates
{
  public const string Verified = "VERIFIED";
  public const string NotGranted = "NOT_GRANTED";
  public const string Failed = "FAILED";
  public const string BlockedExternal = "BLOCKED_EXTERNAL";
}

/// <summary>Append-only per-capability observation for one tenant (§§3, 21).</summary>
public sealed class TenantCapabilityVerification
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid? ConnectionRevisionId { get; set; }
  public Guid? ConsentAttemptId { get; set; }
  public string TenantId { get; set; } = string.Empty;
  public string Capability { get; set; } = string.Empty;
  public string Permission { get; set; } = string.Empty;
  public string State { get; set; } = CapabilityVerificationStates.BlockedExternal;
  public string DiagnosticCode { get; set; } = string.Empty;
  public string? ProviderCorrelationId { get; set; }
  public Guid ObservedByUserId { get; set; }
  public DateTimeOffset ObservedAt { get; set; }
}

/// <summary>Allowlist of AuditSphere-managed Microsoft groups. Membership never grants AuditSphere access.</summary>
public sealed class ManagedDirectoryGroup
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string TenantId { get; set; } = string.Empty;
  public string GroupObjectId { get; set; } = string.Empty;
  public string DisplayName { get; set; } = string.Empty;
  public string Purpose { get; set; } = string.Empty;
  public string ApprovalReason { get; set; } = string.Empty;
  public Guid ApprovedByUserId { get; set; }
  public DateTimeOffset ApprovedAt { get; set; }
  public Guid? RetiredByUserId { get; set; }
  public DateTimeOffset? RetiredAt { get; set; }
}

public static class ExternalOperationKinds
{
  public const string CreateTenantUser = "CREATE_TENANT_USER";
  public const string InviteGuest = "INVITE_GUEST";
  public const string AddGroupMember = "ADD_GROUP_MEMBER";
  public const string RemoveGroupMember = "REMOVE_GROUP_MEMBER";
}

/// <summary>Requested → Authorized → Dispatching → Accepted/Failed/Unknown → Reconciled → Bound (§16).</summary>
public static class ExternalOperationStates
{
  public const string Requested = "REQUESTED";
  public const string Authorized = "AUTHORIZED";
  public const string Dispatching = "DISPATCHING";
  public const string Accepted = "ACCEPTED";
  public const string Failed = "FAILED";
  public const string Unknown = "UNKNOWN";
  public const string Reconciled = "RECONCILED";
  public const string Bound = "BOUND";
  public const string ConflictRequiresReview = "CONFLICT_REQUIRES_REVIEW";
}

/// <summary>
/// One Microsoft mutation with an idempotency key. It is never atomic with local state:
/// the local binding is a separate, later transaction fenced by this record.
/// </summary>
public sealed class Microsoft365ExternalOperation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string IdempotencyKey { get; set; } = string.Empty;
  public string Kind { get; set; } = string.Empty;
  public string State { get; set; } = ExternalOperationStates.Requested;
  public string RequestFingerprint { get; set; } = string.Empty;
  public string TenantId { get; set; } = string.Empty;
  public string TargetDescriptor { get; set; } = string.Empty; // UPN, invited email or group/member pair
  public string? DisplayName { get; set; }
  public string? ResultObjectId { get; set; }
  public string? ProviderCorrelationId { get; set; }
  public string? ResultCode { get; set; }
  public string? ReconciliationResult { get; set; }
  public string Reason { get; set; } = string.Empty;
  public string? RequestedRole { get; set; }
  public string? RequestedScopeKind { get; set; }
  public Guid? RequestedClientId { get; set; }
  public Guid? RequestedEngagementId { get; set; }
  public Guid? ManagedGroupId { get; set; }
  public Guid? BoundUserId { get; set; }
  public Guid? BoundRoleGrantId { get; set; }
  public int AttemptCount { get; set; }
  public Guid RequestedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public DateTimeOffset? DispatchedAt { get; set; }
  public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Immutable administration audit event (§17). Holds no secrets, tokens or callbacks.</summary>
public sealed class Microsoft365AdministrationEvent
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ActorUserId { get; set; }
  public string Operation { get; set; } = string.Empty;
  // Optional local setup request receipt. These are never Microsoft operation/correlation identifiers.
  public Guid? SetupRequestId { get; set; }
  public string? SetupRequestHash { get; set; }
  public Guid? SetupDraftId { get; set; }
  public long? SetupRevisionAfter { get; set; }

  public string? TargetTenantId { get; set; }
  public string? TargetObjectId { get; set; }
  public Guid? TargetUserId { get; set; }
  public string OldState { get; set; } = string.Empty;
  public string NewState { get; set; } = string.Empty;
  public string? RoleScopeChange { get; set; }
  public string Reason { get; set; } = string.Empty;
  public Guid? ExternalOperationId { get; set; }
  public string? ProviderCorrelationId { get; set; }
  public string Result { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
