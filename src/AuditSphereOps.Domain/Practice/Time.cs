// Time, budgets, capacity: task/time approval + correction chains, versioned budgets/rates (§41.5).
namespace AuditSphereOps.Domain.Practice;

public static class PracticeTimeStates
{
  public const string TaskOpen = "OPEN";
  public const string TaskInProgress = "IN_PROGRESS";
  public const string TaskCompleted = "COMPLETED";
  public const string TaskCancelled = "CANCELLED";
  public const string TimeDraft = "DRAFT";
  public const string TimeSubmitted = "SUBMITTED";
  public const string TimeApproved = "APPROVED";
  public const string TimeSuperseded = "SUPERSEDED";
  public const string BudgetDraft = "DRAFT";
  public const string BudgetApproved = "APPROVED";
  public const string BudgetSuperseded = "SUPERSEDED";
  public const string RateDraft = "DRAFT";
  public const string RateApproved = "APPROVED";
  public const string RateSuperseded = "SUPERSEDED";
  public const string Billable = "BILLABLE";
  public const string NonBillable = "NON_BILLABLE";
  public const string NoCharge = "NO_CHARGE";
  public const string NarrativeInternal = "INTERNAL";
  public const string NarrativeClientVisible = "CLIENT_VISIBLE";
}

public sealed class WorkTask
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid? ClientId { get; set; }
  public Guid? EngagementId { get; set; }
  public Guid? ReportingPeriodId { get; set; }
  public string Title { get; set; } = string.Empty;
  public string Status { get; set; } = PracticeTimeStates.TaskOpen;
  public Guid? AssigneeUserId { get; set; }
  public DateOnly? DueDate { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class TimeEntry
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid? ClientId { get; set; }
  public Guid? EngagementId { get; set; }
  public Guid TaskId { get; set; }
  public Guid UserId { get; set; }
  public DateOnly WorkDate { get; set; }
  public int StartMinute { get; set; }
  public int DurationMinutes { get; set; }
  public string Role { get; set; } = string.Empty;
  public string Activity { get; set; } = string.Empty;
  public string BillableClassification { get; set; } = PracticeTimeStates.Billable;
  public string Narrative { get; set; } = string.Empty;
  public string NarrativeVisibility { get; set; } = PracticeTimeStates.NarrativeInternal;
  public string Status { get; set; } = PracticeTimeStates.TimeDraft;
  public string? Currency { get; set; }
  public long Revision { get; set; } = 1;
  public Guid? SupersedesId { get; set; }
  public Guid? RateCardVersionId { get; set; }
  public decimal? RatePerHour { get; set; }
  public DateTimeOffset? SubmittedAt { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public string? CorrectionReason { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RateCardVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public long Version { get; set; } = 1;
  public string Role { get; set; } = string.Empty;
  public string Activity { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  public decimal RatePerHour { get; set; }
  public string Status { get; set; } = PracticeTimeStates.RateDraft;
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EngagementBudget
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid EngagementId { get; set; }
  public long Version { get; set; } = 1;
  public string Currency { get; set; } = string.Empty;
  public string Status { get; set; } = PracticeTimeStates.BudgetDraft;
  public Guid CreatedByUserId { get; set; }
  public Guid? ApprovedByUserId { get; set; }
  public DateTimeOffset? ApprovedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BudgetLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid EngagementBudgetId { get; set; }
  public Guid RateCardVersionId { get; set; }
  public string Role { get; set; } = string.Empty;
  public string Activity { get; set; } = string.Empty;
  public int ForecastMinutes { get; set; }
  public decimal RatePerHour { get; set; }
  public decimal ForecastCost { get; set; }
}
