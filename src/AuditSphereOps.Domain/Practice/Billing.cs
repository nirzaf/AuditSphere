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

public static class InvoicePaymentTermsStates
{
  public const string PendingReview = "PENDING_REVIEW";
  public const string Approved = "APPROVED";
  public const string Rejected = "REJECTED";
}

public static class InvoicePaymentTermsKinds
{
  public const string ContractualDueDate = "CONTRACTUAL_DUE_DATE";
  public const string ReviewedTermsSnapshot = "REVIEWED_TERMS_SNAPSHOT";
}

public static class FirmReceivablesAgingPolicy
{
  public const string Current = "CURRENT_NOT_YET_DUE";
  public const string Overdue1To30 = "OVERDUE_1_30";
  public const string Overdue31To60 = "OVERDUE_31_60";
  public const string Overdue61To90 = "OVERDUE_61_90";
  public const string OverdueOver90 = "OVERDUE_OVER_90";
  public const string Undated = "UNDATED_REVIEW_REQUIRED";
  public const string Settled = "SETTLED";

  /// <summary>Days use calendar-date arithmetic: a due date equal to the as-of date is not overdue.</summary>
  public static int? DaysOverdue(DateOnly? dueDate, DateOnly asOfDate) =>
    dueDate is { } due && due < asOfDate ? asOfDate.DayNumber - due.DayNumber : dueDate is null ? null : 0;

  public static string Bucket(DateOnly? dueDate, DateOnly asOfDate, decimal outstanding)
  {
    if (outstanding == 0m) return Settled;
    if (dueDate is null) return Undated;
    var days = DaysOverdue(dueDate, asOfDate) ?? 0;
    return days switch
    {
      <= 0 => Current,
      <= 30 => Overdue1To30,
      <= 60 => Overdue31To60,
      <= 90 => Overdue61To90,
      _ => OverdueOver90
    };
  }
}

/// <summary>
/// Append-only invoice terms revision. Submission is the only mutable transition: a separate finance reviewer
/// approves or rejects it. Revisions become effective on their review date and are never backdated by user input.
/// </summary>
public sealed class InvoicePaymentTermsRevision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid InvoiceId { get; set; }
  public long Revision { get; set; }
  public DateOnly DueDate { get; set; }
  public string Basis { get; set; } = InvoicePaymentTermsKinds.ContractualDueDate;
  public string TermsDescription { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public string Status { get; set; } = InvoicePaymentTermsStates.PendingReview;
  public Guid SubmittedByUserId { get; set; }
  public DateTimeOffset SubmittedAt { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
  public string? ReviewReason { get; set; }
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

public static class ReceiptAllocationReversalStates
{
  public const string PendingReview = "PENDING_REVIEW";
  public const string Approved = "APPROVED";
  public const string Rejected = "REJECTED";
}

/// <summary>An immutable, independently reviewed partial or full reversal of a receipt allocation.</summary>
public sealed class ReceiptAllocationReversal
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ReceiptAllocationId { get; set; }
  public long Revision { get; set; }
  public decimal Amount { get; set; }
  public string Reference { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public string Status { get; set; } = ReceiptAllocationReversalStates.PendingReview;
  public Guid SubmittedByUserId { get; set; }
  public DateTimeOffset SubmittedAt { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
  public string? ReviewReason { get; set; }
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
