// Practice CRM: Lead → Opportunity → Proposal → PracticeClient → ClientContact (§41.2).
// Billing account links PracticeClient to invoicing; independence holds evaluated at proposal/acceptance.
namespace AuditSphereOps.Domain.Practice;

public static class CrmStates
{
  public const string LeadNew = "NEW";
  public const string LeadQualified = "QUALIFIED";
  public const string LeadUnqualified = "UNQUALIFIED";
  public const string LeadLost = "LOST";
  public const string OpportunityDiscovery = "DISCOVERY";
  public const string OpportunityProposal = "PROPOSAL";
  public const string OpportunityNegotiation = "NEGOTIATION";
  public const string OpportunityWon = "WON";
  public const string OpportunityLost = "LOST";
  public const string ProposalDraft = "DRAFT";
  public const string ProposalInternalReview = "INTERNAL_REVIEW";
  public const string ProposalSent = "SENT";
  public const string ProposalAccepted = "ACCEPTED";
  public const string ProposalDeclined = "DECLINED";
  public const string ProposalSuperseded = "SUPERSEDED";
  public const string ClientProspect = "PROSPECT";
}

public sealed class Lead
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string Name { get; set; } = string.Empty;
  public string Source { get; set; } = string.Empty;
  public string? PrimaryContactName { get; set; }
  public string? PrimaryContactEmail { get; set; }
  public Guid? OwnerUserId { get; set; }
  public string? ConsentRestrictions { get; set; }
  public string Status { get; set; } = CrmStates.LeadNew;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Opportunity
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid LeadId { get; set; }
  public Guid? PracticeClientId { get; set; }
  public string ServiceRoute { get; set; } = string.Empty; // AccountingOnly | FinancialStatementAudit | ...
  public string EntityScope { get; set; } = string.Empty;
  public string PeriodStart { get; set; } = string.Empty;
  public string PeriodEnd { get; set; } = string.Empty;
  public decimal ExpectedFee { get; set; }
  public string Currency { get; set; } = string.Empty;
  public decimal? Probability { get; set; }
  public Guid? OwnerUserId { get; set; }
  public string? NextAction { get; set; }
  public string Stage { get; set; } = CrmStates.OpportunityDiscovery;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Proposal
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid OpportunityId { get; set; }
  public Guid? PracticeClientId { get; set; }
  public long Revision { get; set; } = 1;
  public string Status { get; set; } = CrmStates.ProposalDraft;
  public string ServiceProfileId { get; set; } = string.Empty;
  public string Scope { get; set; } = string.Empty;
  public string Exclusions { get; set; } = string.Empty;
  public string Deliverables { get; set; } = string.Empty;
  public string Dependencies { get; set; } = string.Empty;
  public decimal Fee { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string PeriodStart { get; set; } = string.Empty;
  public string PeriodEnd { get; set; } = string.Empty;
  public Guid? SupersedesId { get; set; }
  public Guid? PreparedByUserId { get; set; }
  /// <summary>Immutable digest of the reviewed creation request when the proposal ID is its request identity.</summary>
  public string? CreateRequestHash { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public DateTimeOffset? SentAt { get; set; }
  /// <summary>Exact offer identity queued for dispatch; client acceptance must cite this hash (STE 4.1.3).</summary>
  public string? SentOfferSha256 { get; set; }
  public DateTimeOffset? ResponseAt { get; set; }
  public string? ResponseReason { get; set; }
  /// <summary>Offer identity the respondent answered; a superseded or revised offer cannot be accepted.</summary>
  public string? ResponseOfferSha256 { get; set; }
  public string? RespondentName { get; set; }
  public string? RespondentEmail { get; set; }
  public string? ResponseEvidenceReference { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class LeadChannels
{
  public const string Phone = "Phone";
  public const string WhatsApp = "WhatsApp";
  public const string Email = "Email";
  public const string WebForm = "Web Form";
  public const string Referral = "Referral";
  public const string InPerson = "In-person";

  public static readonly string[] All = [Phone, WhatsApp, Email, WebForm, Referral, InPerson];
}

public sealed class PracticeClient
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string LegalName { get; set; } = string.Empty;
  public string? CommercialName { get; set; }
  public string? RegistrationNumber { get; set; }
  public string? TaxRegistrationNumber { get; set; }
  public string? EntityType { get; set; }
  public string? Jurisdiction { get; set; }
  public string? RestrictedProfile { get; set; }
  public string Status { get; set; } = CrmStates.ClientProspect;
  public Guid? BillingAccountId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClientContact
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PracticeClientId { get; set; }
  public string FullName { get; set; } = string.Empty;
  public string Email { get; set; } = string.Empty;
  public string? Phone { get; set; }
  public string Role { get; set; } = string.Empty;
  public string? Title { get; set; }
  public string? SignatoryAuthority { get; set; }
  public string? ApprovedScope { get; set; }
  public DateTimeOffset? ValidFrom { get; set; }
  public DateTimeOffset? ValidTo { get; set; }
  public bool Primary { get; set; }
  public bool IsActive { get; set; } = true;
}

public static class ClientRelationshipKinds
{
  public const string Parent = "PARENT";
  public const string Subsidiary = "SUBSIDIARY";
  public const string Affiliate = "AFFILIATE";

  public static readonly string[] All = [Parent, Subsidiary, Affiliate];
}

public sealed class ClientRelationship
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PrimaryClientId { get; set; }
  public Guid RelatedClientId { get; set; }
  public string RelationshipKind { get; set; } = ClientRelationshipKinds.Subsidiary;
  public decimal? OwnershipPercentage { get; set; }
  public DateOnly? EffectiveFrom { get; set; }
  public DateOnly? EffectiveTo { get; set; }
  public string? Notes { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public Guid? RevokedByUserId { get; set; }
  public DateTimeOffset? RevokedAt { get; set; }
  public string? RevocationReason { get; set; }
}

public static class CorrespondencePurposes
{
  public const string Commercial = "COMMERCIAL";
  public const string Finance = "FINANCE";
  public const string AuditFieldwork = "AUDIT_FIELDWORK";
  public const string Completion = "COMPLETION";

  public static readonly string[] All = [Commercial, Finance, AuditFieldwork, Completion];
}

public sealed class ClientContactRouting
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PracticeClientId { get; set; }
  public Guid ClientContactId { get; set; }
  public string Purpose { get; set; } = CorrespondencePurposes.Commercial;
  public DateOnly? EffectiveFrom { get; set; }
  public DateOnly? EffectiveTo { get; set; }
  public bool IsPrimaryForPurpose { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public Guid? RevokedByUserId { get; set; }
  public DateTimeOffset? RevokedAt { get; set; }
  public string? RevocationReason { get; set; }
}

public sealed class CorrespondenceDispatchRecord
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PracticeClientId { get; set; }
  public Guid? EngagementId { get; set; }
  public string Purpose { get; set; } = CorrespondencePurposes.Commercial;
  public Guid RecipientContactId { get; set; }
  public string RecipientName { get; set; } = string.Empty;
  public string RecipientEmail { get; set; } = string.Empty;
  public string DocumentType { get; set; } = string.Empty;
  public string DocumentReference { get; set; } = string.Empty;
  public long DocumentRevision { get; set; }
  public string DocumentSha256 { get; set; } = string.Empty;
  public bool WasOverridden { get; set; }
  public Guid? OverriddenByUserId { get; set; }
  public string? OverrideReason { get; set; }
  public Guid DispatchedByUserId { get; set; }
  public DateTimeOffset DispatchedAt { get; set; }
}
