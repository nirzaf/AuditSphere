namespace AuditSphereOps.Domain.Completion;

/// <summary>
/// A signed-in client user's explicit, immutable report that they received one exact final bundle.
/// This is client-attested evidence; it does not prove a completed browser download or provider delivery.
/// </summary>
public sealed class ClientBundleAcknowledgement
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid BundleId { get; set; }
  public Guid AcknowledgedByUserId { get; set; }
  public string ContentSha256 { get; set; } = string.Empty;
  public string ManifestSha256 { get; set; } = string.Empty;
  public DateTimeOffset AcknowledgedAt { get; set; }
}
