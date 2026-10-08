using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Api.Tests;
public sealed class CurrencyConfigurationApiTests
{
  private const string Root = "/api/ui/accounting/currency-configuration";
  [Fact]
  public async Task ReviewedExactPreparation_IndependentApproval_AndImmutableApprovedInputs()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CURRENCY-CONFIG"); var f = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options)) { db.RoleGrants.Add(PbcSeed.AdminGrant(f.FirmId, f.Reviewer)); await db.SaveChangesAsync(); }
    using var makerFactory = Factory(pg, f.Admin); using var checkerFactory = Factory(pg, f.Reviewer);
    using var maker = makerFactory.CreateClient(new() { AllowAutoRedirect = false }); using var checker = checkerFactory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(maker); var checkerCsrf = await SignIn(checker); var revision = Rev(await Get(maker, Root));
    var input = Set(revision); Assert.Equal(HttpStatusCode.Forbidden, (await Post(maker, "wrong", Root + "/sets", input)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(maker, csrf, Root + "/sets", Set(revision, false))).StatusCode);
    var id = await Created(await Post(maker, csrf, Root + "/sets", input));
    Assert.Equal(HttpStatusCode.Conflict, (await Post(maker, csrf, Root + "/sets", input)).StatusCode);
    var detail = await Get(maker, Root + "/sets/" + id); Assert.True(detail.GetProperty("canWrite").GetBoolean()); Assert.False(detail.GetProperty("canApprove").GetBoolean());
    var old = Rev(detail);
    foreach (var rate in new[] { "0", "-1", "1e3", "3.1234567", "1234567890123", "3,70" }) Assert.Equal(HttpStatusCode.BadRequest, (await Post(maker, csrf, Root + $"/sets/{id}/rates", Rate(old, rate))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(maker, csrf, Root + $"/sets/{id}/rates", Rate(old, "3.700001", "INVERSE"))).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await Post(maker, csrf, Root + $"/sets/{id}/rates", Rate(old))).StatusCode);
    detail = await Get(maker, Root + "/sets/" + id); Assert.Equal("3.700001", detail.GetProperty("rates")[0].GetProperty("rate").GetString());
    Assert.Equal(HttpStatusCode.Conflict, (await Post(maker, csrf, Root + $"/sets/{id}/approve", new { revision = old, reviewed = true })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(maker, csrf, Root + $"/sets/{id}/approve", new { revision = Rev(detail), reviewed = true })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(maker, csrf, Root + $"/sets/{id}/rates", Rate(Rev(detail)))).StatusCode);
    var independent = await Get(checker, Root + "/sets/" + id); Assert.True(independent.GetProperty("canApprove").GetBoolean());
    Assert.Equal(HttpStatusCode.OK, (await Post(checker, checkerCsrf, Root + $"/sets/{id}/approve", new { revision = Rev(independent), reviewed = true })).StatusCode);
    detail = await Get(maker, Root + "/sets/" + id); Assert.False(detail.GetProperty("canWrite").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(maker, csrf, Root + $"/sets/{id}/rates", Rate(Rev(detail), "4"))).StatusCode);
    revision = Rev(await Get(maker, Root));
    var policy = await Created(await Post(maker, csrf, Root + "/policies", new { code = "SYN-POLICY", functionalCurrency = "USD", presentationCurrency = "QAR", closingRule = "CLOSING", averageRule = "AVERAGE", historicalRule = "HISTORICAL", revision, reviewed = true }));
    var p = await Get(maker, Root + "/policies/" + policy); Assert.False(p.GetProperty("canApprove").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(maker, csrf, Root + $"/policies/{policy}/approve", new { revision = Rev(p), reviewed = true })).StatusCode);
    p = await Get(checker, Root + "/policies/" + policy);
    Assert.Equal(HttpStatusCode.OK, (await Post(checker, checkerCsrf, Root + $"/policies/{policy}/approve", new { revision = Rev(p), reviewed = true })).StatusCode);
    await using var proof = new AuditSphereDbContext(pg.Options);
    var approved = await proof.ExchangeRateSetVersions.SingleAsync(); Assert.Equal(f.Admin.Id, approved.CreatedByUserId); Assert.Equal(f.Reviewer.Id, approved.ApprovedByUserId); Assert.Equal("APPROVED", approved.Status); Assert.Single(await proof.ExchangeRates.ToListAsync());
  }
  [Fact]
  public async Task FirmWideScopeOnly_UnknownAndOtherFirmIds_AndRevocation()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CURRENCY-CONFIG-SCOPE"); var f = await PbcSeed.SeedAsync(pg); var other = await PbcSeed.SeedAsync(pg);
    using var af = Factory(pg, f.Admin); using var sf = Factory(pg, f.Staff); using var of = Factory(pg, other.Admin);
    using var a = af.CreateClient(new() { AllowAutoRedirect = false }); using var s = sf.CreateClient(new() { AllowAutoRedirect = false }); using var o = of.CreateClient(new() { AllowAutoRedirect = false }); var ac = await SignIn(a); var sc = await SignIn(s); var oc = await SignIn(o);
    var id = await Created(await Post(o, oc, Root + "/sets", Set(Rev(await Get(o, Root)))));
    Assert.Equal(HttpStatusCode.Forbidden, (await s.GetAsync(Root)).StatusCode);
    foreach (var unknown in new[] { id, Guid.NewGuid() }) { Assert.Equal(HttpStatusCode.Forbidden, (await a.GetAsync(Root + "/sets/" + unknown)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await s.GetAsync(Root + "/sets/" + unknown)).StatusCode); }
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(s, sc, Root + "/sets", Set(new string('a', 64)))).StatusCode);
    var rev = Rev(await Get(a, Root));
    await using (var db = new AuditSphereDbContext(pg.Options)) { await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1)); }
    Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync(Root)).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await Post(a, ac, Root + "/sets", Set(rev))).StatusCode);
  }
  [Fact]
  public async Task ConcurrentReviewedRetry_CreatesOneRecord_AndDisabledActorBlocksWrites()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CURRENCY-CONFIG-RETRY"); var f = await PbcSeed.SeedAsync(pg);
    using var factory = Factory(pg, f.Admin); using var a = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(a); var input = Set(Rev(await Get(a, Root)));
    var results = await Task.WhenAll(Post(a, csrf, Root + "/sets", input), Post(a, csrf, Root + "/sets", input)); Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
    await using var db = new AuditSphereDbContext(pg.Options); Assert.Single(await db.ExchangeRateSetVersions.ToListAsync());
    var revision = Rev(await Get(a, Root)); await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Disabled, true));
    Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync(Root)).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await Post(a, csrf, Root + "/sets", Set(revision))).StatusCode);
  }
  [Fact]
  public async Task InvalidStoredObservation_CannotBecomeApproved()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CURRENCY-INVALID-OBSERVATION"); var f = await PbcSeed.SeedAsync(pg);
    Guid id = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      db.ExchangeRateSetVersions.Add(new() { Id = id, FirmId = f.FirmId, Code = "SYN-INVALID", Source = "Synthetic legacy input", EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31), CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow });
      db.ExchangeRates.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, RateSetVersionId = id, FromCurrency = "USD", ToCurrency = "QAR", RateDate = new(2026, 12, 31), RateType = "CLOSING", Rate = 3.7m, Direction = "INVERSE", CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
    }
    using var factory = Factory(pg, f.Admin); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c);
    var detail = await Get(c, Root + "/sets/" + id); Assert.False(detail.GetProperty("canApprove").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, csrf, Root + $"/sets/{id}/approve", new { revision = Rev(detail), reviewed = true })).StatusCode);
    await using var proof = new AuditSphereDbContext(pg.Options); Assert.Equal("DRAFT", (await proof.ExchangeRateSetVersions.SingleAsync()).Status);
  }
  private static object Set(string revision, bool reviewed = true) => new { code = "SYN-RATES", source = "Synthetic bank observation", version = 1, effectiveFrom = "2026-01-01", effectiveTo = "2026-12-31", revision, reviewed };
  private static object Rate(string revision, string rate = "3.700001", string direction = "DIRECT") => new { fromCurrency = "USD", toCurrency = "QAR", date = "2026-12-31", purpose = "CLOSING", rate, direction, revision, reviewed = true };
  private static string Rev(JsonElement j) => j.GetProperty("revision").GetString()!;
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser u) => new(new Dictionary<string, string?> { ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:TenantId"] = u.TenantId, ["DevelopmentIdentity:Subject"] = u.Subject, ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
  private static async Task<string> SignIn(HttpClient c) { await c.GetAsync("/auth/sign-in"); using var r = await c.GetAsync("/api/ui/session"); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]); }
  private static async Task<JsonElement> Get(HttpClient c, string path) { using var r = await c.GetAsync(path); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return await Json(r); }
  private static async Task<JsonElement> Json(HttpResponseMessage r) { using var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return j.RootElement.Clone(); }
  private static async Task<Guid> Created(HttpResponseMessage r) { Assert.Equal(HttpStatusCode.OK, r.StatusCode); return (await Json(r)).GetProperty("value").GetGuid(); }
  private static Task<HttpResponseMessage> Post(HttpClient c, string csrf, string path, object body) { var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-XSRF-TOKEN", csrf); return c.SendAsync(request); }
}
