using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowTaskActivityRequest(
  DateOnly? From = null,
  DateOnly? To = null,
  DateTimeOffset? AfterOccurredAt = null,
  Guid? AfterActivityId = null,
  int PageSize = 50);

public sealed record WorkflowTaskActivityCursor(DateTimeOffset OccurredAt, Guid ActivityId);
public sealed record WorkflowTaskActivityItem(Guid Id, string Kind, string SourceLabel,
  string ActorDisplayName, string? AssigneeDisplayName, long TaskRevision,
  DateOnly? PreviousDueDate, DateOnly? NewDueDate, DateTimeOffset OccurredAt, string? Reason = null);
public sealed record WorkflowTaskActivityPage(Guid EngagementId, Guid TaskId, DateOnly From, DateOnly To,
  IReadOnlyList<WorkflowTaskActivityItem> Events, bool HasMore, WorkflowTaskActivityCursor? NextCursor);

/// <summary>Read-only, grant-scoped, bounded timeline over immutable operational task activity.</summary>
public static class WorkflowTaskActivityQuery
{
  private static readonly string[] ReadRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff"];
  private const int MaximumDays = 90;

  public static async Task<CommandResult<WorkflowTaskActivityPage>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid taskId,
    WorkflowTaskActivityRequest request, CancellationToken ct = default)
  {
    if (request.PageSize is < 1 or > 100 ||
        request.From.HasValue != request.To.HasValue ||
        request.AfterOccurredAt.HasValue != request.AfterActivityId.HasValue)
      return CommandResult<WorkflowTaskActivityPage>.Fail("workflow.activity-invalid",
        "Choose a page size from 1 to 100 and complete both date and cursor values when supplied.");

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var from = request.From ?? today.AddDays(-(MaximumDays - 1));
    var to = request.To ?? today;
    if (to < from || to > today || to.DayNumber - from.DayNumber >= MaximumDays)
      return CommandResult<WorkflowTaskActivityPage>.Fail("workflow.activity-invalid",
        "Choose a UTC date range no longer than 90 days.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: engagementId,
        RequiredRoles: ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<WorkflowTaskActivityPage>.Fail(auth.ErrorCode!, auth.Message!);

    var taskExists = await db.WorkflowTaskBindings.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.WorkTaskId == taskId, ct);
    if (!taskExists)
      return CommandResult<WorkflowTaskActivityPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var startsAt = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    var endsAt = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    var query = from activity in db.WorkflowTaskActivities.AsNoTracking()
      join actorUser in db.Users.AsNoTracking()
        on new { activity.FirmId, Id = activity.ActorUserId }
        equals new { actorUser.FirmId, actorUser.Id }
      join assignedUser in db.Users.AsNoTracking()
        on new { activity.FirmId, Id = activity.AssigneeUserId }
        equals new { assignedUser.FirmId, Id = (Guid?)assignedUser.Id } into assignedUsers
      from assignedUser in assignedUsers.DefaultIfEmpty()
      where activity.FirmId == actor.FirmId && activity.EngagementId == engagementId &&
        activity.WorkTaskId == taskId && activity.OccurredAt >= startsAt && activity.OccurredAt < endsAt
      select new
      {
        activity.Id, activity.Kind, ActorDisplayName = actorUser.DisplayName,
        AssigneeDisplayName = assignedUser == null ? null : assignedUser.DisplayName,
        activity.TaskRevision, activity.PreviousDueDate, activity.NewDueDate, activity.OccurredAt, activity.Reason
      };

    if (request.AfterOccurredAt.HasValue)
      query = query.Where(x => x.OccurredAt < request.AfterOccurredAt.Value ||
        (x.OccurredAt == request.AfterOccurredAt.Value && x.Id.CompareTo(request.AfterActivityId!.Value) < 0));

    var rows = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
      .Take(request.PageSize + 1).ToListAsync(ct);
    var hasMore = rows.Count > request.PageSize;
    if (hasMore) rows.RemoveAt(rows.Count - 1);
    var events = rows.Select(x => new WorkflowTaskActivityItem(x.Id, x.Kind, "Operational Task",
      x.ActorDisplayName, x.AssigneeDisplayName, x.TaskRevision,
      x.PreviousDueDate, x.NewDueDate, x.OccurredAt, x.Reason)).ToArray();
    var last = rows.Count == 0 ? null : rows[^1];
    return CommandResult<WorkflowTaskActivityPage>.Ok(new WorkflowTaskActivityPage(engagementId, taskId,
      from, to, events, hasMore,
      hasMore && last is not null ? new WorkflowTaskActivityCursor(last.OccurredAt, last.Id) : null));
  }
}
