using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class EngagementWorkflowTaskTests
{
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
      seed.Users.AddRange(manager, staffA, staffB);
      await seed.SaveChangesAsync();
      seed.RoleGrants.AddRange(
        Grant(firmId, manager, "Manager"),
        Grant(firmId, staffA, "Staff", clientA, engagementA),
        Grant(firmId, staffB, "Staff", clientB, engagementB));
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
