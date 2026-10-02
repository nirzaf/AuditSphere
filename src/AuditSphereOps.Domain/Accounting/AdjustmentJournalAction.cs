namespace AuditSphereOps.Domain.Accounting;

/// <summary>Retained native command receipt and before/after revision evidence. Legacy actions
/// without this evidence are never reconstructed as if their history had been observed.</summary>
public sealed class AdjustmentJournalAction
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid JournalId { get; set; }
  public Guid ResultJournalId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = "";
  public string ReviewBasis { get; set; } = "";
  public string Action { get; set; } = "";
  public long OldRevision { get; set; }
  public long NewRevision { get; set; }
  public string OldStatus { get; set; } = "";
  public string NewStatus { get; set; } = "";
  public string Reason { get; set; } = "";
  public string EvidenceReference { get; set; } = "";
  public string BeforeJson { get; set; } = "";
  public string AfterJson { get; set; } = "";
  public DateTimeOffset CreatedAt { get; set; }
}
