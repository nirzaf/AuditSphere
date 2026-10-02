using System.Net;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Api.Tests;
public sealed class TrialBalanceUploadApiTests
{
  private const string Csv = "PeriodCode,AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode\nFY25,1000,Synthetic cash,100.123456,QAR,SYN-ENTITY,\nFY25,3000,Synthetic equity,-100.123456,QAR,SYN-ENTITY,\nFY26,1000,Synthetic cash,200.123456,QAR,SYN-ENTITY,\nFY26,3000,Synthetic equity,-200.123456,QAR,SYN-ENTITY,\n";
  [Fact]
  public async Task EngagementScopedPreview_AllPeriodValidation_Csrf_AndExactReceipts()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TB-UPLOAD"); var f = await Seed(pg);
    using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c); var root = Root(f);
    Assert.Equal(HttpStatusCode.Forbidden, (await Upload(c, "wrong", root + "/preview", Csv)).StatusCode);
    var invalid = await Preview(c, csrf, root, Csv.Replace("-200.123456", "-200.123455")); Assert.False(invalid.GetProperty("canImport").GetBoolean());
    Assert.Contains("does not balance", invalid.GetProperty("periods")[1].GetProperty("error").GetString());
    Assert.Equal(HttpStatusCode.BadRequest, (await Upload(c, csrf, root + "/import", Csv.Replace("-200.123456", "-200.123455"), invalid)).StatusCode);
    var preview = await Preview(c, csrf, root); Assert.True(preview.GetProperty("canImport").GetBoolean()); Assert.Equal("0.000000", preview.GetProperty("periods")[0].GetProperty("netTotal").GetString());
    Assert.Equal(HttpStatusCode.Conflict, (await Upload(c, csrf, root + "/import", Csv, preview, reviewed: false)).StatusCode);
    var imported = await Json(await Upload(c, csrf, root + "/import", Csv, preview));
    Assert.All(imported.GetProperty("periods").EnumerateArray(), p => { Assert.NotEqual(Guid.Empty, p.GetProperty("datasetId").GetGuid()); Assert.Equal("SEALED", p.GetProperty("importState").GetString()); Assert.Equal("Pending", p.GetProperty("validationStatus").GetString()); });
    var ids = Ids(imported);
    var replay = await Json(await Upload(c, csrf, root + "/import", Csv, preview)); Assert.Equal(ids, Ids(replay));
    var observed = await Json(await Upload(c, csrf, root + "/reconcile", Csv)); Assert.Equal(ids, Ids(observed));
    await using var db = new AuditSphereDbContext(pg.Options); Assert.Equal(2, await db.TrialBalanceDatasets.CountAsync()); Assert.Equal(4, await db.TrialBalanceRows.CountAsync());
    var mapping = await TrialBalanceIntakeWorkspaceQuery.CreateDraftAsync(db, PbcSeed.Actor(f.Staff, "Staff"), ids[0], "tax-v1", [], default); Assert.False(mapping.Succeeded); // Pending source is not accepted for mapping.
  }
  [Fact]
  public async Task WrongFile_ChangedPeriod_Currency_ClosedPeriod_AndUnknownPeriodFailClosed()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TB-STALE"); var f = await Seed(pg); using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c); var root = Root(f);
    var preview = await Preview(c, csrf, root);
    Assert.Equal(HttpStatusCode.Conflict, (await Upload(c, csrf, root + "/import", Csv.Replace("Synthetic cash", "Changed name"), preview)).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options)) { await db.ClientReportingPeriods.Where(x => x.PeriodCode == "FY26").ExecuteUpdateAsync(s => s.SetProperty(x => x.EndDate, new DateOnly(2026, 12, 30))); }
    Assert.Equal(HttpStatusCode.Conflict, (await Upload(c, csrf, root + "/import", Csv, preview)).StatusCode);
    foreach (var input in new[] { Csv.Replace("FY25", "UNKNOWN"), Csv.Replace("QAR", "USD"), Csv.Replace("FY26,3000,Synthetic equity,-200.123456,QAR,SYN-ENTITY", "FY26,3000,Synthetic equity,-200.123456,QAR,OTHER-ENTITY") }) Assert.False((await Preview(c, csrf, root, input)).GetProperty("canImport").GetBoolean());
    await using (var db = new AuditSphereDbContext(pg.Options)) { await db.ClientReportingPeriods.Where(x => x.PeriodCode == "FY26").ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "CLOSED")); }
    Assert.False((await Preview(c, csrf, root)).GetProperty("canImport").GetBoolean());
    await using var proof = new AuditSphereDbContext(pg.Options); Assert.Empty(await proof.TrialBalanceDatasets.ToListAsync());
  }
  [Fact]
  public async Task ConcurrentDuplicateAndPartialRecovery_ReuseOnlyExactScopedSources()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TB-RETRY"); var f = await Seed(pg); using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c); var root = Root(f);
    // A first period succeeded but the browser never observed the remaining periods.
    var first = Csv[..Csv.IndexOf("FY26,", StringComparison.Ordinal)]; var p = await Preview(c, csrf, root, first); await Json(await Upload(c, csrf, root + "/import", first, p));
    p = await Preview(c, csrf, root); Assert.NotEqual(Guid.Empty, p.GetProperty("periods")[0].GetProperty("datasetId").GetGuid()); Assert.Equal(JsonValueKind.Null, p.GetProperty("periods")[1].GetProperty("datasetId").ValueKind);
    var responses = await Task.WhenAll(Upload(c, csrf, root + "/import", Csv, p), Upload(c, csrf, root + "/import", Csv, p));
    Assert.Equal(Ids(await Json(responses[0])), Ids(await Json(responses[1])));
    var differentBytes = Csv.Replace("100.123456", "100.1234560"); var changed = await Preview(c, csrf, root, differentBytes);
    Assert.Equal(HttpStatusCode.BadRequest, (await Upload(c, csrf, root + "/import", differentBytes, changed)).StatusCode);
    await using var proof = new AuditSphereDbContext(pg.Options); Assert.Equal(2, await proof.TrialBalanceDatasets.CountAsync());
  }
  [Fact]
  public async Task ForeignAndGuessedEngagements_ClientUsers_Revocation_AndDisabledUsersExposeNoReceipts()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TB-SCOPE"); var f = await Seed(pg); var other = await Seed(pg);
    using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c);
    foreach (var id in new[] { other.EngagementId, Guid.NewGuid() }) foreach (var action in new[] { "preview", "reconcile", "import" }) Assert.Equal(HttpStatusCode.Forbidden, (await Upload(c, csrf, $"/api/ui/engagements/{id}/tb-intake/{action}", Csv)).StatusCode);
    using var cf = Factory(pg, f.Client); using var client = cf.CreateClient(new() { AllowAutoRedirect = false }); var cc = await SignIn(client); Assert.Equal(HttpStatusCode.Forbidden, (await Upload(client, cc, Root(f) + "/preview", Csv)).StatusCode);
    var p = await Preview(c, csrf, Root(f)); await using (var db = new AuditSphereDbContext(pg.Options)) { await db.Users.Where(x => x.Id == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1)); }
    Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(c, csrf, Root(f) + "/import", Csv, p)).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(c, csrf, Root(f) + "/reconcile", Csv)).StatusCode);
    using var af = Factory(pg, f.Admin); using var admin = af.CreateClient(new() { AllowAutoRedirect = false }); var ac = await SignIn(admin);
    await using (var db = new AuditSphereDbContext(pg.Options)) { await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Disabled, true)); }
    Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(admin, ac, Root(f) + "/preview", Csv)).StatusCode);
  }
  [Fact]
  public async Task MappingMemoryDoesNotRevealSiblingEngagementHistoryOrExpiredWiderGrants()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TB-MEMORY-SCOPE"); var f = await PbcSeed.SeedAsync(pg);
    AuditSphereOps.Testing.CurrencyReviewFixture.Inputs source; Guid mapping = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      source = await CurrencyReviewFixture.SeedAsync(db, f, hiddenPrior: true);
      var prior = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == source.PriorDataset);
      var expired = PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer", f.ClientId); expired.GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-2); expired.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); db.RoleGrants.Add(expired);
      db.MappingVersions.Add(new() { Id = mapping, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = prior.EngagementId, DatasetId = prior.Id, TaxonomyVersion = "SYN-TAX", PeriodStart = "2025-01-01", PeriodEnd = "2025-12-31", CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      db.MappingAllocations.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = prior.EngagementId, MappingVersionId = mapping, SourceAccountCode = "1000", DestinationCode = "HIDDEN-DESTINATION", StatementSection = "ASSETS", Fraction = 1m, Rationale = "Historical synthetic proposal", CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
      await db.MappingVersions.Where(x => x.Id == mapping).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "APPROVED").SetProperty(x => x.ApprovedByUserId, f.Reviewer.Id).SetProperty(x => x.ApprovedAt, DateTimeOffset.UtcNow));
    }
    // Exercise expired-grant filtering before the API resolver reconciles expiry and changes the session epoch.
    await using (var db = new AuditSphereDbContext(pg.Options)) { var beforeExpiry = await MappingMemoryService.ProposeAsync(db, PbcSeed.Actor(f.Staff, "Staff"), source.Dataset); Assert.True(beforeExpiry.Succeeded, beforeExpiry.Message); Assert.Null(beforeExpiry.Value!.SourceMappingVersionId); }
    using var sf = Factory(pg, f.Staff); using var staff = sf.CreateClient(new() { AllowAutoRedirect = false });
    await staff.GetAsync("/auth/sign-in"); await staff.GetAsync("/api/ui/session"); // Expiry can invalidate this first cookie; always sign in afresh.
    await SignIn(staff);
    using var af = Factory(pg, f.Admin); using var admin = af.CreateClient(new() { AllowAutoRedirect = false }); await SignIn(admin);
    var path = $"/api/ui/datasets/{source.Dataset}/mapping-memory";
    var scoped = await Json(await staff.GetAsync(path)); Assert.Equal(JsonValueKind.Null, scoped.GetProperty("sourceMappingVersionId").ValueKind);
    Assert.DoesNotContain("HIDDEN-PRIOR-MARKER", scoped.ToString()); Assert.DoesNotContain("HIDDEN-DESTINATION", scoped.ToString());
    var wide = await Json(await admin.GetAsync(path)); Assert.Equal(mapping, wide.GetProperty("sourceMappingVersionId").GetGuid()); Assert.Contains("HIDDEN-DESTINATION", wide.ToString());
    Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/ui/datasets/{source.PriorDataset}/mapping-memory")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/ui/datasets/{Guid.NewGuid()}/mapping-memory")).StatusCode);
  }
  private static string Root(PbcSeed.Fixture f) => $"/api/ui/engagements/{f.EngagementId}/tb-intake";
  private static async Task<PbcSeed.Fixture> Seed(OwnedPostgresDatabase pg) { var f = await PbcSeed.SeedAsync(pg); await using var db = new AuditSphereDbContext(pg.Options); foreach (var (code, year) in new[] { ("FY25", 2025), ("FY26", 2026) }) db.ClientReportingPeriods.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = code, StartDate = new(year, 1, 1), EndDate = new(year, 12, 31), Currency = "QAR", Basis = "IFRS", Status = "ACTIVE", CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); return f; }
  private static Guid[] Ids(JsonElement j) => j.GetProperty("periods").EnumerateArray().Select(x => x.GetProperty("datasetId").GetGuid()).ToArray();
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser u) => new(new Dictionary<string, string?> { ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:TenantId"] = u.TenantId, ["DevelopmentIdentity:Subject"] = u.Subject, ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
  private static async Task<string> SignIn(HttpClient c) { await c.GetAsync("/auth/sign-in"); using var r = await c.GetAsync("/api/ui/session"); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]); }
  private static async Task<JsonElement> Preview(HttpClient c, string csrf, string root, string csv = Csv) => await Json(await Upload(c, csrf, root + "/preview", csv));
  private static async Task<JsonElement> Json(HttpResponseMessage r) { Assert.Equal(HttpStatusCode.OK, r.StatusCode); using var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return j.RootElement.TryGetProperty("value", out var v) ? v.Clone() : j.RootElement.Clone(); }
  private static Task<HttpResponseMessage> Upload(HttpClient c, string csrf, string path, string csv, JsonElement? preview = null, bool reviewed = true) { var body = new MultipartFormDataContent(); body.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "synthetic-tb.csv"); if (preview is { } p) { body.Add(new StringContent(p.GetProperty("revision").GetString()!), "revision"); body.Add(new StringContent(p.GetProperty("fileSha256").GetString()!), "fileSha256"); body.Add(new StringContent(reviewed ? "true" : "false"), "reviewed"); } var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body }; request.Headers.Add("X-XSRF-TOKEN", csrf); return c.SendAsync(request); }
}
