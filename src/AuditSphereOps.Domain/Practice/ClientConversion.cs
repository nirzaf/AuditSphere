namespace AuditSphereOps.Domain.Practice;

/// <summary>Immutable reviewed commercial-to-prospect conversion evidence. It grants no professional or portal access.</summary>
public sealed class ClientConversion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ProposalId { get; set; }
  public Guid ClientId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public string ReviewBasis { get; set; } = string.Empty;
  public string PreviewJson { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
