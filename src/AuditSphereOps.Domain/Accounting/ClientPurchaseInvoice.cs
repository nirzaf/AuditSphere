namespace AuditSphereOps.Domain.Accounting;

/// <summary>Immutable revision of an incoming client purchase invoice preparation.</summary>
public sealed class ClientPurchaseInvoiceDraft
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid InvoiceId { get; set; }
  public long Revision { get; set; }
  public Guid? PreviousRevisionId { get; set; }
  public long? PreviousRevision { get; set; }
  public Guid CommandId { get; set; }
  public string IntentHash { get; set; } = string.Empty;
  public Guid PeriodId { get; set; }
  public Guid SupplierId { get; set; }
  public Guid ChartVersionId { get; set; }
  public string SupplierInvoiceReference { get; set; } = string.Empty;
  public string NormalizedSupplierReference { get; set; } = string.Empty;
  public string VoucherReference { get; set; } = string.Empty;
  public DateOnly ReceiptDate { get; set; }
  public DateOnly DocumentDate { get; set; }
  public DateOnly AccountingDate { get; set; }
  public DateOnly SupplyDate { get; set; }
  public DateOnly DueDate { get; set; }
  public string Currency { get; set; } = string.Empty;
  public decimal NetAmount { get; set; }
  public decimal TaxAmount { get; set; }
  public decimal GrossAmount { get; set; }
  public string SnapshotJson { get; set; } = string.Empty;
  public string SnapshotHash { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Exact purchase invoice posting intent, linked to supplier evidence and an approved AP role.</summary>
public sealed class ClientPurchaseInvoiceSubmission
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid InvoiceId { get; set; }
  public Guid DraftId { get; set; }
  public long DraftRevision { get; set; }
  public Guid SupplierId { get; set; }
  public string SupplierInvoiceReference { get; set; } = string.Empty;
  public string NormalizedSupplierReference { get; set; } = string.Empty;
  public Guid JournalId { get; set; }
  public long JournalSubmittedRevision { get; set; }
  public Guid CommandId { get; set; }
  public string IntentHash { get; set; } = string.Empty;
  public string ManifestJson { get; set; } = string.Empty;
  public string ManifestHash { get; set; } = string.Empty;
  public string PreviewDigest { get; set; } = string.Empty;
  public string SourceBasis { get; set; } = string.Empty;
  public Guid? SourceReceiptId { get; set; }
  public string? SourceReceiptHash { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Append-only independent approval or return decision for one exact purchase invoice.</summary>
public sealed class ClientPurchaseInvoiceDecision
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

/// <summary>Immutable AP origin amount; settlement balance is derived from approved allocations.</summary>
public sealed class ClientPurchaseInvoiceOpenItem
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid InvoiceId { get; set; }
  public Guid SubmissionId { get; set; }
  public Guid JournalId { get; set; }
  public Guid SupplierId { get; set; }
  public string Currency { get; set; } = string.Empty;
  public decimal OriginalAmount { get; set; }
  public DateOnly DueDate { get; set; }
  public DateTimeOffset PostedAt { get; set; }
}
