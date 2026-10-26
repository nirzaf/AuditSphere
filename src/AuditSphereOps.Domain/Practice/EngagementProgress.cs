namespace AuditSphereOps.Domain.Practice;

/// <summary>
/// How a template step counts toward engagement progress. A required step with no stated applicability condition is
/// mandatory; a required step with a condition is conditional (it applies unless an authorized user records it as not
/// applicable, using the existing reasoned task cancellation); a step that is not required is optional.
/// </summary>
public static class WorkflowStepRequirements
{
  public const string Mandatory = "MANDATORY";
  public const string Conditional = "CONDITIONAL";
  public const string Optional = "OPTIONAL";

  public static string Classify(bool requiredTask, string? applicabilityCondition) =>
    !requiredTask ? Optional : string.IsNullOrWhiteSpace(applicabilityCondition) ? Mandatory : Conditional;
}

public static class EngagementProgressStepStates
{
  public const string Completed = "COMPLETED";
  public const string Pending = "PENDING";
  public const string Blocked = "BLOCKED";
  public const string NotApplicable = "NOT_APPLICABLE";
}

/// <summary>Persisted facts about one engagement task, read from current records; nothing here is a UI flag.</summary>
public sealed record EngagementProgressStepFact(
  Guid TaskId, string Title, string Phase, int Ordinal, string Requirement, string TaskStatus,
  bool CancellationRecorded, int UnmetDependencies, int UnmetEvidence, bool WaitingForClient, bool Rework,
  Guid? AssigneeUserId, DateOnly? DueDate, string? ApplicabilityCondition = null);

public sealed record EngagementProgressStep(
  Guid TaskId, string Title, string Phase, int Ordinal, string Requirement, string State, string? Condition,
  Guid? AssigneeUserId, DateOnly? DueDate, string? ApplicabilityCondition);

/// <summary>
/// Progress over applicable mandatory and conditional steps. <see cref="PercentComplete"/> is null when no applicable
/// step exists, and never reaches 100 while a step is outstanding or a gate is open.
/// </summary>
public sealed record EngagementProgressFigures(
  int ApplicableRequired, int CompletedRequired, int PendingRequired, int BlockedRequired, int NotApplicable,
  int OptionalApplicable, int OptionalCompleted, int? PercentComplete, bool GateOpen,
  EngagementProgressStep? NextStep, DateOnly? NextDueDate, IReadOnlyList<EngagementProgressStep> Steps);

/// <summary>Pure progress calculation from persisted task facts and the engagement's open gates.</summary>
public static class EngagementProgressCalculator
{
  public static EngagementProgressFigures Calculate(IReadOnlyList<EngagementProgressStepFact> facts, bool gateOpen)
  {
    var steps = facts.OrderBy(f => f.Ordinal).ThenBy(f => f.TaskId).Select(Classify).ToList();
    var required = steps.Where(s => s.Requirement != WorkflowStepRequirements.Optional).ToList();
    var applicable = required.Where(s => s.State != EngagementProgressStepStates.NotApplicable).ToList();
    var completed = applicable.Count(s => s.State == EngagementProgressStepStates.Completed);
    var optional = steps.Where(s => s.Requirement == WorkflowStepRequirements.Optional &&
      s.State != EngagementProgressStepStates.NotApplicable).ToList();
    int? percent = applicable.Count == 0 ? null : completed * 100 / applicable.Count;
    // A complete task plan is not release: an open gate keeps progress below 100%.
    if (percent == 100 && gateOpen) percent = 99;
    var outstanding = applicable.Where(s => s.State != EngagementProgressStepStates.Completed).ToList();
    var next = outstanding.FirstOrDefault(s => s.State == EngagementProgressStepStates.Pending) ?? outstanding.FirstOrDefault();
    var nextDue = outstanding.Where(s => s.DueDate.HasValue).Select(s => s.DueDate).Min();
    return new EngagementProgressFigures(applicable.Count, completed,
      applicable.Count(s => s.State == EngagementProgressStepStates.Pending),
      applicable.Count(s => s.State == EngagementProgressStepStates.Blocked),
      steps.Count(s => s.State == EngagementProgressStepStates.NotApplicable && s.Requirement != WorkflowStepRequirements.Optional),
      optional.Count, optional.Count(s => s.State == EngagementProgressStepStates.Completed),
      percent, gateOpen, next, nextDue, steps);
  }

  private static EngagementProgressStep Classify(EngagementProgressStepFact fact)
  {
    var (state, condition) = fact.TaskStatus switch
    {
      PracticeTimeStates.TaskCompleted when fact.UnmetEvidence > 0 =>
        (EngagementProgressStepStates.Blocked, "Completed, but its required evidence is no longer current"),
      PracticeTimeStates.TaskCompleted when fact.UnmetDependencies > 0 =>
        (EngagementProgressStepStates.Blocked, "Completed, but a required prerequisite is no longer current"),
      PracticeTimeStates.TaskCompleted => (EngagementProgressStepStates.Completed, (string?)null),
      PracticeTimeStates.TaskCancelled when fact.Requirement == WorkflowStepRequirements.Optional =>
        (EngagementProgressStepStates.NotApplicable, "Optional step cancelled"),
      PracticeTimeStates.TaskCancelled when fact.Requirement == WorkflowStepRequirements.Conditional && fact.CancellationRecorded =>
        (EngagementProgressStepStates.NotApplicable, "Recorded as not applicable"),
      // A mandatory step cannot be waived by cancelling it: it stays outstanding until it is reopened and completed.
      PracticeTimeStates.TaskCancelled => (EngagementProgressStepStates.Blocked, "Cancelled; a required step must be reopened"),
      PracticeTimeStates.TaskOpen or PracticeTimeStates.TaskInProgress when fact.UnmetDependencies > 0 =>
        (EngagementProgressStepStates.Blocked, $"Waiting on {fact.UnmetDependencies} prerequisite task(s)"),
      PracticeTimeStates.TaskOpen or PracticeTimeStates.TaskInProgress =>
        (EngagementProgressStepStates.Pending, fact.Rework ? "Rework required" : fact.WaitingForClient ? "Waiting for client"
          : fact.UnmetEvidence > 0 ? $"{fact.UnmetEvidence} evidence prerequisite(s) pending" : null),
      _ => (EngagementProgressStepStates.Blocked, "Unrecognised task state"),
    };
    return new EngagementProgressStep(fact.TaskId, fact.Title, fact.Phase, fact.Ordinal, fact.Requirement, state, condition,
      fact.AssigneeUserId, fact.DueDate, fact.ApplicabilityCondition);
  }
}
