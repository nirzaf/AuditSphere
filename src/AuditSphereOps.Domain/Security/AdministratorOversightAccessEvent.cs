namespace AuditSphereOps.Domain.Security;

/// <summary>Pre-disclosure authorization evidence, not proof of receipt or professional review.</summary>
public sealed class AdministratorOversightAccessEvent
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ActorUserId { get; set; }
  public long SessionEpoch { get; set; }
  public Guid RequestId { get; set; }
  public string Operation { get; set; } = string.Empty;
  public string ResourceKind { get; set; } = string.Empty;
  public Guid? ResourceId { get; set; }
  public string Outcome { get; set; } = "DISCLOSURE_AUTHORIZED";
  public DateTimeOffset AuthorizedAt { get; set; }
}
