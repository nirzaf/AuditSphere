// Bounded firm ledger: firm accounts/periods, draft + immutable posted journals (§41.5–41.6).
// Client TB/adjustments are separate; this ledger exists only for firm time/billing accounting.
namespace AuditSphereOps.Domain.Practice;

public static class LedgerStates
{
  public const string AccountAsset = "ASSET";
  public const string AccountLiability = "LIABILITY";
  public const string AccountEquity = "EQUITY";
  public const string AccountRevenue = "REVENUE";
  public const string AccountExpense = "EXPENSE";
  public const string Debit = "DEBIT";
  public const string Credit = "CREDIT";
  public const string PeriodOpen = "OPEN";
  public const string PeriodClosed = "CLOSED";
  public const string PeriodReopenRequested = "REOPEN_REQUESTED";
  public const string JournalDraft = "DRAFT";
  public const string JournalReviewRequired = "REVIEW_REQUIRED";
  public const string JournalApproved = "APPROVED";
  public const string JournalPosted = "POSTED";
  /// <summary>Posted-period purpose used for year-end transfers out of revenue and expense accounts.</summary>
  public const string YearEndClosingPurpose = "YEAR_END_CLOSE";
  /// <summary>Manual monthly accrual of the end-of-service obligation to a provision account (ADR-0010).</summary>
  public const string EndOfServiceAccrualPurpose = "END_OF_SERVICE_ACCRUAL";
}

public static class EndOfServiceTreatmentStates
{
  /// <summary>Recorded by finance; no accrual may be prepared or posted under it yet.</summary>
  public const string Recorded = "RECORDED";
  /// <summary>Confirmed by a firm Partner distinct from the recorder; the latest confirmed version governs accruals.</summary>
  public const string Confirmed = "CONFIRMED";
}

/// <summary>
/// The accounting treatment for end-of-service benefits as named by a qualified accountant and confirmed by the
/// firm's owner (ADR-0010, STE-NXT-014). The platform stores who named it and who confirmed it; it never derives a
/// treatment or an amount. No accrual can be prepared or posted until a version is confirmed.
/// </summary>
public sealed class FirmEndOfServiceTreatment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public long Version { get; set; } = 1;
  /// <summary>The measurement basis and the standard it follows, in the accountant's words.</summary>
  public string MeasurementTreatment { get; set; } = string.Empty;
  public string AccountantName { get; set; } = string.Empty;
  /// <summary>The accountant's qualification or membership reference, as recorded by finance.</summary>
  public string AccountantCredential { get; set; } = string.Empty;
  public Guid ProvisionAccountId { get; set; }
  public Guid ExpenseAccountId { get; set; }
  public string Status { get; set; } = EndOfServiceTreatmentStates.Recorded;
  public Guid RecordedByUserId { get; set; }
  public DateTimeOffset RecordedAt { get; set; }
  public Guid? ConfirmedByUserId { get; set; }
  public DateTimeOffset? ConfirmedAt { get; set; }
  public string? ConfirmationNote { get; set; }
}

/// <summary>
/// The entered basis of one end-of-service accrual journal: the method, the inputs and the date of the calculation a
/// person performed outside the platform, with the reason for the entry. Append-only; a correction is a reversing
/// journal followed by a new accrual.
/// </summary>
public sealed class FirmEndOfServiceAccrual
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid JournalId { get; set; }
  public Guid TreatmentId { get; set; }
  public Guid PeriodId { get; set; }
  public decimal Amount { get; set; }
  public string Method { get; set; } = string.Empty;
  public string Inputs { get; set; } = string.Empty;
  public DateOnly CalculationDate { get; set; }
  public string Reason { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FirmAccount
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string Code { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string AccountType { get; set; } = LedgerStates.AccountAsset;
  public string NormalSide { get; set; } = LedgerStates.Debit;
  public bool PostingAllowed { get; set; } = true;
}

public sealed class FirmPeriod
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string PeriodCode { get; set; } = string.Empty; // YYYY-MM
  public string Status { get; set; } = LedgerStates.PeriodOpen;
  public long Revision { get; set; } = 1;
  public DateTimeOffset? ClosedAt { get; set; }
}

public sealed class FirmJournal
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PeriodId { get; set; }
  public string JournalNumber { get; set; } = string.Empty;
  public string SourceKind { get; set; } = string.Empty; // Billing|Receipt|Manual
  public string SourceKey { get; set; } = string.Empty;
  public long SourceRevision { get; set; } = 1;
  public string PostingPurpose { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  public string Status { get; set; } = LedgerStates.JournalDraft;
  public Guid CreatedByUserId { get; set; }
  public string? SupportingEvidenceFileName { get; set; }
  public string? SupportingEvidenceContentType { get; set; }
  public byte[]? SupportingEvidenceContent { get; set; }
  public string? SupportingEvidenceSha256 { get; set; }
  public Guid? SupportingEvidenceUploadedByUserId { get; set; }
  public DateTimeOffset? SupportingEvidenceUploadedAt { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public DateTimeOffset? PostedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FirmJournalLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid JournalId { get; set; }
  public Guid FirmAccountId { get; set; }
  public string Description { get; set; } = string.Empty;
  public decimal Debit { get; set; }
  public decimal Credit { get; set; }
}

public sealed class FirmPosting
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PeriodId { get; set; }
  public Guid JournalId { get; set; }
  public string Currency { get; set; } = string.Empty;
  public Guid PostedByUserId { get; set; }
  public Guid? ReversalOfPostingId { get; set; }
  public DateTimeOffset PostedAt { get; set; }
}

public sealed class FirmPostingLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PostingId { get; set; }
  public Guid FirmAccountId { get; set; }
  public decimal Debit { get; set; }
  public decimal Credit { get; set; }
}

public sealed class LedgerSourceLink
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PostingId { get; set; }
  public string SourceKind { get; set; } = string.Empty;
  public string SourceKey { get; set; } = string.Empty;
  public long SourceRevision { get; set; }
  public string PostingPurpose { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class LedgerPostingReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PostingId { get; set; }
  public string RequestKey { get; set; } = string.Empty;
  public string RequestDigest { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PeriodCloseDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PeriodId { get; set; }
  public string DecisionKind { get; set; } = string.Empty; // CLOSE|REOPEN
  public string Reason { get; set; } = string.Empty;
  public Guid DecidedByUserId { get; set; }
  public DateTimeOffset DecidedAt { get; set; }
}
