namespace AuditSphereOps.Domain.Practice;

/// <summary>Append-only evidence of a reviewed local contact creation. No identity or access grant.</summary>
public sealed class ClientContactCreation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid ContactId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = "";
  public string ReviewBasis { get; set; } = "";
  public long PreviousGeneration { get; set; }
  public long ResultGeneration { get; set; }
  public string InputJson { get; set; } = "";
  public string PreviousPrimaryJson { get; set; } = "";
  public DateTimeOffset CreatedAt { get; set; }
}
