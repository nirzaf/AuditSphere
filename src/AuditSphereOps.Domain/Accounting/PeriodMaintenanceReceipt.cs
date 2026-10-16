namespace AuditSphereOps.Domain.Accounting;

/// <summary>Append-only evidence of one actor's exact reviewed period-maintenance intent and committed outcome.</summary>
public sealed class PeriodMaintenanceReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = "";
  public string Action { get; set; } = "";
  public string ReviewBasis { get; set; } = "";
  public Guid PeriodId { get; set; }
  public Guid? ResultPeriodId { get; set; }
  public Guid? RestatementId { get; set; }
  public string InputJson { get; set; } = "";
  public string BeforeJson { get; set; } = "";
  public string ResultJson { get; set; } = "";
  public DateTimeOffset CreatedAt { get; set; }
}
