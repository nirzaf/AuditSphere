// Practice CRM: Lead → Opportunity → Proposal → PracticeClient → ClientContact (§41.2).
// Billing account links PracticeClient to invoicing; independence holds evaluated at proposal/acceptance.
namespace AuditSphereOps.Domain.Practice;

public sealed class Lead
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string Name { get; set; } = string.Empty;
  public string Source { get; set; } = string.Empty;
  public string Status { get; set; } = "New";
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Opportunity
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid LeadId { get; set; }
  public string ServiceRoute { get; set; } = string.Empty; // AccountingOnly | FinancialStatementAudit | ...
  public string Stage { get; set; } = "Qualified";
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Proposal
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid OpportunityId { get; set; }
  public long Revision { get; set; } = 1;
  public string Status { get; set; } = "Draft";
  public string ServiceProfileId { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PracticeClient
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string LegalName { get; set; } = string.Empty;
  public string? CommercialName { get; set; }
  public string Status { get; set; } = "Prospect";
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
  public bool Primary { get; set; }
}
