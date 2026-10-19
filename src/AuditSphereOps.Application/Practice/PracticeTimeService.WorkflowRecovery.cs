using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class PracticeTimeService
{
  internal static Task<CommandResult> ReopenWorkflowTaskAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid taskId, string reason,
    long expectedRevision, CancellationToken ct = default) =>
    ReopenWorkflowTaskCoreAsync(db, actor, taskId, reason, expectedRevision, ct);

  private static async Task<CommandResult> ReopenWorkflowTaskCoreAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid taskId, string reason,
    long expectedRevision, CancellationToken ct)
  {
    var normalizedReason = reason?.Trim();
    if (expectedRevision < 1 || string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 2000)
      return CommandResult.Fail("workflow.task-invalid", "Enter a reason for reopening of 1 to 2000 characters.");

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
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a bound workflow task can be reopened.");

    await db.WorkflowTaskBindings.Attach(workflowBinding).ReloadAsync(ct);

    // Recover an unknown committed response only for the exact immutable intent; different intent at that old revision conflicts.
    if (expectedRevision < long.MaxValue &&
        workflowBinding.Revision >= expectedRevision + 1)
    {
      var priorReopenings = await db.WorkflowTaskActivities.AsNoTracking().Where(x =>
          x.FirmId == actor.FirmId && x.ClientId == task.ClientId && x.EngagementId == task.EngagementId &&
          x.WorkTaskId == task.Id && x.WorkflowTaskBindingId == workflowBinding.Id &&
          x.Kind == WorkflowTaskActivityKinds.Reopened && x.TaskRevision == expectedRevision + 1)
        .Select(x => new { x.ActorUserId, x.Reason }).Take(2).ToListAsync(ct);
      if (priorReopenings.Count > 1)
        return CommandResult.Fail(ErrorCodes.ProtectedState, "The workflow task has conflicting reopening history.");
      if (priorReopenings.Count == 1)
        return priorReopenings[0].ActorUserId == actor.UserId && priorReopenings[0].Reason == normalizedReason
          ? CommandResult.Ok()
          : CommandResult.Fail(ErrorCodes.IdempotencyConflict,
            "This reopening revision was already used for different content.");
    }

    if (workflowBinding.Revision != expectedRevision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "The workflow task changed. Refresh it and try again.");
    if (task.Status != PracticeTimeStates.TaskCancelled || task.CompletedAt.HasValue)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a cancelled operational task without completed-work evidence can be reopened.");
    if (workflowBinding.Revision == long.MaxValue)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The workflow task revision cannot advance further.");

    auth = await AuthorizeTaskAsync(db, actor, task, ApprovalRoles, ct);
    if (!auth.Succeeded) return auth;
    task.Status = PracticeTimeStates.TaskOpen;
    workflowBinding.Revision++;
    AddWorkflowTaskActivity(db, actor, task, workflowBinding, WorkflowTaskActivityKinds.Reopened,
      null, workflowBinding.Revision, DateTimeOffset.UtcNow, reason: normalizedReason);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

}
