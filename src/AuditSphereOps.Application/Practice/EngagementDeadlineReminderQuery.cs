using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record EngagementDeadlineReminder(Guid SourceId, string Kind, string Title,
  DateOnly? DueDate, string Tone, string Message, string SourceHref);

public sealed record EngagementDeadlineReminders(Guid EngagementId, DateOnly AsOfDateUtc,
  IReadOnlyList<EngagementDeadlineReminder> Items, bool Truncated);

/// <summary>Read-only, current-source reminder projection for one authorized engagement.</summary>
public static class EngagementDeadlineReminderQuery
{
  private static readonly string[] WorkRoles = ["Senior", "Staff", "Manager", "Partner", "Administrator"];
  private static readonly string[] ActiveTaskStates = [PracticeTimeStates.TaskOpen, PracticeTimeStates.TaskInProgress];
  private static readonly string[] ClientWaitingPbcStates =
    [PbcStates.Sent, PbcStates.Acknowledged, PbcStates.PartiallyReceived, PbcStates.ClarificationRequired];

  public static async Task<CommandResult<EngagementDeadlineReminders>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == engagementId)
      .Select(x => new { x.Id, x.PracticeClientId })
      .SingleOrDefaultAsync(ct);
    if (engagement is null)
      return CommandResult<EngagementDeadlineReminders>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizeAsync(db, actor, engagement.PracticeClientId, engagementId, ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementDeadlineReminders>.Fail(auth.ErrorCode!, auth.Message!);

    // A read never invokes the mutating freeze guard: it only observes the exact committed frozen state.
    if (await IsFrozenAsync(db, actor.FirmId, engagement.PracticeClientId, engagementId, ct))
    {
      auth = await AuthorizeAsync(db, actor, engagement.PracticeClientId, engagementId, ct);
      if (!auth.Succeeded)
        return CommandResult<EngagementDeadlineReminders>.Fail(auth.ErrorCode!, auth.Message!);
      return CommandResult<EngagementDeadlineReminders>.Ok(new(engagementId,
        DateOnly.FromDateTime(DateTime.UtcNow), [], false));
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var sevenDaysFromToday = today.DayNumber > DateOnly.MaxValue.DayNumber - 7
      ? DateOnly.MaxValue
      : today.AddDays(7);

    var taskRows = await (from binding in db.WorkflowTaskBindings.AsNoTracking()
                          join task in db.WorkTasks.AsNoTracking()
                            on new { binding.FirmId, WorkTaskId = binding.WorkTaskId }
                            equals new { task.FirmId, WorkTaskId = task.Id }
                          where binding.FirmId == actor.FirmId && binding.ClientId == engagement.PracticeClientId &&
                            binding.EngagementId == engagementId && ActiveTaskStates.Contains(task.Status) &&
                            task.DueDate.HasValue && task.DueDate.Value <= sevenDaysFromToday
                          select new { task.Id, task.Title, task.DueDate })
      .Distinct()
      // An unresolved deadline does not expire from this view merely because it
      // is older than yesterday. Keep oldest overdue work first within the bound.
      .OrderBy(x => x.DueDate).ThenBy(x => x.Id)
      .Take(51).ToListAsync(ct);

    var directPbcCandidates = from binding in db.WorkflowTaskBindings.AsNoTracking()
                              join task in db.WorkTasks.AsNoTracking()
                                on new { binding.FirmId, WorkTaskId = binding.WorkTaskId }
                                equals new { task.FirmId, WorkTaskId = task.Id }
                              join request in db.PbcRequests.AsNoTracking()
                                on new { binding.FirmId, binding.ClientId, binding.EngagementId, Id = binding.PbcRequestId }
                                equals new { request.FirmId, request.ClientId, request.EngagementId, Id = (Guid?)request.Id }
                              where binding.FirmId == actor.FirmId && binding.ClientId == engagement.PracticeClientId &&
                                binding.EngagementId == engagementId && ActiveTaskStates.Contains(task.Status) &&
                                ClientWaitingPbcStates.Contains(request.State)
                              select new { RequestId = request.Id, TaskId = task.Id, TaskTitle = task.Title };

    var evidencePbcCandidates = from rule in WorkflowTaskEvidenceQuery.Heads(db, actor.FirmId)
                                  where rule.State == WorkflowEvidenceRuleStates.Required &&
                                    rule.Kind == WorkflowEvidenceKinds.AcceptedPbc && rule.PbcRequestId.HasValue
                                  join binding in db.WorkflowTaskBindings.AsNoTracking()
                                    on rule.WorkflowTaskBindingId equals binding.Id
                                  join task in db.WorkTasks.AsNoTracking()
                                    on new { binding.FirmId, WorkTaskId = binding.WorkTaskId }
                                    equals new { task.FirmId, WorkTaskId = task.Id }
                                  join request in db.PbcRequests.AsNoTracking()
                                    on new { rule.FirmId, rule.ClientId, rule.EngagementId, Id = rule.PbcRequestId }
                                    equals new { request.FirmId, request.ClientId, request.EngagementId, Id = (Guid?)request.Id }
                                  where binding.FirmId == actor.FirmId && binding.ClientId == engagement.PracticeClientId &&
                                    binding.EngagementId == engagementId && rule.ClientId == engagement.PracticeClientId &&
                                    rule.EngagementId == engagementId && ActiveTaskStates.Contains(task.Status) &&
                                    ClientWaitingPbcStates.Contains(request.State)
                                  select new { RequestId = request.Id, TaskId = task.Id, TaskTitle = task.Title };

    var pbcCandidates = directPbcCandidates.Union(evidencePbcCandidates);
    var clientWaitRows = await pbcCandidates
      .GroupBy(x => x.RequestId)
      .Select(group => new { RequestId = group.Key, TaskTitle = group.Min(x => x.TaskTitle) })
      .OrderBy(x => x.RequestId)
      .Take(51).ToListAsync(ct);

    var milestoneResult = await StatutoryMilestoneService.GetMilestonesAsync(db, actor, engagementId, ct);
    if (!milestoneResult.Succeeded)
    {
      auth = await AuthorizeAsync(db, actor, engagement.PracticeClientId, engagementId, ct);
      if (!auth.Succeeded)
        return CommandResult<EngagementDeadlineReminders>.Fail(auth.ErrorCode!, auth.Message!);
      return CommandResult<EngagementDeadlineReminders>.Fail(milestoneResult.ErrorCode!, milestoneResult.Message!);
    }

    var reminders = new List<EngagementDeadlineReminder>();
    foreach (var task in taskRows)
    {
      var dueDate = task.DueDate!.Value;
      var (tone, message) = TaskTone(dueDate, today);
      reminders.Add(new(task.Id, "TASK_DUE", task.Title, dueDate, tone, message, "/app/work"));
    }

    reminders.AddRange(clientWaitRows.Select(row => new EngagementDeadlineReminder(
      row.RequestId, "CLIENT_WAIT", row.TaskTitle ?? "Linked workflow task", null, "PENDING",
      "A client document response is pending.", $"/app/engagements/{engagementId}/pbc")));

    if (milestoneResult.Value is { } plan)
    {
      AddMilestone(plan.FieldworkStartDate, "FIELDWORK_START", "Fieldwork start");
      AddMilestone(plan.DraftReportDate, "DRAFT_REPORT", "Draft report target");
      AddMilestone(plan.FinalReportDate, "FINAL_REPORT", "Final report target");
      AddMilestone(plan.StatutoryFilingCutoff, "STATUTORY_CUTOFF", "Recorded statutory cutoff");

      void AddMilestone(DateOnly dueDate, string kind, string title)
      {
        if (dueDate > sevenDaysFromToday) return;
        var (tone, message) = MilestoneTone(dueDate, today);
        reminders.Add(new(plan.Id, kind, title, dueDate, tone, message,
          $"/app/engagements/{engagementId}/audit-plan"));
      }
    }

    var ordered = reminders
      .GroupBy(x => (x.Kind, x.SourceId))
      .Select(group => group.First())
      .OrderBy(x => ToneOrder(x.Tone))
      .ThenBy(x => x.DueDate?.DayNumber ?? int.MaxValue)
      .ThenBy(x => x.Kind, StringComparer.Ordinal)
      .ThenBy(x => x.SourceId)
      .Take(51)
      .ToList();
    var truncated = ordered.Count > 50;
    if (truncated) ordered.RemoveAt(ordered.Count - 1);

    auth = await AuthorizeAsync(db, actor, engagement.PracticeClientId, engagementId, ct);
    if (!auth.Succeeded)
      return CommandResult<EngagementDeadlineReminders>.Fail(auth.ErrorCode!, auth.Message!);
    if (await IsFrozenAsync(db, actor.FirmId, engagement.PracticeClientId, engagementId, ct))
      return CommandResult<EngagementDeadlineReminders>.Ok(new(engagementId, today, [], false));

    return CommandResult<EngagementDeadlineReminders>.Ok(new(engagementId, today, ordered, truncated));
  }

  private static Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, WorkRoles, InternalOnly: true), ct);

  private static Task<bool> IsFrozenAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, CancellationToken ct) =>
    db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.ClientId == clientId &&
      x.EngagementId == engagementId && x.State == FileFreezeStates.Frozen, ct);

  private static (string Tone, string Message) TaskTone(DateOnly dueDate, DateOnly today) =>
    dueDate < today ? ("OVERDUE", "Task due date has passed.") :
    dueDate == today ? ("DUE_TODAY", "Task is due today.") :
    ("UPCOMING", "Task is due within 7 days.");

  private static (string Tone, string Message) MilestoneTone(DateOnly dueDate, DateOnly today) =>
    dueDate < today ? ("OVERDUE", "Recorded milestone date has passed; review its current completion status.") :
    dueDate == today ? ("DUE_TODAY", "Recorded milestone is due today.") :
    ("UPCOMING", "Recorded milestone falls within 7 days.");

  private static int ToneOrder(string tone) => tone switch
  {
    "OVERDUE" => 0,
    "DUE_TODAY" => 1,
    "UPCOMING" => 2,
    _ => 3
  };
}
