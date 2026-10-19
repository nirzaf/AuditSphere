using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  [Fact]
  public async Task ManagerReviewSummaryUsesCurrentCompletionFactsAndStableRedactedBlockers()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var result = await ManagerReviewSummaryQuery.GetAsync(db, w.A("manager", "Manager"), w.EngagementId);

    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(1, result.Value!.ApplicableProcedures);
    Assert.Equal(1, result.Value.ProceduresNeedingReview);
    Assert.Equal("NOT_GENERATED", result.Value.SummaryReviewMemorandumState);
    Assert.False(result.Value.PartnerHandoffReady);
    Assert.Contains(result.Value.Blockers, x => x.Code == "procedure-review-required" && x.Count == 1);
    Assert.DoesNotContain(result.Value.Blockers, x => x.Label.Contains(w.ProcedureId.ToString(), StringComparison.Ordinal));
  }

  [Fact]
  public async Task ChangesRequiredReviewCreatesOneLinkedOperationalReworkTaskWithoutChangingReviewAuthority()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var reviewer = w.A("senior", "Senior");

    var unlinkedInbox = await ProcedureReviewInboxQuery.GetAsync(db, reviewer, new ProcedureReviewInboxRequest());
    Assert.True(unlinkedInbox.Succeeded, unlinkedInbox.Message);
    var unlinkedReview = Assert.Single(unlinkedInbox.Value!.Reviews);
    Assert.Equal(w.ResultId, unlinkedReview.ResultId);
    Assert.Equal(w.ProcedureId, unlinkedReview.ProcedureId);

    var preparerInbox = await ProcedureReviewInboxQuery.GetAsync(db, w.A("associate", "Staff"),
      new ProcedureReviewInboxRequest());
    Assert.False(preparerInbox.Succeeded);

    const string reviewComment = "Reconcile the sample exception to source evidence.";
    var decision = await AuditProgramService.ReviewResultAsync(db, reviewer,
      new ReviewProcedureResultRequest(w.ResultId, AuditProcedureReviewDecisions.ChangesRequired, reviewComment),
      createWorkflowReworkTask: true);

    Assert.True(decision.Succeeded, decision.Message);
    var task = await db.WorkTasks.SingleAsync(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId);
    var binding = await db.WorkflowTaskBindings.SingleAsync(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId);
    Assert.Equal(w.U["associate"].Id, task.AssigneeUserId);
    Assert.Equal(WorkflowTaskPhases.AuditFieldwork, binding.Phase);
    Assert.Contains(w.ProcedureId.ToString("N"), binding.SourceKey, StringComparison.Ordinal);
    Assert.Equal(AuditProcedureReviewDecisions.ChangesRequired,
      await db.AuditProcedureReviews.Where(x => x.AuditProcedureResultId == w.ResultId).Select(x => x.Decision).SingleAsync());

    var linkedReviewInbox = await ProcedureReviewInboxQuery.GetAsync(db, reviewer, new ProcedureReviewInboxRequest());
    Assert.True(linkedReviewInbox.Succeeded, linkedReviewInbox.Message);
    Assert.Empty(linkedReviewInbox.Value!.Reviews);

    var changesRequired = await WorkflowTaskQueueQuery.GetAsync(db, w.A("associate", "Staff"),
      new WorkflowTaskQueueRequest("CHANGES_REQUIRED", IncludeSummary: true));
    Assert.True(changesRequired.Succeeded, changesRequired.Message);
    var changeRow = Assert.Single(changesRequired.Value!.Tasks);
    Assert.Equal(task.Id, changeRow.TaskId);
    Assert.Equal(AuditProcedureResultStatuses.ChangesRequired, changeRow.ReviewState);
    Assert.Equal(reviewComment, changeRow.ReviewComment);
    Assert.Equal("1", Assert.Single(changesRequired.Value.Summary!.Counts, x => x.View == "CHANGES_REQUIRED").Count);
    Assert.Equal("0", Assert.Single(changesRequired.Value.Summary.Counts, x => x.View == "UNDER_REVIEW").Count);

    var summary = await ManagerReviewSummaryQuery.GetAsync(db, w.A("manager", "Manager"), w.EngagementId);
    Assert.True(summary.Succeeded, summary.Message);
    Assert.False(summary.Value!.PartnerHandoffReady);
    Assert.Equal(1, summary.Value.ProceduresNeedingReview);
    Assert.Contains(summary.Value.Blockers, x => x.Code == "procedure-review-stale");

    var replay = await AuditProgramService.ReviewResultAsync(db, reviewer,
      new ReviewProcedureResultRequest(w.ResultId, AuditProcedureReviewDecisions.ChangesRequired, "Reconcile the sample exception to source evidence."),
      createWorkflowReworkTask: true);
    Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.ProtectedState, replay.ErrorCode);
    Assert.Equal(1, await db.WorkTasks.CountAsync(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId));
    Assert.Equal(1, await db.AuditProcedureReviews.CountAsync(x => x.AuditProcedureResultId == w.ResultId));

    var resubmission = await AuditProgramService.SubmitResultAsync(db, w.A("associate", "Staff"),
      new SubmitProcedureResultRequest(w.ProcedureId, 1, "Reworked the sample against source evidence.",
        "{\"result\":\"PASS\"}", ["source:updated-sample"], "Updated evidence addresses the review comment."));
    Assert.True(resubmission.Succeeded, resubmission.Message);
    var clearedQueue = await WorkflowTaskQueueQuery.GetAsync(db, w.A("associate", "Staff"),
      new WorkflowTaskQueueRequest("CHANGES_REQUIRED"));
    Assert.True(clearedQueue.Succeeded, clearedQueue.Message);
    Assert.Empty(clearedQueue.Value!.Tasks);

    var underReview = await WorkflowTaskQueueQuery.GetAsync(db, w.A("associate", "Staff"),
      new WorkflowTaskQueueRequest("UNDER_REVIEW", IncludeSummary: true));
    Assert.True(underReview.Succeeded, underReview.Message);
    var reviewRow = Assert.Single(underReview.Value!.Tasks);
    Assert.Equal(task.Id, reviewRow.TaskId);
    Assert.Equal("UNDER_REVIEW", reviewRow.ReviewState);
    Assert.Equal(reviewComment, reviewRow.ReviewComment);
    Assert.Equal("0", Assert.Single(underReview.Value.Summary!.Counts, x => x.View == "CHANGES_REQUIRED").Count);
    Assert.Equal("1", Assert.Single(underReview.Value.Summary.Counts, x => x.View == "UNDER_REVIEW").Count);

    var resubmittedInbox = await ProcedureReviewInboxQuery.GetAsync(db, reviewer, new ProcedureReviewInboxRequest());
    Assert.True(resubmittedInbox.Succeeded, resubmittedInbox.Message);
    Assert.Empty(resubmittedInbox.Value!.Reviews);

    // A later correction can be reviewed while operational rework automation is disabled.
    // The historical task must not hide a new submission that it no longer represents.
    var secondCorrection = await AuditProgramService.ReviewResultAsync(db, reviewer,
      new ReviewProcedureResultRequest(resubmission.Value!.AuditProcedureResultId,
        AuditProcedureReviewDecisions.ChangesRequired, "Resolve the remaining source discrepancy."),
      createWorkflowReworkTask: false);
    Assert.True(secondCorrection.Succeeded, secondCorrection.Message);
    var repeatedChanges = await WorkflowTaskQueueQuery.GetAsync(db, w.A("associate", "Staff"),
      new WorkflowTaskQueueRequest("CHANGES_REQUIRED", IncludeSummary: true));
    Assert.True(repeatedChanges.Succeeded, repeatedChanges.Message);
    var repeatedChange = Assert.Single(repeatedChanges.Value!.Tasks);
    Assert.Equal(task.Id, repeatedChange.TaskId);
    Assert.Equal("Resolve the remaining source discrepancy.", repeatedChange.ReviewComment);
    Assert.Equal("1", Assert.Single(repeatedChanges.Value.Summary!.Counts, x => x.View == "CHANGES_REQUIRED").Count);
    var secondResubmission = await AuditProgramService.SubmitResultAsync(db, w.A("associate", "Staff"),
      new SubmitProcedureResultRequest(w.ProcedureId, 1, "Both source discrepancies reconciled.",
        "{\"result\":\"PASS\"}", ["source:second-corrected-sample"], "Second correction is ready for independent review."));
    Assert.True(secondResubmission.Succeeded, secondResubmission.Message);
    var laterInbox = await ProcedureReviewInboxQuery.GetAsync(db, reviewer, new ProcedureReviewInboxRequest());
    Assert.True(laterInbox.Succeeded, laterInbox.Message);
    var laterReview = Assert.Single(laterInbox.Value!.Reviews);
    Assert.Equal(secondResubmission.Value!.AuditProcedureResultId, laterReview.ResultId);
    Assert.Equal(3, laterReview.Revision);
    var historicalTaskQueue = await WorkflowTaskQueueQuery.GetAsync(db, reviewer,
      new WorkflowTaskQueueRequest("UNDER_REVIEW"));
    Assert.True(historicalTaskQueue.Succeeded, historicalTaskQueue.Message);
    Assert.Empty(historicalTaskQueue.Value!.Tasks);
    Assert.Equal(1, await db.WorkTasks.CountAsync(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId));

    // Re-enabling automation gives the next rejected revision its own task. The old task
    // remains historical, but must not duplicate the current correction or its summary count.
    var thirdCorrection = await AuditProgramService.ReviewResultAsync(db, reviewer,
      new ReviewProcedureResultRequest(secondResubmission.Value!.AuditProcedureResultId,
        AuditProcedureReviewDecisions.ChangesRequired, "Confirm the final source cross-reference."),
      createWorkflowReworkTask: true);
    Assert.True(thirdCorrection.Succeeded, thirdCorrection.Message);
    var latestChanges = await WorkflowTaskQueueQuery.GetAsync(db, w.A("associate", "Staff"),
      new WorkflowTaskQueueRequest("CHANGES_REQUIRED", IncludeSummary: true));
    Assert.True(latestChanges.Succeeded, latestChanges.Message);
    var latestChange = Assert.Single(latestChanges.Value!.Tasks);
    Assert.NotEqual(task.Id, latestChange.TaskId);
    Assert.Equal("Confirm the final source cross-reference.", latestChange.ReviewComment);
    Assert.Equal("1", Assert.Single(latestChanges.Value.Summary!.Counts, x => x.View == "CHANGES_REQUIRED").Count);
    Assert.Equal(w.ResultId, (await db.WorkflowTaskBindings.SingleAsync(x => x.Id == binding.Id)).ReviewProcedureResultId);
    Assert.Equal(2, await db.WorkTasks.CountAsync(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId));
    var finalResubmission = await AuditProgramService.SubmitResultAsync(db, w.A("associate", "Staff"),
      new SubmitProcedureResultRequest(w.ProcedureId, 1, "Final source cross-reference confirmed.",
        "{\"result\":\"PASS\"}", ["source:final-cross-reference"], "Final correction is ready for independent review."));
    Assert.True(finalResubmission.Succeeded, finalResubmission.Message);
    var finalQueue = await WorkflowTaskQueueQuery.GetAsync(db, reviewer,
      new WorkflowTaskQueueRequest("UNDER_REVIEW", IncludeSummary: true));
    Assert.True(finalQueue.Succeeded, finalQueue.Message);
    Assert.Equal(latestChange.TaskId, Assert.Single(finalQueue.Value!.Tasks).TaskId);
    Assert.Equal("1", Assert.Single(finalQueue.Value.Summary!.Counts, x => x.View == "UNDER_REVIEW").Count);
    Assert.Empty((await ProcedureReviewInboxQuery.GetAsync(db, reviewer, new ProcedureReviewInboxRequest())).Value!.Reviews);

    var independentReview = await AuditProgramService.ReviewResultAsync(db, reviewer,
      new ReviewProcedureResultRequest(finalResubmission.Value!.AuditProcedureResultId, AuditProcedureReviewDecisions.Reviewed, null));
    Assert.True(independentReview.Succeeded, independentReview.Message);
    var reviewComplete = await WorkflowTaskQueueQuery.GetAsync(db, w.A("associate", "Staff"),
      new WorkflowTaskQueueRequest("UNDER_REVIEW"));
    Assert.True(reviewComplete.Succeeded, reviewComplete.Message);
    Assert.Empty(reviewComplete.Value!.Tasks);
  }
}
