namespace AuditSphereOps.Domain.Accounting;

/// <summary>Immutable client credit note submission against one posted invoice and its open item.</summary>
public sealed class ClientSalesCreditNoteSubmission
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid CreditNoteId { get; set; }
  public string CreditNoteReference { get; set; } = string.Empty;
  public Guid OriginalInvoiceId { get; set; }
  public Guid OriginalSubmissionId { get; set; }
  public Guid OriginalOpenItemId { get; set; }
  public Guid CustomerId { get; set; }
  public Guid PeriodId { get; set; }
  public DateOnly PostingDate { get; set; }
  public string Currency { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public Guid JournalId { get; set; }
  public long JournalSubmittedRevision { get; set; }
  public Guid CommandId { get; set; }
  public string IntentHash { get; set; } = string.Empty;
  public string ManifestJson { get; set; } = string.Empty;
  public string ManifestHash { get; set; } = string.Empty;
  public string PreviewDigest { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public string SourceBasis { get; set; } = string.Empty;
  public Guid? SourceReceiptId { get; set; }
  public string? SourceReceiptHash { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable independent approval or return decision for one exact credit-note submission.</summary>
public sealed class ClientSalesCreditNoteDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid SubmissionId { get; set; }
  public Guid CommandId { get; set; }
  public string IntentHash { get; set; } = string.Empty;
  public string Decision { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public string PreviewDigest { get; set; } = string.Empty;
  public string ReviewContextJson { get; set; } = string.Empty;
  public Guid ActorUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Positive semantic credit amount retained separately from the original receivable.</summary>
public sealed class ClientSalesCreditNoteLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid SubmissionId { get; set; }
  public int OriginalLineNumber { get; set; }
  public Guid RevenueAccountId { get; set; }
  public string RevenueAccountCode { get; set; } = string.Empty;
  public decimal Amount { get; set; }
}

/// <summary>Unapplied client credit open item created atomically with approved ledger posting.</summary>
public sealed class ClientSalesCreditNoteOpenItem
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid CreditNoteId { get; set; }
  public Guid SubmissionId { get; set; }
  public Guid OriginalInvoiceId { get; set; }
  public Guid JournalId { get; set; }
  public Guid CustomerId { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string Direction { get; set; } = "CREDIT";
  public decimal OriginalAmount { get; set; }
  public DateTimeOffset PostedAt { get; set; }
}
