// Time, budgets, capacity: task/time approval + correction chains, versioned budgets/rates (§41.5).
namespace AuditSphereOps.Domain.Practice;

public sealed class WorkTask
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid? ClientId { get; set; }
  public Guid? EngagementId { get; set; }
  public string Title { get; set; } = string.Empty;
  public string Status { get; set; } = "Open";
  public Guid? AssigneeUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TimeEntry
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid TaskId { get; set; }
  public Guid UserId { get; set; }
  public decimal Hours { get; set; }
  public string Status { get; set; } = "Draft"; // Draft|Submitted|Approved|Corrected
  public Guid? SupersedesId { get; set; }
  public DateTimeOffset OccurredOn { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BudgetVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid EngagementId { get; set; }
  public long Version { get; set; } = 1;
  public decimal Hours { get; set; }
  public decimal Amount { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
