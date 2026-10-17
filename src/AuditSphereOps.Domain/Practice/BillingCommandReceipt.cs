namespace AuditSphereOps.Domain.Practice;

/// <summary>Immutable actor-owned identity and exact outcome for one firm-billing mutation.</summary>
public sealed class BillingCommandReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string Action { get; set; } = string.Empty;
  public string RequestHash { get; set; } = string.Empty;
  public string OutcomeJson { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
