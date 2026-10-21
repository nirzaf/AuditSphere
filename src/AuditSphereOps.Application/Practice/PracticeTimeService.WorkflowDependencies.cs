using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record WorkflowDependencyChangeRequest(Guid PredecessorTaskId, bool Required, long ExpectedRevision,
  long ExpectedPredecessorRevision, string Reason, Guid IdempotencyKey);

public static partial class PracticeTimeService
{
  public static async Task<CommandResult<WorkflowDependencyReceipt>> ChangeWorkflowDependencyAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid engagementId, Guid taskId, WorkflowDependencyChangeRequest request, CancellationToken ct = default)
  {
    var reason = request.Reason?.Trim();
    if (request.PredecessorTaskId == Guid.Empty || request.PredecessorTaskId == taskId || request.IdempotencyKey == Guid.Empty ||
        request.ExpectedRevision < 1 || request.ExpectedPredecessorRevision < 1 || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
      return CommandResult<WorkflowDependencyReceipt>.Fail("request.invalid", "Choose a different predecessor and review both task revisions and a reason.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<WorkflowDependencyReceipt>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var engagement = await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE firm_id = {actor.FirmId} AND id = {engagementId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (engagement is null) return CommandResult<WorkflowDependencyReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var guard = await LockClientAsync(db, actor.FirmId, engagement.PracticeClientId, ct);
    if (!guard.Succeeded) return CommandResult<WorkflowDependencyReceipt>.Fail(guard.ErrorCode!, guard.Message!);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, engagementId, WorkflowTaskEvidenceQuery.ManageRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<WorkflowDependencyReceipt>.Fail(auth.ErrorCode!, auth.Message!);
    // The firm/engagement guards serialize edits to the whole effective graph, including opposite-edge races.
    var task = await LoadTaskForUpdateAsync(db, actor.FirmId, taskId, ct);
    var predecessorTask = await LoadTaskForUpdateAsync(db, actor.FirmId, request.PredecessorTaskId, ct);
    var successor = await LockWorkflowBindingAsync(db, actor.FirmId, taskId, ct);
    var predecessor = await LockWorkflowBindingAsync(db, actor.FirmId, request.PredecessorTaskId, ct);
    if (task is null || predecessorTask is null || successor is null || predecessor is null)
      return CommandResult<WorkflowDependencyReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await db.WorkTasks.Attach(task).ReloadAsync(ct);
    await db.WorkTasks.Attach(predecessorTask).ReloadAsync(ct);
    await db.WorkflowTaskBindings.Attach(successor).ReloadAsync(ct);
    await db.WorkflowTaskBindings.Attach(predecessor).ReloadAsync(ct);
    if (successor.EngagementId != engagementId || predecessor.EngagementId != engagementId ||
        successor.ClientId != engagement.PracticeClientId || predecessor.ClientId != engagement.PracticeClientId ||
        task.EngagementId != engagementId || predecessorTask.EngagementId != engagementId)
      return CommandResult<WorkflowDependencyReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
      engagementId, taskId, request.PredecessorTaskId, request.Required, request.ExpectedRevision, request.ExpectedPredecessorRevision, reason
    }))).ToLowerInvariant();
    var replay = await db.WorkflowTaskDependencyRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorUserId == actor.UserId && x.IdempotencyKey == request.IdempotencyKey, ct);
    if (replay is not null) return replay.RequestDigest == digest
      ? CommandResult<WorkflowDependencyReceipt>.Ok(new(engagementId, taskId, request.IdempotencyKey, replay.Id, replay.TaskRevision))
      : CommandResult<WorkflowDependencyReceipt>.Fail(ErrorCodes.IdempotencyConflict, "This dependency key was used for different reviewed content.");
    if (successor.Revision != request.ExpectedRevision || predecessor.Revision != request.ExpectedPredecessorRevision)
      return CommandResult<WorkflowDependencyReceipt>.Fail(ErrorCodes.StaleRevision, "A task changed. Refresh and review both references again.");
    if (task.Status is PracticeTimeStates.TaskCompleted or PracticeTimeStates.TaskCancelled || successor.Revision == long.MaxValue ||
        (request.Required && predecessorTask.Status == PracticeTimeStates.TaskCancelled))
      return CommandResult<WorkflowDependencyReceipt>.Fail(ErrorCodes.ProtectedState, "Closed successors and cancelled required predecessors cannot be changed.");
    var nodes = await WorkflowTaskDependencyQuery.TasksAsync(db, actor.FirmId, engagementId, ct);
    var edges = await WorkflowTaskDependencyQuery.Effective(db, actor.FirmId).Where(x => x.EngagementId == engagementId)
      .Take(WorkflowTaskDependencyQuery.MaximumEdges + 1).ToListAsync(ct);
    if (nodes.Count > WorkflowTaskDependencyQuery.MaximumTasks || edges.Count > WorkflowTaskDependencyQuery.MaximumEdges)
      return CommandResult<WorkflowDependencyReceipt>.Fail(ErrorCodes.GateBlocked, "Task ordering exceeds the supported complete-graph bound; no partial graph was changed.");
    var current = edges.Any(x => x.PredecessorBindingId == predecessor.Id && x.SuccessorBindingId == successor.Id);
    if (current == request.Required)
      return CommandResult<WorkflowDependencyReceipt>.Fail(ErrorCodes.ProtectedState, "This dependency already has the requested state.");
    var proposed = edges.Where(x => x.PredecessorBindingId != predecessor.Id || x.SuccessorBindingId != successor.Id)
      .Select(x => (From: x.PredecessorBindingId, To: x.SuccessorBindingId)).ToList();
    if (request.Required) proposed.Add((predecessor.Id, successor.Id));
    if (proposed.Count > WorkflowTaskDependencyQuery.MaximumEdges || WorkflowTaskDependencyQuery.HasCycle(nodes.Select(x => x.BindingId), proposed))
      return CommandResult<WorkflowDependencyReceipt>.Fail(ErrorCodes.GateBlocked, "The reviewed dependency would create a cycle or exceed the complete-graph bound.");
    var previous = await WorkflowTaskDependencyQuery.Heads(db, actor.FirmId).SingleOrDefaultAsync(x =>
      x.SuccessorBindingId == successor.Id && x.PredecessorBindingId == predecessor.Id, ct);
    if (previous?.Version == int.MaxValue)
      return CommandResult<WorkflowDependencyReceipt>.Fail(ErrorCodes.ProtectedState, "The dependency version cannot advance further.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, successor.ClientId, engagementId, WorkflowTaskEvidenceQuery.ManageRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<WorkflowDependencyReceipt>.Fail(auth.ErrorCode!, auth.Message!);
    var now = DateTimeOffset.UtcNow; successor.Revision++;
    var row = new WorkflowTaskDependencyRevision { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = successor.ClientId,
      EngagementId = engagementId, PredecessorBindingId = predecessor.Id, SuccessorBindingId = successor.Id,
      Version = (previous?.Version ?? 0) + 1, Required = request.Required, TaskRevision = successor.Revision,
      PredecessorRevision = predecessor.Revision, Reason = reason, ActorUserId = actor.UserId,
      IdempotencyKey = request.IdempotencyKey, RequestDigest = digest, OccurredAt = now };
    db.WorkflowTaskDependencyRevisions.Add(row);
    AddWorkflowTaskActivity(db, actor, task, successor, WorkflowTaskActivityKinds.DependencyChanged, null, successor.Revision, now);
    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    return CommandResult<WorkflowDependencyReceipt>.Ok(new(engagementId, taskId, request.IdempotencyKey, row.Id, row.TaskRevision));
  }
}
