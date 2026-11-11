using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
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
  public async Task GoingConcernCommandsPreserveExactEvidenceReviewRecoveryAndAmbiguousSelection()
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


    using var preparerFactory = Factory(pg, await UserAsync(pg, preparer.UserId));
    using var client = preparerFactory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    using var reviewerFactory = Factory(pg, await UserAsync(pg, reviewer.UserId));
    using var reviewClient = reviewerFactory.CreateClient(new() { AllowAutoRedirect = false });
    var reviewCsrf = await SignIn(reviewClient);
    using var siblingFactory = Factory(pg, await UserAsync(pg, world.Sibling.Preparer.UserId));
    using var siblingClient = siblingFactory.CreateClient(new() { AllowAutoRedirect = false });
    var siblingCsrf = await SignIn(siblingClient);
    var root = $"/api/ui/engagements/{world.Primary.EngagementId:D}/completion/going-concern-assessments";
    var date = new DateOnly(2026, 12, 31);
    var fields = new GoingConcernAssessmentFields(procedureId, date, date.AddMonths(6),
      "Forecast inspected against supported evidence.", false, true, "QAR", "Synthetic practitioner determination.", [evidenceReference]);

    async Task<GoingConcernAssessmentCommandState> State(HttpClient c, DateOnly d)
    {
      using var response = await c.GetAsync(root + $"/state?assessmentDate={d:yyyy-MM-dd}");
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
      return (await response.Content.ReadFromJsonAsync<GoingConcernAssessmentCommandState>())!;
    }
    async Task<T> Value<T>(HttpResponseMessage response)
    {
      using (response)
      {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("value").Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
      }
    }
    async Task<GoingConcernAssessmentCommandRequest> PrepareIntent(GoingConcernAssessmentFields f)
    {
      var state = await State(client, f.AssessmentDate);
      var request = new GoingConcernAssessmentCommandRequest(Guid.CreateVersion7(), state.ExpectedRevision, state.ReviewBasis, f);
      var preview = await Value<GoingConcernAssessmentCommandPreview>(await Post(client, csrf, root + "/prepare/preview", request));
      Assert.True(preview.CanProceed);
      return request with { ExpectedRequestHash = preview.RequestHash, Reviewed = true };
    }
    async Task<GoingConcernAssessmentCommandRequest> ReviewIntent(Guid id, DateOnly d)
    {
      var state = await State(reviewClient, d);
      var request = new GoingConcernAssessmentCommandRequest(Guid.CreateVersion7(), state.ExpectedRevision, state.ReviewBasis);
      var preview = await Value<GoingConcernAssessmentCommandPreview>(await Post(reviewClient, reviewCsrf, root + $"/{id}/review/preview", request));
      Assert.True(preview.CanProceed);
      return request with { ExpectedRequestHash = preview.RequestHash, Reviewed = true };
    }
    async Task AssertCounts(int receipts, long revision)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      Assert.Equal(receipts, await db.AuditCompletionGoingConcernCommandReceipts.CountAsync());
      Assert.Equal(revision, await db.GoingConcernAssessments.Where(x => x.AssessmentDate == date).Select(x => x.Revision).SingleAsync());
    }

    using (var denied = await siblingClient.GetAsync(root + $"/state?assessmentDate={date:yyyy-MM-dd}"))
      Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    var empty = await State(client, date);
    Assert.Equal("0", empty.ExpectedRevision);
    var invalid = new GoingConcernAssessmentCommandRequest(Guid.CreateVersion7(), "0", empty.ReviewBasis, fields);
    foreach (var f in new[] { fields with { MaterialUncertaintyIdentified = null }, fields with { DisclosureAdequate = null }, fields with { EvidenceReferences = [] } })
    {
      using var refused = await Post(client, csrf, root + "/prepare/preview", invalid with { Fields = f });
      Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }
    using (var refused = await Post(client, csrf, root + "/prepare/preview", invalid with { Fields = fields with { EvidenceReferences = ["wrong evidence"] } }))
      Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    var prepare = await PrepareIntent(fields);
    var stale = await PrepareIntent(fields);
    var receipt = await Value<GoingConcernAssessmentCommandReceiptView>(await Post(client, csrf, root + "/prepare", prepare));
    var id = receipt.GoingConcernAssessmentId;
    Assert.Equal(fields.ForecastReviewOutcome, receipt.Result.ForecastReviewOutcome);
    Assert.Equal(fields.Rationale, receipt.Result.Rationale);
    Assert.Equal(fields.PeriodCoveredTo, receipt.Result.PeriodCoveredTo);
    Assert.Equal(fields.EvidenceReferences, receipt.Result.EvidenceReferences);
    Assert.Null(receipt.Result.ReviewedByUserId);
    Assert.Equal(receipt.Id, (await Value<GoingConcernAssessmentCommandReceiptView>(await Post(client, csrf, root + "/prepare", prepare))).Id);
    using (var refused = await Post(client, csrf, root + "/prepare", stale)) Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    using (var refused = await Post(client, csrf, root + "/prepare", prepare with { Fields = fields with { Rationale = "Changed intent" } })) Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    using (var refused = await Post(siblingClient, siblingCsrf, root + "/prepare", prepare)) Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    var selfState = await State(client, date);
    var selfPreview = await Value<GoingConcernAssessmentCommandPreview>(await Post(client, csrf, root + $"/{id}/review/preview",
      new GoingConcernAssessmentCommandRequest(Guid.CreateVersion7(), selfState.ExpectedRevision, selfState.ReviewBasis)));
    Assert.False(selfPreview.CanProceed);
    await AssertCounts(1, 1);
    var review = await ReviewIntent(id, date);
    var reviewed = await Value<GoingConcernAssessmentCommandReceiptView>(await Post(reviewClient, reviewCsrf, root + $"/{id}/review", review));
    Assert.Equal(reviewer.UserId, reviewed.Result.ReviewedByUserId);
    Assert.Equal("1", reviewed.Result.ReviewedRevision);
    Assert.Equal(reviewed.Id, (await Value<GoingConcernAssessmentCommandReceiptView>(await Post(reviewClient, reviewCsrf, root + $"/{id}/review", review))).Id);
    await AssertCounts(2, 2);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, programReviewer, world.Primary.EngagementId);
      Assert.True(completion.Succeeded, completion.Message);
      Assert.DoesNotContain("going-concern:missing", completion.Value!.Blockers);
      Assert.DoesNotContain("going-concern:unreviewed", completion.Value.Blockers);
      Assert.Contains("going-concern:horizon-under-12-months", completion.Value.Blockers);
      Assert.Contains("analytical-review:missing", completion.Value.Blockers);
      var staleDirect = await AuditCompletionService.RecordGoingConcernAssessmentAsync(db, preparer,
        new(world.Primary.EngagementId, procedureId, date, date.AddMonths(12), fields.ForecastReviewOutcome,
          false, true, fields.Currency, "Stale overwrite", [evidenceReference], 1));
      Assert.False(staleDirect.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleDirect.ErrorCode);
    }
    var revisedIntent = await PrepareIntent(fields with { PeriodCoveredTo = date.AddMonths(12) });
    var revised = await Value<GoingConcernAssessmentCommandReceiptView>(await Post(client, csrf, root + "/prepare", revisedIntent));
    Assert.Null(revised.Result.ReviewedByUserId);
    Assert.Null(revised.Result.ReviewedRevision);
    var staleReview = await ReviewIntent(id, date);
    var second = await PrepareIntent(fields with { AssessmentDate = date.AddDays(-1), PeriodCoveredTo = date.AddMonths(12) });
    await Value<GoingConcernAssessmentCommandReceiptView>(await Post(client, csrf, root + "/prepare", second));
    using (var refused = await Post(reviewClient, reviewCsrf, root + $"/{id}/review", staleReview)) Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    Assert.Contains(GoingConcernAssessmentSelection.AmbiguousBlocker, (await State(client, date)).Blockers);
    string ambiguousFactsDigest;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var summary = await AuditDeliverableService.GenerateSummaryReviewMemorandumAsync(db, reviewer,
        world.Primary.EngagementId, "Synthetic assessment ambiguity is unresolved.");
      Assert.True(summary.Succeeded, summary.Message);
      var document = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == summary.Value);
      Assert.Contains(GoingConcernAssessmentSelection.AmbiguousBlocker, document.InputSummaryJson);
      ambiguousFactsDigest = document.InputDigest;
    }
    var exact = await ReviewIntent(id, date);
    await Value<GoingConcernAssessmentCommandReceiptView>(await Post(reviewClient, reviewCsrf, root + $"/{id}/review", exact));
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, programReviewer, world.Primary.EngagementId);
      Assert.True(completion.Succeeded, completion.Message);
      Assert.Contains(GoingConcernAssessmentSelection.AmbiguousBlocker, completion.Value!.Blockers);
      Assert.Contains("analytical-review:missing", completion.Value.Blockers);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var summary = await AuditDeliverableService.GenerateSummaryReviewMemorandumAsync(db, reviewer,
        world.Primary.EngagementId, "Synthetic assessment ambiguity remains unresolved after record-specific review.");
      Assert.True(summary.Succeeded, summary.Message);
      var document = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == summary.Value);
      Assert.Equal(ambiguousFactsDigest, document.InputDigest); // no selected record-local revision or favorable review enters actual deliverable facts.
      Assert.Contains(GoingConcernAssessmentSelection.AmbiguousBlocker, document.InputSummaryJson);
    }
    using var lookup = await reviewClient.GetAsync(root + $"/receipts/{review.RequestId}?requestHash={review.ExpectedRequestHash}");
    Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
    var retained = await lookup.Content.ReadFromJsonAsync<GoingConcernAssessmentCommandLookup>();
    Assert.True(retained!.Found);
    Assert.Equal(reviewed.Id, retained.Receipt!.Id);

    var inadequateIntent = await PrepareIntent(fields with { MaterialUncertaintyIdentified = true, DisclosureAdequate = false });
    var inadequate = await Value<GoingConcernAssessmentCommandReceiptView>(await Post(client, csrf, root + "/prepare", inadequateIntent));
    Assert.Equal(GoingConcernConclusions.InadequateDisclosure, inadequate.Result.Conclusion);
    var inadequateState = await State(reviewClient, date);
    Assert.False(inadequateState.CanReview);
    var inadequateRequest = new GoingConcernAssessmentCommandRequest(Guid.CreateVersion7(), inadequateState.ExpectedRevision, inadequateState.ReviewBasis);
    var inadequatePreview = await Value<GoingConcernAssessmentCommandPreview>(await Post(reviewClient, reviewCsrf, root + $"/{id}/review/preview", inadequateRequest));
    Assert.False(inadequatePreview.CanProceed);
    using (var refused = await Post(reviewClient, reviewCsrf, root + $"/{id}/review",
      inadequateRequest with { ExpectedRequestHash = inadequatePreview.RequestHash, Reviewed = true }))
      Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

    var finalPrepare = await PrepareIntent(fields);
    var finalPrepared = await Value<GoingConcernAssessmentCommandReceiptView>(await Post(client, csrf, root + "/prepare", finalPrepare));
    var changedEvidenceReview = await ReviewIntent(id, date);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var resultId = await db.AuditProcedureResults.Where(x => x.AuditProcedureId == procedureId)
        .OrderByDescending(x => x.Revision).Select(x => x.Id).FirstAsync();
      var returned = await AuditProgramService.ReviewResultAsync(db, reviewer,
        new(resultId, AuditProcedureReviewDecisions.ChangesRequired, "Synthetic evidence requires an updated observation."));
      Assert.True(returned.Succeeded, returned.Message);
      var procedureState = await AuditProcedureCommandWorkspace.StateAsync(db, preparer, procedureId);
      Assert.True(procedureState.Succeeded, procedureState.Message);
      var resubmitted = await AuditProgramService.SubmitResultAsync(db, preparer,
        new(procedureId, procedureState.Value!.Basis.InputGeneration, "Reassessed the exact supporting procedure.",
          "{\"observation\":\"changed synthetic evidence\"}", [evidenceReference], "Updated procedure result."));
      Assert.True(resubmitted.Succeeded, resubmitted.Message);
    }
    using (var refused = await Post(reviewClient, reviewCsrf, root + $"/{id}/review", changedEvidenceReview))
      Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    var revokedReview = await ReviewIntent(id, date);
    int beforeRevocationReceipts;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      beforeRevocationReceipts = await db.AuditCompletionGoingConcernCommandReceipts.CountAsync();
      var grantId = await db.RoleGrants.Where(x => x.UserId == reviewer.UserId && x.Role == "Manager" &&
        x.EngagementId == world.Primary.EngagementId && x.RevokedAt == null).Select(x => x.Id).SingleAsync();
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, world.Administrator,
        new(grantId, Reason: "Synthetic R04 exact review scope revocation."));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    using (var refused = await Post(reviewClient, reviewCsrf, root + $"/{id}/review", revokedReview))
      Assert.False(refused.IsSuccessStatusCode);
    await AssertCounts(beforeRevocationReceipts, long.Parse(finalPrepared.Result.Revision));
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var unchanged = await db.GoingConcernAssessments.AsNoTracking().SingleAsync(x => x.Id == id);
      Assert.Null(unchanged.ReviewedByUserId);
      var immutable = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
        "UPDATE audit_completion_going_concern_command_receipts SET action = 'REVIEW'"));
      Assert.Equal("55000", immutable.SqlState);
      var noTruncate = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
        "TRUNCATE audit_completion_going_concern_command_receipts"));
      Assert.Equal("55000", noTruncate.SqlState);
      await PermanentFileFreezeMigrationAssertions.AssertDowngradeBlockedAsync(db, "AuditCompletionGoingConcernCommandReceipts");
      Assert.Equal(beforeRevocationReceipts, await db.AuditCompletionGoingConcernCommandReceipts.CountAsync());
      Assert.Equal(finalPrepared.Result.Revision, (await db.GoingConcernAssessments.AsNoTracking().SingleAsync(x => x.Id == id)).Revision.ToString());
    }
  }

  [Theory]
  [InlineData(1, 1, false)]
  [InlineData(2, 1, false)]
  [InlineData(1, 2, true)]
  [InlineData(2, 2, true)]
  public void GoingConcernSelectionNeverRanksRevisionsOrDates(long firstRevision, long secondRevision, bool reverse)
  {
    Assert.Null(GoingConcernAssessmentSelection.Select([]).Assessment);
    Assert.False(GoingConcernAssessmentSelection.Select([]).Ambiguous);
    var first = new GoingConcernAssessment { Id = Guid.NewGuid(), Revision = firstRevision, AssessmentDate = new(2026, 1, 1) };
    var second = new GoingConcernAssessment { Id = Guid.NewGuid(), Revision = secondRevision, AssessmentDate = new(2026, 12, 31) };
    Assert.Same(first, GoingConcernAssessmentSelection.Select([first]).Assessment);
    var selection = GoingConcernAssessmentSelection.Select(reverse ? [second, first] : [first, second]);
    Assert.Null(selection.Assessment);
    Assert.True(selection.Ambiguous);
  }
}
