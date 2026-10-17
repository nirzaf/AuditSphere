using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class AuditProgramCommandsApiTests
{
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

    using var reviewerFactory = Factory(pg, f.Reviewer);
    using var reviewer = reviewerFactory.CreateClient(new() { AllowAutoRedirect = false });
    var reviewerCsrf = await SignIn(reviewer);
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
