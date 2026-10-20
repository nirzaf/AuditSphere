using System.Globalization;

namespace AuditSphereOps.Domain.Practice;

/// <summary>Allowed sources for reviewed, firm-configured workflow reminders.</summary>
public static class WorkflowReminderSourceKinds
{
  public const string TaskDue = "TASK_DUE";
  public const string PbcDue = "PBC_DUE";
}

/// <summary>Allowed recipients for reviewed, firm-configured workflow reminders.</summary>
public static class WorkflowReminderRecipientKinds
{
  public const string ResponsibleStaff = "RESPONSIBLE_STAFF";
  public const string EngagementReviewer = "ENGAGEMENT_REVIEWER";
}

/// <summary>Lifecycle states for an immutable-after-review reminder rule revision.</summary>
public static class WorkflowReminderRuleStatuses
{
  public const string Draft = "DRAFT";
  public const string Reviewed = "REVIEWED";
  public const string Published = "PUBLISHED";
  public const string Superseded = "SUPERSEDED";
}

public enum WorkflowReminderScheduleDisposition
{
  NotDue,
  Scheduled,
  Exhausted,
  NeedsRevision
}

public sealed record WorkflowReminderScheduleEvaluation(
  WorkflowReminderScheduleDisposition Disposition, string? OccurrenceKey, int? WindowNumber);

/// <summary>Deterministic schedule-window counting for one source due-date revision.</summary>
public static class WorkflowReminderSchedule
{
  public const int MaximumAllowedWindowCount = 100;

  public static WorkflowReminderScheduleEvaluation Evaluate(
    DateOnly dueDate, DateOnly today, int offsetDays, int repeatEveryDays, int? maxScheduledWindowCount)
  {
    if (repeatEveryDays is < 0 or > 90 || offsetDays is < -30 or > 365 ||
        repeatEveryDays > 0 && maxScheduledWindowCount is not (>= 1 and <= MaximumAllowedWindowCount) ||
        repeatEveryDays == 0 && maxScheduledWindowCount is not null)
      return new(WorkflowReminderScheduleDisposition.NeedsRevision, null, null);

    var elapsedDays = (long)today.DayNumber - dueDate.DayNumber - offsetDays;
    if (elapsedDays < 0)
      return new(WorkflowReminderScheduleDisposition.NotDue, null, null);

    if (repeatEveryDays == 0)
      return new(WorkflowReminderScheduleDisposition.Scheduled, "ONCE", 1);

    var windowIndex = (int)(elapsedDays / repeatEveryDays);
    var windowNumber = windowIndex + 1;
    if (windowNumber > maxScheduledWindowCount)
      return new(WorkflowReminderScheduleDisposition.Exhausted, null, windowNumber);

    return new(WorkflowReminderScheduleDisposition.Scheduled, $"WINDOW:{windowIndex}", windowNumber);
  }

  /// <summary>Returns the latest due window without applying the rule's maximum, for queued retry validation.</summary>
  public static string? CurrentOccurrenceKey(DateOnly dueDate, DateOnly today, int offsetDays, int repeatEveryDays)
  {
    if (repeatEveryDays is < 0 or > 90 || offsetDays is < -30 or > 365) return null;
    var elapsedDays = (long)today.DayNumber - dueDate.DayNumber - offsetDays;
    if (elapsedDays < 0) return null;
    if (repeatEveryDays == 0) return "ONCE";
    var windowIndex = (int)(elapsedDays / repeatEveryDays);
    return $"WINDOW:{windowIndex}";
  }

