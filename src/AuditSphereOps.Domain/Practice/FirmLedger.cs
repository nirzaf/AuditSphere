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
