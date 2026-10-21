using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class WorkflowDependencyAmendmentTests
{
  [Fact]
  public async Task ReviewedEdgesAreScopedFencedReplayableAndBothCompletionRoutesUseTheEffectiveGraph()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg); var other = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator"); await using var db = new AuditSphereDbContext(pg.Options);
    var a = await Make(db, f, "First operational task"); var b = await Make(db, f, "Dependent operational task");
    var foreign = await Make(db, other, "Private foreign task");
    var sibling = Guid.NewGuid();
    db.Engagements.Add(new() { Id = sibling, FirmId = f.FirmId, PracticeClientId = f.ClientId,
      Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var siblingTask = await Make(db, f with { EngagementId = sibling }, "Private sibling task");
    var request = new WorkflowDependencyChangeRequest(a, true, 1, 1, "Review prerequisite ordering.", Guid.NewGuid());
    Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeTimeService.ChangeWorkflowDependencyAsync(db, PbcSeed.Actor(f.Staff, "Staff"), f.EngagementId, b, request)).ErrorCode);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId, f.EngagementId)); await db.SaveChangesAsync();
    Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeTimeService.ChangeWorkflowDependencyAsync(db, PbcSeed.Actor(f.Staff, "Manager"), f.EngagementId, b,
      request with { PredecessorTaskId = siblingTask })).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, b, request with { PredecessorTaskId = foreign })).ErrorCode);
    Assert.Equal("request.invalid", (await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, b, request with { PredecessorTaskId = b })).ErrorCode);
    var made = await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, b, request);
    Assert.True(made.Succeeded, made.Message); Assert.Equal(2, made.Value!.TaskRevision);
    Assert.Equal(made.Value, (await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, b, request)).Value);
    Assert.Equal(made.Value, (await WorkflowTaskDependencyQuery.ReceiptAsync(db, actor, f.EngagementId, b, request.IdempotencyKey)).Value);
    Assert.Equal(ErrorCodes.IdempotencyConflict, (await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, b, request with { Reason = "Different content" })).ErrorCode);
    Assert.Equal(ErrorCodes.StaleRevision, (await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, b,
      request with { Required = false, IdempotencyKey = Guid.NewGuid() })).ErrorCode);
    var state = (await WorkflowTaskDependencyQuery.GetAsync(db, actor, f.EngagementId, b)).Value!;
    Assert.Equal(a, Assert.Single(state.References).PredecessorTaskId);
    var queued = Assert.Single((await WorkflowTaskQueueQuery.GetAsync(db, actor, new("TEAM"))).Value!.Tasks, x => x.TaskId == b);
    Assert.False(queued.CanComplete); Assert.Equal(1, queued.UnmetTaskDependencies);
    Assert.Equal(ErrorCodes.GateBlocked, (await EngagementWorkflowTaskService.CompleteAsync(db, actor, b, 2)).ErrorCode);
    Assert.Equal(ErrorCodes.GateBlocked, (await PracticeTimeService.CompleteTaskAsync(db, actor, b)).ErrorCode);
    Assert.True((await EngagementWorkflowTaskService.CompleteAsync(db, actor, a, 1)).Succeeded);
    Assert.True((await EngagementWorkflowTaskService.CompleteAsync(db, actor, b, 2)).Succeeded);
    Assert.Empty(await db.MaterialityApprovals.ToListAsync()); Assert.Empty(await db.AuditProcedureReviews.ToListAsync());
    Assert.Equal(ErrorCodes.ProtectedState, (await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, b,
      request with { Required = false, ExpectedRevision = 3, ExpectedPredecessorRevision = 2, IdempotencyKey = Guid.NewGuid() })).ErrorCode);
    Assert.Single(await db.WorkflowTaskDependencyRevisions.ToListAsync());
  }

  [Fact]
  public async Task TrackedCancelledPredecessorIsReloadedBeforeDependencyRevisionAndStatusGuards()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    await using var db = new AuditSphereDbContext(pg.Options);
    var predecessorTaskId = await Make(db, f, "Cancelled predecessor");
    var successorTaskId = await Make(db, f, "Dependency successor");

    // Keep both task rows and binding rows tracked while another context changes the predecessor.
    var trackedPredecessorTask = await db.WorkTasks.SingleAsync(x => x.Id == predecessorTaskId);
    var trackedSuccessorTask = await db.WorkTasks.SingleAsync(x => x.Id == successorTaskId);
    var trackedPredecessor = await db.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == predecessorTaskId);
    var trackedSuccessor = await db.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == successorTaskId);
    Assert.Equal(PracticeTimeStates.TaskOpen, trackedPredecessorTask.Status);
    Assert.Equal(PracticeTimeStates.TaskOpen, trackedSuccessorTask.Status);
    Assert.Equal(1L, trackedPredecessor.Revision);
    Assert.Equal(1L, trackedSuccessor.Revision);

    await using (var cancellationDb = new AuditSphereDbContext(pg.Options))
    {
      var cancelled = await EngagementWorkflowTaskService.CancelAsync(cancellationDb, actor,
        predecessorTaskId, "Cancel the prerequisite before dependency review.", expectedRevision: 1);
      Assert.True(cancelled.Succeeded, cancelled.Message);
    }

    var cancellationActivityCount = await db.WorkflowTaskActivities.AsNoTracking().CountAsync();
    Assert.Equal(PracticeTimeStates.TaskCancelled,
      (await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == predecessorTaskId)).Status);
    Assert.Equal(2L, await db.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.WorkTaskId == predecessorTaskId).Select(x => x.Revision).SingleAsync());

    var stale = await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId,
      successorTaskId, new(predecessorTaskId, true, ExpectedRevision: 1, ExpectedPredecessorRevision: 1,
        Reason: "Use the stale predecessor revision.", IdempotencyKey: Guid.NewGuid()));
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);

    var refreshedRevision = await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId,
      successorTaskId, new(predecessorTaskId, true, ExpectedRevision: 1, ExpectedPredecessorRevision: 2,
        Reason: "A cancelled task cannot be a required predecessor.", IdempotencyKey: Guid.NewGuid()));
    Assert.Equal(ErrorCodes.ProtectedState, refreshedRevision.ErrorCode);

    var dependencyProjection = await WorkflowTaskDependencyQuery.GetAsync(db, actor, f.EngagementId, successorTaskId);
    Assert.True(dependencyProjection.Succeeded, dependencyProjection.Message);
    Assert.Empty(dependencyProjection.Value!.References);
    Assert.Empty(await db.WorkflowTaskDependencyRevisions.AsNoTracking().ToListAsync());
    Assert.Equal(1L, await db.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.WorkTaskId == successorTaskId).Select(x => x.Revision).SingleAsync());
    Assert.Equal(2L, await db.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.WorkTaskId == predecessorTaskId).Select(x => x.Revision).SingleAsync());
    Assert.Equal(cancellationActivityCount, await db.WorkflowTaskActivities.AsNoTracking().CountAsync());
    Assert.Empty(await db.WorkflowTaskActivities.AsNoTracking()
      .Where(x => x.Kind == WorkflowTaskActivityKinds.DependencyChanged).ToListAsync());
  }

  [Fact]
  public async Task CyclesAndOppositeEdgeRacesAreRejectedAndWithdrawalsPreserveHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg); var actor = PbcSeed.Actor(f.Admin, "Administrator");
    Guid a, b, c, d, e;
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      a = await Make(db, f, "A"); b = await Make(db, f, "B"); c = await Make(db, f, "C"); d = await Make(db, f, "D"); e = await Make(db, f, "E");
      Assert.True((await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, b, new(a, true, 1, 1, "A before B", Guid.NewGuid()))).Succeeded);
      Assert.True((await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, c, new(b, true, 1, 2, "B before C", Guid.NewGuid()))).Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, (await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, a, new(c, true, 1, 2, "Cycle refused", Guid.NewGuid()))).ErrorCode);
      Assert.True((await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, b, new(a, false, 2, 1, "Withdraw A before B", Guid.NewGuid()))).Succeeded);
      Assert.True((await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, a, new(c, true, 1, 2, "C before A is now acyclic", Guid.NewGuid()))).Succeeded);
      Assert.False(Assert.Single((await WorkflowTaskDependencyQuery.GetAsync(db, actor, f.EngagementId, b)).Value!.References).Required);
    }
    async Task<CommandResult<WorkflowDependencyReceipt>> Send(Guid target, Guid source) {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, target, new(source, true, 1, 1, "Concurrent opposite edges", Guid.NewGuid()));
    }
    var results = await Task.WhenAll(Send(d, e), Send(e, d)); Assert.Single(results, x => x.Succeeded);
    Assert.All(results.Where(x => !x.Succeeded), x => Assert.Contains(x.ErrorCode, new[] { ErrorCodes.GateBlocked, ErrorCodes.StaleRevision }));
    // B -> C -> A already exists. Raw A -> B insertion is rejected by the deferred database guard as well.
    await using (var invalid = new AuditSphereDbContext(pg.Options)) {
      var source = await invalid.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == a);
      var target = await invalid.WorkflowTaskBindings.SingleAsync(x => x.WorkTaskId == b); target.Revision++;
      invalid.WorkflowTaskDependencyRevisions.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        PredecessorBindingId = source.Id, SuccessorBindingId = target.Id, Version = 3, Required = true, TaskRevision = target.Revision,
        PredecessorRevision = source.Revision, ActorUserId = actor.UserId, IdempotencyKey = Guid.NewGuid(), RequestDigest = new string('a', 64),
        Reason = "Raw cyclic insert", OccurredAt = DateTimeOffset.UtcNow });
      var error = await Record.ExceptionAsync(() => invalid.SaveChangesAsync()); Assert.NotNull(error);
      Assert.Contains("acyclic", Assert.IsType<PostgresException>(error.GetBaseException()).MessageText);
    }
    await using (var mutation = new AuditSphereDbContext(pg.Options)) {
      var row = await mutation.WorkflowTaskDependencyRevisions.FirstAsync(); row.Reason = "Rewrite";
      await Assert.ThrowsAsync<DbUpdateException>(() => mutation.SaveChangesAsync());
    }
    await using (var deletion = new AuditSphereDbContext(pg.Options)) {
      Assert.Equal(5, await deletion.WorkflowTaskDependencyRevisions.CountAsync());
      deletion.WorkflowTaskDependencyRevisions.Remove(await deletion.WorkflowTaskDependencyRevisions.FirstAsync());
      await Assert.ThrowsAsync<DbUpdateException>(() => deletion.SaveChangesAsync());
    }
  }

  [Fact]
  public async Task GraphBeyondTaskBoundIsRejectedWithoutPartialApplication()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    await using var db = new AuditSphereDbContext(pg.Options);

    const int taskCount = 501;
    var now = DateTimeOffset.UtcNow;
    var tasks = Enumerable.Range(0, taskCount).Select(i => new WorkTask
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      Title = $"Bounded graph task {i + 1}", Status = PracticeTimeStates.TaskOpen,
      Phase = BudgetPhases.Unassigned, CreatedAt = now
    }).ToArray();
    var bindings = tasks.Select((task, i) => new WorkflowTaskBinding
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      WorkTaskId = task.Id, Phase = WorkflowTaskPhases.AuditPlanning, Ordinal = i + 1,
      SourceKey = $"TEST-BOUND:{i + 1}", CreationRequestDigest = new string('a', 64), Generation = 1,
      DueDatePolicy = "MANUAL", AssignmentSource = "MANUAL", CreatedAt = now, CreatedByUserId = actor.UserId
    }).ToArray();
    db.WorkTasks.AddRange(tasks);
    db.WorkflowTaskBindings.AddRange(bindings);
    await db.SaveChangesAsync();

    var dependency = new WorkflowDependencyChangeRequest(
      tasks[0].Id, true, ExpectedRevision: 1, ExpectedPredecessorRevision: 1,
      Reason: "Do not partially apply an oversized graph.", IdempotencyKey: Guid.NewGuid());
    var result = await PracticeTimeService.ChangeWorkflowDependencyAsync(
      db, actor, f.EngagementId, tasks[1].Id, dependency);

    Assert.Equal(ErrorCodes.GateBlocked, result.ErrorCode);
    Assert.Equal(0, await db.WorkflowTaskDependencyRevisions.CountAsync());
    Assert.All(await db.WorkflowTaskBindings.AsNoTracking().Where(x => x.EngagementId == f.EngagementId)
      .Select(x => x.Revision).ToArrayAsync(), revision => Assert.Equal(1, revision));
    Assert.Equal(0, await db.WorkflowTaskActivities.CountAsync(x => x.Kind == WorkflowTaskActivityKinds.DependencyChanged));
  }

  [Theory]
  [InlineData(4000, false)]
  [InlineData(4001, true)]
  public async Task GraphEdgeBoundOverflowIsRejectedWithoutPartialApplication(int existingEdgeCount, bool requestExistingEdge)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var reviewer = PbcSeed.Actor(f.Reviewer, "Partner");
    await using var db = new AuditSphereDbContext(pg.Options);

    const int taskCount = 90;
    var candidatePairs = Enumerable.Range(0, taskCount - 1)
      .SelectMany(i => Enumerable.Range(i + 1, taskCount - i - 1).Select(j => (Predecessor: i, Successor: j)))
      .Take(existingEdgeCount).ToArray();
    Assert.Equal(existingEdgeCount, candidatePairs.Length);
    var requestedPair = requestExistingEdge
      ? (Predecessor: 0, Successor: 1)
      : (Predecessor: taskCount - 2, Successor: taskCount - 1);
    if (!requestExistingEdge) Assert.DoesNotContain(requestedPair, candidatePairs);
    var dependenciesBySuccessor = candidatePairs.GroupBy(x => x.Successor)
      .ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Select(pair => $"STEP-{pair.Predecessor + 1}").ToArray());
    var steps = Enumerable.Range(0, taskCount).Select(i => new WorkflowTemplateStepDraft(
      $"STEP-{i + 1}", WorkflowTaskPhases.AuditPlanning, $"Edge-bound step {i + 1}",
      "Synthetic ordering fixture.", null, null, null, null, true, i + 1,
      dependenciesBySuccessor.GetValueOrDefault(i, Array.Empty<string>()))).ToArray();

    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
    var engagement = await db.Engagements.SingleAsync(x => x.Id == f.EngagementId);
    engagement.ServiceRoute = "FinancialStatementAudit";
    engagement.PeriodStart = "2026-01-01";
    engagement.PeriodEnd = "2026-12-31";
    await db.SaveChangesAsync();

    var draft = await WorkflowTemplateService.CreateDraftAsync(db, actor,
      new SaveWorkflowTemplateDraftRequest(null, "Edge-bound fixture", "FinancialStatementAudit", null, steps));
    Assert.True(draft.Succeeded, draft.Message);
    var revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == draft.Value);
    var reviewed = await WorkflowTemplateService.ReviewAsync(db, reviewer, revision.TemplateId, revision.Id,
      revision.Revision, "Review the bounded synthetic dependency graph.");
    Assert.True(reviewed.Succeeded, reviewed.Message);
    revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == draft.Value);
    var published = await WorkflowTemplateService.PublishAsync(db, reviewer, revision.TemplateId, revision.Id,
      revision.Revision, "Publish the bounded synthetic dependency graph.");
    Assert.True(published.Succeeded, published.Message);

    var schedule = await StatutoryMilestoneService.SaveMilestonesAsync(db, actor,
      new SaveMilestonesRequest(f.EngagementId, new DateOnly(2027, 4, 30)));
    Assert.True(schedule.Succeeded, schedule.Message);
    var preview = await EngagementWorkflowPlanService.PreviewAsync(db, actor, f.EngagementId,
      new PreviewEngagementWorkflowPlanRequest(revision.Id));
    Assert.True(preview.Succeeded, preview.Message);
    Assert.True(preview.Value!.CanExecute);
    var execute = await EngagementWorkflowPlanService.ExecuteAsync(db, actor, f.EngagementId,
      new ExecuteEngagementWorkflowPlanRequest(revision.Id, preview.Value.PreviewDigest, Guid.NewGuid()));
    Assert.True(execute.Succeeded, execute.Message);
    Assert.Equal(taskCount, execute.Value!.TaskCount);
    var bindings = await db.WorkflowTaskBindings.AsNoTracking().Where(x => x.EngagementId == f.EngagementId)
      .OrderBy(x => x.Ordinal).ToArrayAsync();
    var tasksByOrdinal = bindings.ToDictionary(x => x.Ordinal - 1, x => x.WorkTaskId);
    Assert.Equal(existingEdgeCount, await db.WorkflowTaskDependencies.CountAsync(x => x.EngagementId == f.EngagementId));

    var request = new WorkflowDependencyChangeRequest(
      tasksByOrdinal[requestedPair.Predecessor], true, ExpectedRevision: 1, ExpectedPredecessorRevision: 1,
      Reason: "Do not partially apply an oversized graph.", IdempotencyKey: Guid.NewGuid());
    var result = await PracticeTimeService.ChangeWorkflowDependencyAsync(
      db, actor, f.EngagementId, tasksByOrdinal[requestedPair.Successor], request);

    Assert.Equal(ErrorCodes.GateBlocked, result.ErrorCode);
    Assert.Equal(existingEdgeCount, await db.WorkflowTaskDependencies.CountAsync(x => x.EngagementId == f.EngagementId));
    Assert.Equal(0, await db.WorkflowTaskDependencyRevisions.CountAsync());
    Assert.All(await db.WorkflowTaskBindings.AsNoTracking().Where(x => x.EngagementId == f.EngagementId)
      .Select(x => x.Revision).ToArrayAsync(), revision => Assert.Equal(1, revision));
    Assert.Equal(0, await db.WorkflowTaskActivities.CountAsync(x => x.Kind == WorkflowTaskActivityKinds.DependencyChanged));
  }

  [Fact]
  public async Task TemplateEdgesRemainPinnedWhileReviewedOverlayChangesOnlyOperationalOrdering()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg); var actor = PbcSeed.Actor(f.Admin, "Administrator");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
    var engagement = await db.Engagements.SingleAsync(x => x.Id == f.EngagementId); engagement.ServiceRoute = "FinancialStatementAudit";
    await db.SaveChangesAsync();
    WorkflowTemplateStepDraft Step(string key, int order, params string[] depends) => new(key, WorkflowTaskPhases.AuditPlanning, key,
      "Operational coordination only", null, null, null, null, true, order, depends);
    var draft = await WorkflowTemplateService.CreateDraftAsync(db, actor, new(null, "Pinned graph", "FinancialStatementAudit", null, [Step("A", 1), Step("B", 2, "A")]));
    Assert.True(draft.Succeeded, draft.Message); var revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == draft.Value);
    var partner = PbcSeed.Actor(f.Reviewer, "Partner");
    Assert.True((await WorkflowTemplateService.ReviewAsync(db, partner, revision.TemplateId, revision.Id, revision.Revision, "Independent graph review")).Succeeded);
    Assert.True((await WorkflowTemplateService.PublishAsync(db, partner, revision.TemplateId, revision.Id, revision.Revision, "Publish graph")).Succeeded);
    var preview = await EngagementWorkflowPlanService.PreviewAsync(db, actor, f.EngagementId, new(revision.Id)); Assert.True(preview.Succeeded, preview.Message);
    Assert.True((await EngagementWorkflowPlanService.ExecuteAsync(db, actor, f.EngagementId, new(revision.Id, preview.Value!.PreviewDigest, Guid.NewGuid()))).Succeeded);
    var tasks = (await EngagementWorkflowTaskService.GetAsync(db, actor, f.EngagementId)).Value!.Tasks;
    var a = tasks.Single(x => x.Title == "A"); var b = tasks.Single(x => x.Title == "B");
    Assert.Equal(0, Assert.Single((await WorkflowTaskDependencyQuery.GetAsync(db, actor, f.EngagementId, b.Id)).Value!.References).Version);
    var change = new WorkflowDependencyChangeRequest(a.Id, false, b.Revision, a.Revision, "Reviewed local sequencing exception", Guid.NewGuid());
    Assert.True((await PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, f.EngagementId, b.Id, change)).Succeeded);
    Assert.Single(await db.WorkflowTaskDependencies.ToListAsync()); Assert.Single(await db.WorkflowTemplateDependencies.ToListAsync());
    Assert.True(Assert.Single((await WorkflowTaskQueueQuery.GetAsync(db, actor, new("TEAM"))).Value!.Tasks, x => x.TaskId == b.Id).CanComplete);
    await db.Users.Where(x => x.Id == actor.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, 2));
    Assert.False((await WorkflowTaskDependencyQuery.GetAsync(db, actor, f.EngagementId, b.Id)).Succeeded);
    Assert.False((await WorkflowTaskDependencyQuery.ReceiptAsync(db, actor, f.EngagementId, b.Id, change.IdempotencyKey)).Succeeded);
  }

  private static async Task<Guid> Make(AuditSphereDbContext db, PbcSeed.Fixture f, string title) {
    var made = await EngagementWorkflowTaskService.CreateAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId,
      new(title, WorkflowTaskPhases.AuditPlanning, 1, IdempotencyKey: Guid.NewGuid())); Assert.True(made.Succeeded, made.Message); return made.Value;
  }
}