  /// <summary>Maps an immutable schedule key to its one-based window number.</summary>
  public static int? ScheduledWindowNumber(string occurrenceKey, int repeatEveryDays)
  {
    if (repeatEveryDays == 0) return occurrenceKey == "ONCE" ? 1 : null;
    if (repeatEveryDays is < 1 or > 90 || !occurrenceKey.StartsWith("WINDOW:", StringComparison.Ordinal) ||
        !int.TryParse(occurrenceKey.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var zeroBased) ||
        zeroBased < 0 || zeroBased == int.MaxValue)
      return null;
    if (!string.Equals(occurrenceKey,
        "WINDOW:" + zeroBased.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
      return null;
    return zeroBased + 1;
  }
}

/// <summary>Append-only rule catalogue decisions.</summary>
public static class WorkflowReminderDecisionKinds
{
  public const string DraftCreated = "DRAFT_CREATED";
  public const string DraftEdited = "DRAFT_EDITED";
  public const string Reviewed = "REVIEWED";
  public const string Published = "PUBLISHED";
  public const string Superseded = "SUPERSEDED";
  public const string Suspended = "SUSPENDED";
  public const string Resumed = "RESUMED";
}

/// <summary>
/// Mutable rule head. Published content and its review history live in immutable revisions and decisions;
/// suspension only disables execution of the current head.
/// </summary>
public sealed class WorkflowReminderRule
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public Guid? CurrentPublishedRevisionId { get; set; }
  public DateTimeOffset? SuspendedAt { get; set; }
  public Guid? SuspendedByUserId { get; set; }
  public string? SuspensionReason { get; set; }
  /// <summary>Worker-only fairness checkpoint; it does not revise the approved rule policy.</summary>
  public DateTimeOffset? LastEvaluatedAt { get; set; }
  /// <summary>Bounded worker outcome diagnostic, not proof that a reminder intent was created.</summary>
  public string? LastEvaluationCode { get; set; }
  /// <summary>ISO due-date cursor paired with ScanAfterSourceId for bounded source scans.</summary>
  public string? ScanAfterDueDate { get; set; }
  public Guid? ScanAfterSourceId { get; set; }
  /// <summary>Rule revision whose source scan cursor is being advanced.</summary>
  public Guid? ScanRevisionId { get; set; }
  public long Revision { get; set; } = 1;
}

/// <summary>Versioned reminder configuration. Content is sealed after independent review.</summary>
public sealed class WorkflowReminderRuleRevision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid RuleId { get; set; }
  public int Version { get; set; }
  /// <summary>Caller-supplied key that makes creation of this immutable revision replayable.</summary>
  public Guid CreationRequestKey { get; set; }
  /// <summary>Digest of the exact creation request retained as this revision's receipt.</summary>
  public string CreationRequestDigest { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string SourceKind { get; set; } = string.Empty;
  public string RecipientKind { get; set; } = string.Empty;
  public int OffsetDays { get; set; }
  public int RepeatEveryDays { get; set; }
  /// <summary>Reviewed cap on scheduled windows for each exact source due-date revision; null is legacy/ONCE only.</summary>
  public int? MaxScheduledWindowCount { get; set; }
  public string TimeZoneId { get; set; } = string.Empty;
  public string QuietHoursStart { get; set; } = string.Empty;
  public string QuietHoursEnd { get; set; } = string.Empty;
  public DateTimeOffset EffectiveFromUtc { get; set; }
  public string Status { get; set; } = WorkflowReminderRuleStatuses.Draft;
  public string ContentDigest { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public Guid LastEditedByUserId { get; set; }
  public Guid? ReviewedByUserId { get; set; }
  public Guid? PublishedByUserId { get; set; }
  public long? PublishedSessionEpoch { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset UpdatedAt { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
  public DateTimeOffset? PublishedAt { get; set; }
  public long Revision { get; set; } = 1;
}

/// <summary>Append-only review, publication, supersession and suspension evidence for a reminder rule.</summary>
public sealed class WorkflowReminderRuleDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid RuleId { get; set; }
  public Guid RuleRevisionId { get; set; }
  public string Kind { get; set; } = string.Empty;
  public string ContentDigest { get; set; } = string.Empty;
  public string Rationale { get; set; } = string.Empty;
  public Guid ActorUserId { get; set; }
  public DateTimeOffset OccurredAt { get; set; }
}
