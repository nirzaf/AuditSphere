using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
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
      Assert.True(createdA.Succeeded, createdA.Message);
      Assert.True(createdB.Succeeded, createdB.Message);
      taskA = createdA.Value;
      taskB = createdB.Value;
      Assert.NotEqual(taskA, taskB);

      var replay = await EngagementWorkflowTaskService.CreateAsync(db, managerActor, engagementA, requestA);
      Assert.True(replay.Succeeded, replay.Message);
      Assert.Equal(taskA, replay.Value);
      var conflictingReplay = await EngagementWorkflowTaskService.CreateAsync(db, managerActor, engagementA,
        requestA with { Title = "Different content" });
      Assert.False(conflictingReplay.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, conflictingReplay.ErrorCode);
      Assert.Equal(2, await db.WorkflowTaskBindings.CountAsync());

      var assignment = await EngagementWorkflowTaskService.AssignAsync(db, managerActor, taskA, staffA.Id, 1);
      Assert.True(assignment.Succeeded, assignment.Message);
      var stale = await EngagementWorkflowTaskService.ReassignAsync(db, managerActor, taskA, staffB.Id, 1);
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);

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
      var task = Assert.Single(own.Value!.Tasks);
      Assert.Equal(taskA, task.Id);
      Assert.Equal(2, task.Revision);
      Assert.Equal(staffA.Id, task.AssigneeUserId);
      Assert.Equal(StaffingLevels.AuditManager, task.AssignedRole);

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

      var firstEngagement = await db.Engagements.SingleAsync(x => x.Id == engagementA);
      Assert.Equal(1, firstEngagement.Generation);
      Assert.Equal("Draft", firstEngagement.Status);
      Assert.Equal(engagementB, (await db.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == taskB)).EngagementId);
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
