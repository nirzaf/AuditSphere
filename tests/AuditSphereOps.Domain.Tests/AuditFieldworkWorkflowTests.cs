using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditFieldworkWorkflowTests
{
  [Fact(DisplayName = "Fieldwork retains signed source rows, reviewed selections, confirmation alternatives and area evidence")]
  public async Task FieldworkEvidence_IsScopedAndReviewable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;
    await using var db = new AuditSphereDbContext(pg.Options);

    var program = await AuditProgramService.PublishAsync(db, scope.Actor,
      new PublishAuditProgramRequest("2026.2", AuditProgramCatalog.SourceHash));
    var adopted = await AuditProgramService.AdoptAsync(db, scope.Actor,
      new AdoptAuditProgramRequest(scope.EngagementId, program.Value!.ProgramVersionId));
    var procedure = await db.AuditProcedures.SingleAsync(x => x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-02-01");
    Assert.True((await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityStatuses.Applicable, null))).Succeeded);

    var reviewerId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = reviewerId, FirmId = scope.FirmId, Subject = "fieldwork-reviewer-" + reviewerId.ToString("N"),
      TenantId = "tenant-planning", Email = "fieldwork-reviewer@example.test", DisplayName = "Fieldwork Reviewer",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = reviewerId, Role = "Reviewer",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = scope.Actor.UserId
    });
    await db.SaveChangesAsync();
    var reviewer = new ActorContext(reviewerId, scope.FirmId, 1, ["Reviewer"]);

    var sourceHash = new string('a', 64);
    var schedule = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor, new CreateScheduleRequest(
      scope.EngagementId, "BANK_RECONCILIATION", "bank-001", "receipt-bank-001", new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
      new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19), "QAR", "debits positive; credits negative", sourceHash, 50m,
      [
        new("row-001", 1, "1100", "Operating account", 75m, "QAR", null, new DateOnly(2026, 9, 19), null, null, "{\"source\":\"statement\"}"),
        new("row-002", 2, "1100", "Outstanding cheque", -25m, "QAR", null, new DateOnly(2026, 9, 19), null, null, "{\"source\":\"statement\"}")
      ]));
    Assert.True(schedule.Succeeded);
    Assert.Equal(AuditScheduleStatuses.Reconciled, schedule.Value!.Status);
    Assert.Equal(50m, schedule.Value.SignedControlTotal);
    var scheduleReview = await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
      new ReviewScheduleRequest(schedule.Value.ScheduleId, "Complete source listing; no unexplained residual.", true));
    Assert.True(scheduleReview.Succeeded, scheduleReview.ErrorCode + ": " + scheduleReview.Message);

    var selection = await AuditFieldworkService.CreateSelectionAsync(db, scope.Actor, new CreateSelectionRequest(
      scope.EngagementId, procedure.Id, schedule.Value.ScheduleId, null, "100 percent of bank accounts", "Both signed rows selected for reconciliation.",
      [new("row-001", 75m, "QAR", "All rows in the identified bank account", null), new("row-002", -25m, "QAR", "All rows in the identified bank account", null)]));
    Assert.True(selection.Succeeded);
    Assert.True((await AuditFieldworkService.ReviewSelectionAsync(db, reviewer,
      new ReviewSelectionRequest(selection.Value!.SelectionId, AuditSelectionStatuses.Reviewed, null))).Succeeded);

    var items = await db.AuditSelectionItems.Where(x => x.SelectionId == selection.Value.SelectionId).ToListAsync();
    foreach (var item in items)
    {
      var test = await AuditFieldworkService.RecordItemTestAsync(db, scope.Actor,
        new RecordItemTestRequest(item.Id, "Agreed the selected row to the bank statement.", ["receipt-bank-001#" + item.StableRowId],
          AuditItemTestResults.Pass, null, null, null));
      Assert.True(test.Succeeded);
      Assert.True((await AuditFieldworkService.ReviewItemTestAsync(db, reviewer,
        new ReviewItemTestRequest(test.Value!.AuditItemTestId, AuditItemTestReviewDecisions.Reviewed, "Evidence agrees."))).Succeeded);
    }
    Assert.Equal(2, await db.AuditItemTestReviews.CountAsync());

    var confirmation = await AuditFieldworkService.CreateConfirmationAsync(db, scope.Actor, new CreateConfirmationRequest(
      scope.EngagementId, procedure.Id, AuditAreaCodes.CashBank, "bank-001", 50m, "QAR", new DateOnly(2026, 9, 19),
      "Bank relationship manager", "Client master contact list reviewed by partner."));
    Assert.True(confirmation.Succeeded);
    Assert.True((await AuditFieldworkService.ApproveConfirmationAsync(db, reviewer, confirmation.Value!.ConfirmationCaseId)).Succeeded);
    Assert.True((await AuditFieldworkService.RecordDispatchEvidenceAsync(db, scope.Actor,
      new RecordConfirmationDispatchRequest(confirmation.Value.ConfirmationCaseId, "provider-dispatch-001"))).Succeeded);
    Assert.True((await AuditFieldworkService.RecordConfirmationResponseAsync(db, scope.Actor,
      new RecordConfirmationResponseRequest(confirmation.Value.ConfirmationCaseId, "DIRECT", "PORTAL", "no-response-2026-09-26",
        null, "No response received by the approved follow-up date.", AuditConfirmationDecisions.NoResponse))).Succeeded);
    var responseId = await db.AuditConfirmationResponses.Select(x => x.Id).SingleAsync();
    Assert.True((await AuditFieldworkService.ReviewConfirmationResponseAsync(db, reviewer,
      new ReviewConfirmationResponseRequest(responseId))).Succeeded);
    var alternative = await AuditFieldworkService.RecordAlternativeProcedureAsync(db, scope.Actor,
      new RecordAlternativeProcedureRequest(confirmation.Value.ConfirmationCaseId, "Inspect subsequent cleared transactions.",
        ["bank-statement-subsequent-001"], "Subsequent clearing supports the recorded balance."));
    Assert.True(alternative.Succeeded);
    Assert.True((await AuditFieldworkService.ReviewAlternativeProcedureAsync(db, reviewer,
      new ReviewAlternativeProcedureRequest(await db.AuditAlternativeProcedures.Select(x => x.Id).SingleAsync(), null))).Succeeded);
    Assert.True((await AuditFieldworkService.CloseConfirmationAsync(db, reviewer,
      new CloseConfirmationRequest(confirmation.Value.ConfirmationCaseId, "Alternative work is adequate."))).Succeeded);

    var assessment = await AuditFieldworkService.RecordAreaAssessmentAsync(db, scope.Actor, new RecordAreaAssessmentRequest(
      scope.EngagementId, procedure.Id, AuditAreaCodes.AnalyticalReview, "MONTHLY_TREND", "methodology:analytics-v1",
      "{\"periods\":12,\"thresholdPercent\":10}", 100m, 102m, 2m, 2m, "QAR",
      new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19), ["analytics-source-001"], "Variance is below the approved investigation threshold."));
    Assert.True(assessment.Succeeded);
    Assert.True((await AuditFieldworkService.ReviewAreaAssessmentAsync(db, reviewer,
      new ReviewAreaAssessmentRequest(assessment.Value!.AuditAreaAssessmentId, AuditAreaAssessmentStatuses.Reviewed, null))).Succeeded);

    var difference = await AuditFieldworkService.RecordDifferenceAsync(db, scope.Actor,
      new RecordDifferenceRequest(scope.EngagementId, procedure.Id, "Cash", "KNOWN", "Unpresented cheque timing difference.", -25m, "QAR"));
    Assert.True(difference.Succeeded);
    var unlinkedCorrection = await AuditFieldworkService.EvaluateDifferenceAsync(db, reviewer,
      new EvaluateDifferenceRequest(difference.Value!.AuditDifferenceId, true, "Correction claimed.", null, null));
    Assert.False(unlinkedCorrection.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, unlinkedCorrection.ErrorCode);
    Assert.True((await AuditFieldworkService.EvaluateDifferenceAsync(db, reviewer,
      new EvaluateDifferenceRequest(difference.Value!.AuditDifferenceId, false, "Evaluated against the final unadjusted differences schedule.", "Management will not post; assessed in aggregate.", null))).Succeeded);
    var offsettingDifference = await AuditFieldworkService.RecordDifferenceAsync(db, scope.Actor,
      new RecordDifferenceRequest(scope.EngagementId, procedure.Id, "Cash", "KNOWN", "Offsetting bank timing difference.", 25m, "QAR"));
    Assert.True(offsettingDifference.Succeeded);
    var summaries = await AuditFieldworkService.GetDifferenceSummariesAsync(db, reviewer, scope.EngagementId);
    Assert.True(summaries.Succeeded);
    var qar = Assert.Single(summaries.Value!);
    Assert.Equal("QAR", qar.Currency);
    Assert.Equal(2, qar.DifferenceCount);
    Assert.Equal(50m, qar.GrossAmount);
    Assert.Equal(0m, qar.SignedNetAmount);
    Assert.Equal(50m, qar.UnadjustedGrossAmount);
    Assert.Equal(0m, qar.UnadjustedSignedNetAmount);
    Assert.Equal(0m, qar.CorrectedGrossAmount);

    var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, scope.EngagementId);
    Assert.True(completion.Succeeded);
    Assert.False(completion.Value!.Ready);
    Assert.Contains(completion.Value.Blockers, x => x.StartsWith("procedure:", StringComparison.Ordinal));
    Assert.Equal(2, await db.AuditScheduleRows.CountAsync(x => x.ScheduleId == schedule.Value.ScheduleId));
    Assert.Equal(2, await db.AuditDifferences.CountAsync());
  }
}
