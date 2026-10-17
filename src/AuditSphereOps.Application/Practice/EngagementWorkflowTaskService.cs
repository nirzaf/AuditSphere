using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowTaskCreateRequest(
  string Title,
  string Phase,
  int Ordinal,
  DateOnly? DueDate = null,
  int? PlannedEffortMinutes = null,
  Guid? AssigneeUserId = null,
  Guid? MappingVersionId = null,
  string? FsliCode = null,
  string? AssignedRole = null,
  Guid IdempotencyKey = default);

public sealed record WorkflowTaskRow(
  Guid Id,
  string Title,
  string Phase,
  int Ordinal,
  string Status,
  Guid? AssigneeUserId,
  string? AssigneeDisplayName,
  string? AssignedRole,
  DateOnly? DueDate,
  int? PlannedEffortMinutes,
  Guid? MappingVersionId,
  string? FsliCode,
  Guid? TemplateRevisionId,
  int Generation,
  long Revision,
  DateTimeOffset CreatedAt);

public sealed record WorkflowTaskWorkspace(
  Guid EngagementId,
  IReadOnlyList<WorkflowTaskRow> Tasks,
  bool Truncated);
public sealed record WorkflowTaskAssigneeOption(Guid UserId, string DisplayName);

/// <summary>
/// Workflow task coordination over the existing WorkTask and time-entry records. Task completion is operational only and
/// is deliberately not consulted by the protected engagement lifecycle.
/// </summary>
public static class EngagementWorkflowTaskService
{
  private static readonly string[] WorkRoles = ["Senior", "Staff", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<WorkflowTaskWorkspace>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: engagementId, RequiredRoles: WorkRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<WorkflowTaskWorkspace>.Fail(auth.ErrorCode!, auth.Message!);

    var rows = await (from binding in db.WorkflowTaskBindings.AsNoTracking()
                      join task in db.WorkTasks.AsNoTracking()
                        on new { binding.FirmId, WorkTaskId = binding.WorkTaskId }
                        equals new { task.FirmId, WorkTaskId = task.Id }
                      where binding.FirmId == actor.FirmId && binding.EngagementId == engagementId
                      orderby binding.Phase, binding.Ordinal, binding.Id
                      select new WorkflowTaskRow(task.Id, task.Title, binding.Phase, binding.Ordinal, task.Status,
                        binding.AssigneeUserIdSnapshot, null, binding.AssignedRole, task.DueDate, binding.PlannedEffortMinutes, task.MappingVersionId,
                        task.FsliCode, binding.TemplateRevisionId, binding.Generation, binding.Revision, binding.CreatedAt))
      .Take(501).ToListAsync(ct);
    var truncated = rows.Count > 500;
    if (truncated) rows.RemoveAt(rows.Count - 1);
    var assigneeIds = rows.Where(x => x.AssigneeUserId.HasValue).Select(x => x.AssigneeUserId!.Value).Distinct().ToArray();
    var assignees = assigneeIds.Length == 0 ? new Dictionary<Guid, string>() :
      await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && assigneeIds.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    rows = rows.Select(x => x with
      { AssigneeDisplayName = x.AssigneeUserId is { } id ? assignees.GetValueOrDefault(id) : null }).ToList();
    return CommandResult<WorkflowTaskWorkspace>.Ok(new WorkflowTaskWorkspace(engagementId, rows, truncated));
  }

