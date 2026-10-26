using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class PbcWorkflowTaskTests
{
  [Fact]
  public async Task WorkflowTaskPbcLinkRejectsCrossFirmScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var first = await PbcSeed.SeedAsync(pg);
    var second = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(first.Staff, "Staff");
    Guid foreignPbcRequestId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PbcService.CreateRequestAsync(db, PbcSeed.Actor(second.Staff, "Staff"),
        new CreatePbcRequestRequest(second.EngagementId, "Foreign request", "TEST ENTITY", "2026-01-01",
          "2026-12-31", "Cash", "PDF", string.Empty, second.Client.Id, second.Staff.Id, second.Reviewer.Id,
          "2027-01-31", "Confidential", "Signed and readable."));
      Assert.True(created.Succeeded, created.Message);
      foreignPbcRequestId = created.Value;
    }

    Guid taskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var task = await PracticeTimeService.CreateTaskAsync(db, actor,
        new CreateTaskRequest("Foreign PBC boundary check", first.ClientId, first.EngagementId));
      Assert.True(task.Succeeded, task.Message);
      taskId = task.Value;
    }

    await using var invalid = new AuditSphereDbContext(pg.Options);
    invalid.WorkflowTaskBindings.Add(new WorkflowTaskBinding
    {
      Id = Guid.NewGuid(), FirmId = first.FirmId, ClientId = first.ClientId, EngagementId = first.EngagementId,
      WorkTaskId = taskId, PbcRequestId = foreignPbcRequestId, Phase = WorkflowTaskPhases.AuditFieldwork,
      Ordinal = 1, SourceKey = "PBC-FOLLOW-UP:foreign", CreationRequestDigest = new string('a', 64),
      Generation = 1, DueDatePolicy = "FIXED", AssignmentSource = "AUTOMATION",
      CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = first.Staff.Id, Revision = 1
    });
    await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
  }

  [Fact]
  public async Task EnabledWorkflowCreatesOneScopedFollowUpPerSentPbc_AndCompletionWaitsForAcceptance()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    Guid firstRequestId;
    Guid firstTaskId;
    Guid secondRequestId;
    Guid secondTaskId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var first = await CreateAndSendAsync(db, staff, fixture, createWorkflowTask: true);
      Assert.True(first.Succeeded, first.Message);
      firstRequestId = first.Value;
      var firstBinding = await db.WorkflowTaskBindings.SingleAsync(x => x.PbcRequestId == firstRequestId);
      firstTaskId = firstBinding.WorkTaskId;
      var firstTask = await db.WorkTasks.SingleAsync(x => x.Id == firstTaskId);
      Assert.Equal(WorkflowTaskPhases.AuditFieldwork, firstBinding.Phase);
      Assert.Equal(new DateOnly(2027, 1, 31), firstTask.DueDate);
      Assert.Equal(fixture.Staff.Id, firstTask.AssigneeUserId);

      var replay = await PbcService.SendRequestAsync(db, staff, firstRequestId, 2,
        "https://audit.example.test/", createWorkflowTask: true);
      Assert.False(replay.Succeeded);
      Assert.Equal("pbc.transition", replay.ErrorCode);

      var second = await CreateAndSendAsync(db, staff, fixture, createWorkflowTask: true);
      Assert.True(second.Succeeded, second.Message);
      secondRequestId = second.Value;
      secondTaskId = (await db.WorkflowTaskBindings.SingleAsync(x => x.PbcRequestId == secondRequestId)).WorkTaskId;
      Assert.NotEqual(firstRequestId, secondRequestId);
      Assert.NotEqual(firstTaskId, secondTaskId);
      Assert.Equal(2, await db.WorkflowTaskBindings.CountAsync(x => x.PbcRequestId != null));
      Assert.Equal(2, await db.WorkflowTaskBindings.CountAsync(x => x.SourceKey.StartsWith("PBC-FOLLOW-UP:")));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var waiting = await WorkflowTaskQueueQuery.GetAsync(db, staff,
        new WorkflowTaskQueueRequest("WAITING_FOR_CLIENT"));
      Assert.True(waiting.Succeeded, waiting.Message);
      Assert.Equal(2, waiting.Value!.Tasks.Count);
      var row = Assert.Single(waiting.Value.Tasks, x => x.PbcRequestId == firstRequestId);
      Assert.Equal(PbcStates.Sent, row.PbcState);
      Assert.False(row.CanComplete);

      var blocked = await PracticeTimeService.CompleteTaskAsync(db, staff, firstTaskId);
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
      Assert.Equal(PracticeTimeStates.TaskInProgress,
        (await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == firstTaskId)).Status);

      // Deliberately seed an inconsistent historical completion, bypassing the command, to verify the read
      // never trusts the task status after linked source evidence is missing or unaccepted.
      var task = await db.WorkTasks.SingleAsync(x => x.Id == firstTaskId);
      task.Status = PracticeTimeStates.TaskCompleted;
      task.CompletedAt = DateTimeOffset.UtcNow;
      await db.SaveChangesAsync();
      var stale = await EngagementProgressQuery.GetAsync(db, staff, fixture.EngagementId);
      Assert.True(stale.Succeeded, stale.Message);
      var staleStep = Assert.Single(stale.Value!.Steps, step => step.TaskId == firstTaskId);
      Assert.Equal(EngagementProgressStepStates.Blocked, staleStep.State);
      Assert.Contains("evidence", staleStep.Condition!, StringComparison.OrdinalIgnoreCase);
      task.Status = PracticeTimeStates.TaskInProgress;
      task.CompletedAt = null;
      await db.SaveChangesAsync();
    }

    // Arrange the accepted source-record state directly to isolate the linked task's server-side completion gate.
    await using (var arrange = new AuditSphereDbContext(pg.Options))
    {
      var request = await arrange.PbcRequests.SingleAsync(x => x.Id == firstRequestId);
      request.State = PbcStates.Accepted;
      request.AcceptedAt = DateTimeOffset.UtcNow;
      request.AcceptedByUserId = fixture.Reviewer.Id;
      await arrange.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var team = await WorkflowTaskQueueQuery.GetAsync(db, staff, new WorkflowTaskQueueRequest("TEAM"));
      Assert.True(team.Succeeded, team.Message);
      var accepted = Assert.Single(team.Value!.Tasks, x => x.PbcRequestId == firstRequestId);
      Assert.Equal(PbcStates.Accepted, accepted.PbcState);
      Assert.True(accepted.CanComplete);
      Assert.True((await PracticeTimeService.CompleteTaskAsync(db, staff, firstTaskId)).Succeeded);
      Assert.Equal(PracticeTimeStates.TaskCompleted,
        (await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == firstTaskId)).Status);
      Assert.Equal(PracticeTimeStates.TaskInProgress,
        (await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == secondTaskId)).Status);
    }
  }

  [Fact]
  public async Task DisabledWorkflowDoesNotCreatePbcFollowUpTask()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    await using var db = new AuditSphereDbContext(pg.Options);

    var sent = await CreateAndSendAsync(db, staff, fixture, createWorkflowTask: false);

    Assert.True(sent.Succeeded, sent.Message);
    Assert.Equal(PbcStates.Sent,
      (await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == sent.Value)).State);
    Assert.False(await db.WorkflowTaskBindings.AnyAsync(x => x.PbcRequestId == sent.Value));
  }

  private static async Task<AuditSphereOps.Domain.Shared.CommandResult<Guid>> CreateAndSendAsync(
    AuditSphereDbContext db, ActorContext actor, PbcSeed.Fixture fixture,
    bool createWorkflowTask)
  {
    var created = await PbcService.CreateRequestAsync(db, actor, new CreatePbcRequestRequest(
      fixture.EngagementId, "Upload the signed bank confirmations.", "TEST ENTITY", "2026-01-01", "2026-12-31",
      "Cash", "PDF", string.Empty, fixture.Client.Id, fixture.Staff.Id, fixture.Reviewer.Id,
      "2027-01-31", "Confidential", "Signed and readable."));
    if (!created.Succeeded) return created;
    var sent = await PbcService.SendRequestAsync(db, actor, created.Value, 1,
      "https://audit.example.test/", createWorkflowTask: createWorkflowTask);
    return sent.Succeeded
      ? AuditSphereOps.Domain.Shared.CommandResult<Guid>.Ok(created.Value)
      : AuditSphereOps.Domain.Shared.CommandResult<Guid>.Fail(sent.ErrorCode!, sent.Message!);
  }
}
