namespace AuditSphereOps.Domain.Accounting;

/// <summary>Immutable supplier credit submission linked to one purchase invoice or explicit unlinked exception.</summary>
public sealed class ClientPurchaseCreditNoteSubmission
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid CreditNoteId { get; set; }
  public string CreditNoteReference { get; set; } = string.Empty;
  public Guid? OriginalInvoiceId { get; set; }
  public Guid? OriginalSubmissionId { get; set; }
  public Guid? OriginalOpenItemId { get; set; }
  public Guid SupplierId { get; set; }
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

/// <summary>Append-only independent review decision for the exact supplier-credit posting.</summary>
public sealed class ClientPurchaseCreditNoteDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid SubmissionId { get; set; }
  public Guid CommandId { get; set; }
  public string IntentHash { get; set; } = string.Empty;
  public string Decision { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public string DuplicateResolutionReason { get; set; } = string.Empty;
  public string PreviewDigest { get; set; } = string.Empty;
  public string ReviewContextJson { get; set; } = string.Empty;
  public Guid ActorUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Positive semantic credit amount against one original purchase line.</summary>
public sealed class ClientPurchaseCreditNoteLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid SubmissionId { get; set; }
  public int LineNumber { get; set; }
  public int? OriginalLineNumber { get; set; }
  public Guid ExpenseAccountId { get; set; }
  public string ExpenseAccountCode { get; set; } = string.Empty;
  public decimal Amount { get; set; }
}

/// <summary>Unapplied supplier debit balance created atomically with an approved purchase-credit posting.</summary>
public sealed class ClientPurchaseCreditNoteOpenItem
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid CreditNoteId { get; set; }
  public Guid SubmissionId { get; set; }
  public Guid? OriginalInvoiceId { get; set; }
  public Guid JournalId { get; set; }
  public Guid SupplierId { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string Direction { get; set; } = "DEBIT";
  public decimal OriginalAmount { get; set; }
  public DateTimeOffset PostedAt { get; set; }
}
