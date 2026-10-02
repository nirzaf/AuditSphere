namespace AuditSphereOps.Domain.Audit;

/// <summary>Append-only human closure decision bound to the exact evidence observed at closure.</summary>
public sealed class AuditConfirmationClosure
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ConfirmationCaseId { get; set; }
  public string Conclusion { get; set; } = string.Empty;
  public string EvidenceSnapshotJson { get; set; } = string.Empty;
  public string EvidenceSha256 { get; set; } = string.Empty;
  public Guid ClosedByUserId { get; set; }
  public DateTimeOffset ClosedAt { get; set; }
}
