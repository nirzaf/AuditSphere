namespace AuditSphereOps.Domain.Practice;

/// <summary>Append-only evidence of an exact reviewed budget approval.</summary>
public sealed class BudgetApproval
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid BudgetId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public string ReviewBasis { get; set; } = string.Empty;
  public string PreviewJson { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
