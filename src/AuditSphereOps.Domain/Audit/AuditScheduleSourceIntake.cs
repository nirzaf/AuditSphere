namespace AuditSphereOps.Domain.Audit;

/// <summary>Original verified source and command provenance for one typed schedule version.
/// The existing schedule identity is reused; absence on older schedules remains legacy/unverified.</summary>
public sealed class AuditScheduleSourceCapture
{
  public Guid ScheduleId { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid RequestId { get; set; }
  public Guid ActorUserId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid PbcAcceptanceReceiptId { get; set; }
  public Guid AcceptedSourceDecisionId { get; set; }
  public Guid TrialBalanceDatasetId { get; set; }
  public Guid PeriodId { get; set; }
  public long PeriodRevision { get; set; }
  public Guid? BookId { get; set; }
  public string? Basis { get; set; }
  public string LegalEntityKey { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  public string ProfileVersion { get; set; } = string.Empty;
  public DateOnly SourceDate { get; set; }
  public long InputGeneration { get; set; }
  public string RequestJson { get; set; } = string.Empty;
  public string RequestHash { get; set; } = string.Empty;
  public string ReceiptJson { get; set; } = string.Empty;
  public string ReceiptHash { get; set; } = string.Empty;
  public string ManifestJson { get; set; } = string.Empty;
  public string ManifestHash { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An actual human decision over the exact captured source, with its original command receipt.
/// Legacy approval flags do not supply a reviewer identity or timestamp for this record.</summary>
public sealed class AuditScheduleSourceReview
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ScheduleId { get; set; }
  public Guid RequestId { get; set; }
  public Guid ActorUserId { get; set; }
  public long ActorEpoch { get; set; }
  public long InputGeneration { get; set; }
  public string Action { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public string CaptureHash { get; set; } = string.Empty;
  public string RequestJson { get; set; } = string.Empty;
  public string RequestHash { get; set; } = string.Empty;
  public string ReceiptJson { get; set; } = string.Empty;
  public string ReceiptHash { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
