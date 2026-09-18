// Billing: invoice/credit/receipt/allocation artifacts; owner finance profile authorizes release (§41.4).
namespace AuditSphereOps.Domain.Practice;

public static class BillingStates
{
  public const string AccountOpen = "OPEN";
  public const string InvoiceDraft = "DRAFT";
  public const string InvoiceReviewRequired = "REVIEW_REQUIRED";
  public const string InvoiceApproved = "APPROVED";
  public const string InvoicePosted = "POSTED";
  public const string InvoiceSent = "SENT";
  public const string InvoiceCancelled = "CANCELLED";
  public const string ReceiptRecorded = "RECORDED";
  public const string CreditIssued = "ISSUED";
  public const string TestProfile = "TEST";
  public const string ProductionProfile = "PRODUCTION";
}

public sealed class BillingAccount
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PracticeClientId { get; set; }
  public string Currency { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FirmFinanceProfile
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string FunctionalCurrency { get; set; } = string.Empty;
  public string ProfileKind { get; set; } = BillingStates.TestProfile;
  public bool Approved { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
}

public sealed class Invoice
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid BillingAccountId { get; set; }
  public string InvoiceNumber { get; set; } = string.Empty;
  public string? Currency { get; set; }
  public decimal Subtotal { get; set; }
  public decimal Tax { get; set; }
  public decimal Total { get; set; }
  public long Revision { get; set; } = 1;
  public string Status { get; set; } = BillingStates.InvoiceDraft;
  public Guid? CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public DateTimeOffset? PostedAt { get; set; }
  public DateTimeOffset? SentAt { get; set; }
  public DateTimeOffset? CancelledAt { get; set; }
  public string? CancellationReason { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class InvoiceLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid InvoiceId { get; set; }
  public string Description { get; set; } = string.Empty;
  public decimal Quantity { get; set; }
  public decimal UnitPrice { get; set; }
  public decimal LineTotal { get; set; }
  public string SourceKind { get; set; } = string.Empty;
  public Guid? SourceId { get; set; }
  public long? SourceRevision { get; set; }
}

public sealed class Receipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid BillingAccountId { get; set; }
  public decimal Amount { get; set; }
  public string? Currency { get; set; }
  public string Reference { get; set; } = string.Empty;
  public string Status { get; set; } = BillingStates.ReceiptRecorded;
  public Guid? RecordedByUserId { get; set; }
  public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class ReceiptAllocation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ReceiptId { get; set; }
  public Guid InvoiceId { get; set; }
  public decimal Amount { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreditNote
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid BillingAccountId { get; set; }
  public Guid InvoiceId { get; set; }
  public string NoteNumber { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public string Reason { get; set; } = string.Empty;
  public string Status { get; set; } = BillingStates.CreditIssued;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable source consumption prevents the same approved fact being billed twice.</summary>
public sealed class BillingSourceAllocation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid InvoiceLineId { get; set; }
  public string SourceKind { get; set; } = string.Empty;
  public Guid SourceId { get; set; }
  public long SourceRevision { get; set; }
  public decimal Quantity { get; set; }
  public decimal Amount { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
