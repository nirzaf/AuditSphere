namespace AuditSphereOps.Domain.Accounting;

/// <summary>
/// Retained native-book provenance for one sealed trial balance. These generated
/// artifacts are not received upload bytes and never replace the original books.
/// </summary>
public sealed class NativeTrialBalanceSource
{
  public Guid DatasetId { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid RequestId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public string SourceIdentityHash { get; set; } = string.Empty;
  public string ManifestHash { get; set; } = string.Empty;
  public string ManifestJson { get; set; } = string.Empty;
  public string GeneratedRowsJson { get; set; } = string.Empty;
  public string GeneratedRowsHash { get; set; } = string.Empty;
  public int RowCount { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