  public static async Task<CommandResult> EnsureTaskScopeAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid taskId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: engagementId, RequiredRoles: WorkRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return auth;
    var linked = await db.WorkflowTaskBindings.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.WorkTaskId == taskId, ct);
    return linked ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  }

  /// <summary>Active staff who meet the same firm/client/engagement grant boundary enforced by task assignment.</summary>
  public static async Task<CommandResult<IReadOnlyList<WorkflowTaskAssigneeOption>>> GetAssigneeOptionsAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null)
      return CommandResult<IReadOnlyList<WorkflowTaskAssigneeOption>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id,
        PracticeTimeService.ApprovalRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<WorkflowTaskAssigneeOption>>.Fail(auth.ErrorCode!, auth.Message!);

    var now = DateTimeOffset.UtcNow;
    var options = await (from user in db.Users.AsNoTracking()
      join grant in db.RoleGrants.AsNoTracking() on new { user.FirmId, UserId = user.Id }
        equals new { grant.FirmId, grant.UserId }
      where user.FirmId == actor.FirmId && user.UserKind == "Staff" && !user.Disabled &&
        grant.RevokedAt == null && (grant.ExpiresAt == null || grant.ExpiresAt > now) &&
        (grant.EngagementId == engagement.Id ||
          (grant.EngagementId == null && (grant.ClientId == null || grant.ClientId == engagement.PracticeClientId)))
      select new { UserId = user.Id, user.DisplayName })
      .Distinct().OrderBy(x => x.DisplayName).ThenBy(x => x.UserId).Take(200)
      .Select(x => new WorkflowTaskAssigneeOption(x.UserId, x.DisplayName)).ToListAsync(ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id,
        PracticeTimeService.ApprovalRoles, InternalOnly: true), ct);
    return auth.Succeeded
      ? CommandResult<IReadOnlyList<WorkflowTaskAssigneeOption>>.Ok(options)
      : CommandResult<IReadOnlyList<WorkflowTaskAssigneeOption>>.Fail(auth.ErrorCode!, auth.Message!);
  }

  public static async Task<CommandResult<Guid>> CreateAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, WorkflowTaskCreateRequest request,
    CancellationToken ct = default)
  {
    var title = request.Title?.Trim();
    if (string.IsNullOrWhiteSpace(title) || title.Length > 300)
      return CommandResult<Guid>.Fail("workflow.task-invalid", "Enter a task title of 1 to 300 characters.");
    if (!WorkflowTaskPhases.All.Contains(request.Phase, StringComparer.Ordinal) || request.Ordinal is < 1 or > 100000)
      return CommandResult<Guid>.Fail("workflow.task-invalid", "Choose a valid workflow phase and positive ordering number.");
    if (request.PlannedEffortMinutes is < 1 or > 1000000)
      return CommandResult<Guid>.Fail("workflow.task-invalid", "Planned effort must be between 1 and 1,000,000 minutes.");
    if (request.IdempotencyKey == Guid.Empty)
      return CommandResult<Guid>.Fail("workflow.task-invalid", "A stable task request key is required.");
    var assignedRole = string.IsNullOrWhiteSpace(request.AssignedRole) ? null : request.AssignedRole.Trim();
    if (assignedRole is not null && !StaffingLevels.All.Contains(assignedRole, StringComparer.Ordinal))
      return CommandResult<Guid>.Fail("workflow.task-invalid", "Choose a supported staffing level for the optional role requirement.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, EngagementId: engagementId, RequiredRoles: WorkRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(
      x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var binding = new WorkflowTaskBinding
    {
      Phase = request.Phase,
      Ordinal = request.Ordinal,
      SourceKey = $"MANUAL:{request.IdempotencyKey:N}",
      CreationRequestDigest = CreateRequestDigest(engagementId, title, request, assignedRole),
      Generation = 1,
      PlannedEffortMinutes = request.PlannedEffortMinutes,
      DueDatePolicy = "MANUAL",
      AssignedRole = assignedRole,
      AssignmentSource = "MANUAL"
    };
    return await PracticeTimeService.CreateWorkflowTaskAsync(db, actor,
      new CreateTaskRequest(title, engagement.PracticeClientId, engagementId, request.AssigneeUserId,
        DueDate: request.DueDate, MappingVersionId: request.MappingVersionId, FsliCode: request.FsliCode), binding, ct);
  }

  private static string CreateRequestDigest(Guid engagementId, string title,
    WorkflowTaskCreateRequest request, string? assignedRole)
  {
    var canonical = JsonSerializer.SerializeToUtf8Bytes(new
    {
      engagementId,
      title,
      phase = request.Phase,
      ordinal = request.Ordinal,
      dueDate = request.DueDate?.ToString("yyyy-MM-dd"),
      plannedEffortMinutes = request.PlannedEffortMinutes,
      assigneeUserId = request.AssigneeUserId,
      mappingVersionId = request.MappingVersionId,
      fsliCode = string.IsNullOrWhiteSpace(request.FsliCode) ? null : request.FsliCode.Trim(),
      assignedRole
    });
    return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
  }

  public static Task<CommandResult> AssignAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid taskId, Guid assigneeUserId,
    long expectedRevision, CancellationToken ct = default) =>
    PracticeTimeService.AssignWorkflowTaskAsync(db, actor, taskId, assigneeUserId, expectedRevision, ct);

  public static Task<CommandResult> ReassignAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid taskId, Guid assigneeUserId,
    long expectedRevision, CancellationToken ct = default) =>
    PracticeTimeService.ReassignWorkflowTaskAsync(db, actor, taskId, assigneeUserId, expectedRevision, ct);

  public static Task<CommandResult> CompleteAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid taskId, long expectedRevision, CancellationToken ct = default) =>
    PracticeTimeService.CompleteWorkflowTaskAsync(db, actor, taskId, expectedRevision, ct);

  public static Task<CommandResult> ChangeDueDateAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid taskId, DateOnly? dueDate,
    long expectedRevision, CancellationToken ct = default) =>
    PracticeTimeService.ChangeWorkflowTaskDueDateAsync(db, actor, taskId, dueDate, expectedRevision, ct);
}
