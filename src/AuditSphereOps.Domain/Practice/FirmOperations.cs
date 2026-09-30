// Firm operations (STE OV-05, 4.3-01..03, 4.4-01..02): the versioned technical library, staff cost rates kept
// separate from charge-out rates, and the firm's own operating expenses posted through the firm ledger.
namespace AuditSphereOps.Domain.Practice;

public static class TechnicalLibraryCategories
{
  public const string Ifrs = "IFRS";
  public const string Isa = "ISA";
  public const string FirmGuidance = "FIRM_GUIDANCE";
  public static readonly string[] All = [Ifrs, Isa, FirmGuidance];
}

public static class TechnicalLibraryAudiences
{
  public const string AllStaff = "ALL_STAFF";
  public const string PartnersAndManagers = "PARTNERS_MANAGERS";
}

public static class TechnicalLibraryStates
{
  public const string Draft = "DRAFT";
  public const string Published = "PUBLISHED";
  public const string Superseded = "SUPERSEDED";
}

public sealed class TechnicalLibraryDocument
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string Code { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;
  public string Category { get; set; } = TechnicalLibraryCategories.FirmGuidance;
  public string Audience { get; set; } = TechnicalLibraryAudiences.AllStaff;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One version of a library entry; a published version is immutable and superseded by the next one.</summary>
public sealed class TechnicalLibraryVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid DocumentId { get; set; }
  public int Version { get; set; }
  public string Body { get; set; } = string.Empty;
  public string SourceReference { get; set; } = string.Empty;
  public DateOnly EffectiveFrom { get; set; }
  public string Status { get; set; } = TechnicalLibraryStates.Draft;
  public string ContentSha256 { get; set; } = string.Empty;
  public Guid PreparedByUserId { get; set; }
  public DateTimeOffset PreparedAt { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>Internal hourly cost of a staff member (salary and overhead), distinct from the charge-out rate.</summary>
public sealed class StaffCostRate
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid UserId { get; set; }
  public decimal HourlyCost { get; set; }
  public string Currency { get; set; } = string.Empty;
  public DateOnly EffectiveFrom { get; set; }
  public Guid RecordedByUserId { get; set; }
  public DateTimeOffset RecordedAt { get; set; }
}

public static class FirmExpenseCategories
{
  public const string Rent = "RENT";
  public const string Salaries = "SALARIES";
  public const string PettyCash = "PETTY_CASH";
  public const string Utilities = "UTILITIES";
  public const string Other = "OTHER";
  public static readonly string[] All = [Rent, Salaries, PettyCash, Utilities, Other];
}

public static class FirmExpenseStates
{
  public const string Draft = "DRAFT";
  public const string Submitted = "SUBMITTED";
  public const string Approved = "APPROVED";
  public const string Posted = "POSTED";
  public const string Rejected = "REJECTED";
}

public sealed class FirmExpense
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public DateOnly ExpenseDate { get; set; }
  public string Category { get; set; } = FirmExpenseCategories.Other;
  public string Payee { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public Guid ExpenseAccountId { get; set; }
  public Guid PaymentAccountId { get; set; }
  public string EvidenceFileName { get; set; } = string.Empty;
  public string EvidenceContentType { get; set; } = string.Empty;
  public byte[] EvidenceContent { get; set; } = [];
  public string EvidenceSha256 { get; set; } = string.Empty;
  public string Status { get; set; } = FirmExpenseStates.Draft;
  public Guid PreparedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
  public string? ReviewComment { get; set; }
  public Guid? JournalId { get; set; }
  public Guid? PostingId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
