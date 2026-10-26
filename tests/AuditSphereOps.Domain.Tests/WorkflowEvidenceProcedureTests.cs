using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class WorkflowEvidenceProcedureTests
{
  [Fact]
  public async Task CurrentProfessionalEvidenceCannotBeFakedByTaskCompletionAndUnrelatedReviewedAreasStayValid()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.Actor(f.Staff, "Manager"); var partner = PbcSeed.Actor(f.Reviewer, "Partner"); var admin = PbcSeed.Actor(f.Admin, "Administrator");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId, f.EngagementId),
      PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner", f.ClientId, f.EngagementId)); await db.SaveChangesAsync();
    await PlanningBasisSeed.EstablishAsync(db, f.FirmId, f.ClientId, f.EngagementId, manager, partner);
    var published = await AuditProgramTestHelpers.PublishAsync(db, admin, new("workflow-evidence-v1", AuditProgramCatalog.SourceHash));
    Assert.True(published.Succeeded, published.Message);
    Assert.True((await AuditProgramService.AdoptAsync(db, manager, new(f.EngagementId, published.Value!.ProgramVersionId))).Succeeded);
    var procedures = await db.AuditProcedures.Where(x => x.EngagementId == f.EngagementId).OrderBy(x => x.SourceProcedureId).Take(2).ToListAsync();
    var tasks = new List<Guid>(); var results = new List<Guid>();
    foreach (var procedure in procedures)
    {
      Assert.True((await AuditProgramTestHelpers.MarkApplicableAsync(db, manager, procedure.Id)).Succeeded);
      var task = await EngagementWorkflowTaskService.CreateAsync(db, manager, f.EngagementId,
        new("Coordinate " + procedure.SourceProcedureId, WorkflowTaskPhases.ManagerReview, tasks.Count + 1, AssigneeUserId: f.Staff.Id, IdempotencyKey: Guid.NewGuid()));
      Assert.True(task.Succeeded, task.Message); tasks.Add(task.Value);
      Assert.True((await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, manager, f.EngagementId, task.Value,
        new(WorkflowEvidenceKinds.ReviewedProcedure, procedure.Id, true, "Require the exact current independently reviewed procedure.", 1, Guid.NewGuid()))).Succeeded);
      var submitted = await AuditProgramService.SubmitResultAsync(db, manager,
        new(procedure.Id, 1, "Synthetic evidence tested", "{}", ["synthetic:reference"], "Human-authored conclusion"));
      Assert.True(submitted.Succeeded, submitted.Message); results.Add(submitted.Value!.AuditProcedureResultId);
      Assert.Equal(ErrorCodes.GateBlocked, (await EngagementWorkflowTaskService.CompleteAsync(db, manager, task.Value, 2)).ErrorCode);
      Assert.Equal(AuditProcedureResultStatuses.Submitted, (await db.AuditProcedureResults.AsNoTracking().SingleAsync(x => x.Id == submitted.Value.AuditProcedureResultId)).Status);
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditProgramService.ReviewResultAsync(db, manager,
        new(submitted.Value.AuditProcedureResultId, AuditProcedureReviewDecisions.Reviewed, null))).ErrorCode);
      Assert.True((await AuditProgramService.ReviewResultAsync(db, partner,
        new(submitted.Value.AuditProcedureResultId, AuditProcedureReviewDecisions.Reviewed, null))).Succeeded);
      Assert.True(Assert.Single((await WorkflowTaskEvidenceQuery.GetAsync(db, manager, f.EngagementId, task.Value)).Value!.Rules).Satisfied);
    }
    // A reopened thread affects only its exact procedure. The second reviewed procedure and both prior results remain.
    var note = await ReviewNotesService.AddNoteAsync(db, partner, new(results[0], ReviewNoteFields.WorkPerformed,
      "Synthetic evidence", "Clarify the current evidence before final review."));
    Assert.True(note.Succeeded, note.Message);
    var first = Assert.Single((await WorkflowTaskEvidenceQuery.GetAsync(db, manager, f.EngagementId, tasks[0])).Value!.Rules);
    Assert.False(first.Satisfied);
    Assert.True(Assert.Single((await WorkflowTaskEvidenceQuery.GetAsync(db, manager, f.EngagementId, tasks[1])).Value!.Rules).Satisfied);
    Assert.Equal(2, await db.AuditProcedureResults.CountAsync(x => results.Contains(x.Id)));
    Assert.True((await EngagementWorkflowTaskService.CompleteAsync(db, manager, tasks[1], 2)).Succeeded);
    Assert.Equal(AuditProcedureResultStatuses.Reviewed, (await db.AuditProcedureResults.AsNoTracking().SingleAsync(x => x.Id == results[1])).Status);
    await db.ClientSafetyStates.Where(x => x.Id == f.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
    first = Assert.Single((await WorkflowTaskEvidenceQuery.GetAsync(db, manager, f.EngagementId, tasks[0])).Value!.Rules);
    Assert.False(first.Satisfied); Assert.Equal("STALE_GENERATION", first.SourceState);
  }

  [Fact]
  public async Task ApprovedMaterialityIsLiveAndAReplacementDraftCannotReuseThePriorApproval()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.Actor(f.Staff, "Manager"); var partner = PbcSeed.Actor(f.Reviewer, "Partner");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId, f.EngagementId),
      PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner", f.ClientId, f.EngagementId)); await db.SaveChangesAsync();
    await PlanningBasisSeed.EstablishAsync(db, f.FirmId, f.ClientId, f.EngagementId, manager, partner);
    var task = await EngagementWorkflowTaskService.CreateAsync(db, manager, f.EngagementId,
      new("Use current materiality", WorkflowTaskPhases.AuditPlanning, 1, IdempotencyKey: Guid.NewGuid()));
    Assert.True(task.Succeeded, task.Message);
    Assert.True((await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, manager, f.EngagementId, task.Value,
      new(WorkflowEvidenceKinds.CurrentMateriality, null, true, "Use independently approved current materiality.", 1, Guid.NewGuid()))).Succeeded);
    Assert.True(Assert.Single((await WorkflowTaskEvidenceQuery.GetAsync(db, manager, f.EngagementId, task.Value)).Value!.Rules).Satisfied);
    var replacement = await MaterialityEngineService.CalculateAsync(db, manager,
      new(f.EngagementId, MaterialityBenchmarks.Revenue, null, 1m, 75m, 5m, "Fresh manager assessment"));
    Assert.True(replacement.Succeeded, replacement.Message);
    Assert.False(Assert.Single((await WorkflowTaskEvidenceQuery.GetAsync(db, manager, f.EngagementId, task.Value)).Value!.Rules).Satisfied);
    Assert.Equal(ErrorCodes.GateBlocked, (await EngagementWorkflowTaskService.CompleteAsync(db, manager, task.Value, 2)).ErrorCode);
    Assert.True((await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, partner, replacement.Value!.AssessmentId)).Succeeded);
    Assert.True((await EngagementWorkflowTaskService.CompleteAsync(db, manager, task.Value, 2)).Succeeded);

    var completedTasks = new List<Guid> { task.Value };
    for (var ordinal = 2; ordinal <= 3; ordinal++)
    {
      var dependent = await EngagementWorkflowTaskService.CreateAsync(db, manager, f.EngagementId,
        new($"Use the prior reviewed commitment {ordinal}", WorkflowTaskPhases.AuditPlanning, ordinal,
          IdempotencyKey: Guid.NewGuid()));
      Assert.True(dependent.Succeeded, dependent.Message);
      var predecessor = completedTasks[^1];
      var predecessorRevision = await db.WorkflowTaskBindings.AsNoTracking().Where(x => x.WorkTaskId == predecessor)
        .Select(x => x.Revision).SingleAsync();
      var edge = await PracticeTimeService.ChangeWorkflowDependencyAsync(db, manager, f.EngagementId, dependent.Value,
        new(predecessor, true, 1, predecessorRevision, "The next commitment relies on this current prerequisite.", Guid.NewGuid()));
      Assert.True(edge.Succeeded, edge.Message);
      Assert.True((await EngagementWorkflowTaskService.CompleteAsync(db, manager, dependent.Value, edge.Value!.TaskRevision)).Succeeded);
      completedTasks.Add(dependent.Value);
    }
    var before = (await EngagementProgressQuery.GetAsync(db, manager, f.EngagementId)).Value!;
    Assert.Equal((3, 0), (before.CompletedRequired, before.BlockedRequired));
    var latest = await MaterialityEngineService.CalculateAsync(db, manager,
      new(f.EngagementId, MaterialityBenchmarks.Revenue, null, 1m, 75m, 5m, "Reassess current inputs after downstream work"));
    Assert.True(latest.Succeeded, latest.Message);
    Assert.False(Assert.Single((await WorkflowTaskEvidenceQuery.GetAsync(db, manager, f.EngagementId, task.Value)).Value!.Rules).Satisfied);
    var after = (await EngagementProgressQuery.GetAsync(db, manager, f.EngagementId)).Value!;
    Assert.Equal((0, 3, 0), (after.CompletedRequired, after.BlockedRequired, after.PercentComplete));
    // Current readiness changes; the retained operational completion history is not rewritten.
    Assert.Equal(3, await db.WorkTasks.AsNoTracking().CountAsync(x => completedTasks.Contains(x.Id) &&
      x.Status == PracticeTimeStates.TaskCompleted));
  }
}
