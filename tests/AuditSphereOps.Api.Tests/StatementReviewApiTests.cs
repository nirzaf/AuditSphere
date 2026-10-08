using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Api.Tests;

public sealed class StatementReviewApiTests
{
  [Fact]
  public async Task CompleteTotalsPagedContributionsAndExportShareOneApprovedBasis()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-STATEMENT-REVIEW"); var f = await PbcSeed.SeedAsync(pg);
    StatementReviewSeed.Context seed; await using (var db = new AuditSphereDbContext(pg.Options)) seed = await StatementReviewSeed.SeedAsync(db, f, true);
    using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(); var csrf = await SignIn(c); var url = Url(f.EngagementId);
    var s = await Read(c, url + "/workspace?section=position"); var basis = s.GetProperty("basis"); var revision = basis.GetProperty("revision").GetString()!;
    Assert.Equal(seed.MappingId, basis.GetProperty("mappingId").GetGuid()); Assert.Equal(seed.DatasetId, basis.GetProperty("datasetId").GetGuid());
    Assert.Equal("100.123486", s.GetProperty("total").GetString()); Assert.True(s.GetProperty("balances").GetBoolean());
    var first = await Read(c, Detail(url, revision)); var second = await Read(c, Detail(url, revision) + "&page=2");
    Assert.Equal(31, first.GetProperty("accountCount").GetInt32()); Assert.Equal(25, first.GetProperty("accounts").GetArrayLength()); Assert.Equal(6, second.GetProperty("accounts").GetArrayLength());
    var sum = new[] { first, second }.SelectMany(x => x.GetProperty("accounts").EnumerateArray()).Sum(x => decimal.Parse(x.GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
    Assert.Equal(100.123486m, sum); Assert.Equal("100.123486", first.GetProperty("lineTotal").GetString());
    Assert.Equal(seed.CashProcedureId, first.GetProperty("procedures")[0].GetProperty("procedureId").GetGuid());
    var empty = await Read(c, url + "/workspace?section=position&filter=not-a-line"); Assert.Equal(0, empty.GetProperty("filteredCount").GetInt32()); Assert.Equal(s.GetProperty("total").GetString(), empty.GetProperty("total").GetString());
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "wrong", url + "/export", new { revision })).StatusCode);
    using var export = await Post(c, csrf, url + "/export", new { revision }); Assert.Equal(HttpStatusCode.OK, export.StatusCode);
    Assert.Equal(revision, export.Headers.GetValues("X-Statement-Basis").Single()); var csv = await export.Content.ReadAsStringAsync();
    Assert.Contains(seed.DatasetId.ToString(), csv); Assert.Contains("100.123486", csv); Assert.Contains("\"'=SYNTHETIC_FORMULA\"", csv);
    Assert.Equal(34, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(url + "/workspace?page=2")).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Detail(url, revision) + "&page=3")).StatusCode);
  }

  [Fact]
  public async Task ChangedGenerationOrSupportingProcedureRefusesOldDetailAndExport()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-STATEMENT-STALE"); var f = await PbcSeed.SeedAsync(pg);
    StatementReviewSeed.Context seed; await using (var db = new AuditSphereDbContext(pg.Options)) seed = await StatementReviewSeed.SeedAsync(db, f);
    using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(); var csrf = await SignIn(c); var url = Url(f.EngagementId);
    var s = await Read(c, url + "/workspace?section=position"); var revision = s.GetProperty("basis").GetProperty("revision").GetString()!;
    await using (var db = new AuditSphereDbContext(pg.Options)) await db.AuditProcedures.Where(x => x.Id == seed.CashProcedureId).ExecuteUpdateAsync(x => x.SetProperty(p => p.Title, "Changed synthetic evidence context"));
    Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync(Detail(url, revision))).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(c, csrf, url + "/export", new { revision })).StatusCode);
    var fresh = await Read(c, url + "/workspace?section=position"); Assert.NotEqual(revision, fresh.GetProperty("basis").GetProperty("revision").GetString());
    await using (var db = new AuditSphereDbContext(pg.Options)) await db.ClientSafetyStates.Where(x => x.Id == f.ClientId).ExecuteUpdateAsync(x => x.SetProperty(p => p.InputGeneration, p => p.InputGeneration + 1));
    using var stale = await c.GetAsync(url + "/workspace"); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.DoesNotContain(seed.DatasetId.ToString(), await stale.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task RestrictedAndGuessedScopesExposeNoStatementOrEvidencePopulations()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-STATEMENT-SCOPE"); var f = await PbcSeed.SeedAsync(pg);
    StatementReviewSeed.Context seed; await using (var db = new AuditSphereDbContext(pg.Options)) seed = await StatementReviewSeed.SeedAsync(db, f);
    using var factory = Factory(pg, f.Client); using var c = factory.CreateClient(); var csrf = await SignIn(c);
    foreach (var id in new[] { f.EngagementId, Guid.NewGuid() })
    {
      using var r = await c.GetAsync(Url(id) + "/workspace"); Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode); Assert.DoesNotContain("lineCount", await r.Content.ReadAsStringAsync());
      Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, csrf, Url(id) + "/export", new { revision = new string('a', 64) })).StatusCode);
    }
    Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/api/ui/procedures/{seed.CashProcedureId}/review")).StatusCode);
    await using var db2 = new AuditSphereDbContext(pg.Options);
    var foreign = PbcSeed.Actor(f.Staff, "Staff") with { FirmId = Guid.NewGuid() };
    Assert.Equal(ErrorCodes.ScopeDenied, (await StatementReviewWorkspace.GetAsync(db2, foreign, f.EngagementId)).ErrorCode);
    await db2.RoleGrants.Where(x => x.UserId == f.Staff.Id).ExecuteDeleteAsync();
    Assert.Equal(ErrorCodes.ScopeDenied, (await StatementReviewWorkspace.GetAsync(db2, PbcSeed.Actor(f.Staff, "Staff"), f.EngagementId)).ErrorCode);
  }

  [Fact]
  public async Task LateEpochDuringBasisReadCannotReturnTotals()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-STATEMENT-LATE-EPOCH"); var f = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options)) await StatementReviewSeed.SeedAsync(db, f);
    var interceptor = new LateRead(async () => { await using var db = new AuditSphereDbContext(pg.Options); await db.Users.Where(x => x.Id == f.Staff.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1)); });
    await using var read = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options);
    Assert.Equal(ErrorCodes.GenerationStale, (await StatementReviewWorkspace.GetAsync(read, PbcSeed.Actor(f.Staff, "Staff"), f.EngagementId)).ErrorCode);
  }

  [Fact]
  public async Task SplitDashboardReturnsSimultaneousProfitOrLossAndPositionSectionsWithComparativeAndRisk()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-STATEMENT-SPLIT"); var f = await PbcSeed.SeedAsync(pg);
    StatementReviewSeed.Context seed; await using (var db = new AuditSphereDbContext(pg.Options)) seed = await StatementReviewSeed.SeedAsync(db, f, true);
    using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(); var csrf = await SignIn(c); var url = Url(f.EngagementId);
    var s = await Read(c, url + "/split");
    Assert.True(s.TryGetProperty("profitOrLoss", out var pl));
    Assert.True(s.TryGetProperty("financialPosition", out var bs));
    Assert.True(pl.GetProperty("lines").GetArrayLength() > 0);
    Assert.True(bs.GetProperty("lines").GetArrayLength() > 0);
    var bsLines = bs.GetProperty("lines").EnumerateArray().ToList();
    var cashLine = bsLines.First(x => x.GetProperty("destinationCode").GetString() == "CASH");
    Assert.Equal("100.123486", cashLine.GetProperty("amount").GetString());
    Assert.True(cashLine.TryGetProperty("riskBand", out var rb));
    Assert.False(string.IsNullOrEmpty(rb.GetString()));
    Assert.True(cashLine.TryGetProperty("reviewStatus", out var rs));
    Assert.False(string.IsNullOrEmpty(rs.GetString()));
    Assert.True(cashLine.TryGetProperty("assignedPerformer", out _));
    Assert.True(cashLine.TryGetProperty("requiredReviewer", out _));
  }

  private sealed class LateRead(Func<Task> action) : DbCommandInterceptor
  {
    private bool fired;
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken ct = default)
    { if (!fired && command.CommandText.Contains("trial_balance_rows", StringComparison.Ordinal)) { fired = true; await action(); } return result; }
  }
  private static string Url(Guid id) => $"/api/ui/engagements/{id}/statements";
  private static string Detail(string url, string revision) => url + $"/contributions?section=position&destination=CASH&statementSection=ASSETS&revision={revision}";
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser u) => new(new Dictionary<string, string?> { ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:TenantId"] = u.TenantId, ["DevelopmentIdentity:Subject"] = u.Subject, ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
  private static async Task<string> SignIn(HttpClient c) { await c.GetAsync("/auth/sign-in"); using var r = await c.GetAsync("/api/ui/session"); return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]); }
  private static async Task<JsonElement> Read(HttpClient c, string url) { using var r = await c.GetAsync(url); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone(); }
  private static Task<HttpResponseMessage> Post(HttpClient c, string csrf, string url, object body) { var r = new HttpRequestMessage(HttpMethod.Post, url) {Content=JsonContent.Create(body)}; r.Headers.Add("X-XSRF-TOKEN", csrf); return c.SendAsync(r); }
}
