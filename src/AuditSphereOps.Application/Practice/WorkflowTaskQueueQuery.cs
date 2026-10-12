using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowTaskQueueRequest(string View = "MY_TASKS", DateOnly? AfterDueDate = null,
  Guid? AfterTaskId = null, int PageSize = 50, Guid? AssigneeUserId = null, bool UnassignedOnly = false);
public sealed record WorkflowTaskQueueCursor(DateOnly? DueDate, Guid TaskId);
public sealed record WorkflowTaskQueueRow(Guid TaskId, Guid ClientId, string ClientName, Guid EngagementId,
  string EngagementServiceRoute, string EngagementPeriod, string Title, string Phase, int Ordinal,
  string TaskStatus, Guid? AssigneeUserId, string? AssigneeDisplayName, DateOnly? DueDate,
  Guid? MappingVersionId, string? FsliCode, int UnmetTaskDependencies, bool CanComplete,
  bool CanChangeDueDate, long Revision,
  Guid? PbcRequestId, string? PbcState, string? ReviewState, string? ReviewComment);
public sealed record WorkflowTaskQueue(string View, IReadOnlyList<WorkflowTaskQueueRow> Tasks,
  bool HasMore, WorkflowTaskQueueCursor? NextCursor);

/// <summary>
/// Grant-scoped queue over existing workflow bindings and WorkTask records. Queue views are filters over operational
/// state; audit review decisions remain in their authoritative review services.
/// </summary>
public static class WorkflowTaskQueueQuery
{
  private static readonly string[] ReadRoles = ["Administrator", "Partner", "Manager", "Senior", "Staff"];
  private static readonly string[] ElevatedRoles = ["Administrator", "Partner", "Manager"];
  private static readonly string[] ActiveTaskStates = [PracticeTimeStates.TaskOpen, PracticeTimeStates.TaskInProgress];
  private const string MyTasks = "MY_TASKS";
  private const string Team = "TEAM";
  private const string Overdue = "OVERDUE";
  private const string Completed = "COMPLETED";
  private const string WaitingForClient = "WAITING_FOR_CLIENT";
  private const string ChangesRequired = "CHANGES_REQUIRED";
  private const string UnderReview = "UNDER_REVIEW";
  private static readonly string[] ClientWaitingPbcStates =
    [PbcStates.Sent, PbcStates.Acknowledged, PbcStates.PartiallyReceived, PbcStates.ClarificationRequired];

