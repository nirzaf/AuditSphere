// Accounting: TB datasets, rows (immutable), mappings, journals, packages (§§16–18, 27.2, 42.1–42.6).
namespace AuditSphereOps.Domain.Accounting;

public sealed class TrialBalanceDataset
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string SourceKind { get; set; } = "Raw";     // Raw|Adjusted
  public long Revision { get; set; } = 1;
  public string Currency { get; set; } = string.Empty;
  public string Sha256Hex { get; set; } = string.Empty;
  public bool Balanced { get; set; }
  public string ValidationStatus { get; set; } = "Pending";
  public decimal ControlTotal { get; set; }
  public DateTimeOffset ImportedAt { get; set; }
  public Guid ImportedByUserId { get; set; }
}

public sealed class TrialBalanceRow
{
  public Guid Id { get; set; }
  public Guid DatasetId { get; set; }
  public string AccountCode { get; set; } = string.Empty;
  public string AccountName { get; set; } = string.Empty;
  public decimal Amount { get; set; }                 // signed, ≤6dp
  public string Currency { get; set; } = string.Empty;
  public string Entity { get; set; } = string.Empty;
  public string? MappingCode { get; set; }
}

public sealed class MappingRule
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid EngagementId { get; set; }
  public string MappingCode { get; set; } = string.Empty;
  public string SourcePattern { get; set; } = string.Empty;
  public long Revision { get; set; } = 1;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Adjustment journal header: draft → posted immutable; AJ numbers unique per base TB (§17.4, VX-07).</summary>
public sealed class AdjustmentJournal
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid BaseDatasetId { get; set; }
  public string JournalNumber { get; set; } = string.Empty; // AJ-001
  public string Status { get; set; } = "Draft";             // Draft|Posted|ReflectedInSource|Void
  public long Revision { get; set; } = 1;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AdjustmentLine
{
  public Guid Id { get; set; }
  public Guid JournalId { get; set; }
  public string AccountCode { get; set; } = string.Empty;
  public decimal Debit { get; set; }
  public decimal Credit { get; set; }
}

public sealed class FinancialPackage
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid AdjustedDatasetId { get; set; }
  public string Currency { get; set; } = string.Empty;
  public long Revision { get; set; } = 1;
  public long Generation { get; set; } = 1;
  public string Status { get; set; } = "Draft";
  public DateTimeOffset CreatedAt { get; set; }
}
