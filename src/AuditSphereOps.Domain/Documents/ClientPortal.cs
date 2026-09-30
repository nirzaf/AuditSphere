// Client portal onboarding: the first-sign-in requirement that gates uploads, request delegation by the client's
// primary contact, and the portal intent recorded at lead conversion (STE 1.3-01, 1.3-02, 1.3-05).
namespace AuditSphereOps.Domain.Documents;

/// <summary>How the client identity reached the portal; derived server-side from directory evidence, never supplied.</summary>
public static class ClientIdentityPaths
{
  /// <summary>Member created by AuditSphere with a temporary password that Entra forces to be changed at first sign-in.</summary>
  public const string ProvisionedMember = "PROVISIONED_MEMBER";
  /// <summary>Invited guest or federated identity whose password is owned by the home organisation.</summary>
  public const string ExternalIdentity = "EXTERNAL_IDENTITY";
  /// <summary>No directory observation (e.g. local development trust); only the portal acknowledgement is recorded.</summary>
  public const string Unobserved = "UNOBSERVED_IDENTITY";
}

/// <summary>Append-only record that a client identity satisfied the first-sign-in requirement for its path.</summary>
public sealed class ClientPortalFirstSignIn
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid UserId { get; set; }
  public string IdentityPath { get; set; } = ClientIdentityPaths.Unobserved;
  /// <summary>For provisioned members: the observed sign-in after creation, which Entra only permits after the forced change.</summary>
  public DateTimeOffset? SignInObservedAt { get; set; }
  public string TermsVersion { get; set; } = string.Empty;
  public DateTimeOffset CompletedAt { get; set; }
}

/// <summary>A primary client contact delegating one request to another user of the same client. Revocation is a stamp.</summary>
public sealed class PbcRequestDelegation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PbcRequestId { get; set; }
  public Guid DelegatorUserId { get; set; }
  public Guid DelegateUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? RevokedAt { get; set; }
  public Guid? RevokedByUserId { get; set; }
}

public static class ClientPortalIntentStates
{
  /// <summary>Recorded at conversion; no access exists until professional acceptance and activation.</summary>
  public const string AwaitingAcceptance = "AWAITING_ACCEPTANCE";
  /// <summary>An engagement was activated by a Partner; an administrator may now invite the contact.</summary>
  public const string ReadyToInvite = "READY_TO_INVITE";
  /// <summary>The contact holds a client role for this client.</summary>
  public const string Invited = "INVITED";
}

/// <summary>
/// The portal environment intended for the client's primary contact, recorded when the lead converts. It never grants
/// access by itself: the professional acceptance and Partner activation gates run first, and the Microsoft invitation
/// stays the administrator's user-access action.
/// </summary>
public sealed class ClientPortalIntent
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PracticeClientId { get; set; }
  public Guid ClientContactId { get; set; }
  public string RecipientEmail { get; set; } = string.Empty;
  public Guid SourceProposalId { get; set; }
  public string State { get; set; } = ClientPortalIntentStates.AwaitingAcceptance;
  public Guid? ActivatedEngagementId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
}
