using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  /// <summary>
  /// ASPM-UAT-05 as one application-level journey on one engagement: an independently published template becomes a
  /// planned task, the manager assigns it, it waits on the reviewed procedure, the reviewer returns the work, the
  /// preparer corrects and resubmits, the review is accepted, and only then can the preparer complete the task.
  /// </summary>
  [Fact]
  public async Task TemplatePlanAssignmentReturnCorrectionAndReviewRunAsOneJourney()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var author = w.A("partner", "Partner");
    var publisher = w.A("partner2", "Partner");
    var manager = w.A("manager", "Manager");
    var associate = w.A("associate", "Staff");
    var reviewer = w.A("senior", "Senior");

    var draft = await WorkflowTemplateService.CreateDraftAsync(db, author,
      new SaveWorkflowTemplateDraftRequest(null, "Revenue cut-off checklist", "FinancialStatementAudit", "ProfessionalServices",
        [new WorkflowTemplateStepDraft("CUTOFF", WorkflowTaskPhases.AuditFieldwork, "Clear revenue cut-off",
          "Finish revenue cut-off testing and get it reviewed.", null, 60, -10, null, true, 1, null)]));
    Assert.True(draft.Succeeded, draft.Message);
    var revision = await db.WorkflowTemplateRevisions.AsNoTracking().SingleAsync(x => x.Id == draft.Value);
    Assert.True((await WorkflowTemplateService.ReviewAsync(db, publisher, revision.TemplateId, revision.Id, revision.Revision,
      "Reviewed the cut-off checklist.")).Succeeded);
    revision = await db.WorkflowTemplateRevisions.AsNoTracking().SingleAsync(x => x.Id == draft.Value);
    var published = await WorkflowTemplateService.PublishAsync(db, publisher, revision.TemplateId, revision.Id, revision.Revision,
      "Publish the reviewed cut-off checklist.");
    Assert.True(published.Succeeded, published.Message);

    Assert.True((await StatutoryMilestoneService.SaveMilestonesAsync(db, manager,
      new SaveMilestonesRequest(w.EngagementId, new DateOnly(2027, 4, 29)))).Succeeded);
    var preview = await EngagementWorkflowPlanService.PreviewAsync(db, manager, w.EngagementId,
      new PreviewEngagementWorkflowPlanRequest(revision.Id));
    Assert.True(preview.Succeeded, preview.Message);
    var executed = await EngagementWorkflowPlanService.ExecuteAsync(db, manager, w.EngagementId,
      new ExecuteEngagementWorkflowPlanRequest(revision.Id, preview.Value!.PreviewDigest, Guid.NewGuid()));
    Assert.True(executed.Succeeded, executed.Message);
    var planned = Assert.Single((await EngagementWorkflowTaskService.GetAsync(db, manager, w.EngagementId)).Value!.Tasks);
    Assert.Equal(revision.Id, planned.TemplateRevisionId);

    Assert.True((await EngagementWorkflowTaskService.AssignAsync(db, manager, planned.Id, w.U["associate"].Id, planned.Revision)).Succeeded);
    var evidence = await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, manager, w.EngagementId, planned.Id,
      new WorkflowEvidenceChangeRequest(WorkflowEvidenceKinds.ReviewedProcedure, w.ProcedureId, true,
        "The cut-off task closes only after independent procedure review.", planned.Revision + 1, Guid.NewGuid()));
    Assert.True(evidence.Succeeded, evidence.Message);

    async Task<WorkflowTaskQueueRow> MyTask()
    {
      var mine = await WorkflowTaskQueueQuery.GetAsync(db, associate, new WorkflowTaskQueueRequest("MY_TASKS"));
      Assert.True(mine.Succeeded, mine.Message);
      return Assert.Single(mine.Value!.Tasks, x => x.TaskId == planned.Id);
    }
    var waiting = await MyTask();
    Assert.Equal((true, 1, false), (waiting.RequiredTask, waiting.UnmetEvidencePrerequisites, waiting.CanComplete));
    Assert.Equal(ErrorCodes.GateBlocked,
      (await EngagementWorkflowTaskService.CompleteAsync(db, associate, planned.Id, waiting.Revision)).ErrorCode);

    const string returnComment = "Reconcile the sample exception to source evidence.";
    Assert.True((await AuditProgramService.ReviewResultAsync(db, reviewer,
      new ReviewProcedureResultRequest(w.ResultId, AuditProcedureReviewDecisions.ChangesRequired, returnComment),
      createWorkflowReworkTask: true)).Succeeded);
    var corrections = await WorkflowTaskQueueQuery.GetAsync(db, associate, new WorkflowTaskQueueRequest("CHANGES_REQUIRED"));
    Assert.True(corrections.Succeeded, corrections.Message);
    var rework = Assert.Single(corrections.Value!.Tasks);
    Assert.Equal((w.U["associate"].Id, returnComment), (rework.AssigneeUserId!.Value, rework.ReviewComment));
    Assert.Equal(1, (await MyTask()).UnmetEvidencePrerequisites);

    var resubmission = await AuditProgramService.SubmitResultAsync(db, associate,
      new SubmitProcedureResultRequest(w.ProcedureId, 1, "Reworked the sample against source evidence.",
        "{\"result\":\"PASS\"}", ["source:updated-sample"], "Updated evidence addresses the review comment."));
    Assert.True(resubmission.Succeeded, resubmission.Message);
    var underReview = await WorkflowTaskQueueQuery.GetAsync(db, associate, new WorkflowTaskQueueRequest("UNDER_REVIEW"));
    Assert.True(underReview.Succeeded, underReview.Message);
    Assert.Equal(rework.TaskId, Assert.Single(underReview.Value!.Tasks).TaskId);
    Assert.Equal(1, (await MyTask()).UnmetEvidencePrerequisites);

    Assert.True((await AuditProgramService.ReviewResultAsync(db, reviewer,
      new ReviewProcedureResultRequest(resubmission.Value!.AuditProcedureResultId, AuditProcedureReviewDecisions.Reviewed, null))).Succeeded);
    var ready = await MyTask();
    Assert.Equal((0, true), (ready.UnmetEvidencePrerequisites, ready.CanComplete));
    var completed = await EngagementWorkflowTaskService.CompleteAsync(db, associate, planned.Id, ready.Revision);
    Assert.True(completed.Succeeded, completed.Message);
    Assert.Equal(PracticeTimeStates.TaskCompleted, (await db.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == planned.Id)).Status);
    // Completing the operational task records no professional decision of its own.
    Assert.Equal(2, await db.AuditProcedureReviews.CountAsync(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId));
  }
}
