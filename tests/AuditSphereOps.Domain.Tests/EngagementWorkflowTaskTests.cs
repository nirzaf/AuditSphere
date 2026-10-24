using System.Data.Common;
using System.Text.RegularExpressions;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class EngagementWorkflowTaskTests
{
  [Trait("CompletionUnit", "WU-016")]
  [Fact]
  public async Task ExistingWorkTaskOwnsWorkflowBinding_TimeLineageAndScopedRevisionFencing()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    var clientA = Guid.NewGuid();
    var clientB = Guid.NewGuid();
    var engagementA = Guid.NewGuid();
    var engagementB = Guid.NewGuid();
    var manager = User(firmId, "Manager");
    var staffA = User(firmId, "StaffA");
    var staffB = User(firmId, "StaffB");
    var expiredStaff = User(firmId, "ExpiredStaff");
    var managerActor = Actor(manager, "Manager");
    var staffAActor = Actor(staffA, "Staff");

    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
      seed.ClientSafetyStates.AddRange(
        new ClientSafetyState { Id = clientA, FirmId = firmId },
        new ClientSafetyState { Id = clientB, FirmId = firmId });
      seed.PracticeClients.AddRange(
        new PracticeClient { Id = clientA, FirmId = firmId, LegalName = "Workflow Client A", CreatedAt = DateTimeOffset.UtcNow },
        new PracticeClient { Id = clientB, FirmId = firmId, LegalName = "Workflow Client B", CreatedAt = DateTimeOffset.UtcNow });
      seed.Engagements.AddRange(
        Engagement(engagementA, firmId, clientA),
        Engagement(engagementB, firmId, clientB));
      seed.Users.AddRange(manager, staffA, staffB, expiredStaff);
      await seed.SaveChangesAsync();
      var expiredGrant = Grant(firmId, expiredStaff, "Staff", clientA, engagementA);
      expiredGrant.GrantedAt = DateTimeOffset.UtcNow.AddDays(-2);
      expiredGrant.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
      seed.RoleGrants.AddRange(
        Grant(firmId, manager, "Manager"),
        Grant(firmId, staffA, "Staff", clientA, engagementA),
        Grant(firmId, staffB, "Staff", clientB, engagementB),
        expiredGrant);
      await seed.SaveChangesAsync();
    }

    Guid taskA;
    Guid taskASecond;
    Guid taskB;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var requestA = new WorkflowTaskCreateRequest("Prepare planning memo", WorkflowTaskPhases.AuditPlanning, 1,
        new DateOnly(2026, 11, 30), PlannedEffortMinutes: 90,
        AssignedRole: StaffingLevels.AuditManager, IdempotencyKey: Guid.NewGuid());
      var createdA = await EngagementWorkflowTaskService.CreateAsync(db, managerActor, engagementA,
        requestA);
      var createdB = await EngagementWorkflowTaskService.CreateAsync(db, managerActor, engagementB,
        new WorkflowTaskCreateRequest("Prepare planning memo", WorkflowTaskPhases.AuditPlanning, 1,
          IdempotencyKey: Guid.NewGuid()));
      var createdASecond = await EngagementWorkflowTaskService.CreateAsync(db, managerActor, engagementA,
        new WorkflowTaskCreateRequest("Review planning evidence", WorkflowTaskPhases.AuditPlanning, 2,
          new DateOnly(2026, 10, 10), IdempotencyKey: Guid.NewGuid()));
      Assert.True(createdA.Succeeded, createdA.Message);
      Assert.True(createdB.Succeeded, createdB.Message);
      Assert.True(createdASecond.Succeeded, createdASecond.Message);
      taskA = createdA.Value;
      taskB = createdB.Value;
      taskASecond = createdASecond.Value;
      Assert.NotEqual(taskA, taskB);
      Assert.NotEqual(taskA, taskASecond);

      var replay = await EngagementWorkflowTaskService.CreateAsync(db, managerActor, engagementA, requestA);
      Assert.True(replay.Succeeded, replay.Message);
      Assert.Equal(taskA, replay.Value);
      Assert.Equal(3, await db.WorkflowTaskActivities.CountAsync(x => x.Kind == WorkflowTaskActivityKinds.Created));
      var conflictingReplay = await EngagementWorkflowTaskService.CreateAsync(db, managerActor, engagementA,
        requestA with { Title = "Different content" });
      Assert.False(conflictingReplay.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, conflictingReplay.ErrorCode);
      Assert.Equal(3, await db.WorkflowTaskBindings.CountAsync());

      var assignment = await EngagementWorkflowTaskService.AssignAsync(db, managerActor, taskA, staffA.Id, 1);
      Assert.True(assignment.Succeeded, assignment.Message);
      var assignedActivity = await db.WorkflowTaskActivities.SingleAsync(x =>
        x.WorkTaskId == taskA && x.Kind == WorkflowTaskActivityKinds.Assigned);
      Assert.Equal(manager.Id, assignedActivity.ActorUserId);
      Assert.Equal(staffA.Id, assignedActivity.AssigneeUserId);
      Assert.Equal(2, assignedActivity.TaskRevision);
      var stale = await EngagementWorkflowTaskService.ReassignAsync(db, managerActor, taskA, staffB.Id, 1);
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
      var eligible = await EngagementWorkflowTaskService.GetAssigneeOptionsAsync(db, managerActor, engagementA);
      Assert.True(eligible.Succeeded, eligible.Message);
      Assert.Contains(eligible.Value!, x => x.UserId == staffA.Id);
      Assert.DoesNotContain(eligible.Value!, x => x.UserId == staffB.Id);
      Assert.DoesNotContain(eligible.Value!, x => x.UserId == expiredStaff.Id);
      var expiredAssignment = await EngagementWorkflowTaskService.AssignAsync(db, managerActor,
        taskASecond, expiredStaff.Id, 1);
      Assert.False(expiredAssignment.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, expiredAssignment.ErrorCode);
      Assert.Equal(1, (await db.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == taskASecond)).Revision);
      var ineligible = await EngagementWorkflowTaskService.AssignAsync(db, managerActor, taskASecond, staffB.Id, 1);
      Assert.False(ineligible.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, ineligible.ErrorCode);
      Assert.Equal(1, (await db.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == taskASecond)).Revision);
      var sameAssignee = await EngagementWorkflowTaskService.ReassignAsync(db, managerActor, taskA, staffA.Id, 2);
      Assert.True(sameAssignee.Succeeded, sameAssignee.Message);
      Assert.Equal(2, await db.WorkflowTaskActivities.CountAsync(x => x.WorkTaskId == taskA));
      var secondAssignment = await EngagementWorkflowTaskService.AssignAsync(db, managerActor, taskASecond, staffA.Id, 1);
      Assert.True(secondAssignment.Succeeded, secondAssignment.Message);
      var reassignment = await EngagementWorkflowTaskService.ReassignAsync(db, managerActor, taskASecond, manager.Id, 2);
      Assert.True(reassignment.Succeeded, reassignment.Message);
      var reassignActivity = await db.WorkflowTaskActivities.SingleAsync(x =>
        x.WorkTaskId == taskASecond && x.Kind == WorkflowTaskActivityKinds.Reassigned);
      Assert.Equal(manager.Id, reassignActivity.AssigneeUserId);
      Assert.Equal(3, reassignActivity.TaskRevision);

      var draft = await PracticeTimeService.SaveTimeDraftAsync(db, staffAActor,
        new SaveTimeDraftRequest(taskA, new DateOnly(2026, 10, 12), 540, 45,
          "Staff", "Planning", PracticeTimeStates.NonBillable, "Workflow planning time"));
      Assert.True(draft.Succeeded, draft.Message);
      var time = await db.TimeEntries.SingleAsync(x => x.Id == draft.Value);
      Assert.Equal(taskA, time.TaskId);
      Assert.Null(time.MappingVersionId);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var own = await EngagementWorkflowTaskService.GetAsync(db, staffAActor, engagementA);
      Assert.True(own.Succeeded, own.Message);
      Assert.Equal(2, own.Value!.Tasks.Count);
      var task = Assert.Single(own.Value.Tasks, x => x.Id == taskA);
      Assert.Equal(taskA, task.Id);
      Assert.Equal(2, task.Revision);
      Assert.Equal(staffA.Id, task.AssigneeUserId);
      Assert.Equal(StaffingLevels.AuditManager, task.AssignedRole);

      var firstPage = await WorkflowTaskQueueQuery.GetAsync(db, staffAActor,
        new WorkflowTaskQueueRequest("TEAM", PageSize: 1));
      Assert.True(firstPage.Succeeded, firstPage.Message);
      Assert.True(firstPage.Value!.HasMore);
      Assert.Equal(taskASecond, Assert.Single(firstPage.Value.Tasks).TaskId);
      Assert.Equal(engagementA, firstPage.Value.Tasks[0].EngagementId);
      var cursor = Assert.IsType<WorkflowTaskQueueCursor>(firstPage.Value.NextCursor);
      var secondPage = await WorkflowTaskQueueQuery.GetAsync(db, staffAActor,
        new WorkflowTaskQueueRequest("TEAM", cursor.DueDate, cursor.TaskId, 1));
      Assert.True(secondPage.Succeeded, secondPage.Message);
      Assert.False(secondPage.Value!.HasMore);
      Assert.Equal(taskA, Assert.Single(secondPage.Value.Tasks).TaskId);
      var retainedSource = await db.WorkflowTaskBindings.AsNoTracking().SingleAsync(x => x.WorkTaskId == taskA);
      Assert.Equal(retainedSource.SourceKey, secondPage.Value.Tasks[0].SourceKey);
      Assert.Equal(retainedSource.TemplateRevisionId, secondPage.Value.Tasks[0].TemplateRevisionId);
      Assert.Equal(retainedSource.TemplateStepId, secondPage.Value.Tasks[0].TemplateStepId);
      var assigned = await WorkflowTaskQueueQuery.GetAsync(db, staffAActor,
        new WorkflowTaskQueueRequest("TEAM", AssigneeUserId: staffA.Id));
      Assert.True(assigned.Succeeded, assigned.Message);
      Assert.Equal(taskA, Assert.Single(assigned.Value!.Tasks).TaskId);
      var siblingOwner = await WorkflowTaskQueueQuery.GetAsync(db, staffAActor,
        new WorkflowTaskQueueRequest("TEAM", AssigneeUserId: staffB.Id));
      Assert.True(siblingOwner.Succeeded, siblingOwner.Message);
      Assert.Empty(siblingOwner.Value!.Tasks);
      var contradictory = await WorkflowTaskQueueQuery.GetAsync(db, staffAActor,
        new WorkflowTaskQueueRequest("TEAM", AssigneeUserId: staffA.Id, UnassignedOnly: true));
      Assert.False(contradictory.Succeeded);
      Assert.Equal("workflow.queue-invalid", contradictory.ErrorCode);
      var widenedMine = await WorkflowTaskQueueQuery.GetAsync(db, staffAActor,
        new WorkflowTaskQueueRequest("MY_TASKS", AssigneeUserId: staffB.Id));
      Assert.False(widenedMine.Succeeded);
      var overdue = await WorkflowTaskQueueQuery.GetAsync(db, staffAActor,
        new WorkflowTaskQueueRequest("OVERDUE"));
      Assert.True(overdue.Succeeded, overdue.Message);
      Assert.Equal(taskASecond, Assert.Single(overdue.Value!.Tasks).TaskId);

      var foreign = await EngagementWorkflowTaskService.GetAsync(db, staffAActor, engagementB);
      Assert.False(foreign.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, foreign.ErrorCode);

      var completed = await EngagementWorkflowTaskService.CompleteAsync(db, staffAActor, taskA, task.Revision);
      Assert.True(completed.Succeeded, completed.Message);
      var persisted = await db.WorkTasks.SingleAsync(x => x.Id == taskA);
      var binding = await db.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == taskA);
      Assert.Equal(PracticeTimeStates.TaskCompleted, persisted.Status);
      Assert.Equal(3, binding.Revision);
      Assert.Equal(staffA.Id, binding.AssigneeUserIdSnapshot);
      Assert.Equal(1, await db.TimeEntries.CountAsync(x => x.TaskId == taskA));
      Assert.Equal(3, await db.WorkflowTaskActivities.CountAsync(x => x.WorkTaskId == taskA));
      var firstActivityPage = await WorkflowTaskActivityQuery.GetAsync(db, staffAActor, engagementA, taskA,
        new WorkflowTaskActivityRequest(PageSize: 2));
      Assert.True(firstActivityPage.Succeeded, firstActivityPage.Message);
      Assert.True(firstActivityPage.Value!.HasMore);
      Assert.Equal(engagementA, firstActivityPage.Value.EngagementId);
      Assert.Equal(taskA, firstActivityPage.Value.TaskId);
      Assert.Equal(WorkflowTaskActivityKinds.Completed, firstActivityPage.Value.Events[0].Kind);
      Assert.Equal("Operational Task", firstActivityPage.Value.Events[0].SourceLabel);
      Assert.Equal("StaffA", firstActivityPage.Value.Events[0].ActorDisplayName);
      var nextActivity = await WorkflowTaskActivityQuery.GetAsync(db, staffAActor, engagementA, taskA,
        new WorkflowTaskActivityRequest(AfterOccurredAt: firstActivityPage.Value.NextCursor!.OccurredAt,
          AfterActivityId: firstActivityPage.Value.NextCursor.ActivityId, PageSize: 2));
      Assert.True(nextActivity.Succeeded, nextActivity.Message);
      Assert.False(nextActivity.Value!.HasMore);
      Assert.Equal(WorkflowTaskActivityKinds.Created, Assert.Single(nextActivity.Value.Events).Kind);

      var foreignActivity = await WorkflowTaskActivityQuery.GetAsync(db, staffAActor, engagementB, taskB,
        new WorkflowTaskActivityRequest());
      Assert.False(foreignActivity.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, foreignActivity.ErrorCode);

      var invalidRange = await WorkflowTaskActivityQuery.GetAsync(db, staffAActor, engagementA, taskA,
        new WorkflowTaskActivityRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 5, 1)));
      Assert.False(invalidRange.Succeeded);
      Assert.Equal("workflow.activity-invalid", invalidRange.ErrorCode);

      var staffGrant = await db.RoleGrants.SingleAsync(x => x.FirmId == firmId && x.UserId == staffA.Id);
      staffGrant.RevokedAt = DateTimeOffset.UtcNow;
      await db.SaveChangesAsync();
      var afterRevocation = await WorkflowTaskQueueQuery.GetAsync(db, staffAActor,
        new WorkflowTaskQueueRequest("TEAM"));
      Assert.False(afterRevocation.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, afterRevocation.ErrorCode);
      var activityAfterRevocation = await WorkflowTaskActivityQuery.GetAsync(db, staffAActor, engagementA, taskA,
        new WorkflowTaskActivityRequest());
      Assert.False(activityAfterRevocation.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, activityAfterRevocation.ErrorCode);

      var firstEngagement = await db.Engagements.SingleAsync(x => x.Id == engagementA);
      Assert.Equal(1, firstEngagement.Generation);
      Assert.Equal("Draft", firstEngagement.Status);
      Assert.Equal(engagementB, (await db.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == taskB)).EngagementId);
    }

    await using (var tamper = new AuditSphereDbContext(pg.Options))
    {
      var activity = await tamper.WorkflowTaskActivities.SingleAsync(x =>
        x.WorkTaskId == taskA && x.Kind == WorkflowTaskActivityKinds.Created);
      activity.Kind = WorkflowTaskActivityKinds.Completed;
      await Assert.ThrowsAsync<DbUpdateException>(() => tamper.SaveChangesAsync());
    }

    await using (var tamper = new AuditSphereDbContext(pg.Options))
    {
      var activity = await tamper.WorkflowTaskActivities.SingleAsync(x =>
        x.WorkTaskId == taskA && x.Kind == WorkflowTaskActivityKinds.Created);
      tamper.WorkflowTaskActivities.Remove(activity);
      await Assert.ThrowsAsync<DbUpdateException>(() => tamper.SaveChangesAsync());
    }

    await using (var mismatch = new AuditSphereDbContext(pg.Options))
    {
      var otherBinding = await mismatch.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == taskASecond);
      mismatch.WorkflowTaskActivities.Add(new WorkflowTaskActivity
      {
        Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientA, EngagementId = engagementA,
        WorkTaskId = taskA, WorkflowTaskBindingId = otherBinding.Id,
        Kind = WorkflowTaskActivityKinds.Created, ActorUserId = manager.Id,
        TaskRevision = 1, OccurredAt = DateTimeOffset.UtcNow
      });
      await Assert.ThrowsAsync<DbUpdateException>(() => mismatch.SaveChangesAsync());
    }
  }

  [Fact]
  public async Task DueDateRevisionIsManagerScopedAndRecordedWithExactBeforeAndAfterValues()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var manager = User(firmId, "Manager");
    var staff = User(firmId, "Staff");
    var managerActor = Actor(manager, "Manager");
    var staffActor = Actor(staff, "Staff");
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
      seed.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
      seed.PracticeClients.Add(new PracticeClient
        { Id = clientId, FirmId = firmId, LegalName = "Due Date Client", CreatedAt = DateTimeOffset.UtcNow });
      seed.Engagements.Add(Engagement(engagementId, firmId, clientId));
      seed.Users.AddRange(manager, staff);
      await seed.SaveChangesAsync();
      seed.RoleGrants.AddRange(Grant(firmId, manager, "Manager"),
        Grant(firmId, staff, "Staff", clientId, engagementId));
      await seed.SaveChangesAsync();
    }

    var originalDate = new DateOnly(2026, 10, 31);
    var revisedDate = new DateOnly(2026, 11, 15);
    Guid taskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await EngagementWorkflowTaskService.CreateAsync(db, managerActor, engagementId,
        new WorkflowTaskCreateRequest("Review bank confirmation", WorkflowTaskPhases.AuditFieldwork, 1,
          originalDate, IdempotencyKey: Guid.NewGuid()));
      Assert.True(created.Succeeded, created.Message);
      taskId = created.Value;

      var managerQueue = await WorkflowTaskQueueQuery.GetAsync(db, managerActor,
        new WorkflowTaskQueueRequest("TEAM"));
      Assert.True(managerQueue.Succeeded, managerQueue.Message);
      Assert.True(Assert.Single(managerQueue.Value!.Tasks).CanChangeDueDate);
      var staffQueue = await WorkflowTaskQueueQuery.GetAsync(db, staffActor,
        new WorkflowTaskQueueRequest("TEAM"));
      Assert.True(staffQueue.Succeeded, staffQueue.Message);
      Assert.False(Assert.Single(staffQueue.Value!.Tasks).CanChangeDueDate);

      var staffDenied = await EngagementWorkflowTaskService.ChangeDueDateAsync(db, staffActor, taskId,
        revisedDate, expectedRevision: 1);
      Assert.False(staffDenied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, staffDenied.ErrorCode);

      var changed = await EngagementWorkflowTaskService.ChangeDueDateAsync(db, managerActor, taskId,
        revisedDate, expectedRevision: 1);
      Assert.True(changed.Succeeded, changed.Message);

      var stale = await EngagementWorkflowTaskService.ChangeDueDateAsync(db, managerActor, taskId,
        new DateOnly(2026, 11, 30), expectedRevision: 1);
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);

      var task = await db.WorkTasks.SingleAsync(x => x.Id == taskId);
      var binding = await db.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == taskId);
      var change = await db.WorkflowTaskActivities.SingleAsync(x =>
        x.WorkTaskId == taskId && x.Kind == WorkflowTaskActivityKinds.DueDateChanged);
      Assert.Equal(revisedDate, task.DueDate);
      Assert.Equal(2, binding.Revision);
      Assert.Equal(2, change.TaskRevision);
      Assert.Equal(originalDate, change.PreviousDueDate);
      Assert.Equal(revisedDate, change.NewDueDate);

      var page = await WorkflowTaskActivityQuery.GetAsync(db, managerActor, engagementId, taskId,
        new WorkflowTaskActivityRequest());
      Assert.True(page.Succeeded, page.Message);
      Assert.Contains(page.Value!.Events, x => x.Kind == WorkflowTaskActivityKinds.DueDateChanged &&
        x.PreviousDueDate == originalDate && x.NewDueDate == revisedDate);
    }
  }

  [Trait("CompletionUnit", "WU-016")]
  [Fact]
  public async Task CancellationIsScopedReplayableAndPreservesTaskHistoryAndBlockedDependents()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    var clientA = Guid.NewGuid();
    var clientB = Guid.NewGuid();
    var engagementA = Guid.NewGuid();
    var engagementB = Guid.NewGuid();
    var manager = User(firmId, "CancelManager");
    var staff = User(firmId, "CancelStaff");
    var siblingManager = User(firmId, "SiblingManager");
    var managerActor = Actor(manager, "Manager");
    var staffActor = Actor(staff, "Staff");
    var siblingManagerActor = Actor(siblingManager, "Manager");
    await SeedQueueScopeAsync(pg, firmId, [(clientA, engagementA), (clientB, engagementB)],
      [manager, staff, siblingManager], () =>
      [
        Grant(firmId, manager, "Manager", clientA, engagementA),
        Grant(firmId, staff, "Staff", clientA, engagementA),
        Grant(firmId, siblingManager, "Manager", clientB, engagementB)
      ]);

    Guid predecessorTaskId;
    Guid dependentTaskId;
    Guid completedTaskId;
    Guid predecessorBindingId;
    Guid dependentBindingId;
    Guid timeEntryId;
    const string cancellationReason = "Workstream closed after manager review.";
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      predecessorTaskId = await CreateQueueTaskAsync(db, managerActor, engagementA, "Cancelled prerequisite", 1, null);
      dependentTaskId = await CreateQueueTaskAsync(db, managerActor, engagementA, "Dependent task", 2, null);
      completedTaskId = await CreateQueueTaskAsync(db, managerActor, engagementA, "Already completed task", 3, null);
      var siblingTaskId = await CreateQueueTaskAsync(db, siblingManagerActor, engagementB, "Sibling task", 1, null);

      var assigned = await EngagementWorkflowTaskService.AssignAsync(db, managerActor, predecessorTaskId, staff.Id, 1);
      Assert.True(assigned.Succeeded, assigned.Message);
      predecessorBindingId = (await db.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == predecessorTaskId)).Id;
      var dependency = await PracticeTimeService.ChangeWorkflowDependencyAsync(db, managerActor, engagementA,
        dependentTaskId, new WorkflowDependencyChangeRequest(predecessorTaskId, true,
          ExpectedRevision: 1, ExpectedPredecessorRevision: 2, Reason: "Keep the prerequisite explicit.",
          IdempotencyKey: Guid.NewGuid()));
      Assert.True(dependency.Succeeded, dependency.Message);
      dependentBindingId = (await db.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == dependentTaskId)).Id;

      var savedTime = await PracticeTimeService.SaveTimeDraftAsync(db, staffActor,
        new SaveTimeDraftRequest(predecessorTaskId, new DateOnly(2026, 10, 12), 540, 45,
          "Staff", "Planning", PracticeTimeStates.NonBillable, "Time remains attached to cancelled work."));
      Assert.True(savedTime.Succeeded, savedTime.Message);
      timeEntryId = savedTime.Value;

      var completed = await EngagementWorkflowTaskService.CompleteAsync(db, managerActor, completedTaskId, 1);
      Assert.True(completed.Succeeded, completed.Message);
      var completedBefore = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == completedTaskId);
      var completedAt = completedBefore.CompletedAt;
      var completedRevision = await db.WorkflowTaskBindings.AsNoTracking()
        .Where(x => x.WorkTaskId == completedTaskId).Select(x => x.Revision).SingleAsync();
      var completedEvents = await db.WorkflowTaskActivities.CountAsync(x => x.WorkTaskId == completedTaskId);

      var blankReason = await EngagementWorkflowTaskService.CancelAsync(db, managerActor, predecessorTaskId, " \t ", 2);
      Assert.Equal("workflow.task-invalid", blankReason.ErrorCode);
      var longReason = await EngagementWorkflowTaskService.CancelAsync(db, managerActor, predecessorTaskId,
        new string('x', 2001), 2);
      Assert.Equal("workflow.task-invalid", longReason.ErrorCode);
      var nonManager = await EngagementWorkflowTaskService.CancelAsync(db, staffActor, predecessorTaskId, cancellationReason, 2);
      Assert.Equal(ErrorCodes.ScopeDenied, nonManager.ErrorCode);
      var sibling = await EngagementWorkflowTaskService.CancelAsync(db, siblingManagerActor, predecessorTaskId, cancellationReason, 2);
      Assert.Equal(ErrorCodes.ScopeDenied, sibling.ErrorCode);
      var stale = await EngagementWorkflowTaskService.CancelAsync(db, managerActor, predecessorTaskId, cancellationReason, 1);
      Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);

      var managerQueue = await WorkflowTaskQueueQuery.GetAsync(db, managerActor, new WorkflowTaskQueueRequest("TEAM"));
      Assert.True(managerQueue.Succeeded, managerQueue.Message);
      var managerTaskRow = Assert.Single(managerQueue.Value!.Tasks, x => x.TaskId == predecessorTaskId);
      Assert.True(managerTaskRow.CanCancel);
      Assert.False(managerTaskRow.CanReopen);
      var staffQueue = await WorkflowTaskQueueQuery.GetAsync(db, staffActor, new WorkflowTaskQueueRequest("TEAM"));
      Assert.True(staffQueue.Succeeded, staffQueue.Message);
      var staffTaskRow = Assert.Single(staffQueue.Value!.Tasks, x => x.TaskId == predecessorTaskId);
      Assert.False(staffTaskRow.CanCancel);
      Assert.False(staffTaskRow.CanReopen);

      var beforeCancellationEvents = await db.WorkflowTaskActivities.CountAsync(x => x.WorkTaskId == predecessorTaskId);
      var cancelled = await EngagementWorkflowTaskService.CancelAsync(db, managerActor,
        predecessorTaskId, cancellationReason, 2);
      Assert.True(cancelled.Succeeded, cancelled.Message);
      var afterCancel = await db.WorkflowTaskBindings.AsNoTracking().SingleAsync(x => x.WorkTaskId == predecessorTaskId);
      Assert.Equal(predecessorBindingId, afterCancel.Id);
      Assert.Equal(3, afterCancel.Revision);
      Assert.Equal(beforeCancellationEvents + 1,
        await db.WorkflowTaskActivities.CountAsync(x => x.WorkTaskId == predecessorTaskId));
      var cancellationEvent = await db.WorkflowTaskActivities.SingleAsync(x =>
        x.WorkTaskId == predecessorTaskId && x.Kind == WorkflowTaskActivityKinds.Cancelled);
      Assert.Equal(manager.Id, cancellationEvent.ActorUserId);
      Assert.Equal(3, cancellationEvent.TaskRevision);
      Assert.Equal(cancellationReason, cancellationEvent.Reason);

      var replay = await EngagementWorkflowTaskService.CancelAsync(db, managerActor,
        predecessorTaskId, $"  {cancellationReason}  ", 2);
      Assert.True(replay.Succeeded, replay.Message);
      var differentIntent = await EngagementWorkflowTaskService.CancelAsync(db, managerActor,
        predecessorTaskId, "A different cancellation reason.", 2);
      Assert.Equal(ErrorCodes.IdempotencyConflict, differentIntent.ErrorCode);
      Assert.Equal(3, (await db.WorkflowTaskBindings.AsNoTracking()
        .SingleAsync(x => x.WorkTaskId == predecessorTaskId)).Revision);
      Assert.Equal(beforeCancellationEvents + 1,
        await db.WorkflowTaskActivities.CountAsync(x => x.WorkTaskId == predecessorTaskId));

      var closedTaskCancel = await EngagementWorkflowTaskService.CancelAsync(db, managerActor,
        completedTaskId, "Cannot cancel completed work.", completedRevision);
      Assert.Equal(ErrorCodes.ProtectedState, closedTaskCancel.ErrorCode);
      var completedAfter = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == completedTaskId);
      Assert.Equal(PracticeTimeStates.TaskCompleted, completedAfter.Status);
      Assert.Equal(completedAt, completedAfter.CompletedAt);
      Assert.Equal(completedRevision, await db.WorkflowTaskBindings.AsNoTracking()
        .Where(x => x.WorkTaskId == completedTaskId).Select(x => x.Revision).SingleAsync());
      Assert.Equal(completedEvents, await db.WorkflowTaskActivities.CountAsync(x => x.WorkTaskId == completedTaskId));

      var completedCancelledTask = await EngagementWorkflowTaskService.CompleteAsync(db, managerActor, predecessorTaskId, 3);
      Assert.Equal(ErrorCodes.ProtectedState, completedCancelledTask.ErrorCode);
      var reassignedCancelledTask = await EngagementWorkflowTaskService.AssignAsync(db, managerActor, predecessorTaskId, manager.Id, 3);
      Assert.Equal(ErrorCodes.ProtectedState, reassignedCancelledTask.ErrorCode);
      var lateTime = await PracticeTimeService.SaveTimeDraftAsync(db, staffActor,
        new SaveTimeDraftRequest(predecessorTaskId, new DateOnly(2026, 10, 13), 600, 30,
          "Staff", "Planning", PracticeTimeStates.NonBillable, "Must remain closed."));
      Assert.Equal(ErrorCodes.GateBlocked, lateTime.ErrorCode);
      Assert.Equal(1, await db.TimeEntries.CountAsync(x => x.TaskId == predecessorTaskId));
      var retainedTime = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == timeEntryId);
      Assert.Equal(predecessorTaskId, retainedTime.TaskId);
      Assert.Equal(45, retainedTime.DurationMinutes);

      var stillBlocked = await EngagementWorkflowTaskService.CompleteAsync(db, managerActor, dependentTaskId, 2);
      Assert.Equal(ErrorCodes.GateBlocked, stillBlocked.ErrorCode);
      Assert.Equal(1, await db.WorkflowTaskDependencyRevisions.CountAsync(x => x.SuccessorBindingId == dependentBindingId));
      var cancelledQueue = await WorkflowTaskQueueQuery.GetAsync(db, managerActor,
        new WorkflowTaskQueueRequest("CANCELLED"));
      Assert.True(cancelledQueue.Succeeded, cancelledQueue.Message);
      var cancelledRow = Assert.Single(cancelledQueue.Value!.Tasks, x => x.TaskId == predecessorTaskId);
      Assert.False(cancelledRow.CanCancel);
      Assert.True(cancelledRow.CanReopen);
      var staffCancelledQueue = await WorkflowTaskQueueQuery.GetAsync(db, staffActor,
        new WorkflowTaskQueueRequest("CANCELLED"));
      Assert.True(staffCancelledQueue.Succeeded, staffCancelledQueue.Message);
      Assert.False(Assert.Single(staffCancelledQueue.Value!.Tasks, x => x.TaskId == predecessorTaskId).CanReopen);
      var completedQueue = await WorkflowTaskQueueQuery.GetAsync(db, managerActor,
        new WorkflowTaskQueueRequest("COMPLETED"));
      Assert.True(completedQueue.Succeeded, completedQueue.Message);
      Assert.False(Assert.Single(completedQueue.Value!.Tasks, x => x.TaskId == completedTaskId).CanReopen);
      var activityPage = await WorkflowTaskActivityQuery.GetAsync(db, managerActor, engagementA,
        predecessorTaskId, new WorkflowTaskActivityRequest());
      Assert.True(activityPage.Succeeded, activityPage.Message);
      Assert.Contains(activityPage.Value!.Events, x => x.Kind == WorkflowTaskActivityKinds.Cancelled &&
        x.TaskRevision == 3 && x.Reason == cancellationReason);

      // Add 500 earlier phase rows in one bounded bulk fixture to put the cancelled task beyond GetAsync's default window.
      var now = DateTimeOffset.UtcNow;
      var syntheticTasks = Enumerable.Range(1, 500).Select(i => new WorkTask
      {
        Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientA, EngagementId = engagementA,
        Title = $"Earlier phase task {i}", Status = PracticeTimeStates.TaskOpen,
        Phase = BudgetPhases.Unassigned, CreatedAt = now
      }).ToArray();
      var syntheticBindings = syntheticTasks.Select((task, i) => new WorkflowTaskBinding
      {
        Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientA, EngagementId = engagementA,
        WorkTaskId = task.Id, Phase = WorkflowTaskPhases.ArchiveClose, Ordinal = i + 1,
        SourceKey = $"TEST-CANCEL-RECOVERY:{i + 1:D4}", CreationRequestDigest = new string('a', 64),
        Generation = 1, DueDatePolicy = "MANUAL", AssignmentSource = "MANUAL",
        CreatedAt = now, CreatedByUserId = manager.Id
      }).ToArray();
      db.WorkTasks.AddRange(syntheticTasks);
      db.WorkflowTaskBindings.AddRange(syntheticBindings);
      await db.SaveChangesAsync();

      var defaultWorkspace = await EngagementWorkflowTaskService.GetAsync(db, managerActor, engagementA);
      Assert.True(defaultWorkspace.Succeeded, defaultWorkspace.Message);
      Assert.True(defaultWorkspace.Value!.Truncated);
      Assert.Equal(500, defaultWorkspace.Value.Tasks.Count);
      Assert.DoesNotContain(defaultWorkspace.Value.Tasks, x => x.Id == predecessorTaskId);
      var selectedWorkspace = await EngagementWorkflowTaskService.GetAsync(db, managerActor,
        engagementA, selectedTaskId: predecessorTaskId);
      Assert.True(selectedWorkspace.Succeeded, selectedWorkspace.Message);
      Assert.False(selectedWorkspace.Value!.Truncated);
      var selectedTask = Assert.Single(selectedWorkspace.Value.Tasks);
      Assert.Equal(predecessorTaskId, selectedTask.Id);
      Assert.Equal(PracticeTimeStates.TaskCancelled, selectedTask.Status);

      const string tamperedReason = "Tampered cancellation reason.";
      var mutation = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE workflow_task_activities SET reason = {tamperedReason} WHERE firm_id = {firmId} AND id = {cancellationEvent.Id}"));
      var updateError = Assert.IsType<PostgresException>(mutation.GetBaseException());
      Assert.Equal("P0001", updateError.SqlState);
      Assert.Contains("append-only", updateError.MessageText, StringComparison.OrdinalIgnoreCase);
      var deletion = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM workflow_task_activities WHERE firm_id = {firmId} AND id = {cancellationEvent.Id}"));
      var deleteError = Assert.IsType<PostgresException>(deletion.GetBaseException());
      Assert.Equal("P0001", deleteError.SqlState);
      Assert.Contains("append-only", deleteError.MessageText, StringComparison.OrdinalIgnoreCase);

      var migrations = db.Database.GetMigrations().ToArray();
      var cancellationMigrationIndex = Array.FindIndex(migrations,
        x => x.EndsWith("_WorkflowTaskCancellation", StringComparison.Ordinal));
      Assert.True(cancellationMigrationIndex > 0);
      var cancellationMigrationId = migrations[cancellationMigrationIndex];
      var previousMigration = migrations[cancellationMigrationIndex - 1];
      var downgrade = await Assert.ThrowsAnyAsync<Exception>(() =>
        db.GetService<IMigrator>().MigrateAsync(previousMigration));
      var downgradeError = Assert.IsType<PostgresException>(downgrade.GetBaseException());
      Assert.Equal("55000", downgradeError.SqlState);
      Assert.Contains("Workflow task cancellation evidence prevents rollback without losing cancellation history.",
        downgradeError.MessageText, StringComparison.Ordinal);
      Assert.Contains(cancellationMigrationId, await db.Database.GetAppliedMigrationsAsync());
      Assert.True(await db.Database.SqlQueryRaw<bool>("""
        SELECT EXISTS (
          SELECT 1 FROM information_schema.columns
          WHERE table_schema = current_schema()
            AND table_name = 'workflow_task_activities'
            AND column_name = 'reason'
        ) AS "Value"
        """).SingleAsync());
      var retainedCancellationEvent = await db.WorkflowTaskActivities.AsNoTracking()
        .SingleAsync(x => x.Id == cancellationEvent.Id);
      Assert.Equal(cancellationReason, retainedCancellationEvent.Reason);

      Assert.Null((await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == predecessorTaskId)).CompletedAt);
      Assert.Equal(siblingTaskId, await db.WorkflowTaskBindings.AsNoTracking()
        .Where(x => x.EngagementId == engagementB).Select(x => x.WorkTaskId).SingleAsync());
    }
  }

  [Trait("CompletionUnit", "WU-016")]
  [Fact]
  public async Task ReopeningIsScopedReplayableAndRetainsTaskEvidenceTimeAndDependencyHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    var clientA = Guid.NewGuid();
    var clientB = Guid.NewGuid();
    var engagementA = Guid.NewGuid();
    var engagementB = Guid.NewGuid();
    var manager = User(firmId, "ReopenManager");
    var otherManager = User(firmId, "OtherReopenManager");
    var revokedManager = User(firmId, "RevokedReopenManager");
    var staff = User(firmId, "ReopenStaff");
    var siblingManager = User(firmId, "ReopenSiblingManager");
    var managerActor = Actor(manager, "Manager");
    var otherManagerActor = Actor(otherManager, "Manager");
    var revokedManagerActor = Actor(revokedManager, "Manager");
    var staffActor = Actor(staff, "Staff");
    var siblingManagerActor = Actor(siblingManager, "Manager");
    await SeedQueueScopeAsync(pg, firmId, [(clientA, engagementA), (clientB, engagementB)],
      [manager, otherManager, revokedManager, staff, siblingManager], () =>
      [
        Grant(firmId, manager, "Manager", clientA, engagementA),
        Grant(firmId, otherManager, "Manager", clientA, engagementA),
        Grant(firmId, revokedManager, "Manager", clientA, engagementA),
        Grant(firmId, staff, "Staff", clientA, engagementA),
        Grant(firmId, siblingManager, "Manager", clientB, engagementB)
      ]);

    await using var db = new AuditSphereDbContext(pg.Options);
    var predecessorTaskId = await CreateQueueTaskAsync(db, managerActor, engagementA,
      "Reopened prerequisite", 1, null);
    var dependentTaskId = await CreateQueueTaskAsync(db, managerActor, engagementA,
      "Blocked dependent", 2, null);
    var completedTaskId = await CreateQueueTaskAsync(db, managerActor, engagementA,
      "Completed task stays closed", 3, null);

    var evidenceAdded = await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, managerActor, engagementA,
      predecessorTaskId, new WorkflowEvidenceChangeRequest(WorkflowEvidenceKinds.CurrentMateriality, null, true,
        "Retain the task evidence history.", 1, Guid.NewGuid()));
    Assert.True(evidenceAdded.Succeeded, evidenceAdded.Message);
    var evidenceWithdrawn = await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, managerActor, engagementA,
      predecessorTaskId, new WorkflowEvidenceChangeRequest(WorkflowEvidenceKinds.CurrentMateriality, null, false,
        "Retain the task evidence history.", 2, Guid.NewGuid()));
    Assert.True(evidenceWithdrawn.Succeeded, evidenceWithdrawn.Message);
    var predecessorBindingId = await db.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.WorkTaskId == predecessorTaskId).Select(x => x.Id).SingleAsync();
    var evidenceIds = await db.WorkflowTaskEvidenceRules.AsNoTracking()
      .Where(x => x.WorkflowTaskBindingId == predecessorBindingId)
      .OrderBy(x => x.Version).Select(x => x.Id).ToArrayAsync();
    Assert.Equal([evidenceAdded.Value!.RuleRevisionId, evidenceWithdrawn.Value!.RuleRevisionId], evidenceIds);

    var assigned = await EngagementWorkflowTaskService.AssignAsync(db, managerActor,
      predecessorTaskId, staff.Id, expectedRevision: 3);
    Assert.True(assigned.Succeeded, assigned.Message);
    var dependency = await PracticeTimeService.ChangeWorkflowDependencyAsync(db, managerActor, engagementA,
      dependentTaskId, new WorkflowDependencyChangeRequest(predecessorTaskId, true,
        ExpectedRevision: 1, ExpectedPredecessorRevision: 4, Reason: "Wait for the prerequisite to be completed.",
        IdempotencyKey: Guid.NewGuid()));
    Assert.True(dependency.Succeeded, dependency.Message);
    var dependentBindingId = await db.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.WorkTaskId == dependentTaskId).Select(x => x.Id).SingleAsync();
    var timeEntry = await PracticeTimeService.SaveTimeDraftAsync(db, staffActor,
      new SaveTimeDraftRequest(predecessorTaskId, new DateOnly(2026, 10, 12), 540, 45,
        "Staff", "Planning", PracticeTimeStates.NonBillable, "Retained across task reopening."));
    Assert.True(timeEntry.Succeeded, timeEntry.Message);

    var completed = await EngagementWorkflowTaskService.CompleteAsync(db, managerActor, completedTaskId, 1);
    Assert.True(completed.Succeeded, completed.Message);
    var completedBefore = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == completedTaskId);
    var completedAt = completedBefore.CompletedAt;
    var completedRevision = await db.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.WorkTaskId == completedTaskId).Select(x => x.Revision).SingleAsync();
    var completedEvents = await db.WorkflowTaskActivities.CountAsync(x => x.WorkTaskId == completedTaskId);

    var openTaskReopen = await EngagementWorkflowTaskService.ReopenAsync(db, managerActor,
      predecessorTaskId, "An open task cannot be reopened.", expectedRevision: 4);
    Assert.Equal(ErrorCodes.ProtectedState, openTaskReopen.ErrorCode);
    var cancelled = await EngagementWorkflowTaskService.CancelAsync(db, managerActor,
      predecessorTaskId, "Cancelled before reopening.", expectedRevision: 4);
    Assert.True(cancelled.Succeeded, cancelled.Message);
    const string reopenReason = "Resume the task after the coordination hold was resolved.";

    var staffDenied = await EngagementWorkflowTaskService.ReopenAsync(db, staffActor,
      predecessorTaskId, reopenReason, expectedRevision: 5);
    Assert.Equal(ErrorCodes.ScopeDenied, staffDenied.ErrorCode);
    var wrongScope = await EngagementWorkflowTaskService.ReopenAsync(db, siblingManagerActor,
      predecessorTaskId, reopenReason, expectedRevision: 5);
    Assert.Equal(ErrorCodes.ScopeDenied, wrongScope.ErrorCode);
    var revokedGrant = await db.RoleGrants.SingleAsync(x => x.UserId == revokedManager.Id &&
      x.ClientId == clientA && x.EngagementId == engagementA);
    revokedGrant.RevokedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    var revoked = await EngagementWorkflowTaskService.ReopenAsync(db, revokedManagerActor,
      predecessorTaskId, reopenReason, expectedRevision: 5);
    Assert.Equal(ErrorCodes.ScopeDenied, revoked.ErrorCode);
    var stale = await EngagementWorkflowTaskService.ReopenAsync(db, managerActor,
      predecessorTaskId, reopenReason, expectedRevision: 4);
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    var blankReason = await EngagementWorkflowTaskService.ReopenAsync(db, managerActor,
      predecessorTaskId, "  ", expectedRevision: 5);
    Assert.Equal("workflow.task-invalid", blankReason.ErrorCode);

    var completedReopen = await EngagementWorkflowTaskService.ReopenAsync(db, managerActor,
      completedTaskId, "Completed work remains completed.", completedRevision);
    Assert.Equal(ErrorCodes.ProtectedState, completedReopen.ErrorCode);
    var completedAfter = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == completedTaskId);
    Assert.Equal(PracticeTimeStates.TaskCompleted, completedAfter.Status);
    Assert.Equal(completedAt, completedAfter.CompletedAt);
    Assert.Equal(completedRevision, await db.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.WorkTaskId == completedTaskId).Select(x => x.Revision).SingleAsync());
    Assert.Equal(completedEvents, await db.WorkflowTaskActivities.CountAsync(x => x.WorkTaskId == completedTaskId));
    // Model a legacy cancelled row that still carries its completed-work timestamp.
    var legacyCompletedTask = await db.WorkTasks.SingleAsync(x => x.Id == completedTaskId);
    legacyCompletedTask.Status = PracticeTimeStates.TaskCancelled;
    await db.SaveChangesAsync();
    var legacyShapeReopen = await EngagementWorkflowTaskService.ReopenAsync(db, managerActor,
      completedTaskId, "A cancelled row with completion history remains closed.", completedRevision);
    Assert.Equal(ErrorCodes.ProtectedState, legacyShapeReopen.ErrorCode);
    var legacyShapeAfter = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == completedTaskId);
    Assert.Equal(PracticeTimeStates.TaskCancelled, legacyShapeAfter.Status);
    Assert.Equal(completedAt, legacyShapeAfter.CompletedAt);
    Assert.Equal(completedRevision, await db.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.WorkTaskId == completedTaskId).Select(x => x.Revision).SingleAsync());
    Assert.Equal(completedEvents, await db.WorkflowTaskActivities.CountAsync(x => x.WorkTaskId == completedTaskId));

    var approvalsBefore = await db.Approvals.CountAsync();
    var materialityApprovalsBefore = await db.MaterialityApprovals.CountAsync();
    var reopened = await EngagementWorkflowTaskService.ReopenAsync(db, managerActor,
      predecessorTaskId, reopenReason, expectedRevision: 5);
    Assert.True(reopened.Succeeded, reopened.Message);
    var reopenedTask = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == predecessorTaskId);
    Assert.Equal(PracticeTimeStates.TaskOpen, reopenedTask.Status);
    Assert.Null(reopenedTask.CompletedAt);
    Assert.Equal(staff.Id, reopenedTask.AssigneeUserId);
    Assert.Equal("Reopened prerequisite", reopenedTask.Title);
    var reopenedBinding = await db.WorkflowTaskBindings.AsNoTracking()
      .SingleAsync(x => x.WorkTaskId == predecessorTaskId);
    Assert.Equal(predecessorBindingId, reopenedBinding.Id);
    Assert.Equal(6, reopenedBinding.Revision);
    Assert.Equal(evidenceIds, await db.WorkflowTaskEvidenceRules.AsNoTracking()
      .Where(x => x.WorkflowTaskBindingId == predecessorBindingId)
      .OrderBy(x => x.Version).Select(x => x.Id).ToArrayAsync());
    var retainedTime = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == timeEntry.Value);
    Assert.Equal(predecessorTaskId, retainedTime.TaskId);
    Assert.Equal(45, retainedTime.DurationMinutes);
    var cancellationEvent = await db.WorkflowTaskActivities.AsNoTracking().SingleAsync(x =>
      x.WorkTaskId == predecessorTaskId && x.Kind == WorkflowTaskActivityKinds.Cancelled);
    Assert.Equal(5, cancellationEvent.TaskRevision);
    var reopeningEvent = await db.WorkflowTaskActivities.AsNoTracking().SingleAsync(x =>
      x.WorkTaskId == predecessorTaskId && x.Kind == WorkflowTaskActivityKinds.Reopened);
    Assert.Equal(manager.Id, reopeningEvent.ActorUserId);
    Assert.Equal(6, reopeningEvent.TaskRevision);
    Assert.Equal(reopenReason, reopeningEvent.Reason);
    Assert.Equal(approvalsBefore, await db.Approvals.CountAsync());
    Assert.Equal(materialityApprovalsBefore, await db.MaterialityApprovals.CountAsync());

    var replay = await EngagementWorkflowTaskService.ReopenAsync(db, managerActor,
      predecessorTaskId, reopenReason, expectedRevision: 5);
    Assert.True(replay.Succeeded, replay.Message);
    var originalCancellationReplay = await EngagementWorkflowTaskService.CancelAsync(db, managerActor,
      predecessorTaskId, "Cancelled before reopening.", expectedRevision: 4);
    Assert.True(originalCancellationReplay.Succeeded, originalCancellationReplay.Message);
    Assert.Equal(PracticeTimeStates.TaskOpen,
      (await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == predecessorTaskId)).Status);
    Assert.Single(await db.WorkflowTaskActivities.AsNoTracking().Where(x =>
      x.WorkTaskId == predecessorTaskId && x.Kind == WorkflowTaskActivityKinds.Cancelled).ToListAsync());
    var differentReason = await EngagementWorkflowTaskService.ReopenAsync(db, managerActor,
      predecessorTaskId, "A different reopening intent.", expectedRevision: 5);
    Assert.Equal(ErrorCodes.IdempotencyConflict, differentReason.ErrorCode);
    var differentActor = await EngagementWorkflowTaskService.ReopenAsync(db, otherManagerActor,
      predecessorTaskId, reopenReason, expectedRevision: 5);
    Assert.Equal(ErrorCodes.IdempotencyConflict, differentActor.ErrorCode);
    Assert.Equal(6, (await db.WorkflowTaskBindings.AsNoTracking()
      .SingleAsync(x => x.WorkTaskId == predecessorTaskId)).Revision);
    Assert.Single(await db.WorkflowTaskActivities.AsNoTracking().Where(x =>
      x.WorkTaskId == predecessorTaskId && x.Kind == WorkflowTaskActivityKinds.Reopened).ToListAsync());
    var reopenedQueue = await WorkflowTaskQueueQuery.GetAsync(db, managerActor,
      new WorkflowTaskQueueRequest("TEAM"));
    Assert.True(reopenedQueue.Succeeded, reopenedQueue.Message);
    Assert.False(Assert.Single(reopenedQueue.Value!.Tasks, x => x.TaskId == predecessorTaskId).CanReopen);

    var dependentBlocked = await EngagementWorkflowTaskService.CompleteAsync(db, managerActor,
      dependentTaskId, expectedRevision: 2);
    Assert.Equal(ErrorCodes.GateBlocked, dependentBlocked.ErrorCode);
    Assert.Equal(1, await db.WorkflowTaskDependencyRevisions.CountAsync(x =>
      x.SuccessorBindingId == dependentBindingId));
    var cancelledAgain = await EngagementWorkflowTaskService.CancelAsync(db, managerActor,
      predecessorTaskId, "The prerequisite was cancelled again.", expectedRevision: 6);
    Assert.True(cancelledAgain.Succeeded, cancelledAgain.Message);
    var cancelledAgainTask = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == predecessorTaskId);
    Assert.Equal(PracticeTimeStates.TaskCancelled, cancelledAgainTask.Status);
    Assert.Null(cancelledAgainTask.CompletedAt);
    var cancelledAgainRevision = await db.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.WorkTaskId == predecessorTaskId).Select(x => x.Revision).SingleAsync();
    Assert.Equal(7, cancelledAgainRevision);
    var activityHistoryBeforeHistoricalReplay = await db.WorkflowTaskActivities.AsNoTracking()
      .Where(x => x.WorkTaskId == predecessorTaskId).OrderBy(x => x.TaskRevision)
      .Select(x => new { x.Id, x.Kind, x.TaskRevision, x.ActorUserId, x.Reason }).ToArrayAsync();
    var evidenceIdsBeforeHistoricalReplay = await db.WorkflowTaskEvidenceRules.AsNoTracking()
      .Where(x => x.WorkflowTaskBindingId == predecessorBindingId)
      .OrderBy(x => x.Version).Select(x => x.Id).ToArrayAsync();
    var timeBeforeHistoricalReplay = await db.TimeEntries.AsNoTracking()
      .Where(x => x.Id == timeEntry.Value)
      .Select(x => new
      {
        x.Id, x.TaskId, x.WorkDate, x.StartMinute, x.DurationMinutes, x.Role, x.Activity,
        x.Phase, x.BillableClassification, x.Narrative, x.Status, x.Revision
      }).SingleAsync();

    var historicalReplay = await EngagementWorkflowTaskService.ReopenAsync(db, managerActor,
      predecessorTaskId, reopenReason, expectedRevision: 5);
    Assert.True(historicalReplay.Succeeded, historicalReplay.Message);
    var afterHistoricalReplay = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == predecessorTaskId);
    Assert.Equal(PracticeTimeStates.TaskCancelled, afterHistoricalReplay.Status);
    Assert.Null(afterHistoricalReplay.CompletedAt);
    Assert.Equal(cancelledAgainRevision, await db.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.WorkTaskId == predecessorTaskId).Select(x => x.Revision).SingleAsync());
    Assert.Equal(activityHistoryBeforeHistoricalReplay,
      await db.WorkflowTaskActivities.AsNoTracking().Where(x => x.WorkTaskId == predecessorTaskId)
        .OrderBy(x => x.TaskRevision)
        .Select(x => new { x.Id, x.Kind, x.TaskRevision, x.ActorUserId, x.Reason }).ToArrayAsync());
    Assert.Equal(evidenceIdsBeforeHistoricalReplay, await db.WorkflowTaskEvidenceRules.AsNoTracking()
      .Where(x => x.WorkflowTaskBindingId == predecessorBindingId)
      .OrderBy(x => x.Version).Select(x => x.Id).ToArrayAsync());
    Assert.Equal(timeBeforeHistoricalReplay, await db.TimeEntries.AsNoTracking()
      .Where(x => x.Id == timeEntry.Value)
      .Select(x => new
      {
        x.Id, x.TaskId, x.WorkDate, x.StartMinute, x.DurationMinutes, x.Role, x.Activity,
        x.Phase, x.BillableClassification, x.Narrative, x.Status, x.Revision
      }).SingleAsync());

    var managerGrant = await db.RoleGrants.SingleAsync(x => x.UserId == manager.Id &&
      x.ClientId == clientA && x.EngagementId == engagementA);
    managerGrant.RevokedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    var unauthorizedHistoricalReplay = await EngagementWorkflowTaskService.ReopenAsync(db, managerActor,
      predecessorTaskId, reopenReason, expectedRevision: 5);
    Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedHistoricalReplay.ErrorCode);
    var afterUnauthorizedReplay = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == predecessorTaskId);
    Assert.Equal(PracticeTimeStates.TaskCancelled, afterUnauthorizedReplay.Status);
    Assert.Equal(cancelledAgainRevision, await db.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.WorkTaskId == predecessorTaskId).Select(x => x.Revision).SingleAsync());
    Assert.Equal(activityHistoryBeforeHistoricalReplay.Length,
      await db.WorkflowTaskActivities.AsNoTracking().CountAsync(x => x.WorkTaskId == predecessorTaskId));

    var reopenedAfterLaterCancellation = await EngagementWorkflowTaskService.ReopenAsync(db, otherManagerActor,
      predecessorTaskId, "Resume after the second cancellation.", expectedRevision: 7);
    Assert.True(reopenedAfterLaterCancellation.Succeeded, reopenedAfterLaterCancellation.Message);
    var predecessorCompleted = await EngagementWorkflowTaskService.CompleteAsync(db, otherManagerActor,
      predecessorTaskId, expectedRevision: 8);
    Assert.True(predecessorCompleted.Succeeded, predecessorCompleted.Message);
    var dependentCompleted = await EngagementWorkflowTaskService.CompleteAsync(db, otherManagerActor,
      dependentTaskId, expectedRevision: 2);
    Assert.True(dependentCompleted.Succeeded, dependentCompleted.Message);

    var migrations = db.Database.GetMigrations().ToArray();
    var reopeningMigrationIndex = Array.FindIndex(migrations,
      x => x.EndsWith("_WorkflowTaskReopening", StringComparison.Ordinal));
    Assert.True(reopeningMigrationIndex > 0);
    var reopeningMigrationId = migrations[reopeningMigrationIndex];
    var beforeReopeningMigration = migrations[reopeningMigrationIndex - 1];
    var reopeningDowngrade = await Assert.ThrowsAnyAsync<Exception>(() =>
      db.GetService<IMigrator>().MigrateAsync(beforeReopeningMigration));
    var reopeningDowngradeError = Assert.IsType<PostgresException>(reopeningDowngrade.GetBaseException());
    Assert.Equal("55000", reopeningDowngradeError.SqlState);
    Assert.Contains(reopeningMigrationId, await db.Database.GetAppliedMigrationsAsync());
    Assert.Contains(reopeningEvent.Id, await db.WorkflowTaskActivities.AsNoTracking()
      .Select(x => x.Id).ToListAsync());
  }

  [Fact]
  public async Task DatabaseRejectsWorkflowBindingWhoseScopeDiffersFromItsExistingWorkTask()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var user = User(firmId, "Manager");
    var actor = Actor(user, "Manager");
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
      seed.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
      seed.PracticeClients.Add(new PracticeClient
        { Id = clientId, FirmId = firmId, LegalName = "Bound Client", CreatedAt = DateTimeOffset.UtcNow });
      seed.Engagements.Add(Engagement(engagementId, firmId, clientId));
      seed.Users.Add(user);
      await seed.SaveChangesAsync();
      seed.RoleGrants.Add(Grant(firmId, user, "Manager"));
      await seed.SaveChangesAsync();
    }

    Guid taskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PracticeTimeService.CreateTaskAsync(db, actor,
        new CreateTaskRequest("Existing standalone practice task", clientId, engagementId));
      Assert.True(created.Succeeded, created.Message);
      taskId = created.Value;
      Assert.False(await db.WorkflowTaskBindings.AnyAsync(x => x.WorkTaskId == taskId));
    }

    await using var bad = new AuditSphereDbContext(pg.Options);
    bad.WorkflowTaskBindings.Add(new WorkflowTaskBinding
    {
      Id = Guid.NewGuid(), FirmId = firmId, ClientId = Guid.NewGuid(), EngagementId = engagementId,
      WorkTaskId = taskId, Phase = WorkflowTaskPhases.AuditPlanning, Ordinal = 2,
      SourceKey = "MANUAL:scope-mismatch", CreationRequestDigest = new string('a', 64),
      Generation = 1, DueDatePolicy = "MANUAL",
      AssignmentSource = "MANUAL", CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = user.Id, Revision = 1
    });
    await Assert.ThrowsAsync<DbUpdateException>(() => bad.SaveChangesAsync());
  }

  private static readonly string[] SummaryViewOrder =
    ["MY_TASKS", "DUE_TODAY", "OVERDUE", "WAITING_FOR_CLIENT", "UNDER_REVIEW", "CHANGES_REQUIRED", "UPCOMING"];

  [Fact]
  public async Task DueTodaySummaryCountsTheCompleteAuthorizedPopulationRatherThanTheLoadedPage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var manager = User(firmId, "Manager");
    var managerActor = Actor(manager, "Manager");
    await SeedQueueScopeAsync(pg, firmId, [(clientId, engagementId)], [manager],
      () => [Grant(firmId, manager, "Manager")]);

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var taskIds = new List<Guid>();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      for (var ordinal = 1; ordinal <= 101; ordinal++)
        taskIds.Add(await CreateQueueTaskAsync(db, managerActor, engagementId, $"Due today {ordinal}", ordinal, today));
      var assigned = await EngagementWorkflowTaskService.AssignAsync(db, managerActor, taskIds[0], manager.Id, 1);
      Assert.True(assigned.Succeeded, assigned.Message);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var before = await QueueStateFingerprintAsync(db, firmId);

      var first = await WorkflowTaskQueueQuery.GetAsync(db, managerActor,
        new WorkflowTaskQueueRequest("DUE_TODAY", PageSize: 1, IncludeSummary: true));
      Assert.True(first.Succeeded, first.Message);
      var firstRow = Assert.Single(first.Value!.Tasks);
      Assert.True(first.Value.HasMore);
      Assert.Equal("DUE_TODAY", first.Value.View);
      var summary = Assert.IsType<WorkflowTaskQueueSummary>(first.Value.Summary);
      Assert.Equal(today, summary.AsOfDateUtc);
      Assert.Equal(SummaryViewOrder, summary.Counts.Select(x => x.View));
      Assert.Equal("101", SummaryCount(summary, "DUE_TODAY"));
      Assert.Equal("1", SummaryCount(summary, "MY_TASKS"));
      foreach (var empty in new[] { "OVERDUE", "WAITING_FOR_CLIENT", "UNDER_REVIEW", "CHANGES_REQUIRED", "UPCOMING" })
        Assert.Equal("0", SummaryCount(summary, empty));

      var cursor = Assert.IsType<WorkflowTaskQueueCursor>(first.Value.NextCursor);
      var second = await WorkflowTaskQueueQuery.GetAsync(db, managerActor,
        new WorkflowTaskQueueRequest("DUE_TODAY", cursor.DueDate, cursor.TaskId, 1, IncludeSummary: true));
      Assert.True(second.Succeeded, second.Message);
      Assert.NotEqual(firstRow.TaskId, Assert.Single(second.Value!.Tasks).TaskId);
      Assert.Equal(summary.Counts, second.Value.Summary!.Counts);

      var owned = await WorkflowTaskQueueQuery.GetAsync(db, managerActor,
        new WorkflowTaskQueueRequest("DUE_TODAY", AssigneeUserId: manager.Id, IncludeSummary: true));
      Assert.True(owned.Succeeded, owned.Message);
      Assert.Equal(taskIds[0], Assert.Single(owned.Value!.Tasks).TaskId);
      Assert.Equal(summary.Counts, owned.Value.Summary!.Counts);

      var unassigned = await WorkflowTaskQueueQuery.GetAsync(db, managerActor,
        new WorkflowTaskQueueRequest("TEAM", PageSize: 100, UnassignedOnly: true, IncludeSummary: true));
      Assert.True(unassigned.Succeeded, unassigned.Message);
      Assert.Equal(100, unassigned.Value!.Tasks.Count);
      Assert.False(unassigned.Value.HasMore);
      Assert.Equal(summary.Counts, unassigned.Value.Summary!.Counts);

      var emptyPage = await WorkflowTaskQueueQuery.GetAsync(db, managerActor,
        new WorkflowTaskQueueRequest("COMPLETED", IncludeSummary: true));
      Assert.True(emptyPage.Succeeded, emptyPage.Message);
      Assert.Empty(emptyPage.Value!.Tasks);
      Assert.Equal(summary.Counts, emptyPage.Value.Summary!.Counts);

      var withoutSummary = await WorkflowTaskQueueQuery.GetAsync(db, managerActor,
        new WorkflowTaskQueueRequest("DUE_TODAY", PageSize: 1));
      Assert.True(withoutSummary.Succeeded, withoutSummary.Message);
      Assert.Single(withoutSummary.Value!.Tasks);
      Assert.Null(withoutSummary.Value.Summary);

      Assert.Equal(before, await QueueStateFingerprintAsync(db, firmId));
    }
  }

  [Fact]
  public async Task DateViewsUseTheUtcCalendarDateAndSummaryStaysScopedDeniedAndReadOnly()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    var clientA = Guid.NewGuid();
    var clientB = Guid.NewGuid();
    var engagementA = Guid.NewGuid();
    var engagementB = Guid.NewGuid();
    var manager = User(firmId, "Manager");
    var staff = User(firmId, "Staff");
    var clientUser = User(firmId, "ClientUser");
    clientUser.UserKind = "Client";
    var expiredUser = User(firmId, "Expired");
    var revokedUser = User(firmId, "Revoked");
    var managerActor = Actor(manager, "Manager");
    var staffActor = Actor(staff, "Staff");
    await SeedQueueScopeAsync(pg, firmId, [(clientA, engagementA), (clientB, engagementB)],
      [manager, staff, clientUser, expiredUser, revokedUser], () =>
      {
        var expired = Grant(firmId, expiredUser, "Staff", clientA, engagementA);
        expired.GrantedAt = DateTimeOffset.UtcNow.AddDays(-30);
        expired.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        var revoked = Grant(firmId, revokedUser, "Staff", clientA, engagementA);
        revoked.RevokedAt = DateTimeOffset.UtcNow.AddHours(-1);
        return
        [
          Grant(firmId, manager, "Manager"), Grant(firmId, staff, "Staff", clientA, engagementA),
          Grant(firmId, clientUser, "Staff", clientA, engagementA), expired, revoked
        ];
      });

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    Guid yesterdayTask, todayTask, plusOneTask, plusSevenTask, plusEightTask, undatedTask, completedTask, cancelledTask;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      yesterdayTask = await CreateQueueTaskAsync(db, managerActor, engagementA, "Overdue client request", 1, today.AddDays(-1));
      todayTask = await CreateQueueTaskAsync(db, managerActor, engagementA, "Due today", 2, today);
      plusOneTask = await CreateQueueTaskAsync(db, managerActor, engagementA, "Due tomorrow", 3, today.AddDays(1));
      plusSevenTask = await CreateQueueTaskAsync(db, managerActor, engagementA, "Due in seven days", 4, today.AddDays(7));
      plusEightTask = await CreateQueueTaskAsync(db, managerActor, engagementA, "Due in eight days", 5, today.AddDays(8));
      undatedTask = await CreateQueueTaskAsync(db, managerActor, engagementA, "No due date", 6, null);
      completedTask = await CreateQueueTaskAsync(db, managerActor, engagementA, "Completed today", 7, today);
      cancelledTask = await CreateQueueTaskAsync(db, managerActor, engagementA, "Cancelled today", 8, today);
      var completed = await EngagementWorkflowTaskService.CompleteAsync(db, managerActor, completedTask, 1);
      Assert.True(completed.Succeeded, completed.Message);
      var assigned = await EngagementWorkflowTaskService.AssignAsync(db, managerActor, todayTask, staff.Id, 1);
      Assert.True(assigned.Succeeded, assigned.Message);
    }

    // Arrange a sent PBC request for the overdue task and a cancelled task directly; neither is a queue mutation.
    await using (var arrange = new AuditSphereDbContext(pg.Options))
    {
      var pbcId = Guid.NewGuid();
      arrange.PbcRequests.Add(new PbcRequest
      {
        Id = pbcId, FirmId = firmId, ClientId = clientA, EngagementId = engagementA,
        Objective = "Upload the signed bank confirmations.", EntityScope = "TEST ENTITY",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Area = "Cash", RequestedFormat = "PDF",
        ClientOwnerUserId = clientUser.Id, FirmOwnerUserId = staff.Id, ReviewerUserId = manager.Id,
        DueDate = "2026-12-31", Confidentiality = "Confidential", AcceptanceCriteria = "Signed and readable.",
        State = PbcStates.Sent, Revision = 2, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = staff.Id,
        UpdatedAt = DateTimeOffset.UtcNow
      });
      await arrange.SaveChangesAsync();
      var binding = await arrange.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == yesterdayTask);
      binding.PbcRequestId = pbcId;
      binding.Revision++;
      (await arrange.WorkTasks.SingleAsync(x => x.Id == cancelledTask)).Status = PracticeTimeStates.TaskCancelled;
      await arrange.SaveChangesAsync();
    }

    WorkflowTaskQueueSummary scopedSummary;
    Dictionary<string, Guid[]> scopedRows;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var before = await QueueStateFingerprintAsync(db, firmId);
      scopedRows = await QueueTaskIdsAsync(db, staffActor);
      Assert.Equal([todayTask], scopedRows["DUE_TODAY"]);
      Assert.Equal([plusOneTask, plusSevenTask], scopedRows["UPCOMING"]);
      Assert.Equal([yesterdayTask], scopedRows["OVERDUE"]);
      Assert.Equal([yesterdayTask], scopedRows["WAITING_FOR_CLIENT"]);
      Assert.Equal([todayTask], scopedRows["MY_TASKS"]);
      Assert.DoesNotContain(cancelledTask, scopedRows.Values.SelectMany(x => x));
      Assert.Contains(plusEightTask, scopedRows["TEAM"]);
      Assert.Contains(undatedTask, scopedRows["TEAM"]);
      Assert.Equal([completedTask], scopedRows["COMPLETED"]);

      var result = await WorkflowTaskQueueQuery.GetAsync(db, staffActor,
        new WorkflowTaskQueueRequest("TEAM", IncludeSummary: true));
      Assert.True(result.Succeeded, result.Message);
      scopedSummary = result.Value!.Summary!;
      Assert.Equal(today, scopedSummary.AsOfDateUtc);
      // An overdue task waiting for the client appears once on each card; no grand total is produced.
      Assert.Equal(SummaryViewOrder, scopedSummary.Counts.Select(x => x.View));
      Assert.Equal(["1", "1", "1", "1", "0", "0", "2"], scopedSummary.Counts.Select(x => x.Count));

      foreach (var invalid in new[]
        {
          new WorkflowTaskQueueRequest("NOT_A_VIEW", IncludeSummary: true),
          new WorkflowTaskQueueRequest("DUE_TODAY", AfterDueDate: today, IncludeSummary: true),
          new WorkflowTaskQueueRequest("UPCOMING", PageSize: 0, IncludeSummary: true),
          new WorkflowTaskQueueRequest("DUE_TODAY", AssigneeUserId: staff.Id, UnassignedOnly: true, IncludeSummary: true),
          new WorkflowTaskQueueRequest("MY_TASKS", AssigneeUserId: manager.Id, IncludeSummary: true)
        })
      {
        var rejected = await WorkflowTaskQueueQuery.GetAsync(db, staffActor, invalid);
        Assert.False(rejected.Succeeded);
        Assert.Equal("workflow.queue-invalid", rejected.ErrorCode);
        Assert.Equal("Choose a supported queue view, page size from 1 to 100, and a valid task cursor.", rejected.Message);
        Assert.Null(rejected.Value);
      }

      foreach (var denied in new[] { Actor(clientUser, "ClientUser"), Actor(expiredUser, "Staff"), Actor(revokedUser, "Staff") })
      foreach (var view in new[] { "DUE_TODAY", "UPCOMING", "TEAM" })
      {
        var refused = await WorkflowTaskQueueQuery.GetAsync(db, denied,
          new WorkflowTaskQueueRequest(view, IncludeSummary: true));
        Assert.False(refused.Succeeded);
        Assert.Equal(ErrorCodes.ScopeDenied, refused.ErrorCode);
        Assert.Null(refused.Value);
      }

      Assert.Equal(before, await QueueStateFingerprintAsync(db, firmId));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await CreateQueueTaskAsync(db, managerActor, engagementB, "Sibling due today", 1, today);
      await CreateQueueTaskAsync(db, managerActor, engagementB, "Sibling overdue", 2, today.AddDays(-1));
      await CreateQueueTaskAsync(db, managerActor, engagementB, "Sibling upcoming", 3, today.AddDays(1));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingRows = await QueueTaskIdsAsync(db, staffActor);
      Assert.Equal(scopedRows.Keys.Order(), siblingRows.Keys.Order());
      foreach (var (view, ids) in scopedRows)
        Assert.Equal(ids, siblingRows[view]);
      var siblingSummary = await WorkflowTaskQueueQuery.GetAsync(db, staffActor,
        new WorkflowTaskQueueRequest("TEAM", IncludeSummary: true));
      Assert.True(siblingSummary.Succeeded, siblingSummary.Message);
      Assert.Equal(scopedSummary.AsOfDateUtc, siblingSummary.Value!.Summary!.AsOfDateUtc);
      Assert.Equal(scopedSummary.Counts, siblingSummary.Value.Summary.Counts);

      var firmWide = await WorkflowTaskQueueQuery.GetAsync(db, managerActor,
        new WorkflowTaskQueueRequest("DUE_TODAY", IncludeSummary: true));
      Assert.True(firmWide.Succeeded, firmWide.Message);
      Assert.Equal(2, firmWide.Value!.Tasks.Count);
      Assert.Equal("2", SummaryCount(firmWide.Value.Summary!, "DUE_TODAY"));
      Assert.Equal("2", SummaryCount(firmWide.Value.Summary!, "OVERDUE"));
    }
  }

  [Fact]
  public async Task QueueSummaryRefusesGrantExpiryAtSnapshotCommitEvenWhenAnotherEngagementGrantRemains()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    var clientA = Guid.NewGuid();
    var clientB = Guid.NewGuid();
    var engagementA = Guid.NewGuid();
    var engagementB = Guid.NewGuid();
    var manager = User(firmId, "Manager");
    var staff = User(firmId, "Staff");
    var staffActor = Actor(staff, "Staff");
    var grantA = Grant(firmId, staff, "Staff", clientA, engagementA);
    grantA.ExpiresAt = DateTimeOffset.UtcNow.AddDays(1);
    var grantB = Grant(firmId, staff, "Staff", clientB, engagementB);
    await SeedQueueScopeAsync(pg, firmId, [(clientA, engagementA), (clientB, engagementB)],
      [manager, staff], () => [Grant(firmId, manager, "Manager"), grantA, grantB]);

    Guid taskA;
    string before;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      taskA = await CreateQueueTaskAsync(db, Actor(manager, "Manager"), engagementA,
        "Protected engagement A task", 1, DateOnly.FromDateTime(DateTime.UtcNow));
      var assigned = await EngagementWorkflowTaskService.AssignAsync(db, Actor(manager, "Manager"), taskA, staff.Id, 1);
      Assert.True(assigned.Succeeded, assigned.Message);
      var positive = await WorkflowTaskQueueQuery.GetAsync(db, staffActor,
        new WorkflowTaskQueueRequest("TEAM", IncludeSummary: true));
      Assert.True(positive.Succeeded, positive.Message);
      Assert.Equal(taskA, Assert.Single(positive.Value!.Tasks).TaskId);
      Assert.Equal("1", SummaryCount(positive.Value.Summary!, "MY_TASKS"));
      Assert.Equal("1", SummaryCount(positive.Value.Summary!, "DUE_TODAY"));
      before = await QueueStateFingerprintAsync(db, firmId);
    }

    var cutoffs = new QueueGrantCutoffObserver();
    DateTimeOffset storedExpiry = default;
    var barrier = new ExpireGrantOnQueueSnapshotCommit(async ct =>
    {
      var capturedCutoff = Assert.Single(cutoffs.Values);
      var callbackUtcNow = DateTimeOffset.UtcNow;
      Assert.True(callbackUtcNow > capturedCutoff);
      // Separate context, after the owned read committed. No revocation or session-epoch workaround.
      await using var update = new AuditSphereDbContext(pg.Options);
      Assert.Equal(1, await update.RoleGrants.Where(x => x.Id == grantA.Id)
        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAt, callbackUtcNow), ct));
      storedExpiry = (await update.RoleGrants.AsNoTracking().SingleAsync(x => x.Id == grantA.Id, ct)).ExpiresAt!.Value;
      // Assert the persisted PostgreSQL timestamp, including its precision, crosses the original cutoff.
      Assert.True(storedExpiry > capturedCutoff);
      Assert.True(storedExpiry <= DateTimeOffset.UtcNow);
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(cutoffs, barrier).Options;
    await using var queryDb = new AuditSphereDbContext(options);
    var result = await WorkflowTaskQueueQuery.GetAsync(queryDb, staffActor,
      new WorkflowTaskQueueRequest("TEAM", IncludeSummary: true));

    Assert.Equal(1, barrier.CommitCount);
    Assert.Equal(2, cutoffs.Values.Count);
    await using (var control = new AuditSphereDbContext(pg.Options))
    {
      var actual = await control.RoleGrants.AsNoTracking().Where(x => x.UserId == staff.Id).ToListAsync();
      Assert.Equal(2, actual.Count);
      Assert.All(actual, x => Assert.Null(x.RevokedAt));
      Assert.Equal(storedExpiry, Assert.Single(actual, x => x.Id == grantA.Id).ExpiresAt);
      Assert.Null(Assert.Single(actual, x => x.Id == grantB.Id).ExpiresAt);
      Assert.Equal(staff.SessionEpoch, (await control.Users.AsNoTracking().SingleAsync(x => x.Id == staff.Id)).SessionEpoch);
      Assert.True((await AuthorizationDecision.AuthorizeAsync(control, staffActor,
        new AuthorizationRequest(firmId, RequiredRoles: ["Staff"], InternalOnly: true))).Succeeded);
      Assert.False((await AuthorizationDecision.AuthorizeAsync(control, staffActor,
        new AuthorizationRequest(firmId, ClientId: clientA, EngagementId: engagementA,
          RequiredRoles: ["Staff"], InternalOnly: true))).Succeeded);
      Assert.True((await AuthorizationDecision.AuthorizeAsync(control, staffActor,
        new AuthorizationRequest(firmId, ClientId: clientB, EngagementId: engagementB,
          RequiredRoles: ["Staff"], InternalOnly: true))).Succeeded);
      Assert.Equal(before, await QueueStateFingerprintAsync(control, firmId));
    }

    Assert.False(result.Succeeded,
      $"Expired A released: rows={result.Value?.Tasks.Count}, dueToday={result.Value?.Summary?.Counts.Single(x => x.View == "DUE_TODAY").Count}; " +
      $"initialCutoff={cutoffs.Values[0]:O}, expiry={storedExpiry:O}, finalCutoff={cutoffs.Values[1]:O}");
    Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    Assert.Null(result.Value); // Neither rows nor summary counts may leave the query after scope loss.
    Assert.True(cutoffs.Values[1] >= storedExpiry);
  }

  private sealed class ExpireGrantOnQueueSnapshotCommit(Func<CancellationToken, Task> expire) : DbTransactionInterceptor
  {
    public int CommitCount { get; private set; }

    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
      CancellationToken cancellationToken = default)
    {
      Assert.Equal(0, CommitCount++);
      await expire(cancellationToken);
    }
  }

  private sealed class QueueGrantCutoffObserver : DbCommandInterceptor
  {
    public List<DateTimeOffset> Values { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      // Observe the effective-grant predicate, without relying on EF's generated parameter name.
      var match = Regex.Match(command.CommandText, @"\bexpires_at""? > (?<parameter>@[A-Za-z0-9_]+)");
      if (match.Success)
      {
        var parameterName = match.Groups["parameter"].Value.TrimStart('@');
        var parameter = Assert.Single(command.Parameters.Cast<DbParameter>(),
          x => x.ParameterName.TrimStart('@') == parameterName);
        Values.Add(Assert.IsType<DateTimeOffset>(parameter.Value));
      }
      return ValueTask.FromResult(result);
    }
  }

  private static async Task SeedQueueScopeAsync(PgTestSchema pg, Guid firmId,
    (Guid ClientId, Guid EngagementId)[] scopes, AppUser[] users, Func<RoleGrant[]> grants)
  {
    await using var seed = new AuditSphereDbContext(pg.Options);
    seed.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    foreach (var (clientId, engagementId) in scopes)
    {
      seed.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
      seed.PracticeClients.Add(new PracticeClient
        { Id = clientId, FirmId = firmId, LegalName = $"Queue Client {clientId:N}", CreatedAt = DateTimeOffset.UtcNow });
      seed.Engagements.Add(Engagement(engagementId, firmId, clientId));
    }
    seed.Users.AddRange(users);
    await seed.SaveChangesAsync();
    seed.RoleGrants.AddRange(grants());
    await seed.SaveChangesAsync();
  }

  private static async Task<Guid> CreateQueueTaskAsync(AuditSphereDbContext db, ActorContext actor,
    Guid engagementId, string title, int ordinal, DateOnly? dueDate)
  {
    var created = await EngagementWorkflowTaskService.CreateAsync(db, actor, engagementId,
      new WorkflowTaskCreateRequest(title, WorkflowTaskPhases.AuditFieldwork, ordinal, dueDate,
        IdempotencyKey: Guid.NewGuid()));
    Assert.True(created.Succeeded, created.Message);
    return created.Value;
  }

  private static async Task<Dictionary<string, Guid[]>> QueueTaskIdsAsync(AuditSphereDbContext db, ActorContext actor)
  {
    var rows = new Dictionary<string, Guid[]>();
    foreach (var view in SummaryViewOrder.Concat(["TEAM", "COMPLETED"]))
    {
      var page = await WorkflowTaskQueueQuery.GetAsync(db, actor, new WorkflowTaskQueueRequest(view, PageSize: 100));
      Assert.True(page.Succeeded, page.Message);
      Assert.False(page.Value!.HasMore);
      rows[view] = page.Value.Tasks.Select(x => x.TaskId).ToArray();
    }
    return rows;
  }

  private static string SummaryCount(WorkflowTaskQueueSummary summary, string view) =>
    Assert.Single(summary.Counts, x => x.View == view).Count;

  private static async Task<string> QueueStateFingerprintAsync(AuditSphereDbContext db, Guid firmId)
  {
    var tasks = await db.WorkTasks.AsNoTracking().Where(x => x.FirmId == firmId)
      .Select(x => new { x.Id, x.Status, x.DueDate, x.AssigneeUserId }).ToListAsync();
    var bindings = await db.WorkflowTaskBindings.AsNoTracking().Where(x => x.FirmId == firmId)
      .Select(x => new { x.WorkTaskId, x.Revision, x.Generation, x.PbcRequestId }).ToListAsync();
    var engagements = await db.Engagements.AsNoTracking().Where(x => x.FirmId == firmId)
      .Select(x => new { x.Id, x.Generation, x.Status }).ToListAsync();
    return string.Join("|",
      string.Join(";", tasks.OrderBy(x => x.Id).Select(x => $"{x.Id}:{x.Status}:{x.DueDate}:{x.AssigneeUserId}")),
      string.Join(";", bindings.OrderBy(x => x.WorkTaskId).Select(x => $"{x.WorkTaskId}:{x.Revision}:{x.Generation}:{x.PbcRequestId}")),
      string.Join(";", engagements.OrderBy(x => x.Id).Select(x => $"{x.Id}:{x.Generation}:{x.Status}")),
      await db.WorkflowTaskActivities.CountAsync(x => x.FirmId == firmId),
      await db.MaterialityApprovals.CountAsync(),
      await db.AuditProcedureReviews.CountAsync(),
      await db.AuditProcedureResults.CountAsync(),
      await db.FinancialPackageReviewDecisions.CountAsync());
  }

  private static Engagement Engagement(Guid id, Guid firmId, Guid clientId) => new()
  {
    Id = id, FirmId = firmId, PracticeClientId = clientId,
    ServiceRoute = "FinancialStatementAudit", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
    ServiceProfileId = "test", CreatedAt = DateTimeOffset.UtcNow
  };

  private static AppUser User(Guid firmId, string label) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId,
    Subject = $"sub-{label}-{Guid.NewGuid():N}", TenantId = "tenant-test",
    Email = $"{label.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test",
    DisplayName = label, UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role,
    Guid? clientId = null, Guid? engagementId = null) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = clientId, EngagementId = engagementId, GrantedAt = DateTimeOffset.UtcNow,
    GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, params string[] roles) =>
    new(user.Id, user.FirmId, user.SessionEpoch, roles);
}
