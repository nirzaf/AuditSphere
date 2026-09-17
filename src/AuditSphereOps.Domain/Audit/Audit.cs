// Audit: risks, procedures, populations, samples, workpapers, findings, misstatements (§§19–23, 27.2).
namespace AuditSphereOps.Domain.Audit;

public sealed class AuditRisk
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string Assertion { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public string Severity { get; set; } = "Normal"; // Normal|Significant
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditProcedure
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid RiskId { get; set; }
  public string Title { get; set; } = string.Empty;
  public string Status { get; set; } = "Planned";
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Workpaper
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ProcedureId { get; set; }
  public string Title { get; set; } = string.Empty;
  public string State { get; set; } = "Draft"; // Draft|Submitted|Reviewed|Approved
  public long Revision { get; set; } = 1;
  public long Generation { get; set; } = 1;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Finding
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public string Title { get; set; } = string.Empty;
  public string Severity { get; set; } = "Normal";
  public string Status { get; set; } = "Open";
  public DateTimeOffset CreatedAt { get; set; }
}
