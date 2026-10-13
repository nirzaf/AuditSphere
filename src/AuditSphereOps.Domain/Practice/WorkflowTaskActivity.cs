namespace AuditSphereOps.Domain.Practice;

public static class WorkflowTaskActivityKinds
{
  public const string Created = "TASK_CREATED";
  public const string Assigned = "TASK_ASSIGNED";
  public const string Reassigned = "TASK_REASSIGNED";
  public const string Completed = "TASK_COMPLETED";
  public const string DueDateChanged = "TASK_DUE_DATE_CHANGED";
  public const string DependencyChanged = "TASK_DEPENDENCIES_CHANGED";
  public const string EvidenceChanged = "TASK_EVIDENCE_RULE_CHANGED";

  public static readonly string[] All = [Created, Assigned, Reassigned, Completed, DueDateChanged, EvidenceChanged, DependencyChanged];
}

/// <summary>
/// Append-only operational trace linked to the exact existing task and workflow binding. It is explanatory evidence,
/// not a source of task, audit, lifecycle, approval, or release state.
/// </summary>
public sealed class WorkflowTaskActivity
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid WorkTaskId { get; set; }
  public Guid WorkflowTaskBindingId { get; set; }
  public string Kind { get; set; } = string.Empty;
  public Guid ActorUserId { get; set; }
  public Guid? AssigneeUserId { get; set; }
  public long TaskRevision { get; set; }
  public DateOnly? PreviousDueDate { get; set; }
  public DateOnly? NewDueDate { get; set; }
  public DateTimeOffset OccurredAt { get; set; }
}
