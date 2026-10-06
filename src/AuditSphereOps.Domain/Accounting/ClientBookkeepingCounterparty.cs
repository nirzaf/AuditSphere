namespace AuditSphereOps.Domain.Accounting;

/// <summary>Client-owned immutable party profile. It is not the firm's CRM client or a group counterparty.</summary>
public sealed class ClientBookkeepingCounterparty
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public string LegalName { get; set; } = "";
  public string NormalizedLegalName { get; set; } = "";
  public string DisplayName { get; set; } = "";
  public string Role { get; set; } = "";
  public string Address { get; set; } = "";
  public string Country { get; set; } = "";
  public string TaxIdentifier { get; set; } = "";
  public string ContactDetails { get; set; } = "";
  public string PaymentTerms { get; set; } = "";
  public string DefaultCurrency { get; set; } = "";
  public string ExternalSystem { get; set; } = "";
  public string ExternalReference { get; set; } = "";
  public string? NormalizedExternalIdentity { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
