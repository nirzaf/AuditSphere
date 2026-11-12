using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NativeSourceWorker = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Api.Tests;

public sealed partial class CompletionVarianceCommandsApiTests
{
  [Fact]
  public async Task DraftPeriodSourceAcceptanceAndCloseReopenCurrentnessUseSupportedTransitions()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var accountingPreparer = await ProvisionClientRoleAsync(pg, world.Administrator,
      world.Primary.ClientId, "AccountingPreparer", "period-compat-accounting-preparer");
    var partner = await ProvisionFirmPartnerAsync(pg, world.Administrator, "period-compat-partner");
    Guid periodId, datasetId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var period = await ClientAccountingService.CreatePeriodAsync(db, accountingPreparer,
        new(world.Primary.ClientId, "PERIOD-COMPAT-2026", new(2026, 1, 1), new(2026, 12, 31), "STATUTORY", "QAR"));
      Assert.True(period.Succeeded, period.Message);
      periodId = period.Value;
      Assert.Equal(AccountingWorkflowStates.Draft,
        await db.ClientReportingPeriods.Where(x => x.Id == periodId).Select(x => x.Status).SingleAsync());
      const string csv = "AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode\n" +
        "1000,Cash,10000,QAR,TEST,CASH\n3000,Equity,-5000,QAR,TEST,EQUITY\n" +
        "4000,Revenue,-10000,QAR,TEST,REVENUE\n5000,Expense,5000,QAR,TEST,EXPENSE\n";
      var imported = await TrialBalanceImportService.ImportAsync(db, accountingPreparer,
        world.Primary.ClientId, world.Primary.EngagementId, csv, new(periodId, null, "STATUTORY"));
      Assert.True(imported.Succeeded, imported.Message);
      datasetId = imported.Value;
    }

    var factory = new OperationContextFactory(new R03DbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var options = new WorkerOptions(world.Primary.FirmId, "Test");
    var handler = new TrialBalanceValidationHandler();
    var worker = new NativeSourceWorker(new OperationDispatcher(store,
      new DurableOperationRegistry([handler], options), options),
      [new TrialBalanceDiscovery(factory, store, handler, options)], NullLogger<NativeSourceWorker>.Instance);
    Assert.True(await worker.ProcessNextAsync());

    string beforeCloseRevision;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var read = await SourceAcceptanceWorkspace.GetAsync(db, world.Reviewer, datasetId);
      Assert.True(read.Succeeded, read.Message);
      Assert.True(read.Value!.CanAccept, read.Value.Blocker);
      beforeCloseRevision = read.Value.Revision;
      var closed = await ClientAccountingService.ClosePeriodAsync(db, partner, periodId, "Synthetic period closure");
      Assert.True(closed.Succeeded, closed.Message);
      var refused = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, world.Reviewer,
        new(world.Primary.ClientId, world.Primary.EngagementId, AccountingSourceKinds.TrialBalance,
          datasetId, null, "Synthetic source evidence", beforeCloseRevision, Reviewed: true));
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
      Assert.Empty(await db.SourceAcceptanceDecisions.ToListAsync());
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var reopened = await ClientAccountingService.ReopenPeriodAsync(db, partner, periodId,
        "Reopen synthetic period for currentness verification");
      Assert.True(reopened.Succeeded, reopened.Message);
      var stale = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, world.Reviewer,
        new(world.Primary.ClientId, world.Primary.EngagementId, AccountingSourceKinds.TrialBalance,
          datasetId, null, "Synthetic source evidence", beforeCloseRevision, Reviewed: true));
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
      var current = await SourceAcceptanceWorkspace.GetAsync(db, world.Reviewer, datasetId);
      Assert.True(current.Succeeded, current.Message);
      Assert.True(current.Value!.CanAccept, current.Value.Blocker);
      Assert.NotEqual(beforeCloseRevision, current.Value.Revision);
      Assert.True((await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, world.Reviewer,
        new(world.Primary.ClientId, world.Primary.EngagementId, AccountingSourceKinds.TrialBalance,
          datasetId, null, "Synthetic source evidence", current.Value.Revision, Reviewed: true))).Succeeded);
      Assert.Equal(AccountingWorkflowStates.Draft,
        await db.ClientReportingPeriods.Where(x => x.Id == periodId).Select(x => x.Status).SingleAsync());
      Assert.Single(await db.SourceAcceptanceDecisions.ToListAsync());
    }
  }

  [Fact]
  public async Task VariancePreparationAndIndependentReviewUseExactEngagementEvidenceAndRecoverExactOutcomes()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var preparer = world.Primary.Preparer;
    var reviewer = world.Reviewer;
    var programReviewer = await ProvisionFirmPartnerAsync(pg, world.Administrator, "r03-program-reviewer");
    const string evidenceReference = "r03-supported-current-procedure-evidence";
    Guid procedureId;

    // Build planning and submitted procedure evidence through the existing application owners.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var draft = await AuditProgramService.PrepareControlledAuditProgramAsync(db, world.Administrator,
        new("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(draft.Succeeded, draft.Message);
      var published = await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db, programReviewer,
        new(draft.Value!.ProgramVersionId, draft.Value.ContentDigest!, "synthetic-R03-controlled-program-review"));
      Assert.True(published.Succeeded, published.Message);
      var adopted = await AuditProgramService.AdoptAsync(db, preparer,
        new(world.Primary.EngagementId, published.Value!.ProgramVersionId,
          AuditProgramCatalog.SourceHash, published.Value.ContentDigest));
      Assert.True(adopted.Succeeded, adopted.Message);

      await EstablishVariancePlanningBasisAsync(pg, world, preparer, reviewer, programReviewer);
      var procedure = await db.AuditProcedures.SingleAsync(x => x.EngagementId == world.Primary.EngagementId &&
        x.SourceProcedureId == "AWP-01-01");
      procedureId = procedure.Id;
      var applicability = await AuditProgramService.DecideApplicabilityAsync(db, preparer,
        new(procedureId, AuditApplicabilityActions.Applicable,
          "Synthetic engagement evidence is in scope for this analytical review.",
          procedure.CurrentApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.CreateVersion7()));
      Assert.True(applicability.Succeeded, applicability.Message);
      var procedureState = await AuditProcedureCommandWorkspace.StateAsync(db, preparer, procedureId);
      Assert.True(procedureState.Succeeded, procedureState.Message);
      Assert.True(procedureState.Value!.Basis.CanSubmit);
      var submitted = await AuditProgramService.SubmitResultAsync(db, preparer,
        new(procedureId, procedureState.Value.Basis.InputGeneration,
          "Inspected synthetic revenue support for the selected engagement period.",
          "{\"observation\":\"synthetic support inspected\"}", [evidenceReference],
          "The selected evidence was inspected."));
      Assert.True(submitted.Succeeded, submitted.Message);
      Assert.NotEqual(Guid.Empty, submitted.Value!.AuditProcedureResultId);
    }

    // R11 connects supported producers to the existing readiness boundary, not a fabricated clearance.
    async Task<AuditCompletionEvaluation> CompletionAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var result = await AuditFieldworkService.EvaluateCompletionAsync(db, programReviewer, world.Primary.EngagementId);
      Assert.True(result.Succeeded, result.Message);
      return result.Value!;
    }
    var missing = await CompletionAsync();
    Assert.False(missing.Ready);
    Assert.Contains("analytical-review:missing", missing.Blockers);
    Assert.Contains("going-concern:missing", missing.Blockers);
    var unrelatedBlockers = missing.Blockers.Where(x =>
      !x.StartsWith("analytical-review:", StringComparison.Ordinal) &&
      !x.StartsWith("going-concern:", StringComparison.Ordinal)).ToArray();
    Assert.NotEmpty(unrelatedBlockers);
    Assert.Contains(unrelatedBlockers, x => x.StartsWith("procedure:", StringComparison.Ordinal));
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var task = await EngagementWorkflowTaskService.CreateAsync(db, preparer, world.Primary.EngagementId,
        new("Synthetic completion evidence checked", WorkflowTaskPhases.ManagerReview, 1,
          AssigneeUserId: preparer.UserId, IdempotencyKey: Guid.CreateVersion7()));
      Assert.True(task.Succeeded, task.Message);
      var tasks = await EngagementWorkflowTaskService.GetAsync(db, preparer, world.Primary.EngagementId, task.Value);
      Assert.True(tasks.Succeeded, tasks.Message);
      var row = Assert.Single(tasks.Value!.Tasks);
      Assert.True(row.CanComplete);
      var completed = await EngagementWorkflowTaskService.CompleteAsync(db, preparer, task.Value, row.Revision);
      Assert.True(completed.Succeeded, completed.Message);
      Assert.Equal(PracticeTimeStates.TaskCompleted,
        await db.WorkTasks.Where(x => x.Id == task.Value).Select(x => x.Status).SingleAsync());
    }
    var checkedOnly = await CompletionAsync();
    Assert.False(checkedOnly.Ready);
    Assert.Equal(missing.Blockers.OrderBy(x => x), checkedOnly.Blockers.OrderBy(x => x));

    var preparerUser = await UserAsync(pg, preparer.UserId);
    using var preparerFactory = Factory(pg, preparerUser);
    using var preparerClient = preparerFactory.CreateClient(new() { AllowAutoRedirect = false });
    var preparerCsrf = await SignIn(preparerClient);
    var root = $"/api/ui/engagements/{world.Primary.EngagementId:D}/completion/variance-investigations";
    using var stateResponse = await preparerClient.GetAsync(root + "/state?accountArea=REVENUE&periodReference=2026-Q1");
    Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
    using var stateJson = JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync());
    var state = stateJson.RootElement;
    Assert.True(state.GetProperty("canPrepare").GetBoolean());
    Assert.True(!state.TryGetProperty("record", out var emptyRecord) || emptyRecord.ValueKind == JsonValueKind.Null);

    var siblingUser = await UserAsync(pg, world.Sibling.Preparer.UserId);
    using var siblingFactory = Factory(pg, siblingUser);
    using var siblingClient = siblingFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(siblingClient);
    using (var siblingRead = await siblingClient.GetAsync(root + "/state?accountArea=REVENUE&periodReference=2026-Q1"))
      Assert.Equal(HttpStatusCode.Forbidden, siblingRead.StatusCode);

    var requestId = Guid.CreateVersion7();
    var fields = new VarianceInvestigationFields(procedureId, "REVENUE", "2026-Q1", 100m, 150m, 10m,
      "QAR", null, [evidenceReference]);
    var prepare = new VarianceInvestigationCommandRequest(requestId, state.GetProperty("expectedRevision").GetString()!,
      state.GetProperty("reviewBasis").GetString()!, fields);
    using (var wrongProcedure = await Post(preparerClient, preparerCsrf, root + "/prepare/preview",
      prepare with { RequestId = Guid.CreateVersion7(), Fields = fields with { ProcedureId = Guid.CreateVersion7() } }))
      Assert.Equal(HttpStatusCode.Forbidden, wrongProcedure.StatusCode);
    using (var wrongProcedureWithoutEvidence = await Post(preparerClient, preparerCsrf, root + "/prepare/preview",
      prepare with { RequestId = Guid.CreateVersion7(), Fields = fields with { ProcedureId = Guid.CreateVersion7(), EvidenceReferences = [] } }))
      Assert.Equal(HttpStatusCode.Forbidden, wrongProcedureWithoutEvidence.StatusCode);
    using (var wrongEvidence = await Post(preparerClient, preparerCsrf, root + "/prepare/preview",
      prepare with { RequestId = Guid.CreateVersion7(), Fields = fields with { EvidenceReferences = ["sibling-engagement-evidence"] } }))
      Assert.Equal(HttpStatusCode.Forbidden, wrongEvidence.StatusCode);
    using (var invalidCsrf = await Post(preparerClient, "invalid", root + "/prepare/preview", prepare))
      Assert.Equal(HttpStatusCode.Forbidden, invalidCsrf.StatusCode);
    using var previewResponse = await Post(preparerClient, preparerCsrf, root + "/prepare/preview", prepare);
    Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
    using var previewJson = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
    var preview = previewJson.RootElement.GetProperty("value");
    Assert.True(preview.GetProperty("canProceed").GetBoolean());
    Assert.Equal("PREPARE", preview.GetProperty("action").GetString());
    Assert.Equal("UNEXPLAINED", preview.GetProperty("summary").GetString()!.Split(' ').Last().TrimEnd('.'));
    var staleRequest = prepare with { RequestId = Guid.CreateVersion7() };
    using var stalePreviewResponse = await Post(preparerClient, preparerCsrf, root + "/prepare/preview", staleRequest);
    Assert.Equal(HttpStatusCode.OK, stalePreviewResponse.StatusCode);
    using var stalePreviewJson = JsonDocument.Parse(await stalePreviewResponse.Content.ReadAsStringAsync());
    var stalePreview = stalePreviewJson.RootElement.GetProperty("value");
    Assert.True(stalePreview.GetProperty("canProceed").GetBoolean());

    var unassented = prepare with { Reviewed = false, ExpectedRequestHash = preview.GetProperty("requestHash").GetString() };
    using (var refusedAssent = await Post(preparerClient, preparerCsrf, root + "/prepare", unassented))
      Assert.Equal(HttpStatusCode.BadRequest, refusedAssent.StatusCode);
    using (var createdResponse = await Post(preparerClient, preparerCsrf, root + "/prepare",
      prepare with { Reviewed = true, ExpectedRequestHash = preview.GetProperty("requestHash").GetString() }))
    {
      Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
      using var createdJson = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync());
      var receipt = createdJson.RootElement.GetProperty("value");
      Assert.Equal(requestId, receipt.GetProperty("requestId").GetGuid());
      Assert.Equal("PREPARE", receipt.GetProperty("action").GetString());
      var result = receipt.GetProperty("result");
      Assert.Equal(world.Primary.EngagementId, result.GetProperty("engagementId").GetGuid());
      Assert.Equal("UNEXPLAINED", result.GetProperty("conclusion").GetString());
      Assert.Null(result.GetProperty("reviewedByUserId").GetGuidOrNull());
      var varianceId = receipt.GetProperty("varianceInvestigationId").GetGuid();

      using var staleWrite = await Post(preparerClient, preparerCsrf, root + "/prepare",
        staleRequest with { Reviewed = true, ExpectedRequestHash = stalePreview.GetProperty("requestHash").GetString() });
      Assert.Equal(HttpStatusCode.Conflict, staleWrite.StatusCode);
      using var staleWriteJson = JsonDocument.Parse(await staleWrite.Content.ReadAsStringAsync());
      Assert.Equal(ErrorCodes.GenerationStale, staleWriteJson.RootElement.GetProperty("code").GetString());

      using var replay = await Post(preparerClient, preparerCsrf, root + "/prepare",
        prepare with { Reviewed = true, ExpectedRequestHash = preview.GetProperty("requestHash").GetString() });
      Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
      using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
      Assert.Equal(receipt.GetProperty("id").GetGuid(), replayJson.RootElement.GetProperty("value").GetProperty("id").GetGuid());

      using (var changedRevisionReplay = await Post(preparerClient, preparerCsrf, root + "/prepare",
        prepare with { Reviewed = true, ExpectedRevision = "999", ExpectedRequestHash = preview.GetProperty("requestHash").GetString() }))
      {
        Assert.Equal(HttpStatusCode.Conflict, changedRevisionReplay.StatusCode);
        using var changedRevisionJson = JsonDocument.Parse(await changedRevisionReplay.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.IdempotencyConflict, changedRevisionJson.RootElement.GetProperty("code").GetString());
      }
      await using (var replayProof = new AuditSphereDbContext(pg.Options))
      {
        Assert.Equal(1, await replayProof.AuditCompletionVarianceCommandReceipts.CountAsync(x => x.RequestId == requestId));
        Assert.Equal(1, await replayProof.AnalyticalReviewVarianceInvestigations.CountAsync(x => x.Id == varianceId));
        var unchanged = await replayProof.AnalyticalReviewVarianceInvestigations.AsNoTracking().SingleAsync(x => x.Id == varianceId);
        Assert.Equal(1, unchanged.Revision);
        Assert.Equal(VarianceInvestigationConclusions.Unexplained, unchanged.Conclusion);
        Assert.Null(unchanged.ReviewedByUserId);
      }

      var exactLookupUrl = root + $"/receipts/{requestId:D}?requestHash={receipt.GetProperty("requestHash").GetString()}";
      using var exactLookup = await preparerClient.GetAsync(exactLookupUrl);
      Assert.Equal(HttpStatusCode.OK, exactLookup.StatusCode);
      using var exactLookupJson = JsonDocument.Parse(await exactLookup.Content.ReadAsStringAsync());
      Assert.True(exactLookupJson.RootElement.GetProperty("found").GetBoolean());
      Assert.Equal(receipt.GetProperty("id").GetGuid(), exactLookupJson.RootElement.GetProperty("receipt").GetProperty("id").GetGuid());

      var recordCountBeforeScopeRefusal = 0;
      var receiptCountBeforeScopeRefusal = 0;
      await using (var proof = new AuditSphereDbContext(pg.Options))
      {
        recordCountBeforeScopeRefusal = await proof.AnalyticalReviewVarianceInvestigations.CountAsync(x => x.EngagementId == world.Primary.EngagementId);
        receiptCountBeforeScopeRefusal = await proof.AuditCompletionVarianceCommandReceipts.CountAsync(x => x.EngagementId == world.Primary.EngagementId);
      }
      using var wrongScopeLookup = await siblingClient.GetAsync(exactLookupUrl);
      Assert.Equal(HttpStatusCode.Forbidden, wrongScopeLookup.StatusCode);
      await using (var proof = new AuditSphereDbContext(pg.Options))
      {
        Assert.Equal(recordCountBeforeScopeRefusal,
          await proof.AnalyticalReviewVarianceInvestigations.CountAsync(x => x.EngagementId == world.Primary.EngagementId));
        Assert.Equal(receiptCountBeforeScopeRefusal,
          await proof.AuditCompletionVarianceCommandReceipts.CountAsync(x => x.EngagementId == world.Primary.EngagementId));
      }

      // Keep a planning grant after review access is narrowed so the command replay
      // must independently reauthorize its mutating REVIEW action.
      await using (var grantDb = new AuditSphereDbContext(pg.Options))
      {
        var planningGrant = await RoleAdministrationService.ApplyRoleGrantAsync(grantDb, world.Administrator,
          new(reviewer.UserId, "Staff", "ENGAGEMENT", world.Primary.ClientId, world.Primary.EngagementId));
        Assert.True(planningGrant.Succeeded, planningGrant.Message);
      }
      var reviewerUser = await UserAsync(pg, reviewer.UserId);
      reviewer = new(reviewerUser.Id, reviewerUser.FirmId, reviewerUser.SessionEpoch, ["Manager", "Staff"],
        world.Primary.ClientId, world.Primary.EngagementId);
      using var reviewerFactory = Factory(pg, reviewerUser);
      using var reviewerClient = reviewerFactory.CreateClient(new() { AllowAutoRedirect = false });
      var reviewerCsrf = await SignIn(reviewerClient);
      var reviewRoot = root + $"/{varianceId:D}/review";
      using var reviewerStateResponse = await reviewerClient.GetAsync(root + "/state?accountArea=REVENUE&periodReference=2026-Q1");
      Assert.Equal(HttpStatusCode.OK, reviewerStateResponse.StatusCode);
      using var reviewerStateJson = JsonDocument.Parse(await reviewerStateResponse.Content.ReadAsStringAsync());
      var reviewerState = reviewerStateJson.RootElement;
      var reviewRequest = new VarianceInvestigationCommandRequest(Guid.CreateVersion7(),
        reviewerState.GetProperty("expectedRevision").GetString()!, reviewerState.GetProperty("reviewBasis").GetString()!);
      using var blockedReviewPreview = await Post(reviewerClient, reviewerCsrf, reviewRoot + "/preview", reviewRequest);
      Assert.Equal(HttpStatusCode.OK, blockedReviewPreview.StatusCode);
      using var blockedPreviewJson = JsonDocument.Parse(await blockedReviewPreview.Content.ReadAsStringAsync());
      Assert.False(blockedPreviewJson.RootElement.GetProperty("value").GetProperty("canProceed").GetBoolean());
      using var blockedReview = await Post(reviewerClient, reviewerCsrf, reviewRoot,
        reviewRequest with { Reviewed = true, ExpectedRequestHash = blockedPreviewJson.RootElement.GetProperty("value").GetProperty("requestHash").GetString() });
      Assert.Equal(HttpStatusCode.BadRequest, blockedReview.StatusCode);
      using var blockedJson = JsonDocument.Parse(await blockedReview.Content.ReadAsStringAsync());
      Assert.Equal(ErrorCodes.GateBlocked, blockedJson.RootElement.GetProperty("code").GetString());

      await using (var proof = new AuditSphereDbContext(pg.Options))
      {
        var unchanged = await proof.AnalyticalReviewVarianceInvestigations.AsNoTracking().SingleAsync(x => x.Id == varianceId);
        Assert.Equal(1, unchanged.Revision);
        Assert.Null(unchanged.ReviewedByUserId);
        Assert.Equal(1, await proof.AuditCompletionVarianceCommandReceipts.CountAsync(x => x.VarianceInvestigationId == varianceId));
      }

      // Resolve the same record with the same exact-engagement evidence, then review its current revision independently.
      using var latestStateResponse = await preparerClient.GetAsync(root + "/state?accountArea=REVENUE&periodReference=2026-Q1");
      using var latestStateJson = JsonDocument.Parse(await latestStateResponse.Content.ReadAsStringAsync());
      var latestState = latestStateJson.RootElement;
      var reviseRequest = new VarianceInvestigationCommandRequest(Guid.CreateVersion7(),
        latestState.GetProperty("expectedRevision").GetString()!, latestState.GetProperty("reviewBasis").GetString()!,
        fields with { Explanation = "Reconciled the difference to the inspected synthetic revenue schedule." });
      using var revisePreviewResponse = await Post(preparerClient, preparerCsrf, root + "/prepare/preview", reviseRequest);
      Assert.Equal(HttpStatusCode.OK, revisePreviewResponse.StatusCode);
      using var revisePreviewJson = JsonDocument.Parse(await revisePreviewResponse.Content.ReadAsStringAsync());
      var revisePreview = revisePreviewJson.RootElement.GetProperty("value");
      using var revisedResponse = await Post(preparerClient, preparerCsrf, root + "/prepare",
        reviseRequest with { Reviewed = true, ExpectedRequestHash = revisePreview.GetProperty("requestHash").GetString() });
      Assert.Equal(HttpStatusCode.OK, revisedResponse.StatusCode);

      using var exactReviewerStateResponse = await reviewerClient.GetAsync(root + "/state?accountArea=REVENUE&periodReference=2026-Q1");
      using var exactReviewerStateJson = JsonDocument.Parse(await exactReviewerStateResponse.Content.ReadAsStringAsync());
      var exactReviewerState = exactReviewerStateJson.RootElement;
      var finalReviewRequest = new VarianceInvestigationCommandRequest(Guid.CreateVersion7(),
        exactReviewerState.GetProperty("expectedRevision").GetString()!, exactReviewerState.GetProperty("reviewBasis").GetString()!);
      using var finalPreviewResponse = await Post(reviewerClient, reviewerCsrf, reviewRoot + "/preview", finalReviewRequest);
      Assert.Equal(HttpStatusCode.OK, finalPreviewResponse.StatusCode);
      using var finalPreviewJson = JsonDocument.Parse(await finalPreviewResponse.Content.ReadAsStringAsync());
      var finalPreview = finalPreviewJson.RootElement.GetProperty("value");
      Assert.True(finalPreview.GetProperty("canProceed").GetBoolean());
      using var reviewedResponse = await Post(reviewerClient, reviewerCsrf, reviewRoot,
        finalReviewRequest with { Reviewed = true, ExpectedRequestHash = finalPreview.GetProperty("requestHash").GetString() });
      Assert.Equal(HttpStatusCode.OK, reviewedResponse.StatusCode);
      using var reviewedJson = JsonDocument.Parse(await reviewedResponse.Content.ReadAsStringAsync());
      var reviewReceipt = reviewedJson.RootElement.GetProperty("value");
      Assert.Equal("REVIEW", reviewReceipt.GetProperty("action").GetString());
      Assert.Equal("CORROBORATED", reviewReceipt.GetProperty("result").GetProperty("conclusion").GetString());
      Assert.Equal("2", reviewReceipt.GetProperty("result").GetProperty("reviewedRevision").GetString());

      using var reviewReplay = await Post(reviewerClient, reviewerCsrf, reviewRoot,
        finalReviewRequest with { Reviewed = true, ExpectedRequestHash = finalPreview.GetProperty("requestHash").GetString() });
      Assert.Equal(HttpStatusCode.OK, reviewReplay.StatusCode);
      using var reviewReplayJson = JsonDocument.Parse(await reviewReplay.Content.ReadAsStringAsync());
      Assert.Equal(reviewReceipt.GetProperty("id").GetGuid(), reviewReplayJson.RootElement.GetProperty("value").GetProperty("id").GetGuid());

      var varianceReady = await CompletionAsync();
      Assert.DoesNotContain(varianceReady.Blockers, x => x.StartsWith("analytical-review:", StringComparison.Ordinal));
      Assert.Contains("going-concern:missing", varianceReady.Blockers);
      Assert.False(varianceReady.Ready);

      // Use the same current procedure evidence and independently authenticated actors for going concern.
      var concernRoot = $"/api/ui/engagements/{world.Primary.EngagementId:D}/completion/going-concern-assessments";
      var assessmentDate = new DateOnly(2026, 12, 31);
      async Task<GoingConcernAssessmentCommandState> ConcernStateAsync(HttpClient client)
      {
        using var response = await client.GetAsync(concernRoot + $"/state?assessmentDate={assessmentDate:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GoingConcernAssessmentCommandState>())!;
      }
      async Task<T> CommandValueAsync<T>(HttpResponseMessage response)
      {
        using (response)
        {
          var body = await response.Content.ReadAsStringAsync();
          Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
          using var json = JsonDocument.Parse(body);
          return json.RootElement.GetProperty("value").Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
      }
      var concernState = await ConcernStateAsync(preparerClient);
      var concernRequest = new GoingConcernAssessmentCommandRequest(Guid.CreateVersion7(),
        concernState.ExpectedRevision, concernState.ReviewBasis,
        new(procedureId, assessmentDate, assessmentDate.AddMonths(12),
          "Synthetic forecast inspected against current procedure evidence.", false, true, "QAR",
          "Synthetic practitioner assessment for this connected test only.", [evidenceReference]));
      var concernPreview = await CommandValueAsync<GoingConcernAssessmentCommandPreview>(
        await Post(preparerClient, preparerCsrf, concernRoot + "/prepare/preview", concernRequest));
      Assert.True(concernPreview.CanProceed);
      var concernReceipt = await CommandValueAsync<GoingConcernAssessmentCommandReceiptView>(
        await Post(preparerClient, preparerCsrf, concernRoot + "/prepare",
          concernRequest with { Reviewed = true, ExpectedRequestHash = concernPreview.RequestHash }));
      Assert.Null(concernReceipt.Result.ReviewedByUserId);
      var unreviewedConcern = await CompletionAsync();
      Assert.Contains("going-concern:unreviewed", unreviewedConcern.Blockers);
      Assert.DoesNotContain("going-concern:missing", unreviewedConcern.Blockers);
      Assert.False(unreviewedConcern.Ready);
      var concernReviewState = await ConcernStateAsync(reviewerClient);
      var concernReviewRequest = new GoingConcernAssessmentCommandRequest(Guid.CreateVersion7(),
        concernReviewState.ExpectedRevision, concernReviewState.ReviewBasis);
      var concernReviewRoot = concernRoot + $"/{concernReceipt.GoingConcernAssessmentId:D}/review";
      var concernReviewPreview = await CommandValueAsync<GoingConcernAssessmentCommandPreview>(
        await Post(reviewerClient, reviewerCsrf, concernReviewRoot + "/preview", concernReviewRequest));
      Assert.True(concernReviewPreview.CanProceed);
      var concernReviewed = await CommandValueAsync<GoingConcernAssessmentCommandReceiptView>(
        await Post(reviewerClient, reviewerCsrf, concernReviewRoot,
          concernReviewRequest with { Reviewed = true, ExpectedRequestHash = concernReviewPreview.RequestHash }));
      Assert.Equal(concernReceipt.GoingConcernAssessmentId, concernReviewed.GoingConcernAssessmentId);
      Assert.NotEqual(preparer.UserId, reviewer.UserId);
      Assert.Equal(reviewer.UserId, concernReviewed.Result.ReviewedByUserId);
      Assert.Equal("1", concernReviewed.Result.ReviewedRevision);
      var correspondingReady = await CompletionAsync();
      Assert.DoesNotContain(correspondingReady.Blockers, x =>
        x.StartsWith("analytical-review:", StringComparison.Ordinal) || x.StartsWith("going-concern:", StringComparison.Ordinal));
      Assert.False(correspondingReady.Ready);
      Assert.Equal(unrelatedBlockers.OrderBy(x => x), correspondingReady.Blockers.OrderBy(x => x));

      // Apply the other evidence producers to the same statutory-audit scope and independent reviewer.
      var completionRoot = $"/api/ui/engagements/{world.Primary.EngagementId:D}/completion";
      var eventRoot = completionRoot + "/subsequent-events";
      var eventState = (await preparerClient.GetFromJsonAsync<SubsequentEventReviewCommandState>(eventRoot + "/state"))!;
      var eventRequest = new SubsequentEventReviewCommandRequest(Guid.CreateVersion7(), eventState.ExpectedRevision, eventState.ReviewBasis,
        new(procedureId, assessmentDate, assessmentDate.AddDays(20), "Synthetic post-period event",
          SubsequentEventClassifications.NonAdjusting, false, true, "Note 27", "The synthetic condition arose after period end.",
          "QAR", "125.123456", [evidenceReference]));
      var eventPreview = await CommandValueAsync<SubsequentEventReviewCommandPreview>(
        await Post(preparerClient, preparerCsrf, eventRoot + "/prepare/preview", eventRequest));
      Assert.True(eventPreview.CanProceed);
      var eventPrepared = await CommandValueAsync<SubsequentEventReviewCommandReceiptView>(
        await Post(preparerClient, preparerCsrf, eventRoot + "/prepare",
          eventRequest with { Reviewed = true, ExpectedRequestHash = eventPreview.RequestHash }));
      Assert.Null(eventPrepared.Result.ReviewedByUserId);
      var eventId = eventPrepared.SubsequentEventReviewId;
      var eventReviewState = (await reviewerClient.GetFromJsonAsync<SubsequentEventReviewCommandState>(
        eventRoot + $"/state?selectedRecordId={eventId:D}"))!;
      var eventReview = new SubsequentEventReviewCommandRequest(Guid.CreateVersion7(), eventReviewState.ExpectedRevision,
        eventReviewState.ReviewBasis, SelectedRecordId: eventId);
      var eventReviewRoot = eventRoot + $"/{eventId:D}/review";
      var eventReviewPreview = await CommandValueAsync<SubsequentEventReviewCommandPreview>(
        await Post(reviewerClient, reviewerCsrf, eventReviewRoot + "/preview", eventReview));
      Assert.True(eventReviewPreview.CanProceed);
      var eventReviewed = await CommandValueAsync<SubsequentEventReviewCommandReceiptView>(
        await Post(reviewerClient, reviewerCsrf, eventReviewRoot,
          eventReview with { Reviewed = true, ExpectedRequestHash = eventReviewPreview.RequestHash }));
      Assert.Equal((eventId, reviewer.UserId, "1"),
        (eventReviewed.SubsequentEventReviewId, eventReviewed.Result.ReviewedByUserId!.Value, eventReviewed.Result.ReviewedRevision));

      var openingRoot = completionRoot + "/opening-balances";
      var openingDate = new DateOnly(2026, 1, 1);
      var openingStatePath = openingRoot + $"/state?asOfDate={openingDate:yyyy-MM-dd}";
      var openingState = (await preparerClient.GetFromJsonAsync<OpeningBalanceCommandState>(openingStatePath))!;
      var openingRequest = new OpeningBalanceCommandRequest(Guid.CreateVersion7(), openingState.ExpectedRevision, openingState.ReviewBasis,
        new(procedureId, null, "Synthetic prior signed statements", openingDate, "QAR", "1000.123456", "1000.123456",
          true, "Synthetic opening balances agree to the prior statements.", [evidenceReference]));
      var openingPreview = await CommandValueAsync<OpeningBalanceCommandPreview>(
        await Post(preparerClient, preparerCsrf, openingRoot + "/prepare/preview", openingRequest));
      Assert.True(openingPreview.CanProceed);
      var openingPrepared = await CommandValueAsync<OpeningBalanceCommandReceiptView>(
        await Post(preparerClient, preparerCsrf, openingRoot + "/prepare",
          openingRequest with { Reviewed = true, ExpectedRequestHash = openingPreview.RequestHash }));
      Assert.False(openingPrepared.Result.CurrentIndependentReview);
      var openingId = openingPrepared.VerificationId;
      var openingReviewState = (await reviewerClient.GetFromJsonAsync<OpeningBalanceCommandState>(openingStatePath))!;
      var openingReview = new OpeningBalanceCommandRequest(Guid.CreateVersion7(), openingReviewState.ExpectedRevision,
        openingReviewState.ReviewBasis, ExpectedVerificationId: openingId, Comment: "Synthetic independent review of the exact opening evidence.");
      var openingReviewRoot = openingRoot + $"/{openingId:D}/review";
      var openingReviewPreview = await CommandValueAsync<OpeningBalanceCommandPreview>(
        await Post(reviewerClient, reviewerCsrf, openingReviewRoot + "/preview", openingReview));
      Assert.True(openingReviewPreview.CanProceed);
      var openingReviewed = await CommandValueAsync<OpeningBalanceCommandReceiptView>(
        await Post(reviewerClient, reviewerCsrf, openingReviewRoot,
          openingReview with { Reviewed = true, ExpectedRequestHash = openingReviewPreview.RequestHash }));
      Assert.Equal((openingId, reviewer.UserId, "1"),
        (openingReviewed.VerificationId, openingReviewed.Result.ReviewedByUserId!.Value, openingReviewed.Result.ReviewedBasisRevision));
      Assert.True(openingReviewed.Result.CurrentIndependentReview);

      var allEvidence = await CompletionAsync();
      Assert.False(allEvidence.Ready);
      Assert.Equal(correspondingReady.Blockers.Order(StringComparer.Ordinal), allEvidence.Blockers.Order(StringComparer.Ordinal));
      using var archiveFactory = Factory(pg, await UserAsync(pg, programReviewer.UserId));
      using var archiveClient = archiveFactory.CreateClient(new() { AllowAutoRedirect = false });
      await SignIn(archiveClient);
      var handoff = (await archiveClient.GetFromJsonAsync<EngagementArchiveHandoff>(
        $"/api/ui/engagements/{world.Primary.EngagementId:D}/archive"))!;
      Assert.Equal(world.Primary.EngagementId, handoff.EngagementId);
      Assert.False(handoff.CanCreate);
      Assert.Empty(handoff.Archives);
      Assert.Equal("The engagement must be issued and completed before an archive can be prepared.", handoff.BlockedReason);

      var reviewReceiptId = reviewReceipt.GetProperty("id").GetGuid();
      var reviewReceiptCount = 0;
      DateTimeOffset reviewedAt;
      await using (var proof = new AuditSphereDbContext(pg.Options))
      {
        reviewReceiptCount = await proof.AuditCompletionVarianceCommandReceipts.CountAsync(x => x.EngagementId == world.Primary.EngagementId);
        reviewedAt = await proof.AnalyticalReviewVarianceInvestigations.Where(x => x.Id == varianceId)
          .Select(x => x.ReviewedAt!.Value).SingleAsync();
      }

      var reviewLookupUrl = root +
        $"/receipts/{finalReviewRequest.RequestId:D}?requestHash={finalPreview.GetProperty("requestHash").GetString()}";
      using (var authorizedReviewLookup = await reviewerClient.GetAsync(reviewLookupUrl))
      {
        Assert.Equal(HttpStatusCode.OK, authorizedReviewLookup.StatusCode);
        using var authorizedLookupJson = JsonDocument.Parse(await authorizedReviewLookup.Content.ReadAsStringAsync());
        Assert.Equal(reviewReceiptId, authorizedLookupJson.RootElement.GetProperty("receipt").GetProperty("id").GetGuid());
      }

      Guid managerGrantId;
      await using (var grantDb = new AuditSphereDbContext(pg.Options))
      {
        managerGrantId = await grantDb.RoleGrants.Where(x => x.FirmId == world.Primary.FirmId && x.UserId == reviewer.UserId &&
          x.Role == "Manager" && x.ClientId == world.Primary.ClientId && x.EngagementId == world.Primary.EngagementId && x.RevokedAt == null)
          .Select(x => x.Id).SingleAsync();
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(grantDb, world.Administrator,
          new(managerGrantId, Reason: "Synthetic R03 review access removal"));
        Assert.True(revoked.Succeeded, revoked.Message);
      }
      var currentReviewerUser = await UserAsync(pg, reviewer.UserId);
      reviewer = new(currentReviewerUser.Id, currentReviewerUser.FirmId, currentReviewerUser.SessionEpoch, ["Staff"],
        world.Primary.ClientId, world.Primary.EngagementId);
      using var currentReviewerFactory = Factory(pg, currentReviewerUser);
      using var currentReviewerClient = currentReviewerFactory.CreateClient(new() { AllowAutoRedirect = false });
      var currentReviewerCsrf = await SignIn(currentReviewerClient);

      using (var planningStateResponse = await currentReviewerClient.GetAsync(root + "/state?accountArea=EXPENSE&periodReference=2026-Q1"))
      {
        Assert.Equal(HttpStatusCode.OK, planningStateResponse.StatusCode);
        using var planningStateJson = JsonDocument.Parse(await planningStateResponse.Content.ReadAsStringAsync());
        Assert.True(planningStateJson.RootElement.GetProperty("canPrepare").GetBoolean());
        Assert.False(planningStateJson.RootElement.GetProperty("canReview").GetBoolean());
      }
      using (var expiredReviewReplay = await Post(currentReviewerClient, currentReviewerCsrf, reviewRoot,
        finalReviewRequest with { Reviewed = true, ExpectedRequestHash = finalPreview.GetProperty("requestHash").GetString() }))
      {
        Assert.Equal(HttpStatusCode.Conflict, expiredReviewReplay.StatusCode);
        using var staleReplayJson = JsonDocument.Parse(await expiredReviewReplay.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.GenerationStale, staleReplayJson.RootElement.GetProperty("code").GetString());
      }

      // A current session may read only receipts bound to that session epoch.
      using (var historicalLookup = await currentReviewerClient.GetAsync(reviewLookupUrl))
      {
        Assert.Equal(HttpStatusCode.Conflict, historicalLookup.StatusCode);
        using var historicalLookupJson = JsonDocument.Parse(await historicalLookup.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.GenerationStale, historicalLookupJson.RootElement.GetProperty("code").GetString());
      }
      using (var currentStateResponse = await currentReviewerClient.GetAsync(root + "/state?accountArea=REVENUE&periodReference=2026-Q1"))
      {
        Assert.Equal(HttpStatusCode.OK, currentStateResponse.StatusCode);
        using var currentStateJson = JsonDocument.Parse(await currentStateResponse.Content.ReadAsStringAsync());
        var currentState = currentStateJson.RootElement;
        Assert.False(currentState.GetProperty("canReview").GetBoolean());
        var newReviewRequest = new VarianceInvestigationCommandRequest(Guid.CreateVersion7(),
          currentState.GetProperty("expectedRevision").GetString()!, currentState.GetProperty("reviewBasis").GetString()!);
        using var deniedReviewPreview = await Post(currentReviewerClient, currentReviewerCsrf, reviewRoot + "/preview", newReviewRequest);
        Assert.Equal(HttpStatusCode.OK, deniedReviewPreview.StatusCode);
        using var deniedPreviewJson = JsonDocument.Parse(await deniedReviewPreview.Content.ReadAsStringAsync());
        var deniedPreview = deniedPreviewJson.RootElement.GetProperty("value");
        Assert.False(deniedPreview.GetProperty("canProceed").GetBoolean());
        using var deniedNewReview = await Post(currentReviewerClient, currentReviewerCsrf, reviewRoot,
          newReviewRequest with { Reviewed = true, ExpectedRequestHash = deniedPreview.GetProperty("requestHash").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, deniedNewReview.StatusCode);
        using var deniedReviewJson = JsonDocument.Parse(await deniedNewReview.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.GateBlocked, deniedReviewJson.RootElement.GetProperty("code").GetString());
      }
      await using (var proof = new AuditSphereDbContext(pg.Options))
      {
        var unchanged = await proof.AnalyticalReviewVarianceInvestigations.AsNoTracking().SingleAsync(x => x.Id == varianceId);
        Assert.Equal(3, unchanged.Revision);
        Assert.Equal(reviewer.UserId, unchanged.ReviewedByUserId);
        Assert.Equal(reviewedAt, unchanged.ReviewedAt);
        Assert.Equal(2, unchanged.ReviewedRevision);
        Assert.Equal(reviewReceiptCount,
          await proof.AuditCompletionVarianceCommandReceipts.CountAsync(x => x.EngagementId == world.Primary.EngagementId));
      }

      await using var completionDb = new AuditSphereDbContext(pg.Options);
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(completionDb, programReviewer, world.Primary.EngagementId);
      Assert.True(completion.Succeeded, completion.Message);
      Assert.DoesNotContain("analytical-review:unreviewed", completion.Value!.Blockers);
      Assert.DoesNotContain("analytical-review:unexplained-variance", completion.Value.Blockers);
      Assert.NotEmpty(completion.Value.Blockers); // unrelated completion gates remain in force.
      Assert.DoesNotContain(completion.Value.Blockers, x => x.StartsWith("going-concern:", StringComparison.Ordinal));
      Assert.False(completion.Value.Ready);
      var workspace = await EngagementCompletionWorkspaceQuery.GetAsync(completionDb, programReviewer,
        world.Primary.EngagementId, DateTimeOffset.UtcNow);
      Assert.True(workspace.Succeeded, workspace.Message);
      Assert.Contains(workspace.Value!.Gates, x => x.Name == "Final financial statements approved" && x.Status == "NO_PACKAGE");
      Assert.False(workspace.Value.PartnerApproved);
      Assert.Null(workspace.Value.Clearance);
      Assert.Null(workspace.Value.Opinion);
      Assert.Null(workspace.Value.ReleaseCandidateId);
    }
  }

  private static async Task EstablishVariancePlanningBasisAsync(PgTestSchema pg, SupportedAuditCompletionWorld world,
    ActorContext preparer, ActorContext sourceReviewer, ActorContext partner)
  {
    var accountingPreparer = await ProvisionClientRoleAsync(pg, world.Administrator,
      world.Primary.ClientId, "AccountingPreparer", "r03-accounting-preparer");
    Guid periodId;
    Guid datasetId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var period = await ClientAccountingService.CreatePeriodAsync(db, accountingPreparer,
        new(world.Primary.ClientId, "R03-2026", new(2026, 1, 1), new(2026, 12, 31), "STATUTORY", "QAR"));
      Assert.True(period.Succeeded, period.Message);
      periodId = period.Value;
      const string csv = "AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode\n" +
        "1000,Cash,10000,QAR,TEST,CASH\n3000,Equity,-5000,QAR,TEST,EQUITY\n" +
        "4000,Revenue,-10000,QAR,TEST,REVENUE\n5000,Expense,5000,QAR,TEST,EXPENSE\n";
      var imported = await TrialBalanceImportService.ImportAsync(db, accountingPreparer, world.Primary.ClientId,
        world.Primary.EngagementId, csv, new(periodId, null, "STATUTORY"));
      Assert.True(imported.Succeeded, imported.Message);
      datasetId = imported.Value;
    }

    var factory = new OperationContextFactory(new R03DbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var options = new WorkerOptions(world.Primary.FirmId, "Test");
    var handler = new TrialBalanceValidationHandler();
    var worker = new NativeSourceWorker(new OperationDispatcher(store,
      new DurableOperationRegistry([handler], options), options),
      [new TrialBalanceDiscovery(factory, store, handler, options)], NullLogger<NativeSourceWorker>.Instance);
    Assert.True(await worker.ProcessNextAsync());

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, sourceReviewer,
        new(world.Primary.ClientId, world.Primary.EngagementId, AccountingSourceKinds.TrialBalance,
          datasetId, null, "Synthetic independently reviewed R03 trial balance", Reviewed: true));
      Assert.True(accepted.Succeeded, accepted.Message);

      var taxonomy = await ClientAccountingService.CreateTaxonomyVersionAsync(db, world.Administrator,
        "R03-TAX-2026", "IFRS", "Synthetic R03 reporting taxonomy", new(2026, 1, 1));
      Assert.True(taxonomy.Succeeded, taxonomy.Message);
      Assert.True((await ClientAccountingService.AddTaxonomyNodesAsync(db, world.Administrator, taxonomy.Value,
      [
        new("CASH", "Cash", "ASSETS", "+", "DEBIT", "BALANCE_SHEET", true, "ALL"),
        new("EQUITY", "Equity", "EQUITY", "+", "CREDIT", "BALANCE_SHEET", true, "ALL"),
        new("REVENUE", "Revenue", "INCOME", "+", "CREDIT", "INCOME_STATEMENT", true, "ALL"),
        new("EXPENSE", "Expense", "EXPENSE", "+", "DEBIT", "INCOME_STATEMENT", true, "ALL")
      ])).Succeeded);
      Assert.True((await ClientAccountingService.PublishTaxonomyVersionAsync(db, partner, taxonomy.Value)).Succeeded);

      var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new(datasetId, "R03-TAX-2026", "2026-01-01", "2026-12-31",
        [
          new("1000", "CASH", "ASSETS", 1m, "Synthetic R03 mapping"),
          new("3000", "EQUITY", "EQUITY", 1m, "Synthetic R03 mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Synthetic R03 mapping"),
          new("5000", "EXPENSE", "EXPENSE", 1m, "Synthetic R03 mapping")
        ]));
      Assert.True(mapping.Succeeded, mapping.Message);
      var mappingReview = await MappingApprovalWorkspace.GetAsync(db, partner, mapping.Value);
      Assert.True(mappingReview.Succeeded, mappingReview.Message);
      Assert.True(mappingReview.Value!.CanApprove, mappingReview.Value.Blocker);
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, partner, mapping.Value, 1,
        reviewRevision: mappingReview.Value.Revision, reviewed: true)).Succeeded);

      var materiality = await MaterialityEngineService.CalculateAsync(db, preparer,
        new(world.Primary.EngagementId, MaterialityBenchmarks.Revenue, null, 1m, 75m, 5m,
          "Synthetic R03 planning materiality basis."));
      Assert.True(materiality.Succeeded, materiality.Message);
      Assert.True((await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, partner,
        materiality.Value!.AssessmentId)).Succeeded);
    }
  }

  private static async Task<ActorContext> ProvisionFirmPartnerAsync(PgTestSchema pg, ActorContext administrator, string suffix)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var admin = await db.Users.AsNoTracking().SingleAsync(x => x.FirmId == administrator.FirmId && x.Id == administrator.UserId);
    var userId = await RoleAdministrationService.EnsureUserAsync(db, administrator,
      new(admin.TenantId, Guid.CreateVersion7().ToString("D"), $"{suffix}-{Guid.CreateVersion7():N}@example.test",
        "Synthetic R03 independent Partner", "Staff", "APPROVED_ROSTER"));
    Assert.True(userId.Succeeded, userId.Message);
    var grant = await RoleAdministrationService.ApplyRoleGrantAsync(db, administrator,
      new(userId.Value, "Partner", "FIRM_WIDE"));
    Assert.True(grant.Succeeded, grant.Message);
    var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId.Value);
    return new(user.Id, user.FirmId, user.SessionEpoch, ["Partner"]);
  }

  private static async Task<ActorContext> ProvisionClientRoleAsync(PgTestSchema pg, ActorContext administrator,
    Guid clientId, string role, string suffix)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var admin = await db.Users.AsNoTracking().SingleAsync(x => x.FirmId == administrator.FirmId && x.Id == administrator.UserId);
    var userId = await RoleAdministrationService.EnsureUserAsync(db, administrator,
      new(admin.TenantId, Guid.CreateVersion7().ToString("D"), $"{suffix}-{Guid.CreateVersion7():N}@example.test",
        "Synthetic R03 " + role, "Staff", "APPROVED_ROSTER"));
    Assert.True(userId.Succeeded, userId.Message);
    var grant = await RoleAdministrationService.ApplyRoleGrantAsync(db, administrator,
      new(userId.Value, role, "CLIENT", clientId));
    Assert.True(grant.Succeeded, grant.Message);
    var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId.Value);
    return new(user.Id, user.FirmId, user.SessionEpoch, [role], clientId);
  }

  private sealed class R03DbContextFactory(Microsoft.EntityFrameworkCore.DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
    public Task<AuditSphereDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult(new AuditSphereDbContext(options));
  }

  private static async Task<AppUser> UserAsync(PgTestSchema pg, Guid userId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    return await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId);
  }

  private static StandaloneApiApplicationFactory Factory(PgTestSchema pg, AppUser user) =>
    new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = user.Subject,
      ["DevelopmentIdentity:TenantId"] = user.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    });

  private static async Task<string> SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x =>
      x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static Task<HttpResponseMessage> Post(HttpClient client, string csrf, string url, object body)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
    request.Headers.Add("X-XSRF-TOKEN", csrf);
    return client.SendAsync(request);
  }
}

internal static class CompletionVarianceJsonAssertions
{
  public static Guid? GetGuidOrNull(this JsonElement value) =>
    value.ValueKind == JsonValueKind.Null ? null : value.GetGuid();
}
