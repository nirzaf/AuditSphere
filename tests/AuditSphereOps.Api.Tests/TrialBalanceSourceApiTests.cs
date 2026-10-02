using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace AuditSphereOps.Api.Tests;
public sealed class TrialBalanceSourceApiTests
{
  [Fact]
  public async Task BoundedRows_CurrentRevisionIssues_ExactExport_CsrfAndStaleRevision()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TB-SOURCE"); var f = await PbcSeed.SeedAsync(pg); var id = await Seed(pg, f, 201);
    using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c);
    using var response = await c.GetAsync(Path(id)); Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var j = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var v = j.RootElement;
    Assert.Equal(f.ClientId, v.GetProperty("clientId").GetGuid()); Assert.Equal("SEALED", v.GetProperty("importState").GetString()); Assert.Equal("Pending", v.GetProperty("validationStatus").GetString());
    Assert.Equal(201, v.GetProperty("rows").GetProperty("totalCount").GetInt32()); Assert.Equal(100, v.GetProperty("rows").GetProperty("items").GetArrayLength()); Assert.Equal("0.000000", v.GetProperty("rows").GetProperty("totalAmount").GetString()); Assert.Equal(JsonValueKind.Null, v.GetProperty("rows").GetProperty("totalDebit").ValueKind); Assert.Equal(JsonValueKind.Null, v.GetProperty("rows").GetProperty("totalCredit").ValueKind); Assert.Equal(101, v.GetProperty("issues").GetProperty("totalCount").GetInt32()); Assert.DoesNotContain("OLD-REVISION-MARKER", v.ToString());
    var page = await c.GetStringAsync(Path(id) + "?page=3&issuePage=2"); using var jp = JsonDocument.Parse(page); Assert.Single(jp.RootElement.GetProperty("rows").GetProperty("items").EnumerateArray()); Assert.Single(jp.RootElement.GetProperty("issues").GetProperty("items").EnumerateArray());
    foreach (var suffix in new[] { "?page=2147483647", "?issuePage=2147483647", "?filter=" + new string('a', 101) }) Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Path(id) + suffix)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await Export(c, "wrong", id, 1)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await Export(c, csrf, id, 2)).StatusCode);
    using var export = await Export(c, csrf, id, 1); Assert.Equal(HttpStatusCode.OK, export.StatusCode); Assert.Equal("1", export.Headers.GetValues("X-Dataset-Revision").Single()); Assert.Equal(id.ToString("D"), export.Headers.GetValues("X-Dataset-Id").Single()); Assert.Contains(id.ToString("D"), export.Content.Headers.ContentDisposition!.FileName!);
    var csv = await export.Content.ReadAsStringAsync(); Assert.Contains("100.123456", csv); Assert.Contains("\"'  =SUM", csv); Assert.Contains("\"dataset_revision\"", csv); Assert.Equal(203, csv.Split('\n').Length);
  }
  [Fact]
  public async Task SiblingForeignGuessedAndUnsealedSources_ClientIdentity_AndRevocationExposeNoRowsOrFiles()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TB-SOURCE-SCOPE"); var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg); var id = await Seed(pg, f, 2); var fid = await Seed(pg, foreign, 2); var loading = await Seed(pg, f, 2, seal: false);
    var sibling = Guid.NewGuid(); await using (var db = new AuditSphereDbContext(pg.Options)) { db.Engagements.Add(new() { Id = sibling, FirmId = f.FirmId, PracticeClientId = f.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); }
    var sid = await Seed(pg, f with { EngagementId = sibling }, 2);
    using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c);
    foreach (var denied in new[] { sid, fid, loading, Guid.NewGuid() }) { Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(Path(denied))).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Export(c, csrf, denied, 1)).StatusCode); }
    using var cf = Factory(pg, f.Client); using var client = cf.CreateClient(new() { AllowAutoRedirect = false }); var cc = await SignIn(client); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Path(id))).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Export(client, cc, id, 1)).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options)) await db.Users.Where(x => x.Id == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(Path(id))).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await Export(c, csrf, id, 1)).StatusCode);
  }
  [Fact]
  public async Task DirectExportCap_AndFinalReadAuthorityAndValidationStateFences()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-TB-SOURCE-FENCES"); var f = await PbcSeed.SeedAsync(pg); var id = await Seed(pg, f, 2); var actor = PbcSeed.Actor(f.Staff, "Staff");
    var changed = new BeforeRows(async () => { await using var db = new AuditSphereDbContext(pg.Options); await db.TrialBalanceDatasets.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ValidationStatus, "Accepted")); });
    await using (var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(changed).Options)) { var r = await TrialBalanceSourceWorkspace.GetAsync(db, actor, id); Assert.False(r.Succeeded); Assert.Equal(ErrorCodes.StaleRevision, r.ErrorCode); Assert.Null(r.Value); }
    var revoked = new BeforeRows(async () => { await using var db = new AuditSphereDbContext(pg.Options); await db.Users.Where(x => x.Id == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1)); });
    await using (var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(revoked).Options)) { var r = await TrialBalanceDatasetQuery.ExportTrialBalanceCsvAsync(db, actor, id); Assert.False(r.Succeeded); Assert.Equal(ErrorCodes.GenerationStale, r.ErrorCode); Assert.Null(r.Value); }
    var large = await Seed(pg, f, TrialBalanceDatasetQuery.MaxExportRows + 1);
    using var factory = Factory(pg, f.Admin); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c); Assert.Equal(HttpStatusCode.BadRequest, (await Export(c, csrf, large, 1)).StatusCode);
  }
  private sealed class BeforeRows(Func<Task> change) : DbCommandInterceptor
  {
    private bool done;
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { if (!done && command.CommandText.Contains("trial_balance_rows", StringComparison.Ordinal)) { done = true; await change(); } return result; }
  }
  private static string Path(Guid id) => $"/api/ui/datasets/{id}/source";
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser u) => new(new Dictionary<string, string?> { ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:TenantId"] = u.TenantId, ["DevelopmentIdentity:Subject"] = u.Subject, ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
  private static async Task<string> SignIn(HttpClient c) { await c.GetAsync("/auth/sign-in"); using var r = await c.GetAsync("/api/ui/session"); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]); }
  private static Task<HttpResponseMessage> Export(HttpClient c, string csrf, Guid id, long revision) { var request = new HttpRequestMessage(HttpMethod.Post, Path(id) + "/export") { Content = JsonContent.Create(new { revision }) }; request.Headers.Add("X-XSRF-TOKEN", csrf); return c.SendAsync(request); }
  private static async Task<Guid> Seed(OwnedPostgresDatabase pg, PbcSeed.Fixture f, int count, bool seal = true)
  {
    await using var db = new AuditSphereDbContext(pg.Options); var id = Guid.NewGuid(); db.TrialBalanceDatasets.Add(new() { Id = id, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, Currency = "QAR", LegalEntityKey = "SYN-ENTITY", Balanced = true, RawFileSha256Hex = id.ToString("N") + id.ToString("N"), NormalizedDatasetDigest = id.ToString("N") + id.ToString("N"), ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = f.Staff.Id }); await db.SaveChangesAsync();
    await db.Database.ExecuteSqlAsync($"INSERT INTO trial_balance_rows (id, dataset_id, account_code, account_name, amount, currency, entity) SELECT gen_random_uuid(), {id}, lpad(n::text, 6, '0'), CASE WHEN n = 1 THEN '  =SUM(1,2)' ELSE 'Synthetic account' END, CASE WHEN n = 1 THEN 100.123456 WHEN n = {count} THEN -100.123456 ELSE 0 END, 'QAR', 'SYN-ENTITY' FROM generate_series(1, {count}) AS n");
    for (var n = 0; n < 102; n++) db.TrialBalanceValidationIssues.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, DatasetId = id, Revision = n == 101 ? 2 : 1, RowKey = n.ToString(), Code = "SYN-ISSUE", Message = n == 101 ? "OLD-REVISION-MARKER" : "Synthetic pending issue", CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
    if (seal) await db.TrialBalanceDatasets.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed)); return id;
  }
}
