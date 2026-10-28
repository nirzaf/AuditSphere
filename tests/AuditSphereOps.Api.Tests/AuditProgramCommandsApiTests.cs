using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Api.Ui;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class AuditProgramCommandsApiTests
{
  [Fact]
  public async Task SamplingEvidenceBinding_RejectsOmittedNullAndBlankReferencesWithoutWriting()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("SAMPLING-EVIDENCE-BINDING");
    var f = await PbcSeed.SeedAsync(pg);
    var maker = PbcSeed.Actor(f.Staff, "Partner");
    var reviewer = PbcSeed.Actor(f.Reviewer, "Partner");
    Guid itemId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, f.Staff, "Partner"), PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
      await db.SaveChangesAsync();
      var draft = await AuditProgramService.PrepareControlledAuditProgramAsync(db, maker, new("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(draft.Succeeded, draft.Message);
      var published = await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db, reviewer,
        new(draft.Value!.ProgramVersionId, draft.Value.ContentDigest!, "sampling-binding-methodology-fixture"));
      Assert.True(published.Succeeded, published.Message);
      Assert.True((await AuditProgramService.AdoptAsync(db, maker,
        new(f.EngagementId, published.Value!.ProgramVersionId, AuditProgramCatalog.SourceHash, published.Value.ContentDigest))).Succeeded);
      await PlanningBasisSeed.EstablishAsync(db, f.FirmId, f.ClientId, f.EngagementId, maker, reviewer);
      var procedure = await db.AuditProcedures.SingleAsync(x => x.EngagementId == f.EngagementId && x.SourceProcedureId == "AWP-01-01");
      Assert.True((await AuditProgramService.DecideApplicabilityAsync(db, maker,
        new(procedure.Id, AuditApplicabilityActions.Applicable, "Synthetic sampled evidence", procedure.CurrentApplicabilityRevision,
          procedure.CurrentDefinitionRevision, Guid.CreateVersion7()))).Succeeded);
      var schedule = await AuditFieldworkService.CreateScheduleAsync(db, maker, new(f.EngagementId, "RECEIVABLES_LEAD", "sample-ledger", "sample-receipt",
        DateTimeOffset.UtcNow, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "Debit positive", Hashing.Sha256Hex("sample"), 10m,
        [new("sample", 1, "1000", "Sample", 10m, "QAR", null, new(2026, 12, 20), null, null, "{}")]));
      Assert.True(schedule.Succeeded, schedule.Message);
      Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer, new(schedule.Value!.ScheduleId, "Independent population review", true))).Succeeded);
      var selection = await AuditFieldworkService.CreateSelectionAsync(db, maker, new(f.EngagementId, procedure.Id, schedule.Value.ScheduleId,
        null, "KEY_ITEM", "Exact identified row", [new("sample", 10m, "QAR", "Selected exact row")]));
      Assert.True(selection.Succeeded, selection.Message);
      Assert.True((await AuditFieldworkService.ReviewSelectionAsync(db, reviewer, new(selection.Value!.SelectionId, "REVIEWED", null))).Succeeded);
      itemId = await db.AuditSelectionItems.Where(x => x.SelectionId == selection.Value.SelectionId).Select(x => x.Id).SingleAsync();
    }
    using var factory = Factory(pg, f.Staff);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    foreach (var route in new[] { "tests", "cut-off-test" })
    {
      foreach (var variant in new[] { "omitted", "null", "blank" })
      {
        var body = new Dictionary<string, object?>
        {
          ["workPerformed"] = "Reviewed retained sampled evidence", ["result"] = "PASS",
          ["periodEndDate"] = "2026-12-31", ["transactionDate"] = "2026-12-20"
        };
        if (variant != "omitted") body["evidenceReferences"] = variant == "null" ? null : new[] { " \t " };
        using var rejected = await Post(client, csrf, $"/api/ui/selection-items/{itemId:D}/{route}", body);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
      }
    }
    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await proof.AuditItemTests.ToListAsync());
    Assert.Empty(await proof.AuditCutOffTestRecords.ToListAsync());
  }

  [Fact]
  public async Task PublicationRequiresAntiforgeryAndAnIndependentFirmWideReviewer()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-AUDIT-PROGRAM-PUBLICATION");
    var f = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Staff, "Partner"),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
      await db.SaveChangesAsync();
    }

    using var preparerFactory = Factory(pg, f.Staff);
    using var preparer = preparerFactory.CreateClient(new() { AllowAutoRedirect = false });
    var preparerCsrf = await SignIn(preparer);
    const string prepareUrl = "/api/ui/audit/library/versions/prepare";
    var prepareBody = new { version = "2026.1", sourceHash = AuditProgramCatalog.SourceHash };
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(preparer, "invalid", prepareUrl, prepareBody)).StatusCode);
    using var prepareResponse = await Post(preparer, preparerCsrf, prepareUrl, prepareBody);
    Assert.Equal(HttpStatusCode.OK, prepareResponse.StatusCode);
    using var prepareJson = JsonDocument.Parse(await prepareResponse.Content.ReadAsStringAsync());
    var draft = prepareJson.RootElement.GetProperty("value");
    var versionId = draft.GetProperty("programVersionId").GetGuid();
    var contentDigest = draft.GetProperty("contentDigest").GetString()!;
    Assert.Equal("DRAFT", draft.GetProperty("status").GetString());
    Assert.Equal(165, draft.GetProperty("procedureCount").GetInt32());
    Assert.Equal(JsonValueKind.Null, draft.GetProperty("approvedByUserId").ValueKind);

    var publicationUrl = $"/api/ui/audit/library/versions/{versionId:D}/publish";
    var publicationBody = new { expectedContentDigest = contentDigest, publicationReviewReference = "api-methodology-review-2026-1" };
    using var selfReview = await Post(preparer, preparerCsrf, publicationUrl, publicationBody);
    Assert.Equal(HttpStatusCode.Forbidden, selfReview.StatusCode);

    using var reviewerFactory = Factory(pg, f.Reviewer);
    using var reviewer = reviewerFactory.CreateClient(new() { AllowAutoRedirect = false });
    var reviewerCsrf = await SignIn(reviewer);
    using var publishResponse = await Post(reviewer, reviewerCsrf, publicationUrl, publicationBody);
    Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
    using var publishJson = JsonDocument.Parse(await publishResponse.Content.ReadAsStringAsync());
    var published = publishJson.RootElement.GetProperty("value");
    Assert.Equal("PUBLISHED", published.GetProperty("status").GetString());
    Assert.Equal(f.Staff.Id, published.GetProperty("createdByUserId").GetGuid());
    Assert.Equal(f.Reviewer.Id, published.GetProperty("approvedByUserId").GetGuid());
    Assert.Equal("api-methodology-review-2026-1", published.GetProperty("publicationReviewReference").GetString());

    using var replay = await Post(reviewer, reviewerCsrf, publicationUrl, publicationBody);
    Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
    Assert.Equal(versionId, replayJson.RootElement.GetProperty("value").GetProperty("programVersionId").GetGuid());

    var optionsUrl = $"/api/ui/engagements/{f.EngagementId:D}/fieldwork/program-options";
    using var optionsResponse = await preparer.GetAsync(optionsUrl);
    Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
    using var optionsJson = JsonDocument.Parse(await optionsResponse.Content.ReadAsStringAsync());
    var option = optionsJson.RootElement.GetProperty("options").EnumerateArray()
      .Single(x => x.GetProperty("programVersionId").GetGuid() == versionId);
    Assert.True(option.GetProperty("eligible").GetBoolean());
    var adoptionUrl = $"/api/ui/engagements/{f.EngagementId:D}/fieldwork/program";
    var adoptionBody = new { programVersionId = versionId, sourceHash = AuditProgramCatalog.SourceHash,
      contentDigest, expectedAdoptionId = (Guid?)null };
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(preparer, "invalid", adoptionUrl, adoptionBody)).StatusCode);
    using var adoptionResponse = await Post(preparer, preparerCsrf, adoptionUrl, adoptionBody);
    Assert.Equal(HttpStatusCode.OK, adoptionResponse.StatusCode);
    using var adoptionJson = JsonDocument.Parse(await adoptionResponse.Content.ReadAsStringAsync());
    var adoption = adoptionJson.RootElement.GetProperty("value");
    var adoptionId = adoption.GetProperty("engagementProgramId").GetGuid();
    Assert.Equal(165, adoption.GetProperty("baselineProcedureCount").GetInt32());
    Assert.Equal(0, adoption.GetProperty("adHocProcedureCount").GetInt32());
    var adoptionReplay = await Post(preparer, preparerCsrf, adoptionUrl, adoptionBody with { expectedAdoptionId = adoptionId });
    Assert.Equal(HttpStatusCode.OK, adoptionReplay.StatusCode);
    using var fieldworkResponse = await preparer.GetAsync($"/api/ui/engagements/{f.EngagementId:D}/fieldwork");
    Assert.Equal(HttpStatusCode.OK, fieldworkResponse.StatusCode);
    using var fieldworkJson = JsonDocument.Parse(await fieldworkResponse.Content.ReadAsStringAsync());
    var displayedProcedure = fieldworkJson.RootElement.GetProperty("procedures").EnumerateArray()
      .Single(x => x.GetProperty("sourceProcedureId").GetString() == "AWP-01-01");
    var sourceWording = AuditProgramCatalog.Items.Single(x => x.SourceProcedureId == "AWP-01-01").SourceWording;
    Assert.Equal(sourceWording, displayedProcedure.GetProperty("originalWording").GetString());
    Assert.Equal(sourceWording, displayedProcedure.GetProperty("effectiveWording").GetString());
    Assert.Equal("AUTHORED_REVISION", displayedProcedure.GetProperty("definitionProvenance").GetString());
    Assert.Equal(1, displayedProcedure.GetProperty("definitionRevision").GetInt32());

    var procedureId = displayedProcedure.GetProperty("id").GetGuid();
    var applicabilityRevision = displayedProcedure.GetProperty("applicabilityRevision").GetInt32();
    var definitionRevision = displayedProcedure.GetProperty("definitionRevision").GetInt32();
    var applicabilityUrl = $"/api/ui/procedures/{procedureId:D}/applicability";
    var proposalBody = new
    {
      decision = "PROPOSE_NOT_APPLICABLE", rationale = "This synthetic engagement has no inventory population.",
      expectedApplicabilityRevision = applicabilityRevision, expectedDefinitionRevision = definitionRevision,
      commandId = Guid.NewGuid(), proposalDecisionId = (Guid?)null
    };
    using var proposalResponse = await Post(preparer, preparerCsrf, applicabilityUrl, proposalBody);
    Assert.Equal(HttpStatusCode.OK, proposalResponse.StatusCode);
    using var proposalJson = JsonDocument.Parse(await proposalResponse.Content.ReadAsStringAsync());
    var proposal = proposalJson.RootElement.GetProperty("value");
    Assert.Equal("NA_PENDING_REVIEW", proposal.GetProperty("status").GetString());
    Assert.Equal("PROPOSE_NOT_APPLICABLE", proposal.GetProperty("action").GetString());
    var proposalId = proposal.GetProperty("decisionId").GetGuid();
    var proposalRevision = proposal.GetProperty("applicabilityRevision").GetInt32();

    var selfApproval = new
    {
      decision = "APPROVE_NOT_APPLICABLE", rationale = (string?)null,
      expectedApplicabilityRevision = proposalRevision, expectedDefinitionRevision = definitionRevision,
      commandId = Guid.NewGuid(), proposalDecisionId = proposalId
    };
    using var selfApprovalResponse = await Post(preparer, preparerCsrf, applicabilityUrl, selfApproval);
    Assert.Equal(HttpStatusCode.Forbidden, selfApprovalResponse.StatusCode);

    using var reviewerWorkspace = await reviewer.GetAsync($"/api/ui/engagements/{f.EngagementId:D}/fieldwork");
    Assert.Equal(HttpStatusCode.OK, reviewerWorkspace.StatusCode);
    using var reviewerWorkspaceJson = JsonDocument.Parse(await reviewerWorkspace.Content.ReadAsStringAsync());
    var pendingView = reviewerWorkspaceJson.RootElement.GetProperty("procedures").EnumerateArray()
      .Single(x => x.GetProperty("id").GetGuid() == procedureId);
    Assert.True(pendingView.GetProperty("canApproveNotApplicable").GetBoolean());
    Assert.Equal(proposalId, pendingView.GetProperty("pendingApplicabilityProposalId").GetGuid());
    var approvalBody = new
    {
      decision = "APPROVE_NOT_APPLICABLE", rationale = (string?)null,
      expectedApplicabilityRevision = proposalRevision, expectedDefinitionRevision = definitionRevision,
      commandId = Guid.NewGuid(), proposalDecisionId = proposalId
    };
    using var approvalResponse = await Post(reviewer, reviewerCsrf, applicabilityUrl, approvalBody);
    Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);
    using var approvalJson = JsonDocument.Parse(await approvalResponse.Content.ReadAsStringAsync());
    Assert.Equal("NA_APPROVED", approvalJson.RootElement.GetProperty("value").GetProperty("status").GetString());

    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Single(await proof.AuditProgramVersions.ToListAsync());
    Assert.Equal(165, await proof.AuditProgramProcedures.CountAsync());
    Assert.Single(await proof.EngagementAuditPrograms.ToListAsync());
    Assert.Equal(165, await proof.AuditProcedures.CountAsync());
  }

  [Fact]
  public async Task ProcedureCommandHttpPublishesAndRecoversTheExactSubmittedOutcome()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-AUDIT-PROCEDURE-COMMAND");
    var f = await PbcSeed.SeedAsync(pg);
    var preparer = PbcSeed.Actor(f.Staff, "Partner");
    var reviewer = PbcSeed.Actor(f.Reviewer, "Partner");
    Guid procedureId;

    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.RoleGrants.AddRange(
        PbcSeed.Grant(f.FirmId, f.Staff, "Partner"),
        PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
      await setup.SaveChangesAsync();

      var draft = await AuditProgramService.PrepareControlledAuditProgramAsync(setup, preparer,
        new("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(draft.Succeeded, draft.Message);
      var published = await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(setup, reviewer,
        new(draft.Value!.ProgramVersionId, draft.Value.ContentDigest!, "api-procedure-command-review"));
      Assert.True(published.Succeeded, published.Message);
      var adopted = await AuditProgramService.AdoptAsync(setup, preparer,
        new(f.EngagementId, published.Value!.ProgramVersionId, AuditProgramCatalog.SourceHash,
          published.Value.ContentDigest));
      Assert.True(adopted.Succeeded, adopted.Message);

      await PlanningBasisSeed.EstablishAsync(setup, f.FirmId, f.ClientId, f.EngagementId, preparer, reviewer);
      var procedure = await setup.AuditProcedures.SingleAsync(x => x.EngagementId == f.EngagementId &&
        x.SourceProcedureId == "AWP-01-01");
      procedureId = procedure.Id;
      var applicability = await AuditProgramService.DecideApplicabilityAsync(setup, preparer,
        new(procedureId, AuditApplicabilityActions.Applicable,
          "The synthetic engagement includes registration particulars for inspection.",
          procedure.CurrentApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.CreateVersion7()));
      Assert.True(applicability.Succeeded, applicability.Message);
    }

    using var factory = Factory(pg, f.Staff);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    var root = $"/api/ui/procedures/{procedureId:D}/commands";
    using var stateResponse = await client.GetAsync(root + "/state");
    Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
    using var stateJson = JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync());
    var basis = stateJson.RootElement.GetProperty("basis");
    Assert.Equal("AWP-01-01", basis.GetProperty("sourceProcedureId").GetString());
    Assert.True(basis.GetProperty("canSubmit").GetBoolean());
    var requestId = Guid.NewGuid();
    var request = new UiEndpoints.ProcedureCommandInput(requestId, ProcedureCommandActions.SubmitResult,
      basis.GetProperty("definitionRevision").GetInt32(), basis.GetProperty("inputGeneration").GetInt64(),
      basis.GetProperty("currentResultRevision").GetInt64(), null,
      "Inspected the synthetic company registration record against the entity profile.",
      "{\"observation\":\"name and registration number agree\"}", ["manual:synthetic-registration-record"],
      "The inspected registration particulars agree.", null, null, false, null,
      [new ProcedureEvidenceSelection(ProcedureEvidenceSelectionKinds.ManualReference,
        ManualReference: "SYNTHETIC-REGISTRATION-01", Observation: "The name and registration number agree to the entity profile.")]);
    using var previewResponse = await Post(client, csrf, root + "/preview", request);
    Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
    using var previewJson = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
    var preview = previewJson.RootElement.GetProperty("value");
    Assert.True(preview.GetProperty("canProceed").GetBoolean());
    Assert.Equal(ProcedureCommandActions.SubmitResult, preview.GetProperty("action").GetString());
    var requestHash = preview.GetProperty("requestHash").GetString();
    Assert.Matches("^[0-9a-f]{64}$", requestHash!);

    using var unassented = await Post(client, csrf, root + "/actions", request with
    {
      Reviewed = false,
      ExpectedRequestHash = requestHash
    });
    Assert.Equal(HttpStatusCode.BadRequest, unassented.StatusCode);

    var action = request with { Reviewed = true, ExpectedRequestHash = requestHash };
    using var response = await Post(client, csrf, root + "/actions", action);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var receipt = responseJson.RootElement.GetProperty("value");
    Assert.Equal(requestId, receipt.GetProperty("requestId").GetGuid());
    Assert.Equal(requestHash, receipt.GetProperty("requestHash").GetString());
    Assert.Equal(ProcedureCommandActions.SubmitResult, receipt.GetProperty("action").GetString());
    Assert.Equal(f.Staff.SessionEpoch, receipt.GetProperty("actorEpoch").GetInt64());
    Assert.Equal(1L, receipt.GetProperty("resultRevision").GetInt64());
    var receiptId = receipt.GetProperty("id").GetGuid();

    using var replay = await Post(client, csrf, root + "/actions", action);
    Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
    Assert.Equal(receiptId, replayJson.RootElement.GetProperty("value").GetProperty("id").GetGuid());
    var lookup = await client.GetFromJsonAsync<JsonElement>(
      root + $"/receipts/{requestId:D}?requestHash={requestHash}");
    Assert.True(lookup.GetProperty("found").GetBoolean());
    Assert.Equal(receiptId, lookup.GetProperty("receipt").GetProperty("id").GetGuid());

    await using var proof = new AuditSphereDbContext(pg.Options);
    var savedResult = await proof.AuditProcedureResults.SingleAsync(x => x.AuditProcedureId == procedureId);
    using var structured = JsonDocument.Parse(savedResult.StructuredResultJson);
    var evidenceSnapshot = structured.RootElement.GetProperty(AuditProgramService.EvidenceSnapshotProperty);
    var selectedEvidence = Assert.Single(evidenceSnapshot.GetProperty("selections").EnumerateArray());
    Assert.Equal("SYNTHETIC-REGISTRATION-01", selectedEvidence.GetProperty("manualReference").GetString());
    Assert.Equal("The name and registration number agree to the entity profile.",
      selectedEvidence.GetProperty("observation").GetString());
    Assert.Single(await proof.AuditProcedureCommandReceipts.Where(x => x.AuditProcedureId == procedureId).ToListAsync());
    Assert.Single(await proof.WorkpaperSubmissions.Where(x => x.EngagementId == f.EngagementId).ToListAsync());
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser user) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
    ["DevelopmentIdentity:Enabled"] = "true",
    ["DevelopmentIdentity:TenantId"] = user.TenantId,
    ["DevelopmentIdentity:Subject"] = user.Subject,
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
