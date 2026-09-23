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
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public DateTimeOffset? SentAt { get; set; }
  public DateTimeOffset? ResponseAt { get; set; }
  public string? ResponseReason { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PracticeClient
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string LegalName { get; set; } = string.Empty;
  public string? CommercialName { get; set; }
  public string? RegistrationNumber { get; set; }
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
  public string Role { get; set; } = string.Empty;
  public string? ApprovedScope { get; set; }
  public DateTimeOffset? ValidFrom { get; set; }
  public DateTimeOffset? ValidTo { get; set; }
  public bool Primary { get; set; }
}
