namespace AuditSphereOps.Domain.Practice;

/// <summary>
/// Immutable task-to-task ordering captured from the exact published template revision. A dependency only controls
/// whether an operational task may be completed; it never satisfies an audit, commercial or engagement lifecycle gate.
/// </summary>
public sealed class WorkflowTaskDependency
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid TemplateRevisionId { get; set; }
  public Guid TemplateDependencyId { get; set; }
  public Guid PredecessorBindingId { get; set; }
  public Guid SuccessorBindingId { get; set; }
  public string DependencyType { get; set; } = WorkflowDependencyTypes.TaskCompleted;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
