namespace AuditSphereOps.Domain.Audit;

/// <summary>
/// Operational milestones calculated relative to statutory cutoffs (§4.2.2, AS-COMP-10).
/// Enforces chronology, 60-day archive fence, and distinguish overridable scheduling warnings from forbidden dates.
/// </summary>
public sealed class EngagementMilestonePlan
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public DateOnly PeriodEnd { get; set; }
  public DateOnly StatutoryFilingCutoff { get; set; }
  public DateOnly FieldworkStartDate { get; set; }
  public DateOnly DraftReportDate { get; set; }
  public DateOnly FinalReportDate { get; set; }
  public DateOnly ArchiveDeadlineDate { get; set; }
  public string? AdjustmentReason { get; set; }
  public string? WarningOverrideReason { get; set; }
  public string WarningsJson { get; set; } = "[]";
  public long Revision { get; set; } = 1;
  public Guid ScheduledByUserId { get; set; }
  public DateTimeOffset ScheduledAt { get; set; }
}
