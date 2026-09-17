// Engagements: one row per client-service-period (§6). Holds gate professional work (§8.6).
namespace AuditSphereOps.Domain.Engagements;

public sealed class Engagement
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid PracticeClientId { get; set; }
  public string ServiceRoute { get; set; } = string.Empty;
  public string PeriodStart { get; set; } = string.Empty; // ISO date
  public string PeriodEnd { get; set; } = string.Empty;
  public string ServiceProfileId { get; set; } = string.Empty;
  public string Status { get; set; } = "Draft";           // Draft|Active|Completed|Closed
  public bool ProfessionalWorkBlocked { get; set; } = true;
  public long Generation { get; set; } = 1;
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EngagementHold
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid EngagementId { get; set; }
  public string HoldKind { get; set; } = string.Empty;    // Acceptance|Continuance|Independence|Terms|...
  public string Reason { get; set; } = string.Empty;
  public bool Released { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? ReleasedAt { get; set; }
}