  public static async Task<CommandResult<WorkflowTaskQueue>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, WorkflowTaskQueueRequest request, CancellationToken ct = default)
  {
    var view = request.View?.Trim().ToUpperInvariant();
    if (view is not (MyTasks or Team or Overdue or Completed or WaitingForClient or ChangesRequired or UnderReview) || request.PageSize is < 1 or > 100 ||
        (request.AfterTaskId is null && request.AfterDueDate.HasValue) || request.AssigneeUserId == Guid.Empty ||
        (request.UnassignedOnly && request.AssigneeUserId.HasValue) ||
        (view == MyTasks && (request.UnassignedOnly ||
          (request.AssigneeUserId.HasValue && request.AssigneeUserId != actor.UserId))))
      return CommandResult<WorkflowTaskQueue>.Fail("workflow.queue-invalid",
        "Choose a supported queue view, page size from 1 to 100, and a valid task cursor.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<WorkflowTaskQueue>.Fail(auth.ErrorCode!, auth.Message!);

    var now = DateTimeOffset.UtcNow;
    var grants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.UserId == actor.UserId && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now) &&
        ReadRoles.Contains(x.Role))
      .Select(x => new { x.Role, x.ClientId, x.EngagementId }).ToListAsync(ct);
    var firmWide = grants.Any(x => x.ClientId is null && x.EngagementId is null);
    var clientIds = grants.Where(x => x.ClientId.HasValue && x.EngagementId is null)
      .Select(x => x.ClientId!.Value).Distinct().ToArray();
    var engagementGrantPairs = grants.Where(x => x.ClientId.HasValue && x.EngagementId.HasValue)
      .Select(x => new { ClientId = x.ClientId!.Value, EngagementId = x.EngagementId!.Value }).Distinct().ToArray();
    var candidateEngagementIds = engagementGrantPairs.Select(x => x.EngagementId).Distinct().ToArray();
    var engagementScopes = candidateEngagementIds.Length == 0
      ? []
      : await db.Engagements.AsNoTracking().Where(x => x.FirmId == actor.FirmId && candidateEngagementIds.Contains(x.Id))
        .Select(x => new { x.Id, x.PracticeClientId }).ToListAsync(ct);
    var engagementIds = engagementScopes.Where(e => engagementGrantPairs.Any(g =>
        g.EngagementId == e.Id && g.ClientId == e.PracticeClientId))
      .Select(x => x.Id).Distinct().ToArray();
    if (!firmWide && clientIds.Length == 0 && engagementIds.Length == 0)
      return CommandResult<WorkflowTaskQueue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var query = from binding in db.WorkflowTaskBindings.AsNoTracking()
      join task in db.WorkTasks.AsNoTracking() on new { binding.FirmId, WorkTaskId = binding.WorkTaskId }
        equals new { task.FirmId, WorkTaskId = task.Id }
      join engagement in db.Engagements.AsNoTracking() on new { binding.FirmId, Id = binding.EngagementId }
        equals new { engagement.FirmId, engagement.Id }
      join client in db.PracticeClients.AsNoTracking() on new { binding.FirmId, Id = binding.ClientId }
        equals new { client.FirmId, client.Id }
      join pbcRequest in db.PbcRequests.AsNoTracking()
        on new { binding.FirmId, binding.ClientId, binding.EngagementId, Id = binding.PbcRequestId }
        equals new { pbcRequest.FirmId, pbcRequest.ClientId, pbcRequest.EngagementId, Id = (Guid?)pbcRequest.Id }
        into pbcRequests
      from pbcRequest in pbcRequests.DefaultIfEmpty()
      join reviewResult in db.AuditProcedureResults.AsNoTracking()
        on new { binding.FirmId, binding.ClientId, binding.EngagementId, binding.ReviewProcedureResultId }
        equals new { reviewResult.FirmId, reviewResult.ClientId, reviewResult.EngagementId, ReviewProcedureResultId = (Guid?)reviewResult.Id }
        into reviewResults
      from reviewResult in reviewResults.DefaultIfEmpty()
      join reviewProcedure in db.AuditProcedures.AsNoTracking()
        on new { reviewResult.FirmId, Id = reviewResult.AuditProcedureId }
        equals new { reviewProcedure.FirmId, Id = reviewProcedure.Id }
        into reviewProcedures
      from reviewProcedure in reviewProcedures.DefaultIfEmpty()
      join currentResult in db.AuditProcedureResults.AsNoTracking()
        on new { reviewProcedure.FirmId, reviewProcedure.ClientId, reviewProcedure.EngagementId,
          AuditProcedureId = reviewProcedure.Id, Revision = reviewProcedure.CurrentResultRevision }
        equals new { currentResult.FirmId, currentResult.ClientId, currentResult.EngagementId,
          currentResult.AuditProcedureId, currentResult.Revision }
        into currentResults
      from currentResult in currentResults.DefaultIfEmpty()
      where binding.FirmId == actor.FirmId &&
        (firmWide || clientIds.Contains(binding.ClientId) || engagementIds.Contains(binding.EngagementId))
      select new
      {
        BindingId = binding.Id, binding.Revision, binding.Phase, binding.Ordinal,
        task.Id, ClientId = binding.ClientId, ClientName = client.LegalName, EngagementId = binding.EngagementId,
        EngagementServiceRoute = engagement.ServiceRoute,
        EngagementPeriod = engagement.PeriodStart + " – " + engagement.PeriodEnd,
        task.Title, task.Status, task.AssigneeUserId, task.DueDate, task.MappingVersionId, task.FsliCode,
        PbcRequestId = binding.PbcRequestId, PbcState = pbcRequest == null ? null : pbcRequest.State,
        ReviewState = reviewResult != null && currentResult != null &&
          reviewResult.Status == AuditProcedureResultStatuses.ChangesRequired &&
          ((currentResult.Revision == reviewResult.Revision && currentResult.Status == AuditProcedureResultStatuses.ChangesRequired) ||
           (currentResult.Revision == reviewResult.Revision + 1 && currentResult.Status == AuditProcedureResultStatuses.Submitted))
          ? currentResult.Status == AuditProcedureResultStatuses.ChangesRequired ? ChangesRequired : UnderReview
          : null,
        ReviewComment = reviewResult != null && currentResult != null &&
          reviewResult.Status == AuditProcedureResultStatuses.ChangesRequired &&
          ((currentResult.Revision == reviewResult.Revision && currentResult.Status == AuditProcedureResultStatuses.ChangesRequired) ||
           (currentResult.Revision == reviewResult.Revision + 1 && currentResult.Status == AuditProcedureResultStatuses.Submitted))
          ? reviewResult.ReviewComment : null
      };

    if (view == MyTasks)
      query = query.Where(x => x.AssigneeUserId == actor.UserId && ActiveTaskStates.Contains(x.Status));
    else if (view == Team)
      query = query.Where(x => ActiveTaskStates.Contains(x.Status));
    else if (view == Overdue)
      query = query.Where(x => x.DueDate < DateOnly.FromDateTime(DateTime.UtcNow) && ActiveTaskStates.Contains(x.Status));
    else if (view == WaitingForClient)
      query = query.Where(x => ActiveTaskStates.Contains(x.Status) && x.PbcRequestId.HasValue &&
        x.PbcState != null && ClientWaitingPbcStates.Contains(x.PbcState));
    else if (view == ChangesRequired)
      query = query.Where(x => x.ReviewState == AuditProcedureResultStatuses.ChangesRequired);
    else if (view == UnderReview)
      query = query.Where(x => x.ReviewState == UnderReview);
    else
      query = query.Where(x => x.Status == PracticeTimeStates.TaskCompleted);

    // Assignee selection only narrows the already grant-scoped query. It is never an authority source.
    if (request.AssigneeUserId.HasValue)
      query = query.Where(x => x.AssigneeUserId == request.AssigneeUserId);
    else if (request.UnassignedOnly)
      query = query.Where(x => x.AssigneeUserId == null);

    if (request.AfterTaskId.HasValue)
    {
      if (request.AfterDueDate.HasValue)
        query = query.Where(x => x.DueDate == null || x.DueDate > request.AfterDueDate ||
          (x.DueDate == request.AfterDueDate && x.Id.CompareTo(request.AfterTaskId.Value) > 0));
      else
        query = query.Where(x => x.DueDate == null && x.Id.CompareTo(request.AfterTaskId.Value) > 0);
    }

    var page = await query.OrderBy(x => x.DueDate == null).ThenBy(x => x.DueDate).ThenBy(x => x.Id)
      .Take(request.PageSize + 1).ToListAsync(ct);
    var hasMore = page.Count > request.PageSize;
    if (hasMore) page.RemoveAt(page.Count - 1);
    if (page.Count == 0)
      return CommandResult<WorkflowTaskQueue>.Ok(new WorkflowTaskQueue(view!, [], false, null));

    var bindingIds = page.Select(x => x.BindingId).ToArray();
    var unmet = await (from dependency in db.WorkflowTaskDependencies.AsNoTracking()
      join predecessor in db.WorkflowTaskBindings.AsNoTracking()
        on new { dependency.FirmId, dependency.ClientId, dependency.EngagementId, Id = dependency.PredecessorBindingId }
        equals new { predecessor.FirmId, predecessor.ClientId, predecessor.EngagementId, predecessor.Id }
      join predecessorTask in db.WorkTasks.AsNoTracking()
        on new { predecessor.FirmId, WorkTaskId = predecessor.WorkTaskId }
        equals new { predecessorTask.FirmId, WorkTaskId = predecessorTask.Id }
      where dependency.FirmId == actor.FirmId && bindingIds.Contains(dependency.SuccessorBindingId) &&
        predecessorTask.Status != PracticeTimeStates.TaskCompleted
      select dependency.SuccessorBindingId).ToListAsync(ct);
    var unmetByBinding = unmet.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
    var assigneeIds = page.Where(x => x.AssigneeUserId.HasValue).Select(x => x.AssigneeUserId!.Value).Distinct().ToArray();
    var assignees = assigneeIds.Length == 0 ? new Dictionary<Guid, string>() :
      await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && assigneeIds.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

    bool CanElevatedManage(Guid clientId, Guid engagementId) => grants.Any(g =>
      ElevatedRoles.Contains(g.Role) && ((g.ClientId is null && g.EngagementId is null) ||
        (g.ClientId == clientId && (g.EngagementId is null || g.EngagementId == engagementId))));
    var rows = page.Select(x =>
    {
      var unmetCount = unmetByBinding.GetValueOrDefault(x.BindingId);
      var pbcComplete = !x.PbcRequestId.HasValue || x.PbcState is PbcStates.Accepted or PbcStates.Closed;
      var canComplete = ActiveTaskStates.Contains(x.Status) && unmetCount == 0 &&
        pbcComplete && (x.AssigneeUserId == actor.UserId || CanElevatedManage(x.ClientId, x.EngagementId));
      var canChangeDueDate = ActiveTaskStates.Contains(x.Status) && CanElevatedManage(x.ClientId, x.EngagementId);
      return new WorkflowTaskQueueRow(x.Id, x.ClientId, x.ClientName, x.EngagementId,
        x.EngagementServiceRoute, x.EngagementPeriod, x.Title, x.Phase, x.Ordinal, x.Status,
        x.AssigneeUserId, x.AssigneeUserId is { } userId ? assignees.GetValueOrDefault(userId) : null,
        x.DueDate, x.MappingVersionId, x.FsliCode, unmetCount, canComplete, canChangeDueDate, x.Revision,
        x.PbcRequestId, x.PbcState, x.ReviewState, x.ReviewComment);
    }).ToArray();
    var last = page[^1];
    return CommandResult<WorkflowTaskQueue>.Ok(new WorkflowTaskQueue(view!, rows, hasMore,
      hasMore ? new WorkflowTaskQueueCursor(last.DueDate, last.Id) : null));
  }
}
