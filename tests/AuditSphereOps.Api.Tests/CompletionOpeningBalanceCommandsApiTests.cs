using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed partial class CompletionVarianceCommandsApiTests
{
  [Fact]
  public async Task OpeningBalanceProductPreservesExactReviewRecoveryAndUnresolvedDifferenceRefusal()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    using var preparerFactory = Factory(pg, await UserAsync(pg, world.Primary.Preparer.UserId));
    using var client = preparerFactory.CreateClient(new() { AllowAutoRedirect = false });
    using var reviewerFactory = Factory(pg, await UserAsync(pg, world.Reviewer.UserId));
    using var reviewClient = reviewerFactory.CreateClient(new() { AllowAutoRedirect = false });
    using var siblingFactory = Factory(pg, await UserAsync(pg, world.Sibling.Preparer.UserId));
    using var siblingClient = siblingFactory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client); var reviewCsrf = await SignIn(reviewClient); var siblingCsrf = await SignIn(siblingClient);
    var root = $"/api/ui/engagements/{world.Primary.EngagementId}/completion/opening-balances";
    var date = new DateOnly(2026, 1, 1);
    var fields = new OpeningBalanceFields(null, null, "Prior signed statements", date, "QAR", "1000.123456", "990.123456", true,
      "Synthetic comparison of prior evidence to opening figures.", ["synthetic-opening-source"]);
    async Task<OpeningBalanceCommandState> State(HttpClient c, DateOnly? d = null)
    {
      using var response = await c.GetAsync(root + $"/state?asOfDate={d ?? date:yyyy-MM-dd}");
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
      return (await response.Content.ReadFromJsonAsync<OpeningBalanceCommandState>())!;
    }
    async Task<T> Value<T>(HttpResponseMessage response)
    {
      using (response) {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("value").Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
      }
    }
    async Task<OpeningBalanceCommandRequest> Prepare(OpeningBalanceFields f)
    {
      var state = await State(client, f.AsOfDate);
      var request = new OpeningBalanceCommandRequest(Guid.CreateVersion7(), state.ExpectedRevision, state.ReviewBasis, f, state.Record?.Id);
      var preview = await Value<OpeningBalanceCommandPreview>(await Post(client, csrf, root + "/prepare/preview", request));
      Assert.True(preview.CanProceed, string.Join(";", preview.Blockers));
      return request with { ExpectedRequestHash = preview.RequestHash, Reviewed = true };
    }
    async Task<(OpeningBalanceCommandRequest Request, OpeningBalanceCommandPreview Preview)> Review(HttpClient c, string token, DateOnly? d = null)
    {
      var state = await State(c, d);
      var request = new OpeningBalanceCommandRequest(Guid.CreateVersion7(), state.ExpectedRevision, state.ReviewBasis,
        ExpectedVerificationId: state.Record!.Id, Comment: "Synthetic independent review of the exact preparation.");
      var preview = await Value<OpeningBalanceCommandPreview>(await Post(c, token, root + $"/{state.Record.Id}/review/preview", request));
      return (request with { ExpectedRequestHash = preview.RequestHash, Reviewed = true }, preview);
    }
    async Task<string> Stored()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return JsonSerializer.Serialize(new {
        rows = await db.OpeningBalanceVerifications.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        receipts = await db.OpeningBalanceCommandReceipts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync() });
    }
    async Task Refused(HttpClient c, string token, string path, OpeningBalanceCommandRequest request, HttpStatusCode expected)
    {
      var before = await Stored();
      using var response = await Post(c, token, path, request);
      Assert.Equal(expected, response.StatusCode);
      Assert.Equal(before, await Stored());
    }
    using (var denied = await siblingClient.GetAsync(root + $"/state?asOfDate={date:yyyy-MM-dd}")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    var empty = await State(client);
    Assert.Equal("0", empty.ExpectedRevision); Assert.Null(empty.Record);
    var invalid = new OpeningBalanceCommandRequest(Guid.CreateVersion7(), "0", empty.ReviewBasis, fields);
    foreach (var f in new[] { fields with { OpeningSignedTotal = "1.0000001" }, fields with { AgreedSignedTotal = "1e3" },
      fields with { AccountingPoliciesConsistent = null }, fields with { EvidenceReferences = [] } })
      await Refused(client, csrf, root + "/prepare/preview", invalid with { Fields = f }, HttpStatusCode.BadRequest);

    var unresolvedIntent = await Prepare(fields);
    var unresolved = await Value<OpeningBalanceCommandReceiptView>(await Post(client, csrf, root + "/prepare", unresolvedIntent));
    Assert.Equal("10", unresolved.Result.DifferenceAmount); Assert.False(unresolved.Result.ComparisonResolved);
    Assert.False(unresolved.Result.CurrentIndependentReview); Assert.Null(unresolved.Result.ReviewedByUserId);
    var unresolvedReview = await Review(reviewClient, reviewCsrf);
    Assert.False(unresolvedReview.Preview.CanProceed);
    await Refused(reviewClient, reviewCsrf, root + $"/{unresolved.VerificationId}/review", unresolvedReview.Request, HttpStatusCode.BadRequest);
    Assert.False((await State(reviewClient)).Record!.CurrentIndependentReview);

    // A stale separately prepared intent cannot amend the record after another command commits.
    var resolvedFields = fields with { AgreedSignedTotal = fields.OpeningSignedTotal };
    var amendment = await Prepare(resolvedFields);
    var stale = await Prepare(resolvedFields with { Rationale = "A distinct stale intent." });
    var resolved = await Value<OpeningBalanceCommandReceiptView>(await Post(client, csrf, root + "/prepare", amendment));
    Assert.Equal(unresolved.VerificationId, resolved.VerificationId); Assert.Equal("2", resolved.Result.Revision);
    Assert.Equal("0", resolved.Result.DifferenceAmount); Assert.True(resolved.Result.ComparisonResolved);
    Assert.False(resolved.Result.CurrentIndependentReview);
    await Refused(client, csrf, root + "/prepare", stale, HttpStatusCode.Conflict);
    var self = await Review(client, csrf); Assert.False(self.Preview.CanProceed);
    await Refused(client, csrf, root + $"/{resolved.VerificationId}/review", self.Request, HttpStatusCode.BadRequest);
    await Refused(siblingClient, siblingCsrf, root + "/prepare", amendment, HttpStatusCode.Forbidden);

    var review = await Review(reviewClient, reviewCsrf); Assert.True(review.Preview.CanProceed);
    var approved = await Value<OpeningBalanceCommandReceiptView>(await Post(reviewClient, reviewCsrf, root + $"/{resolved.VerificationId}/review", review.Request));
    Assert.True(approved.Result.CurrentIndependentReview); Assert.Equal("2", approved.Result.ReviewedBasisRevision);
    Assert.Equal("3", approved.Result.Revision); Assert.Equal(world.Reviewer.UserId, approved.Result.ReviewedByUserId);
    Assert.Equal(world.Primary.Preparer.UserId, approved.Result.RecordedByUserId);
    using (var detail = await reviewClient.GetAsync(root + $"/{resolved.VerificationId}")) {
      Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
      var read = (await detail.Content.ReadFromJsonAsync<OpeningBalanceSnapshot>())!;
      Assert.True(read.CurrentIndependentReview); Assert.Equal("1000.123456", read.OpeningSignedTotal);
    }
    using (var denied = await siblingClient.GetAsync(root + $"/{resolved.VerificationId}")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

    // Amend after review; the original prepare and review receipts remain exact and immutable.
    var afterReview = await Prepare(resolvedFields with { Rationale = "Updated synthetic source comparison." });
    var amended = await Value<OpeningBalanceCommandReceiptView>(await Post(client, csrf, root + "/prepare", afterReview));
    Assert.Equal("4", amended.Result.Revision); Assert.False(amended.Result.CurrentIndependentReview);
    Assert.Null(amended.Result.ReviewedByUserId); Assert.Equal("2", amended.Result.ReviewedBasisRevision);
    Assert.Equal(review.Request.Comment, amended.Result.ReviewComment);
    var beforeReplay = await Stored();
    var original = await Value<OpeningBalanceCommandReceiptView>(await Post(client, csrf, root + "/prepare", unresolvedIntent));
    Assert.Equal(JsonSerializer.Serialize(unresolved), JsonSerializer.Serialize(original));
    var originalReview = await Value<OpeningBalanceCommandReceiptView>(await Post(reviewClient, reviewCsrf, root + $"/{resolved.VerificationId}/review", review.Request));
    Assert.Equal(JsonSerializer.Serialize(approved), JsonSerializer.Serialize(originalReview));
    using (var response = await reviewClient.GetAsync(root + $"/receipts/{review.Request.RequestId}?requestHash={review.Request.ExpectedRequestHash}")) {
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
      var recovered = (await response.Content.ReadFromJsonAsync<OpeningBalanceCommandLookup>())!;
      Assert.True(recovered.Found); Assert.Equal(JsonSerializer.Serialize(approved), JsonSerializer.Serialize(recovered.Receipt));
    }
    Assert.Equal(beforeReplay, await Stored());
    await Refused(client, csrf, root + "/prepare", unresolvedIntent with { Fields = resolvedFields }, HttpStatusCode.Conflict);
    using (var response = await siblingClient.GetAsync(root + $"/receipts/{review.Request.RequestId}?requestHash={review.Request.ExpectedRequestHash}")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    using (var response = await client.GetAsync(root + $"/receipts/{review.Request.RequestId}?requestHash={review.Request.ExpectedRequestHash}")) {
      Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.False((await response.Content.ReadFromJsonAsync<OpeningBalanceCommandLookup>())!.Found);
    }

    // Optional procedure linkage uses a supported adopted program, not an invented submitted-evidence gate.
    var otherClient = await SupportedAuditCompletionWorldBuilder.CreateAdditionalClientAsync(pg, world);
    var partner = await ProvisionFirmPartnerAsync(pg, world.Administrator, "r06-program-reviewer");
    Guid primaryProcedure; Guid siblingProcedure; Guid foreignPeriod; Guid barredEngagement;
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      var draft = await AuditProgramService.PrepareControlledAuditProgramAsync(db, world.Administrator, new("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(draft.Succeeded, draft.Message);
      var published = await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db, partner, new(draft.Value!.ProgramVersionId, draft.Value.ContentDigest!, "Synthetic controlled program review."));
      Assert.True(published.Succeeded, published.Message);
      foreach (var scope in new[] { world.Primary, world.Sibling })
        Assert.True((await AuditProgramService.AdoptAsync(db, scope.Preparer, new(scope.EngagementId, published.Value!.ProgramVersionId, AuditProgramCatalog.SourceHash, published.Value.ContentDigest))).Succeeded);
      primaryProcedure = await db.AuditProcedures.Where(x => x.EngagementId == world.Primary.EngagementId && x.SourceProcedureId == "AWP-01-01").Select(x => x.Id).SingleAsync();
      siblingProcedure = await db.AuditProcedures.Where(x => x.EngagementId == world.Sibling.EngagementId && x.SourceProcedureId == "AWP-01-01").Select(x => x.Id).SingleAsync();
      var period = await ClientAccountingService.CreatePeriodAsync(db, world.Administrator, new(otherClient, "FY2024", new(2024, 1, 1), new(2024, 12, 31), "IFRS", "QAR"));
      Assert.True(period.Succeeded, period.Message); foreignPeriod = period.Value;
      var barred = await EngagementLifecycleService.CreateDraftAsync(db, partner, new(world.Primary.ClientId, "FinancialStatementAudit", "2028-01-01", "2028-12-31", "FinancialStatementAudit"));
      Assert.True(barred.Succeeded, barred.Message); barredEngagement = barred.Value;
    }
    var fresh = await State(client);
    var wrongScope = new OpeningBalanceCommandRequest(Guid.CreateVersion7(), fresh.ExpectedRevision, fresh.ReviewBasis, resolvedFields, fresh.Record!.Id);
    await Refused(client, csrf, root + "/prepare/preview", wrongScope with { Fields = resolvedFields with { ProcedureId = siblingProcedure } }, HttpStatusCode.Forbidden);
    await Refused(client, csrf, root + "/prepare/preview", wrongScope with { Fields = resolvedFields with { PriorPeriodId = foreignPeriod } }, HttpStatusCode.Forbidden);
    var linked = await Prepare(resolvedFields with { AsOfDate = date.AddDays(1), ProcedureId = primaryProcedure });
    var linkedResult = await Value<OpeningBalanceCommandReceiptView>(await Post(client, csrf, root + "/prepare", linked));
    Assert.Equal(primaryProcedure, linkedResult.Result.ProcedureId);
    Assert.Null(linkedResult.Result.ReviewedByUserId);
    using var adminFactory = Factory(pg, await UserAsync(pg, world.Administrator.UserId));
    using var adminClient = adminFactory.CreateClient(new() { AllowAutoRedirect = false });
    var adminCsrf = await SignIn(adminClient);
    var barredRoot = $"/api/ui/engagements/{barredEngagement}/completion/opening-balances";
    using var stateResponse = await adminClient.GetAsync(barredRoot + $"/state?asOfDate={date:yyyy-MM-dd}");
    Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
    var barredState = (await stateResponse.Content.ReadFromJsonAsync<OpeningBalanceCommandState>())!;
    Assert.False(barredState.CanPrepare); Assert.False(barredState.CanReview);
    var barredIntent = new OpeningBalanceCommandRequest(Guid.CreateVersion7(), "0", barredState.ReviewBasis, fields);
    var barredPreview = await Value<OpeningBalanceCommandPreview>(await Post(adminClient, adminCsrf, barredRoot + "/prepare/preview", barredIntent));
    Assert.False(barredPreview.CanProceed);
    await Refused(adminClient, adminCsrf, barredRoot + "/prepare", barredIntent with { ExpectedRequestHash = barredPreview.RequestHash, Reviewed = true }, HttpStatusCode.BadRequest);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.False(verify.Database.HasPendingModelChanges());
    // Actual hold/freeze lifecycle is not fabricated here; its supported lifecycle verification remains separately reported.
  }
}
