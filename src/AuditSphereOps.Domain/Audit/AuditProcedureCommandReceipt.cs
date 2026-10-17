namespace AuditSphereOps.Domain.Audit;

/// <summary>Exact procedure submission/review command identities retained for recovery.</summary>
public sealed class AuditProcedureCommandReceipt
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid AuditProcedureId { get; set; }
  public string SourceProcedureId { get; set; } = string.Empty;
  public string Action { get; set; } = string.Empty;
  public string RequestHash { get; set; } = string.Empty;
  public Guid ActorUserId { get; set; }
  public long ActorEpoch { get; set; }
  public Guid RequestId { get; set; }
  public int DefinitionRevision { get; set; }
  public long InputGeneration { get; set; }
  public long ExpectedResultRevision { get; set; }
  public Guid? ResultId { get; set; }
  public Guid? WorkpaperId { get; set; }
  public Guid? ReviewId { get; set; }
  public long ResultRevision { get; set; }
  public string? ResultStatus { get; set; }
  public string? Decision { get; set; }
  public DateTimeOffset CommittedAt { get; set; }
}
