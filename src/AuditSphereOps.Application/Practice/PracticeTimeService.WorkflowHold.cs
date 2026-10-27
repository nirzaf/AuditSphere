using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class PracticeTimeService
{
  /// <summary>
  /// Puts an active workflow task on hold, or releases its hold. A held task keeps its status, owner, due date, time and
  /// evidence; it cannot be completed or receive new time until a manager resumes it.
  /// </summary>
  internal static Task<CommandResult> HoldWorkflowTaskAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid taskId, bool hold, string reason,
    long expectedRevision, CancellationToken ct = default) =>
    HoldWorkflowTaskCoreAsync(db, actor, taskId, hold, reason, expectedRevision, ct);

  private static async Task<CommandResult> HoldWorkflowTaskCoreAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid taskId, bool hold, string reason,
    long expectedRevision, CancellationToken ct)
  {
    var action = hold ? "putting on hold" : "resuming";
    var normalizedReason = reason?.Trim();
    if (expectedRevision < 1 || string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 2000)
      return CommandResult.Fail("workflow.task-invalid", $"Enter a reason for {action} of 1 to 2000 characters.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var taskSnapshot = await db.WorkTasks.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == taskId && x.FirmId == actor.FirmId, ct);
    if (taskSnapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var guard = await LockClientAsync(db, actor.FirmId, taskSnapshot.ClientId, ct);
    if (!guard.Succeeded) return guard;
    var task = await LoadTaskForUpdateAsync(db, actor.FirmId, taskId, ct);
    if (task is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await db.WorkTasks.Attach(task).ReloadAsync(ct);
    if (task.ClientId != taskSnapshot.ClientId) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeTaskAsync(db, actor, task, ApprovalRoles, ct);
    if (!auth.Succeeded) return auth;
    var workflowBinding = await LockWorkflowBindingAsync(db, actor.FirmId, task.Id, ct);
    if (workflowBinding is null)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a bound workflow task can be put on hold or resumed.");
    await db.WorkflowTaskBindings.Attach(workflowBinding).ReloadAsync(ct);

    // Recover an unknown committed response only for the exact immutable intent; different intent at that old revision conflicts.
    var kind = hold ? WorkflowTaskActivityKinds.Suspended : WorkflowTaskActivityKinds.Resumed;
    if (expectedRevision < long.MaxValue && workflowBinding.Revision >= expectedRevision + 1)
    {
      var prior = await db.WorkflowTaskActivities.AsNoTracking().Where(x =>
          x.FirmId == actor.FirmId && x.ClientId == task.ClientId && x.EngagementId == task.EngagementId &&
          x.WorkTaskId == task.Id && x.WorkflowTaskBindingId == workflowBinding.Id &&
          x.Kind == kind && x.TaskRevision == expectedRevision + 1)
        .Select(x => new { x.ActorUserId, x.Reason }).Take(2).ToListAsync(ct);
      if (prior.Count > 1)
        return CommandResult.Fail(ErrorCodes.ProtectedState, "The workflow task has conflicting hold history.");
      if (prior.Count == 1)
        return prior[0].ActorUserId == actor.UserId && prior[0].Reason == normalizedReason
          ? CommandResult.Ok()
          : CommandResult.Fail(ErrorCodes.IdempotencyConflict, "This hold revision was already used for different content.");
    }

    if (workflowBinding.Revision != expectedRevision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "The workflow task changed. Refresh it and try again.");
    if (task.Status is not (PracticeTimeStates.TaskOpen or PracticeTimeStates.TaskInProgress))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only an open or in-progress task can be put on hold or resumed.");
    if (hold == workflowBinding.HeldAt.HasValue)
      return CommandResult.Fail(ErrorCodes.ProtectedState, hold ? "The task is already on hold." : "The task is not on hold.");
    if (workflowBinding.Revision == long.MaxValue)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The workflow task revision cannot advance further.");

    auth = await AuthorizeTaskAsync(db, actor, task, ApprovalRoles, ct);
    if (!auth.Succeeded) return auth;
    var now = DateTimeOffset.UtcNow;
    workflowBinding.HeldAt = hold ? now : null;
    workflowBinding.HoldReason = hold ? normalizedReason : null;
    workflowBinding.Revision++;
    AddWorkflowTaskActivity(db, actor, task, workflowBinding, kind, null, workflowBinding.Revision, now, reason: normalizedReason);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }
}
