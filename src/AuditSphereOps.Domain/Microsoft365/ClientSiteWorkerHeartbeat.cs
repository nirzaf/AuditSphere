namespace AuditSphereOps.Domain.Microsoft365;

/// <summary>Short-lived operational projection for the isolated client-sites worker.</summary>
public sealed class ClientSiteWorkerHeartbeat
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public string WorkerGroup { get; set; } = "client-sites";
  public string WorkerInstanceId { get; set; } = string.Empty;
  public long DeploymentEpoch { get; set; }
  public string ReadinessState { get; set; } = "BLOCKED_CONFIGURATION";
  public string? DiagnosticCode { get; set; }
  public DateTimeOffset StartedAt { get; set; }
  public DateTimeOffset LastSeenAt { get; set; }
}
