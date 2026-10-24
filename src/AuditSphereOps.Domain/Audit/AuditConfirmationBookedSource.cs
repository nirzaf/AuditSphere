namespace AuditSphereOps.Domain.Audit;

/// <summary>Append-only source snapshot for a newly prepared, explicitly source-bound case.
/// Absence is legacy/unverified provenance, never evidence of a zero balance.</summary>
public sealed class AuditConfirmationBookedSource
{
  public Guid ConfirmationCaseId { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string SourceKind { get; set; } = "BANK_RECONCILIATION";
  public Guid BankReconciliationId { get; set; }
  public Guid LedgerScheduleId { get; set; }
  public Guid StatementScheduleId { get; set; }
  public string SnapshotJson { get; set; } = string.Empty;
  public string SnapshotSha256 { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
