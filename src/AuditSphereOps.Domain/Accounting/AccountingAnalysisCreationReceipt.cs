namespace AuditSphereOps.Domain.Accounting;

/// <summary>Immutable actor-owned receipt for a reviewed reconciliation creation.</summary>
public sealed class AccountingReconciliationPreparation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public string ReviewBasis { get; set; } = string.Empty;
  public Guid ReconciliationId { get; set; }
  public string InputJson { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable actor-owned receipt for a reviewed specialist schedule revision.</summary>
public sealed class SpecialistSchedulePreparation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public string ReviewBasis { get; set; } = string.Empty;
  public Guid ScheduleId { get; set; }
  public string InputJson { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
