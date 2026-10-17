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
    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Single(await proof.AuditProgramVersions.ToListAsync());
    Assert.Equal(165, await proof.AuditProgramProcedures.CountAsync());
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
