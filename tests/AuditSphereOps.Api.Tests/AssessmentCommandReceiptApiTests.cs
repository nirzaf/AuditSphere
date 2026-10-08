using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class AssessmentCommandReceiptApiTests
{
  [Fact]
  public async Task ReviewedAssessmentCsrfRecoveryIsolationAndRetainedEvidenceAreEnforced()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-ASSESSMENT-RECEIPTS");
    var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      await AssessmentParitySeed.PopulateAsync(db, f);
      var migrations = db.Database.GetMigrations().ToArray();
      var index = Array.FindIndex(migrations, x => x.EndsWith("_NativeAssessmentCommandReceipts", StringComparison.Ordinal));
      Assert.True(index > 0);
      await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
      await db.GetService<IMigrator>().MigrateAsync();
    }
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Admin.Subject, ["DevelopmentIdentity:TenantId"] = f.Admin.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
    using var c = factory.CreateClient(new() { AllowAutoRedirect = false });
    var url = "/api/ui/clients/" + f.ClientId + "/assessment";
    var request = new AssessmentCommandRequest(Guid.NewGuid(), new("DECISION", "2", ServiceRoute:"AccountingOnly", Decision:"Deferred", Rationale:"Exact API reviewed decision"));
    Assert.Equal(HttpStatusCode.Unauthorized, (await Post(c, "forged", url + "/preview", request)).StatusCode);
    await c.GetAsync("/auth/sign-in");
    using var session = await c.GetAsync("/api/ui/session");
    var csrf = Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "forged", url + "/preview", request)).StatusCode);
    using var preview = await Post(c, csrf, url + "/preview", request);
    Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
    Assert.True(preview.Headers.CacheControl!.NoStore);
    var p = JsonDocument.Parse(await preview.Content.ReadAsStringAsync()).RootElement.GetProperty("value")
      .Deserialize<AssessmentCommandPreview>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, csrf, url + "/commands", request)).StatusCode);
    request = request with { Fields=p.Fields, Reviewed=true, ReviewBasis=p.ReviewBasis, RequestHash=p.RequestHash };
    using var first = await Post(c, csrf, url + "/commands", request);
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    Assert.True(first.Headers.CacheControl!.NoStore);
    using var replay = await Post(c, csrf, url + "/commands", request);
    Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
    var reference = "/receipts/" + request.RequestId + "?requestHash=" + p.RequestHash;
    using var lookup = await c.GetAsync(url + reference);
    Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
    Assert.True(lookup.Headers.CacheControl!.NoStore);
    Assert.True((await lookup.Content.ReadFromJsonAsync<AssessmentReceiptLookup>())!.Found);
    using var denied = await c.GetAsync("/api/ui/clients/" + foreign.ClientId + "/assessment" + reference);
    using var missing = await c.GetAsync("/api/ui/clients/" + Guid.NewGuid() + "/assessment" + reference);
    Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    Assert.Equal(denied.StatusCode, missing.StatusCode);
    Assert.Equal(await denied.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      var migrations = db.Database.GetMigrations().ToArray();
      var index = Array.FindIndex(migrations, x => x.EndsWith("_NativeAssessmentCommandReceipts", StringComparison.Ordinal));
      await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]));
      Assert.Single(await db.AssessmentCommandReceipts.ToListAsync());
      await db.RoleGrants.Where(x => x.UserId == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    }
    Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(url + reference)).StatusCode);
  }

  private static Task<HttpResponseMessage> Post(HttpClient c, string csrf, string url, object input)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, url) { Content=JsonContent.Create(input) };
    request.Headers.Add("X-XSRF-TOKEN", csrf);
    return c.SendAsync(request);
  }
}
