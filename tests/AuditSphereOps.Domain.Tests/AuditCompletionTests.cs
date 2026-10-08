using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>T073-T075: variance investigation, going-concern assessment and subsequent-event
/// review each derive their conclusion from the recorded evidence and block review while unresolved.</summary>
[Trait("Profile", "Database")]
public sealed class AuditCompletionTests
{
  [Fact(DisplayName = "Variance, going-concern and subsequent-event conclusions follow from evidence")]
  public async Task CompletionRecords_DeriveConclusionsAndBlockUnresolved()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;
    Guid reviewerId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      reviewerId = Guid.NewGuid();
      db.Users.Add(new AppUser
      {
        Id = reviewerId, FirmId = scope.FirmId, Subject = "completion-reviewer-" + reviewerId.ToString("N"),
        TenantId = "tenant-planning", Email = "completion-reviewer@example.test", DisplayName = "Completion Reviewer",
        UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = reviewerId, Role = "Reviewer",
        ClientId = scope.ClientId, EngagementId = scope.EngagementId,
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Actor.UserId
      });
      await db.SaveChangesAsync();
    }
    var reviewer = new ActorContext(reviewerId, scope.FirmId, 1, ["Reviewer", "Partner"]);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // --- variance: below threshold is explained; above threshold without an explanation is unexplained.
      var belowThreshold = await AuditCompletionService.RecordVarianceInvestigationAsync(db, scope.Actor,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Revenue, "2026-Q1",
          1000m, 1050m, 100m, "QAR", null, ["tb-extract"]));
      Assert.True(belowThreshold.Succeeded, belowThreshold.Message);
      Assert.False(belowThreshold.Value!.ExceedsThreshold);
      Assert.Equal(VarianceInvestigationConclusions.Explained, belowThreshold.Value.Conclusion);
      Assert.Equal(5m, belowThreshold.Value.DifferencePercent);

      var aboveUnexplained = await AuditCompletionService.RecordVarianceInvestigationAsync(db, scope.Actor,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Receivables, "2026-Q1",
          1000m, 1400m, 100m, "QAR", null, ["tb-extract"]));
      Assert.True(aboveUnexplained.Succeeded);
      Assert.True(aboveUnexplained.Value!.ExceedsThreshold);
      Assert.Equal(VarianceInvestigationConclusions.Unexplained, aboveUnexplained.Value.Conclusion);
      var blockedVarianceReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "VARIANCE", aboveUnexplained.Value.InvestigationId);
      Assert.False(blockedVarianceReview.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blockedVarianceReview.ErrorCode);

      // Corroborating the variance clears the block and permits independent review.
      var corroborated = await AuditCompletionService.RecordVarianceInvestigationAsync(db, scope.Actor,
        new VarianceInvestigationRequest(scope.EngagementId, null, AuditAreaCodes.Receivables, "2026-Q1",
          1000m, 1400m, 100m, "QAR", "Volume growth from the two signed contracts shipped in March.",
          ["contract-a", "contract-b"]));
      Assert.True(corroborated.Succeeded);
      Assert.Equal(VarianceInvestigationConclusions.Corroborated, corroborated.Value!.Conclusion);
      Assert.Equal(2, corroborated.Value.Revision);
      Assert.True((await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "VARIANCE", corroborated.Value.InvestigationId)).Succeeded);
      var selfReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, scope.Actor,
        "VARIANCE", corroborated.Value.InvestigationId);
      Assert.False(selfReview.Succeeded);

      // --- going concern: no uncertainty needs no disclosure; uncertainty with disclosure is recorded.
      var noUncertainty = await AuditCompletionService.RecordGoingConcernAssessmentAsync(db, scope.Actor,
        new GoingConcernAssessmentRequest(scope.EngagementId, null, new DateOnly(2026, 12, 31),
          new DateOnly(2027, 12, 31), "Twelve-month forecast reviewed with no indicators of concern.",
          false, true, "QAR", "Cash-flow forecast supports operations for at least twelve months.",
          ["board-minutes", "cashflow-forecast"]));
      Assert.True(noUncertainty.Succeeded, noUncertainty.Message);
      Assert.Equal(GoingConcernConclusions.NoMaterialUncertainty, noUncertainty.Value!.Conclusion);
      Assert.True((await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "GOING_CONCERN", noUncertainty.Value.AssessmentId)).Succeeded);

      var uncertainty = await AuditCompletionService.RecordGoingConcernAssessmentAsync(db, scope.Actor,
        new GoingConcernAssessmentRequest(scope.EngagementId, null, new DateOnly(2026, 12, 15),
          new DateOnly(2027, 12, 31), "Forecast shows a covenant breach risk within the next twelve months.",
          true, false, "QAR", "Material uncertainty identified; disclosure assessment pending.",
          ["cashflow-forecast"]));
      Assert.True(uncertainty.Succeeded);
      Assert.Equal(GoingConcernConclusions.InadequateDisclosure, uncertainty.Value!.Conclusion);
      var blockedGoingConcern = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "GOING_CONCERN", uncertainty.Value.AssessmentId);
      Assert.False(blockedGoingConcern.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blockedGoingConcern.ErrorCode);

      // --- subsequent events: the classification is recorded, never inferred.
      var pendingEvent = await AuditCompletionService.RecordSubsequentEventAsync(db, scope.Actor,
        new SubsequentEventReviewRequest(scope.EngagementId, null, new DateOnly(2026, 12, 31),
          new DateOnly(2027, 1, 20), "Major customer entered administration in January 2027.",
          SubsequentEventClassifications.PendingAssessment, false, false, null, null, "QAR", null,
          ["news-article-1"]));
      Assert.True(pendingEvent.Succeeded, pendingEvent.Message);
      Assert.Equal(1, pendingEvent.Value!.PendingEventCount);
      var blockedEventReview = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "SUBSEQUENT_EVENT", pendingEvent.Value.ReviewId);
      Assert.False(blockedEventReview.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blockedEventReview.ErrorCode);

      // A non-adjusting event with an inconsistent adjustment flag is refused.
      var inconsistent = await AuditCompletionService.RecordSubsequentEventAsync(db, scope.Actor,
        new SubsequentEventReviewRequest(scope.EngagementId, null, new DateOnly(2026, 12, 31),
          new DateOnly(2027, 1, 20), "Inconsistent classification", SubsequentEventClassifications.NonAdjusting,
          true, false, null, "Rationale", "QAR", null, ["evidence"]));
      Assert.False(inconsistent.Succeeded);

      // Disclosure without a reference is refused.
      var missingDisclosure = await AuditCompletionService.RecordSubsequentEventAsync(db, scope.Actor,
        new SubsequentEventReviewRequest(scope.EngagementId, null, new DateOnly(2026, 12, 31),
          new DateOnly(2027, 1, 20), "Disclosure needed", SubsequentEventClassifications.NonAdjusting,
          false, true, null, "Rationale", "QAR", null, ["evidence"]));
      Assert.False(missingDisclosure.Succeeded);

      var classified = await AuditCompletionService.RecordSubsequentEventAsync(db, scope.Actor,
        new SubsequentEventReviewRequest(scope.EngagementId, null, new DateOnly(2026, 12, 31),
          new DateOnly(2027, 1, 20), "Major customer entered administration in January 2027.",
          SubsequentEventClassifications.NonAdjusting, false, true, "Note 27 subsequent events",
          "Conditions arose after the reporting date, so no adjustment is made.", "QAR", 250000m,
          ["news-article-1", "admin-order"]));
      Assert.True(classified.Succeeded);
      Assert.Equal(2, classified.Value!.Revision);
      Assert.Equal(0, classified.Value.PendingEventCount);
      Assert.True((await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "SUBSEQUENT_EVENT", classified.Value.ReviewId)).Succeeded);

      var unsupportedKind = await AuditCompletionService.ReviewCompletionRecordAsync(db, reviewer,
        "NOT_A_KIND", classified.Value.ReviewId);
      Assert.False(unsupportedKind.Succeeded);
    }
  }
}
