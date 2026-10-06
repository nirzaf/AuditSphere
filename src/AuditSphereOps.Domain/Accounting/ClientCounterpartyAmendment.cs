namespace AuditSphereOps.Domain.Accounting;

/// <summary>Immutable proposed contact-detail revision; party identity and financial role stay fixed.</summary>
public sealed class ClientCounterpartyAmendment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid CounterpartyId { get; set; }
  public long Revision { get; set; }
  public string DisplayName { get; set; } = "";
  public string Address { get; set; } = "";
  public string TaxIdentifier { get; set; } = "";
  public string ContactDetails { get; set; } = "";
  public string PaymentTerms { get; set; } = "";
  public string Reason { get; set; } = "";
  public Guid ProposedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Append-only independent decision on an exact proposed revision.</summary>
public sealed class ClientCounterpartyAmendmentDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid CounterpartyId { get; set; }
  public Guid AmendmentId { get; set; }
  public long Revision { get; set; }
  public string Decision { get; set; } = "";
  public string Reason { get; set; } = "";
  public Guid ReviewedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
