using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Accounting;
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
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = reviewerId, Role = "Partner",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = scope.Actor.UserId
    });
    await db.SaveChangesAsync();
    var reviewer = new ActorContext(reviewerId, scope.FirmId, 1, ["Reviewer", "Partner"]);

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

    var glBatchId = Guid.NewGuid();
    var glTransactionId = Guid.NewGuid();
    var glHash = new string('b', 64);
    db.SourceImportBatches.Add(new SourceImportBatch
    {
      Id = glBatchId, FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      PeriodId = Guid.NewGuid(), SourceKind = "GL", ProfileVersion = "gl-v1", ParserVersion = "parser-v1",
      RawFileSha256Hex = glHash, NormalizedDatasetDigest = glHash, LegalEntityKey = "bank-001", Currency = "QAR",
      RowCount = 1, ExpectedChunkCount = 1, ExpectedTransactionCount = 1, ExpectedLineCount = 1,
      AcceptedChunkCount = 1, AcceptedTransactionCount = 1, AcceptedLineCount = 1, Status = "SEALED",
      ReceiptReference = "receipt-bank-ledger-001", CreatedByUserId = scope.Actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    db.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
    {
      Id = glTransactionId, FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      ImportBatchId = glBatchId, StableJournalId = "GL-001", DocumentNumber = "GL-001",
      PostingDate = new DateOnly(2026, 9, 19), SourceUser = "client", SourceSystem = "LEDGER",
      Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
    });
    db.GeneralLedgerLines.Add(new GeneralLedgerLine
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      ImportBatchId = glBatchId, TransactionId = glTransactionId, StableLineId = "GL-001-L1", AccountCode = "1100",
      Debit = 100m, Credit = 0m, OriginalCurrency = "QAR", OriginalAmount = 100m, FunctionalAmount = 100m,
      CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var ledgerSchedule = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor, new CreateScheduleRequest(
      scope.EngagementId, "BANK_LEDGER", "bank-001", "receipt-bank-ledger-001", new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
      new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19), "QAR", "debits positive; credits negative", glHash, 100m,
      [new("ledger-001", 1, "1100", "Operating account ledger", 100m, "QAR", null, new DateOnly(2026, 9, 19), null, null, "{\"source\":\"ledger\"}")],
      glBatchId));
    var statementSchedule = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor, new CreateScheduleRequest(
      scope.EngagementId, "BANK_STATEMENT", "bank-001", "receipt-bank-statement-001", new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
      new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19), "QAR", "debits positive; credits negative", new string('c', 64), 90m,
      [new("statement-001", 1, "1100", "Operating account statement", 90m, "QAR", null, new DateOnly(2026, 9, 19), null, null, "{\"source\":\"statement\"}")]));
    Assert.True(ledgerSchedule.Succeeded);
    Assert.Equal(0m, ledgerSchedule.Value!.Residual);
    Assert.Equal(glBatchId, await db.AuditSchedules.Where(x => x.Id == ledgerSchedule.Value.ScheduleId)
      .Select(x => x.SourceImportBatchId).SingleAsync());
    Assert.Equal(100m, await db.AuditSchedules.Where(x => x.Id == ledgerSchedule.Value.ScheduleId)
      .Select(x => x.GlControlTotal).SingleAsync());
    var wrongEntitySource = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor, new CreateScheduleRequest(
      scope.EngagementId, "BANK_LEDGER", "other-bank", "receipt-bank-ledger-wrong-entity", new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
      new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19), "QAR", "debits positive; credits negative", glHash, 100m,
      [new("ledger-wrong-entity-001", 1, "1100", "Other entity ledger", 100m, "QAR", null, new DateOnly(2026, 9, 19), null, null, "{\"source\":\"ledger\"}")],
      glBatchId));
    Assert.False(wrongEntitySource.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, wrongEntitySource.ErrorCode);
    var mismatchedLedgerSource = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor, new CreateScheduleRequest(
      scope.EngagementId, "BANK_LEDGER", "bank-001", "receipt-bank-ledger-mismatch", new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
      new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19), "QAR", "debits positive; credits negative", glHash, 99m,
      [new("ledger-mismatch-001", 1, "1100", "Operating account ledger", 99m, "QAR", null, new DateOnly(2026, 9, 19), null, null, "{\"source\":\"ledger\"}")],
      glBatchId));
    Assert.False(mismatchedLedgerSource.Succeeded);
    Assert.Equal(ErrorCodes.ManifestMismatch, mismatchedLedgerSource.ErrorCode);
    Assert.True(statementSchedule.Succeeded);
    Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
      new ReviewScheduleRequest(ledgerSchedule.Value!.ScheduleId, "Ledger source reviewed.", true))).Succeeded);
    Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
      new ReviewScheduleRequest(statementSchedule.Value!.ScheduleId, "Statement source reviewed.", true))).Succeeded);
    var bankReconciliation = await AuditFieldworkService.CreateBankReconciliationAsync(db, scope.Actor,
      new CreateBankReconciliationRequest(scope.EngagementId, procedure.Id, ledgerSchedule.Value.ScheduleId,
        statementSchedule.Value.ScheduleId, new DateOnly(2026, 9, 19),
        [
          new("ledger-balance", AuditBankReconciliationItemTypes.Ledger, 100m, "Ledger balance", "receipt-bank-ledger-001", "ledger-source-001", ledgerSchedule.Value.ScheduleId),
          new("statement-balance", AuditBankReconciliationItemTypes.Statement, 90m, "Statement balance", "receipt-bank-statement-001", "statement-source-001", statementSchedule.Value.ScheduleId),
          new("timing-001", AuditBankReconciliationItemTypes.Timing, -10m, "Outstanding cheque", "receipt-bank-statement-001#timing-001", "timing-evidence-001", statementSchedule.Value.ScheduleId)
        ]));
    Assert.True(bankReconciliation.Succeeded, bankReconciliation.ErrorCode + ": " + bankReconciliation.Message);
    Assert.Equal(AuditBankReconciliationStatuses.Reconciled, bankReconciliation.Value!.Status);
    Assert.Equal(0m, bankReconciliation.Value.Residual);
    Assert.Equal(-10m, bankReconciliation.Value.TimingItemTotal);
    Assert.True((await AuditFieldworkService.ReviewBankReconciliationAsync(db, reviewer,
      new ReviewBankReconciliationRequest(bankReconciliation.Value.BankReconciliationId,
        AuditBankReconciliationStatuses.Approved, "Ledger, statement and timing items reconcile at 19 September 2026."))).Succeeded);
    Assert.Equal(AuditBankReconciliationStatuses.Approved,
      await db.AuditBankReconciliations.Where(x => x.Id == bankReconciliation.Value.BankReconciliationId).Select(x => x.Status).SingleAsync());
    var missingProposedJournal = await AuditFieldworkService.CreateBankReconciliationAsync(db, scope.Actor,
      new CreateBankReconciliationRequest(scope.EngagementId, procedure.Id, ledgerSchedule.Value.ScheduleId,
        statementSchedule.Value.ScheduleId, new DateOnly(2026, 9, 19),
        [new("proposed-001", AuditBankReconciliationItemTypes.ProposedCorrection, 5m, "Proposed correction", "bank-workpaper-001", "correction-evidence-001")]));
    Assert.False(missingProposedJournal.Succeeded);

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
      new RecordDifferenceRequest(scope.EngagementId, procedure.Id, "Cash", "KNOWN", "Unpresented cheque timing difference.", -25m, "QAR",
        "materiality-2026", "No qualitative concern identified."));
    Assert.True(difference.Succeeded);
    var differenceMetadata = await db.AuditDifferences.SingleAsync(x => x.Id == difference.Value!.AuditDifferenceId);
    Assert.Equal("materiality-2026", differenceMetadata.MaterialityReference);
    Assert.Equal("No qualitative concern identified.", differenceMetadata.QualitativeConcerns);
    var unlinkedCorrection = await AuditFieldworkService.EvaluateDifferenceAsync(db, reviewer,
      new EvaluateDifferenceRequest(difference.Value!.AuditDifferenceId, true, "Correction claimed.", null, null));
    Assert.False(unlinkedCorrection.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, unlinkedCorrection.ErrorCode);
    Assert.True((await AuditFieldworkService.EvaluateDifferenceAsync(db, reviewer,
      new EvaluateDifferenceRequest(difference.Value!.AuditDifferenceId, false, "Evaluated against the final unadjusted differences schedule.", "Management will not post; assessed in aggregate.", null))).Succeeded);
    var rejected = await AuditFieldworkService.SetDifferenceCorrectionStateAsync(db, reviewer,
      new SetDifferenceCorrectionStateRequest(difference.Value.AuditDifferenceId, AuditDifferenceCorrectionStates.Rejected,
        "Management rejected the proposed correction; the difference remains unadjusted."));
    Assert.True(rejected.Succeeded, rejected.Message);
    Assert.Equal(AuditDifferenceCorrectionStates.Rejected,
      await db.AuditDifferences.Where(x => x.Id == difference.Value.AuditDifferenceId).Select(x => x.CorrectionState).SingleAsync());
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

    var unapprovedAggregate = await AuditFieldworkService.RecordAreaAssessmentAsync(db, scope.Actor,
      new RecordAreaAssessmentRequest(scope.EngagementId, null, AuditAreaCodes.AuditDifferences,
        AuditAreaAssessmentKinds.AggregateDifferences, "audit-differences-aggregate.v1", "{}", null, null,
        null, null, null, null, null, ["aggregate-difference-schedule"], "Prepared conclusion."));
    Assert.False(unapprovedAggregate.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, unapprovedAggregate.ErrorCode);
    var materiality = await AuditPlanningService.CreateMaterialityAssessmentAsync(db, scope.Actor,
      new CreateMaterialityRequest(scope.EngagementId, "Total assets", "AFS-v1", "Stable benchmark",
        1_000_000m, 0.05m, 50_000m, 37_500m, 2_500m, null));
    Assert.True(materiality.Succeeded);
    Assert.True((await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, reviewer,
      materiality.Value!.AssessmentId)).Succeeded);
    var aggregate = await AuditFieldworkService.RecordAreaAssessmentAsync(db, scope.Actor,
      new RecordAreaAssessmentRequest(scope.EngagementId, null, AuditAreaCodes.AuditDifferences,
        AuditAreaAssessmentKinds.AggregateDifferences, "audit-differences-aggregate.v1", "{}", null, null, null,
        null, null, null, null, ["aggregate-difference-schedule"], "Gross QAR differences exceed performance materiality; unadjusted amounts and qualitative factors require partner reporting judgment."));
    Assert.True(aggregate.Succeeded, aggregate.Message);
    var aggregateRow = await db.AuditAreaAssessments.SingleAsync(x => x.Id == aggregate.Value!.AuditAreaAssessmentId);
    Assert.Contains("audit-difference-aggregate.v1", aggregateRow.InputSnapshotJson, StringComparison.Ordinal);
    Assert.True((await AuditFieldworkService.ReviewAreaAssessmentAsync(db, reviewer,
      new ReviewAreaAssessmentRequest(aggregateRow.Id, AuditAreaAssessmentStatuses.Reviewed, null))).Succeeded);

    var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, scope.EngagementId);
    Assert.True(completion.Succeeded);
    Assert.False(completion.Value!.Ready);
    Assert.Contains(completion.Value.Blockers, x => x.StartsWith("procedure:", StringComparison.Ordinal));
    Assert.DoesNotContain("difference-aggregate:missing-stale-or-unreviewed", completion.Value.Blockers);
    Assert.True((await AuditFieldworkService.RecordDifferenceAsync(db, scope.Actor,
      new RecordDifferenceRequest(scope.EngagementId, procedure.Id, "Revenue", "KNOWN", "Later identified cut-off difference.", 100m, "QAR"))).Succeeded);
    var changedSchedule = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, scope.EngagementId);
    Assert.Contains("difference-aggregate:missing-stale-or-unreviewed", changedSchedule.Value!.Blockers);
    Assert.Equal(2, await db.AuditScheduleRows.CountAsync(x => x.ScheduleId == schedule.Value.ScheduleId));
    Assert.Equal(3, await db.AuditDifferences.CountAsync());
  }
}
