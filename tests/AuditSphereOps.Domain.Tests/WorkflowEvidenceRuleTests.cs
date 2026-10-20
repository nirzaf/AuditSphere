using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class WorkflowEvidenceRuleTests
{
  [Fact]
  public async Task SameClientSiblingScopeAndDatabaseForeignReferencesAreDeniedAndDistinctRulesAreBounded()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    var admin = PbcSeed.Actor(f.Admin, "Administrator"); var scoped = PbcSeed.Actor(f.Staff, "Manager");
    var sibling = Guid.NewGuid(); Guid task; Guid siblingTask; Guid foreignRequest;
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId, f.EngagementId));
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement { Id = sibling, FirmId = f.FirmId,
        PracticeClientId = f.ClientId, Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      var made = await EngagementWorkflowTaskService.CreateAsync(db, admin, f.EngagementId,
        new("Bounded current evidence", WorkflowTaskPhases.AuditPlanning, 1, IdempotencyKey: Guid.NewGuid()));
      var siblingMade = await EngagementWorkflowTaskService.CreateAsync(db, admin, sibling,
        new("Private sibling task", WorkflowTaskPhases.AuditPlanning, 1, IdempotencyKey: Guid.NewGuid()));
      Assert.True(made.Succeeded, made.Message); Assert.True(siblingMade.Succeeded, siblingMade.Message);
      task = made.Value; siblingTask = siblingMade.Value;
      Assert.Equal(ErrorCodes.ScopeDenied, (await WorkflowTaskEvidenceQuery.GetAsync(db, scoped, sibling, siblingTask)).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, scoped, sibling, siblingTask,
        new(WorkflowEvidenceKinds.CurrentMateriality, null, true, "No sibling authority", 1, Guid.NewGuid()))).ErrorCode);
      var requested = await PbcService.CreateRequestAsync(db, PbcSeed.Actor(foreign.Staff, "Staff"),
        new(foreign.EngagementId, "Foreign exact reference", "TEST ENTITY", "2026-01-01", "2026-12-31", "Cash", "PDF", "",
          foreign.Client.Id, foreign.Staff.Id, foreign.Reviewer.Id, "2027-01-31", "Confidential", "Signed and readable."));
      Assert.True(requested.Succeeded, requested.Message); foreignRequest = requested.Value;
    }
    await using (var invalid = new AuditSphereDbContext(pg.Options)) {
      var binding = await invalid.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == task); binding.Revision++;
      invalid.WorkflowTaskEvidenceRules.Add(new WorkflowTaskEvidenceRule { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, WorkflowTaskBindingId = binding.Id, RuleKey = Guid.NewGuid(), Kind = WorkflowEvidenceKinds.AcceptedPbc,
        SourceId = foreignRequest, PbcRequestId = foreignRequest, Version = 1, TaskRevision = 2, ActorUserId = f.Admin.Id,
        IdempotencyKey = Guid.NewGuid(), RequestDigest = new string('a', 64), Reason = "Invalid cross-firm source", OccurredAt = DateTimeOffset.UtcNow });
      var error = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
      Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
    await using var final = new AuditSphereDbContext(pg.Options);
    var procedures = Enumerable.Range(0, 9).Select(n => new AuditProcedure { Id = Guid.NewGuid(), FirmId = f.FirmId,
      ClientId = f.ClientId, EngagementId = f.EngagementId, SourceProcedureId = "SYN-EVIDENCE-" + n, Title = "Synthetic bounded evidence", Status = AuditProcedureStatuses.Planned, CreatedAt = DateTimeOffset.UtcNow }).ToArray();
    final.AuditProcedures.AddRange(procedures); await final.SaveChangesAsync();
    for (var n = 0; n < 8; n++) Assert.True((await PracticeTimeService.ChangeWorkflowEvidenceAsync(final, scoped, f.EngagementId, task,
      new(WorkflowEvidenceKinds.ReviewedProcedure, procedures[n].Id, true, "Require exact reviewed source.", n + 1, Guid.NewGuid()))).Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, (await PracticeTimeService.ChangeWorkflowEvidenceAsync(final, scoped, f.EngagementId, task,
      new(WorkflowEvidenceKinds.ReviewedProcedure, procedures[8].Id, true, "Ninth distinct source.", 9, Guid.NewGuid()))).ErrorCode);
    Assert.Equal(8, (await WorkflowTaskEvidenceQuery.GetAsync(final, scoped, f.EngagementId, task)).Value!.Rules.Count);
  }

  [Fact]
  public async Task EvidencePolicyIsScopedRevisionedAppendOnlyAndReplayableWithoutApprovingSources()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    var admin = PbcSeed.Actor(f.Admin, "Administrator");
    Guid taskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await EngagementWorkflowTaskService.CreateAsync(db, admin, f.EngagementId,
        new("Await current planning", WorkflowTaskPhases.AuditPlanning, 1, AssigneeUserId: f.Staff.Id, IdempotencyKey: Guid.NewGuid()));
      Assert.True(created.Succeeded, created.Message); taskId = created.Value;
    }
    var change = new WorkflowEvidenceChangeRequest(WorkflowEvidenceKinds.CurrentMateriality, null, true,
      "Require independently approved current materiality.", 1, Guid.NewGuid());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeTimeService.ChangeWorkflowEvidenceAsync(db,
        PbcSeed.Actor(f.Staff, "Staff"), f.EngagementId, taskId, change)).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeTimeService.ChangeWorkflowEvidenceAsync(db,
        PbcSeed.Actor(foreign.Admin, "Administrator"), f.EngagementId, taskId, change)).ErrorCode);
      var added = await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, admin, f.EngagementId, taskId, change);
      Assert.True(added.Succeeded, added.Message); Assert.Equal(2, added.Value!.TaskRevision);
      var replay = await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, admin, f.EngagementId, taskId, change);
      Assert.Equal(added.Value, replay.Value);
      Assert.Equal(added.Value, (await WorkflowTaskEvidenceQuery.ReceiptAsync(db, admin, f.EngagementId, taskId, change.IdempotencyKey)).Value);
      Assert.Equal(ErrorCodes.IdempotencyConflict, (await PracticeTimeService.ChangeWorkflowEvidenceAsync(db,
        admin, f.EngagementId, taskId, change with { Reason = "Different reviewed content" })).ErrorCode);
      var stale = change with { Required = false, IdempotencyKey = Guid.NewGuid() };
      Assert.Equal(ErrorCodes.StaleRevision, (await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, admin, f.EngagementId, taskId, stale)).ErrorCode);
      var workspace = await WorkflowTaskEvidenceQuery.GetAsync(db, admin, f.EngagementId, taskId);
      Assert.False(Assert.Single(workspace.Value!.Rules).Satisfied);
      var queued = Assert.Single((await WorkflowTaskQueueQuery.GetAsync(db, admin, new("TEAM"))).Value!.Tasks);
      Assert.False(queued.CanComplete); Assert.Equal(1, queued.UnmetEvidencePrerequisites);
      // Both the workflow and legacy completion route must enforce the same live prerequisite.
      Assert.Equal(ErrorCodes.GateBlocked, (await EngagementWorkflowTaskService.CompleteAsync(db, admin, taskId, 2)).ErrorCode);
      Assert.Equal(ErrorCodes.GateBlocked, (await PracticeTimeService.CompleteTaskAsync(db, admin, taskId)).ErrorCode);
      Assert.Empty(await db.MaterialityApprovals.ToListAsync());
      var withdrawn = await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, admin, f.EngagementId, taskId,
        change with { Required = false, Reason = "Move this coordination requirement to the planning review task.", ExpectedRevision = 2, IdempotencyKey = Guid.NewGuid() });
      Assert.True(withdrawn.Succeeded, withdrawn.Message);
      Assert.Equal(2, await db.WorkflowTaskEvidenceRules.CountAsync());
      Assert.True((await EngagementWorkflowTaskService.CompleteAsync(db, admin, taskId, 3)).Succeeded);
      Assert.Empty(await db.MaterialityApprovals.ToListAsync());
      Assert.Equal(ErrorCodes.ProtectedState, (await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, admin, f.EngagementId, taskId,
        change with { ExpectedRevision = 4, IdempotencyKey = Guid.NewGuid() })).ErrorCode);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await Assert.ThrowsAsync<DbUpdateException>(async () => {
        var row = await db.WorkflowTaskEvidenceRules.OrderBy(x => x.Version).FirstAsync(); row.Reason = "Rewrite history"; await db.SaveChangesAsync();
      });
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      await Assert.ThrowsAsync<DbUpdateException>(async () => { db.WorkflowTaskEvidenceRules.Remove(await db.WorkflowTaskEvidenceRules.FirstAsync()); await db.SaveChangesAsync(); });
  }

  [Fact]
  public async Task ExactPbcAcceptanceChangesReadinessWithoutAStoredSatisfiedFlagAndForeignReferencesAreDenied()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg); var other = await PbcSeed.SeedAsync(pg);
    var admin = PbcSeed.Actor(f.Admin, "Administrator");
    await using var db = new AuditSphereDbContext(pg.Options);
    async Task<Guid> Request(PbcSeed.Fixture scope) {
      var result = await PbcService.CreateRequestAsync(db, PbcSeed.Actor(scope.Staff, "Staff"),
        new(scope.EngagementId, "Synthetic evidence request", "TEST ENTITY", "2026-01-01", "2026-12-31", "Cash", "PDF",
          "", scope.Client.Id, scope.Staff.Id, scope.Reviewer.Id, "2027-01-31", "Confidential", "Signed and readable."));
      Assert.True(result.Succeeded, result.Message); return result.Value;
    }
    var requestId = await Request(f); var foreignId = await Request(other);
    var created = await EngagementWorkflowTaskService.CreateAsync(db, admin, f.EngagementId,
      new("Wait for exact PBC evidence", WorkflowTaskPhases.AuditFieldwork, 1, IdempotencyKey: Guid.NewGuid()));
    Assert.True(created.Succeeded, created.Message); var task = created.Value;
    var change = new WorkflowEvidenceChangeRequest(WorkflowEvidenceKinds.AcceptedPbc, foreignId, true, "Wait for the exact request.", 1, Guid.NewGuid());
    Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, admin, f.EngagementId, task, change)).ErrorCode);
    Assert.True((await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, admin, f.EngagementId, task, change with { SourceId = requestId, IdempotencyKey = Guid.NewGuid() })).Succeeded);
    Assert.False(Assert.Single((await WorkflowTaskEvidenceQuery.GetAsync(db, admin, f.EngagementId, task)).Value!.Rules).Satisfied);
    Assert.Equal(requestId, Assert.Single((await WorkflowTaskEvidenceQuery.GetAsync(db, admin, f.EngagementId, task)).Value!.Rules).EvaluatedSourceId);
    var request = await db.PbcRequests.SingleAsync(x => x.Id == requestId);
    request.State = PbcStates.Sent; await db.SaveChangesAsync();
    Assert.Equal(task, Assert.Single((await WorkflowTaskQueueQuery.GetAsync(db, admin, new("WAITING_FOR_CLIENT"))).Value!.Tasks).TaskId);
    request.State = PbcStates.Accepted; request.AcceptedAt = DateTimeOffset.UtcNow; request.AcceptedByUserId = f.Reviewer.Id;
    await db.SaveChangesAsync();
    Assert.Empty((await WorkflowTaskQueueQuery.GetAsync(db, admin, new("WAITING_FOR_CLIENT"))).Value!.Tasks);
    Assert.True(Assert.Single((await WorkflowTaskEvidenceQuery.GetAsync(db, admin, f.EngagementId, task)).Value!.Rules).Satisfied);
    Assert.True(Assert.Single((await WorkflowTaskQueueQuery.GetAsync(db, admin, new("TEAM"))).Value!.Tasks).CanComplete);
    Assert.True((await EngagementWorkflowTaskService.CompleteAsync(db, admin, task, 2)).Succeeded);
    Assert.Equal(PbcStates.Accepted, (await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId)).State);
  }

  [Fact]
  public async Task ConcurrentReviewedChangesPublishOnceAndRevokedReceiptsAndInvalidKindsFailClosed()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    var admin = PbcSeed.Actor(f.Admin, "Administrator"); Guid task;
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      var made = await EngagementWorkflowTaskService.CreateAsync(db, admin, f.EngagementId,
        new("Concurrent evidence change", WorkflowTaskPhases.AuditPlanning, 1, IdempotencyKey: Guid.NewGuid()));
      Assert.True(made.Succeeded, made.Message); task = made.Value;
      var expired = PbcSeed.Grant(f.FirmId, f.Staff, "Administrator");
      expired.GrantedAt = DateTimeOffset.UtcNow.AddHours(-2); expired.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
      db.RoleGrants.Add(expired); await db.SaveChangesAsync();
      Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeTimeService.CompleteTaskAsync(db, PbcSeed.Actor(f.Staff, "Staff"), task)).ErrorCode);
    }
    var change = new WorkflowEvidenceChangeRequest(WorkflowEvidenceKinds.CurrentMateriality, null, true, "Current approved evidence only.", 1, Guid.NewGuid());
    async Task<CommandResult<WorkflowEvidenceChangeReceipt>> Send() {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, admin, f.EngagementId, task, change);
    }
    var results = await Task.WhenAll(Send(), Send()); Assert.All(results, x => Assert.True(x.Succeeded, x.Message)); Assert.Equal(results[0].Value, results[1].Value);
    await using var final = new AuditSphereDbContext(pg.Options);
    Assert.Single(await final.WorkflowTaskEvidenceRules.ToListAsync());
    Assert.Single(await final.WorkflowTaskActivities.Where(x => x.Kind == WorkflowTaskActivityKinds.EvidenceChanged).ToListAsync());
    Assert.Equal("request.invalid", (await PracticeTimeService.ChangeWorkflowEvidenceAsync(final, admin, f.EngagementId, task,
      change with { Kind = "EDITABLE_SATISFIED", ExpectedRevision = 2, IdempotencyKey = Guid.NewGuid() })).ErrorCode);
    await final.Users.Where(x => x.Id == admin.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, 2));
    Assert.False((await WorkflowTaskEvidenceQuery.GetAsync(final, admin, f.EngagementId, task)).Succeeded);
    Assert.False((await WorkflowTaskEvidenceQuery.ReceiptAsync(final, admin, f.EngagementId, task, change.IdempotencyKey)).Succeeded);
  }
}
