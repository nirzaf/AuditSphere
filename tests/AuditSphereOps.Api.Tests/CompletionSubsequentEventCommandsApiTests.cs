using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed partial class CompletionVarianceCommandsApiTests
{
  [Fact]
  public async Task SubsequentEventCommandsPreserveMultipleExactRecordsIndependentReviewAndRecovery()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var preparer = world.Primary.Preparer;
    var reviewer = world.Reviewer;
    var programReviewer = await ProvisionFirmPartnerAsync(pg, world.Administrator, "r05-program-reviewer");
    const string evidenceReference = "r05-supported-current-procedure-evidence";
    Guid procedureId;

    // Build planning and submitted procedure evidence through the existing application owners.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var draft = await AuditProgramService.PrepareControlledAuditProgramAsync(db, world.Administrator,
        new("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(draft.Succeeded, draft.Message);
      var published = await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db, programReviewer,
        new(draft.Value!.ProgramVersionId, draft.Value.ContentDigest!, "synthetic-R05-controlled-program-review"));
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
    var root = $"/api/ui/engagements/{world.Primary.EngagementId:D}/completion/subsequent-events";
    var fields = new SubsequentEventReviewFields(null, new(2026, 12, 31), new(2027, 1, 20),
      "Synthetic customer administration after year end.", SubsequentEventClassifications.NonAdjusting,
      false, true, "Note 27", "Conditions arose after the reporting date.", "QAR", "250000.123456", ["news-article-1", "admin-order"]);
    async Task<SubsequentEventReviewCommandState> State(HttpClient c, Guid? id = null)
    {
      using var response = await c.GetAsync(root + "/state" + (id is null ? "" : $"?selectedRecordId={id}"));
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
      return (await response.Content.ReadFromJsonAsync<SubsequentEventReviewCommandState>())!;
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
    async Task<SubsequentEventReviewCommandRequest> Prepare(SubsequentEventReviewFields f, Guid? id = null)
    {
      var state = await State(client, id);
      var request = new SubsequentEventReviewCommandRequest(Guid.CreateVersion7(), state.ExpectedRevision, state.ReviewBasis, f, SelectedRecordId: id);
      var preview = await Value<SubsequentEventReviewCommandPreview>(await Post(client, csrf, root + "/prepare/preview", request));
      Assert.True(preview.CanProceed, string.Join(";", preview.Blockers));
      return request with { Reviewed = true, ExpectedRequestHash = preview.RequestHash };
    }
    async Task<SubsequentEventReviewCommandRequest> Review(Guid id)
    {
      var state = await State(reviewClient, id);
      var request = new SubsequentEventReviewCommandRequest(Guid.CreateVersion7(), state.ExpectedRevision, state.ReviewBasis, SelectedRecordId: id);
      var preview = await Value<SubsequentEventReviewCommandPreview>(await Post(reviewClient, reviewCsrf, root + $"/{id}/review/preview", request));
      Assert.True(preview.CanProceed, string.Join(";", preview.Blockers));
      return request with { Reviewed = true, ExpectedRequestHash = preview.RequestHash };
    }
    async Task<string> Stored()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var rows = await db.SubsequentEventReviews.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
      var receipts = await db.AuditCompletionSubsequentEventCommandReceipts.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
      return JsonSerializer.Serialize(new { rows, receipts });
    }
    async Task Refused(HttpClient c, string token, string path, SubsequentEventReviewCommandRequest request, HttpStatusCode status)
    {
      var before = await Stored();
      using var response = await Post(c, token, path, request);
      Assert.Equal(status, response.StatusCode);
      Assert.Equal(before, await Stored());
    }
    using (var denied = await siblingClient.GetAsync(root)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    using (var denied = await siblingClient.GetAsync(root + "/state")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    var empty = await State(client);
    var invalid = new SubsequentEventReviewCommandRequest(Guid.CreateVersion7(), "0", empty.ReviewBasis, fields);
    foreach (var f in new[] {
      fields with { AdjustmentRequired = null }, fields with { DisclosureRequired = null },
      fields with { Classification = SubsequentEventClassifications.Adjusting, AdjustmentRequired = false },
      fields with { DisclosureReference = null }, fields with { Rationale = new string('r', 4001) },
      fields with { DisclosureReference = new string('d', 201) }, fields with { EvidenceReferences = [new string('e', 20001)] },
      fields with { FinancialEffect = "0.0000001" }, fields with { FinancialEffect = "10000000000000" },
      fields with { FinancialEffect = "0.12345600000000000000000000000001" } })
      await Refused(client, csrf, root + "/prepare/preview", invalid with { Fields = f }, HttpStatusCode.BadRequest);
    await Refused(client, csrf, root + "/prepare/preview", invalid with { Fields = fields with { ProcedureId = Guid.CreateVersion7() } }, HttpStatusCode.Forbidden);
    await Refused(client, csrf, root + "/prepare/preview", invalid with { Fields = fields with { ProcedureId = procedureId } }, HttpStatusCode.Forbidden);
    var siblingRoot = $"/api/ui/engagements/{world.Sibling.EngagementId:D}/completion/subsequent-events";
    using (var response = await siblingClient.GetAsync(siblingRoot + "/state"))
    {
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
      var siblingState = (await response.Content.ReadFromJsonAsync<SubsequentEventReviewCommandState>())!;
      await Refused(siblingClient, siblingCsrf, siblingRoot + "/prepare/preview",
        new(Guid.CreateVersion7(), "0", siblingState.ReviewBasis,
          fields with { ProcedureId = procedureId, EvidenceReferences = [evidenceReference] }), HttpStatusCode.Forbidden);
    }
    var prepare = await Prepare(fields);
    var stale = await Prepare(fields);
    var receipt = await Value<SubsequentEventReviewCommandReceiptView>(await Post(client, csrf, root + "/prepare", prepare));
    var id = receipt.SubsequentEventReviewId;
    Assert.Equal("1", receipt.Result.Revision);
    Assert.Equal(fields.Description, receipt.Result.Description);
    Assert.Equal(fields.Classification, receipt.Result.Classification);
    Assert.Equal(fields.AdjustmentRequired, receipt.Result.AdjustmentRequired);
    Assert.Equal(fields.DisclosureRequired, receipt.Result.DisclosureRequired);
    Assert.Equal(fields.DisclosureReference, receipt.Result.DisclosureReference);
    Assert.Equal(fields.Rationale, receipt.Result.Rationale);
    Assert.Equal(fields.Currency, receipt.Result.Currency);
    Assert.Equal(fields.FinancialEffect, receipt.Result.FinancialEffect);
    Assert.Equal(fields.PeriodEndDate, receipt.Result.PeriodEndDate);
    Assert.Equal(fields.EventDate, receipt.Result.EventDate);
    Assert.Equal(fields.EvidenceReferences, receipt.Result.EvidenceReferences);
    Assert.Null(receipt.Result.ProcedureId);
    Assert.Null(receipt.Result.ReviewedByUserId);
    Assert.Equal(JsonSerializer.Serialize(receipt.Result), JsonSerializer.Serialize((await State(client, id)).Record));
    Assert.Equal(receipt.Id, (await Value<SubsequentEventReviewCommandReceiptView>(await Post(client, csrf, root + "/prepare", prepare))).Id);
    using (var response = await client.GetAsync(root + $"/receipts/{prepare.RequestId}?requestHash={prepare.ExpectedRequestHash}"))
    {
      var found = (await response.Content.ReadFromJsonAsync<SubsequentEventReviewCommandLookup>())!;
      Assert.True(found.Found); Assert.Equal(receipt.Id, found.Receipt!.Id);
    }
    var beforeUnknown = await Stored();
    using (var response = await client.GetAsync(root + $"/receipts/{Guid.CreateVersion7()}?requestHash={prepare.ExpectedRequestHash}"))
    {
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
      var missing = (await response.Content.ReadFromJsonAsync<SubsequentEventReviewCommandLookup>())!;
      Assert.False(missing.Found); Assert.Null(missing.Receipt);
    }
    Assert.Equal(beforeUnknown, await Stored());
    await Refused(client, csrf, root + "/prepare", stale, HttpStatusCode.Conflict);
    await Refused(client, csrf, root + "/prepare", prepare with { Fields = fields with { Rationale = "changed" } }, HttpStatusCode.Conflict);
    await Refused(client, csrf, root + "/prepare", prepare with { ExpectedRevision = "1" }, HttpStatusCode.Conflict);
    await Refused(siblingClient, siblingCsrf, root + "/prepare", prepare, HttpStatusCode.Forbidden);
    var current = await State(client, id);
    var update = new SubsequentEventReviewCommandRequest(Guid.CreateVersion7(), current.ExpectedRevision, current.ReviewBasis, fields, SelectedRecordId: id);
    await Refused(client, csrf, root + "/prepare/preview", update with { Fields = fields with { Description = "Different event identity" } }, HttpStatusCode.Conflict);
    await Refused(client, csrf, root + "/prepare/preview", update with { Fields = fields with { EventDate = fields.EventDate.AddDays(1) } }, HttpStatusCode.Conflict);
    await Refused(client, csrf, root + "/prepare/preview", update with { SelectedRecordId = Guid.CreateVersion7() }, HttpStatusCode.Forbidden);
    var self = update with { Fields = null };
    var selfPreview = await Value<SubsequentEventReviewCommandPreview>(await Post(client, csrf, root + $"/{id}/review/preview", self));
    Assert.False(selfPreview.CanProceed);
    await Refused(client, csrf, root + $"/{id}/review", self with { Reviewed = true, ExpectedRequestHash = selfPreview.RequestHash }, HttpStatusCode.BadRequest);
    var review = await Review(id);
    await Refused(siblingClient, siblingCsrf, root + $"/{id}/review", review, HttpStatusCode.Forbidden);
    var reviewed = await Value<SubsequentEventReviewCommandReceiptView>(await Post(reviewClient, reviewCsrf, root + $"/{id}/review", review));
    Assert.Equal(reviewer.UserId, reviewed.Result.ReviewedByUserId);
    Assert.Equal("1", reviewed.Result.ReviewedRevision); Assert.Equal("2", reviewed.Result.Revision);
    Assert.Equal(reviewed.Id, (await Value<SubsequentEventReviewCommandReceiptView>(await Post(reviewClient, reviewCsrf, root + $"/{id}/review", review))).Id);
    await Refused(reviewClient, reviewCsrf, root + $"/{id}/review", review with { ExpectedRevision = "2" }, HttpStatusCode.Conflict);
    var pendingFields = fields with { Classification = SubsequentEventClassifications.PendingAssessment, AdjustmentRequired = false,
      DisclosureRequired = false, DisclosureReference = null, Rationale = null, FinancialEffect = null, EvidenceReferences = [] };
    var pendingIntent = await Prepare(pendingFields, id);
    var pending = await Value<SubsequentEventReviewCommandReceiptView>(await Post(client, csrf, root + "/prepare", pendingIntent));
    Assert.Equal("3", pending.Result.Revision); Assert.Null(pending.Result.ReviewedByUserId);
    Assert.Null(pending.Result.ReviewedRevision); Assert.Null(pending.Result.FinancialEffect);
    var pendingState = await State(reviewClient, id); Assert.False(pendingState.CanReview);
    var pendingReview = new SubsequentEventReviewCommandRequest(Guid.CreateVersion7(), pendingState.ExpectedRevision, pendingState.ReviewBasis, SelectedRecordId: id);
    var pendingPreview = await Value<SubsequentEventReviewCommandPreview>(await Post(reviewClient, reviewCsrf, root + $"/{id}/review/preview", pendingReview));
    Assert.False(pendingPreview.CanProceed);
    await Refused(reviewClient, reviewCsrf, root + $"/{id}/review", pendingReview with { Reviewed = true, ExpectedRequestHash = pendingPreview.RequestHash }, HttpStatusCode.BadRequest);
    // A distinct event on the SAME DATE is legitimate and independently selectable.
    var secondFields = fields with { Description = "Another independent subsequent event", ProcedureId = procedureId, FinancialEffect = null, EvidenceReferences = [evidenceReference] };
    var secondIntent = await Prepare(secondFields);
    var second = await Value<SubsequentEventReviewCommandReceiptView>(await Post(client, csrf, root + "/prepare", secondIntent));
    Assert.NotEqual(id, second.SubsequentEventReviewId);
    using (var response = await client.GetAsync(root))
    {
      var list = (await response.Content.ReadFromJsonAsync<SubsequentEventReviewList>())!;
      Assert.Equal(2, list.Records.Count); Assert.False(list.HasMore);
      Assert.Contains(list.Records, x => x.Id == id && x.Classification == SubsequentEventClassifications.PendingAssessment);
      Assert.Contains(list.Records, x => x.Id == second.SubsequentEventReviewId);
    }
    var classifiedIntent = await Prepare(fields, id);
    var classified = await Value<SubsequentEventReviewCommandReceiptView>(await Post(client, csrf, root + "/prepare", classifiedIntent));
    var staleReview = await Review(id);
    var changedIntent = await Prepare(fields with { Rationale = "Updated professional explanation." }, id);
    await Value<SubsequentEventReviewCommandReceiptView>(await Post(client, csrf, root + "/prepare", changedIntent));
    await Refused(reviewClient, reviewCsrf, root + $"/{id}/review", staleReview, HttpStatusCode.Conflict);
    // Retained outcomes stay original after later record revisions, not a replacement snapshot.
    var replay = await Value<SubsequentEventReviewCommandReceiptView>(await Post(client, csrf, root + "/prepare", prepare));
    Assert.Equal("1", replay.Result.Revision); Assert.Equal(receipt.Id, replay.Id);
    var beforeSession = await Stored();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var staleActor = preparer with { SessionEpoch = preparer.SessionEpoch + 1 };
      Assert.False((await AuditCompletionSubsequentEventWorkspace.ExecutePrepareAsync(db, staleActor, world.Primary.EngagementId, prepare)).Succeeded);
      Assert.False((await AuditCompletionSubsequentEventWorkspace.LookupAsync(db, staleActor, world.Primary.EngagementId, prepare.RequestId, prepare.ExpectedRequestHash)).Succeeded);
    }
    Assert.Equal(beforeSession, await Stored());
    var beforeRevocation = await Stored();
    var revokedIntent = await Review(id);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var grantId = await db.RoleGrants.Where(x => x.UserId == reviewer.UserId && x.Role == "Manager" &&
        x.EngagementId == world.Primary.EngagementId && x.RevokedAt == null).Select(x => x.Id).SingleAsync();
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, world.Administrator,
        new(grantId, Reason: "Synthetic R05 current authority revocation."));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    using (var denied = await Post(reviewClient, reviewCsrf, root + $"/{id}/review", revokedIntent)) Assert.False(denied.IsSuccessStatusCode);
    using (var denied = await reviewClient.GetAsync(root + $"/receipts/{review.RequestId}?requestHash={review.ExpectedRequestHash}")) Assert.False(denied.IsSuccessStatusCode);
    Assert.Equal(beforeRevocation, await Stored());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var immutable = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
        "UPDATE audit_completion_subsequent_event_command_receipts SET action = 'REVIEW'"));
      Assert.Equal("55000", immutable.SqlState);
      var truncate = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
        "TRUNCATE audit_completion_subsequent_event_command_receipts"));
      Assert.Equal("55000", truncate.SqlState);
      await PermanentFileFreezeMigrationAssertions.AssertDowngradeBlockedAsync(db, "AuditCompletionSubsequentEventCommandReceipts");
    }
    Assert.Equal(beforeRevocation, await Stored());
  }
}
