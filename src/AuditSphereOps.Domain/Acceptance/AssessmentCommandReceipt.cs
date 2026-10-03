namespace AuditSphereOps.Domain.Acceptance;

/// <summary>Append-only evidence of one explicitly reviewed local assessment command.</summary>
public sealed class AssessmentCommandReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string Kind { get; set; } = string.Empty;
  public string RequestHash { get; set; } = string.Empty;
  public string ReviewBasis { get; set; } = string.Empty;
  public string PreviewJson { get; set; } = string.Empty;
  public long Generation { get; set; }
  public long ResultGeneration { get; set; }
  public Guid ResourceId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
