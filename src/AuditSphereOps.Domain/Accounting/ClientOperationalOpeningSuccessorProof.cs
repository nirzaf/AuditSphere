namespace AuditSphereOps.Domain.Accounting;

/// <summary>Append-only source lineage supplementing an existing native opening; never a second approval.</summary>
public sealed class ClientOperationalOpeningSuccessorProof
{
  public Guid OpeningId { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid PriorPeriodId { get; set; }
  public long PriorPeriodRevision { get; set; }
  public Guid SourceDatasetId { get; set; }
  public string SourceIdentityHash { get; set; } = string.Empty;
  public string ProofJson { get; set; } = string.Empty;
  public string ProofHash { get; set; } = string.Empty;
  public Guid RequestId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public DateTimeOffset CreatedAt { get; set; }
}
