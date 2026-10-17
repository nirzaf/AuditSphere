namespace AuditSphereOps.Domain.Practice;

/// <summary>Immutable actor-owned identity and exact outcome for one firm-billing mutation.</summary>
public sealed class BillingCommandReceipt
{
  public Guid Id { get; init; }
  public Guid FirmId { get; init; }
  public Guid ClientId { get; init; }
  public Guid ActorId { get; init; }
  public long ActorEpoch { get; init; }
  public Guid RequestId { get; init; }
  public string Action { get; init; } = string.Empty;
  public string RequestHash { get; init; } = string.Empty;
  public Guid ResultId { get; init; }
  public string OutcomeJson { get; init; } = string.Empty;
  public DateTimeOffset CreatedAt { get; init; }
}
