namespace AuditSphereOps.Domain.Accounting;

/// <summary>Immutable invoice submission linked to one exact retained journal submission.</summary>
public sealed class ClientSalesInvoiceSubmission
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid InvoiceId { get; set; }
  public Guid DraftId { get; set; }
  public long DraftRevision { get; set; }
  public Guid JournalId { get; set; }
  public long JournalSubmittedRevision { get; set; }
  public Guid CommandId { get; set; }
  public string IntentHash { get; set; } = "";
  public string ManifestJson { get; set; } = "";
  public string ManifestHash { get; set; } = "";
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClientSalesInvoiceDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid SubmissionId { get; set; }
  public Guid CommandId { get; set; }
  public string IntentHash { get; set; } = "";
  public string Decision { get; set; } = "";
  public string Reason { get; set; } = "";
  public string PreviewDigest { get; set; } = "";
  public string ReviewContextJson { get; set; } = "";
  public Guid ActorUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable origin amount. Settlement balance is derived from ledger-backed allocations, never overwritten.</summary>
public sealed class ClientSalesInvoiceOpenItem
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid InvoiceId { get; set; }
  public Guid SubmissionId { get; set; }
  public Guid JournalId { get; set; }
  public Guid CustomerId { get; set; }
  public string Currency { get; set; } = "";
  public decimal OriginalAmount { get; set; }
  public DateOnly DueDate { get; set; }
  public DateTimeOffset PostedAt { get; set; }
}
