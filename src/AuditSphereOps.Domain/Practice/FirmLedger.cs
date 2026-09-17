// Bounded firm ledger: firm accounts/periods, draft + immutable posted journals (§41.6–41.7).
// Client TB/adjustments are separate; this ledger exists only for firm time/billing accounting.
namespace AuditSphereOps.Domain.Practice;

public sealed class FirmAccount
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string Code { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string NormalSide { get; set; } = "Debit";
  public bool PostingAllowed { get; set; } = true;
}

public sealed class FirmPeriod
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string PeriodCode { get; set; } = string.Empty; // YYYY-MM
  public string Status { get; set; } = "Open";           // Open|Closed
  public DateTimeOffset? ClosedAt { get; set; }
}

public sealed class FirmJournal
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PeriodId { get; set; }
  public string JournalNumber { get; set; } = string.Empty;
  public string SourceKind { get; set; } = string.Empty; // Billing|Receipt|Manual
  public string SourceKey { get; set; } = string.Empty;  // idempotency vs source
  public string Status { get; set; } = "Draft";          // Draft|Posted
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FirmJournalLine
{
  public Guid Id { get; set; }
  public Guid JournalId { get; set; }
  public Guid FirmAccountId { get; set; }
  public decimal Debit { get; set; }
  public decimal Credit { get; set; }
}
