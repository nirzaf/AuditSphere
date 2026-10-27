namespace AuditSphereOps.Domain.Practice;

/// <summary>Presentation-only workflow phases. They never satisfy an audit or commercial gate.</summary>
public static class WorkflowTaskPhases
{
  public const string ClientOnboarding = "CLIENT_ONBOARDING";
  public const string AuditPlanning = "AUDIT_PLANNING";
  public const string AuditFieldwork = "AUDIT_FIELDWORK";
  public const string ManagerReview = "MANAGER_REVIEW";
  public const string PartnerSignoff = "PARTNER_SIGNOFF";
  public const string ReportDelivery = "REPORT_DELIVERY";
  public const string ArchiveClose = "ARCHIVE_CLOSE";

  public static readonly string[] All =
  [
    ClientOnboarding, AuditPlanning, AuditFieldwork, ManagerReview,
    PartnerSignoff, ReportDelivery, ArchiveClose
  ];
}

/// <summary>
/// Optional workflow metadata bound to the existing practice-time task. WorkTask remains the only operational task/status
/// record, so time entries and their approved rate/FSLI lineage continue to point at the same task.
/// </summary>
public sealed class WorkflowTaskBinding
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid WorkTaskId { get; set; }
  /// <summary>Optional exact-scope PBC request that governs this task's completion.</summary>
  public Guid? PbcRequestId { get; set; }
  /// <summary>Exact audit procedure result that generated this operational rework task, when applicable.</summary>
  public Guid? ReviewProcedureResultId { get; set; }
  public Guid? TemplateRevisionId { get; set; }
  public Guid? TemplateStepId { get; set; }
  /// <summary>Exact published reminder-rule revision that caused this operational task, when applicable.</summary>
  public Guid? ReminderRuleRevisionId { get; set; }
  /// <summary>Existing durable operation that recorded the idempotent reminder intent and exact scope.</summary>
  public Guid? ReminderOperationId { get; set; }
  public string Phase { get; set; } = WorkflowTaskPhases.ClientOnboarding;
  public int Ordinal { get; set; } = 1;
  /// <summary>Stable within an engagement generation; template instances use the immutable source step key.</summary>
  public string SourceKey { get; set; } = string.Empty;
  /// <summary>Hash of the caller's immutable create intent, used to recover retries without duplicate tasks.</summary>
  public string CreationRequestDigest { get; set; } = string.Empty;
  public int Generation { get; set; } = 1;
  public int? PlannedEffortMinutes { get; set; }
  public string DueDatePolicy { get; set; } = "MANUAL";
  /// <summary>Optional staffing level requested by the template; it never grants a role or replaces live authorization.</summary>
  public string? AssignedRole { get; set; }
  public Guid? AssigneeUserIdSnapshot { get; set; }
  public string AssignmentSource { get; set; } = "MANUAL";
  public DateTimeOffset CreatedAt { get; set; }
  public Guid CreatedByUserId { get; set; }
  public long Revision { get; set; } = 1;
  public string? CancellationReason { get; set; }
  public DateTimeOffset? SupersededAt { get; set; }
  public string? SupersessionReason { get; set; }
  /// <summary>Set while a manager has put the active task on hold; the hold and its release are retained as activity.</summary>
  public DateTimeOffset? HeldAt { get; set; }
  public string? HoldReason { get; set; }
  /// <summary>Required/optional designation chosen for a manual task; template tasks use their pinned step. Null means required.</summary>
  public bool? RequiredTask { get; set; }
}
