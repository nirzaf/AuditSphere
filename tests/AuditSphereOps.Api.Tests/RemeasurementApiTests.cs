using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Api.Tests;
public sealed class RemeasurementApiTests
{
  private const string Path = "/api/ui/accounting/remeasurement";
  [Fact]
  public async Task ReviewedPreparation_IsExact_Idempotent_AndRequiresIndependentCurrentApproval()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-FX-LIFECYCLE"); var f = await PbcSeed.SeedAsync(pg);
    RemeasurementFixture.Inputs inputs; await using (var db = new AuditSphereDbContext(pg.Options)) inputs = await RemeasurementFixture.SeedAsync(db, f);
    using var sf = Factory(pg, f.Staff); using var rf = Factory(pg, f.Reviewer); using var staff = sf.CreateClient(new() { AllowAutoRedirect = false }); using var reviewer = rf.CreateClient(new() { AllowAutoRedirect = false });
    var sc = await SignIn(staff); var rc = await SignIn(reviewer); var choices = await Get(staff, Choices(inputs.Period, f.EngagementId)); var token = choices.GetProperty("baseRevision").GetString();
    var body = Body(f, inputs, token); Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(Path, body)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(staff, sc, Path, Body(f, inputs, token, reviewed: false))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(staff, sc, Path, Body(f, inputs, token, amount: "1.1234567"))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(staff, sc, Path, Body(f, inputs, token, amount: "1.00000000000000000000000000001"))).StatusCode);
    var responses = await Task.WhenAll(Post(staff, sc, Path, body), Post(staff, sc, Path, body));
    Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode)); var ids = await Task.WhenAll(responses.Select(async r => (await Json(r)).GetProperty("value").GetGuid())); Assert.Equal(ids[0], ids[1]);
    var id = ids[0]; var detail = await Get(staff, Path + "/" + id); Assert.False(detail.GetProperty("canApprove").GetBoolean());
    Assert.Equal("100.123456", detail.GetProperty("schedule").GetProperty("items")[0].GetProperty("foreignCurrencyAmount").GetString());
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(staff, sc, Path + "/" + id + "/approve", new { reviewed = true, reviewToken = detail.GetProperty("reviewToken").GetString() })).StatusCode);
    detail = await Get(reviewer, Path + "/" + id); Assert.True(detail.GetProperty("canApprove").GetBoolean()); var reviewToken = detail.GetProperty("reviewToken").GetString();
    Assert.Equal(HttpStatusCode.Conflict, (await Post(reviewer, rc, Path + "/" + id + "/approve", new { reviewed = false, reviewToken })).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await Post(reviewer, rc, Path + "/" + id + "/approve", new { reviewed = true, reviewToken })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(reviewer, rc, Path + "/" + id + "/approve", new { reviewed = true, reviewToken })).StatusCode);
    detail = await Get(reviewer, Path + "/" + id); Assert.Equal("APPROVED", detail.GetProperty("schedule").GetProperty("status").GetString()); Assert.False(detail.GetProperty("canApprove").GetBoolean());
    await using var proof = new AuditSphereDbContext(pg.Options); Assert.Single(await proof.CurrencyRemeasurementSchedules.ToListAsync()); Assert.Single(await proof.CurrencyRemeasurementItems.ToListAsync());
  }
  [Fact]
  public async Task SourceChanges_InvalidateReviewedPreparation_AndPersistStaleApprovalOutcome()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-FX-STALE"); var f = await PbcSeed.SeedAsync(pg);
    RemeasurementFixture.Inputs i; await using (var db = new AuditSphereDbContext(pg.Options)) i = await RemeasurementFixture.SeedAsync(db, f);
    using var sf = Factory(pg, f.Staff); using var rf = Factory(pg, f.Reviewer); using var staff = sf.CreateClient(new() { AllowAutoRedirect = false }); using var reviewer = rf.CreateClient(new() { AllowAutoRedirect = false });
    var sc = await SignIn(staff); var rc = await SignIn(reviewer); var before = await Get(staff, Choices(i.Period, f.EngagementId));
    var response = await Post(staff, sc, Path, Body(f, i, before.GetProperty("baseRevision").GetString())); Assert.Equal(HttpStatusCode.OK, response.StatusCode); var id = (await Json(response)).GetProperty("value").GetGuid();
    var reviewed = await Get(reviewer, Path + "/" + id);
    await using (var db = new AuditSphereDbContext(pg.Options)) await db.SourceImportBatches.Where(x => x.FirmId == f.FirmId && x.PeriodId == i.Period).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "REJECTED"));
    Assert.Equal(HttpStatusCode.Conflict, (await Post(staff, sc, Path, Body(f, i, before.GetProperty("baseRevision").GetString()))).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(reviewer, rc, Path + "/" + id + "/approve", new { reviewed = true, reviewToken = reviewed.GetProperty("reviewToken").GetString() })).StatusCode);
    reviewed = await Get(reviewer, Path + "/" + id);
    Assert.NotEqual(HttpStatusCode.OK, (await Post(reviewer, rc, Path + "/" + id + "/approve", new { reviewed = true, reviewToken = reviewed.GetProperty("reviewToken").GetString() })).StatusCode);
    await using var proof = new AuditSphereDbContext(pg.Options); Assert.Equal("STALE", (await proof.CurrencyRemeasurementSchedules.SingleAsync()).Status);
  }
  [Fact]
  public async Task ExactScope_ExpiredWiderGrant_GuessedIds_AndEpochRevocation_DoNotDiscloseInputs()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-FX-SCOPE"); var f = await PbcSeed.SeedAsync(pg); var other = await PbcSeed.SeedAsync(pg);
    RemeasurementFixture.Inputs i; await using (var db = new AuditSphereDbContext(pg.Options)) { i = await RemeasurementFixture.SeedAsync(db, f); await RemeasurementFixture.SeedAsync(db, other); var expired = PbcSeed.AdminGrant(f.FirmId, f.Staff); expired.GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-2); expired.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); db.RoleGrants.Add(expired);
      var sibling = Guid.NewGuid(); var siblingEngagement = Guid.NewGuid();
      db.PracticeClients.Add(new() { Id = sibling, FirmId = f.FirmId, LegalName = "Unauthorized sibling", CreatedAt = DateTimeOffset.UtcNow });
      db.Engagements.Add(new() { Id = siblingEngagement, FirmId = f.FirmId, PracticeClientId = sibling, Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
      db.ClientReportingPeriods.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = sibling, PeriodCode = "HIDDEN-SIBLING", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), Basis = "IFRS", Currency = "QAR", CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync(); }
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      var beforeExpiry = await AuditSphereOps.Application.Accounting.CurrencyRemeasurementWorkspaceQuery.GetAsync(db, PbcSeed.Actor(f.Staff, "AccountingPreparer"));
      Assert.True(beforeExpiry.Succeeded); Assert.Single(beforeExpiry.Value!.Contexts); Assert.Equal(f.ClientId, beforeExpiry.Value.Contexts[0].ClientId);
      Assert.True(await AuditSphereOps.Application.Security.RoleGrantExpiry.RevokeExpiredForUserAsync(db, f.FirmId, f.Staff.Id, DateTimeOffset.UtcNow));
      Assert.False((await AuditSphereOps.Application.Accounting.CurrencyRemeasurementWorkspaceQuery.GetAsync(db, PbcSeed.Actor(f.Staff, "AccountingPreparer"))).Succeeded);
    }
    using var factory = Factory(pg, f.Staff); using var staff = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(staff);
    var workspace = await Get(staff, Path); Assert.Single(workspace.GetProperty("contexts").EnumerateArray());
    foreach (var engagement in new[] { other.EngagementId, Guid.NewGuid() }) Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(Choices(i.Period, engagement))).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(Path + "/" + Guid.NewGuid())).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options)) { (await db.Users.SingleAsync(x => x.Id == f.Staff.Id)).SessionEpoch++; await db.SaveChangesAsync(); }
    Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync(Path)).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await Post(staff, csrf, Path, Body(f, i, new string('a', 64)))).StatusCode);
  }
  [Fact]
  public async Task HistoricalMissingGlLineage_IsReadable_WithoutInventingApprovalEligibility()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-FX-HISTORICAL"); var f = await PbcSeed.SeedAsync(pg); var id = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      var i = await RemeasurementFixture.SeedAsync(db, f);
      var rate = await db.ExchangeRates.SingleAsync(x => x.RateSetVersionId == i.RateSet && x.RateType == "CLOSING");
      db.CurrencyRemeasurementSchedules.Add(new() { Id = id, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, PeriodId = i.Period, RateSetVersionId = i.RateSet, TranslationPolicyVersionId = i.Policy, AsOfDate = new(2026, 12, 31), FunctionalCurrency = "QAR", InputHash = new string('b', 64), ItemCount = 1, Status = "SUBMITTED", CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow });
      db.CurrencyRemeasurementItems.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, ScheduleId = id, EvidenceSnapshotId = i.Snapshot, RateSetVersionId = i.RateSet, ExchangeRateId = rate.Id, StableItemReference = "historical-unlinked", SourceEvidenceSha256 = new string('a', 64), IsMonetary = true, ForeignCurrency = "USD", ForeignCurrencyAmount = 100m, PriorFunctionalCarryingAmount = 370m, RateDate = rate.RateDate, RateType = "CLOSING", AppliedRate = rate.Rate, RemeasuredFunctionalAmount = 370m });
      await db.SaveChangesAsync();
    }
    using var factory = Factory(pg, f.Reviewer); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c);
    var detail = await Get(c, Path + "/" + id); Assert.False(detail.GetProperty("canApprove").GetBoolean()); Assert.Contains("inspection only", detail.GetProperty("blocker").GetString());
    Assert.Equal(JsonValueKind.Null, detail.GetProperty("schedule").GetProperty("items")[0].GetProperty("sourceGeneralLedgerLineId").ValueKind);
    Assert.Equal("", detail.GetProperty("schedule").GetProperty("items")[0].GetProperty("sourceGlLineDigest").GetString());
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, csrf, Path + "/" + id + "/approve", new { reviewed = true, reviewToken = detail.GetProperty("reviewToken").GetString() })).StatusCode);
    await using var proof = new AuditSphereDbContext(pg.Options); Assert.Equal("SUBMITTED", (await proof.CurrencyRemeasurementSchedules.SingleAsync()).Status);
  }
  private static string Choices(Guid period, Guid engagement) => Path + "/choices?periodId=" + period + "&engagementId=" + engagement;
  private static object Body(PbcSeed.Fixture f, RemeasurementFixture.Inputs i, string? token, bool reviewed = true, string amount = "100.123456") => new { clientId = f.ClientId, engagementId = f.EngagementId, periodId = i.Period, rateSetId = i.RateSet, policyId = i.Policy, asOfDate = "2026-12-31", reviewToken = token, reviewed, items = new[] { new { reference = "open-001", snapshotId = i.Snapshot, sourceLineId = i.Line, isMonetary = true, currency = "USD", foreignAmount = amount, priorCarrying = "370.123456", historicalRateDate = (string?)null } } };
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AuditSphereOps.Domain.Security.AppUser user) => new(new Dictionary<string, string?> { ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:TenantId"] = user.TenantId, ["DevelopmentIdentity:Subject"] = user.Subject, ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
  private static async Task<string> SignIn(HttpClient c) { await c.GetAsync("/auth/sign-in"); using var r = await c.GetAsync("/api/ui/session"); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]); }
  private static async Task<JsonElement> Get(HttpClient c, string path) { using var r = await c.GetAsync(path); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return await Json(r); }
  private static async Task<JsonElement> Json(HttpResponseMessage r) { using var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return j.RootElement.Clone(); }
  private static Task<HttpResponseMessage> Post(HttpClient c, string csrf, string path, object body) { var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-XSRF-TOKEN", csrf); return c.SendAsync(request); }
}
