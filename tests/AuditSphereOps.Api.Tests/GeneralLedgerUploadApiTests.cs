using System.Net;
using System.Text;
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

public sealed class GeneralLedgerUploadApiTests
{
    [Fact] public async Task ExactReviewedConcurrentUploadRetainsOneSourceAndReconcilesWithoutMutatingSelection() { await using var pg = await OwnedPostgresDatabase.CreateAsync("API-GL-UPLOAD"); var f = await PbcSeed.SeedAsync(pg); await using (var db = new AuditSphereDbContext(pg.Options)) await GeneralLedgerUploadSeed.SeedAsync(db, f); using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c); var root = Root(f); Assert.Equal(HttpStatusCode.Forbidden, (await Upload(c, "wrong", root + "/preview")).StatusCode); var p = await Json(await Upload(c, csrf, root + "/preview")); Assert.True(p.GetProperty("canImport").GetBoolean()); Assert.Equal("100.123456", p.GetProperty("debit").GetString()); Assert.Equal(HttpStatusCode.Conflict, (await Upload(c, csrf, root + "/import", p, reviewed: false)).StatusCode); var writes = await Task.WhenAll(Upload(c, csrf, root + "/import", p), Upload(c, csrf, root + "/import", p)); var receipts = await Task.WhenAll(writes.Select(Json)); Assert.All(receipts, r => Assert.Equal("SEALED", r.GetProperty("importState").GetString())); var id = receipts[0].GetProperty("importBatchId").GetGuid(); Assert.Equal(id, receipts[1].GetProperty("importBatchId").GetGuid()); Assert.Equal(id, (await Json(await Upload(c, csrf, root + "/reconcile"))).GetProperty("importBatchId").GetGuid()); Assert.Equal(id, (await Json(await Upload(c, csrf, root + "/import", p))).GetProperty("importBatchId").GetGuid()); await using var proof = new AuditSphereDbContext(pg.Options); Assert.Single(await proof.SourceImportBatches.ToListAsync()); Assert.Equal(2, await proof.GeneralLedgerLines.CountAsync()); Assert.Empty(await proof.SourceAcceptanceDecisions.ToListAsync()); Assert.Empty(await proof.GeneralLedgerCompletenessBridges.ToListAsync()); Assert.Equal(1, await proof.ClientSafetyStates.Where(x => x.Id == f.ClientId).Select(x => x.InputGeneration).SingleAsync()); }
    [Fact] public async Task MalformedUnbalancedUnknownAccountsDatesAndDimensionsPublishNothing() { await using var pg = await OwnedPostgresDatabase.CreateAsync("API-GL-UPLOAD-INVALID"); var f = await PbcSeed.SeedAsync(pg); await using (var db = new AuditSphereDbContext(pg.Options)) await GeneralLedgerUploadSeed.SeedAsync(db, f); using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c); var texts = new[] { GeneralLedgerUploadSeed.Csv.Replace(",3000,", ",UNKNOWN,"), GeneralLedgerUploadSeed.Csv.Replace("-100.123456,-100.123456", "-100.123455,-100.123455"), GeneralLedgerUploadSeed.Csv.Replace("2026-06-30", "2027-06-30"), GeneralLedgerUploadSeed.Csv.Replace("100.123456", "1e2"), GeneralLedgerUploadSeed.Csv + "\"unterminated", GeneralLedgerUploadSeed.Csv.Replace("PeriodCode", "Unknown"), GeneralLedgerUploadSeed.Csv.Replace("L-2", "L-1") }; foreach (var text in texts) Assert.Equal(HttpStatusCode.BadRequest, (await Upload(c, csrf, Root(f) + "/preview", text: text)).StatusCode); var p = await Json(await Upload(c, csrf, Root(f) + "/preview")); Assert.Equal(HttpStatusCode.Conflict, (await Upload(c, csrf, Root(f) + "/import", p, text: GeneralLedgerUploadSeed.Csv.Replace("Source user", "Different user"))).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await Upload(c, csrf, Root(f) + "/preview", text: GeneralLedgerUploadSeed.Csv, name: "source.xlsx")).StatusCode); await using var db2 = new AuditSphereDbContext(pg.Options); Assert.Empty(await db2.SourceImportBatches.ToListAsync()); }

    [Fact]
    public async Task PublishedHierarchyAndRetiredAccountsCannotPreviewAsImportable()
    {
        await using var pg = await OwnedPostgresDatabase.CreateAsync("API-GL-ACCOUNT-ELIGIBILITY");
        var f = await PbcSeed.SeedAsync(pg);
        await using (var db = new AuditSphereDbContext(pg.Options))
            await GeneralLedgerUploadSeed.SeedAsync(db, f);
        await PublishChartWithRestrictedAccounts(pg, f);

        using var factory = Factory(pg, f.Staff);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var csrf = await SignIn(client);
        var hierarchyAccount = GeneralLedgerUploadSeed.Csv.Replace(",1000,", ",9000,");
        var retiredAccount = GeneralLedgerUploadSeed.Csv.Replace(",1000,", ",9200,");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Upload(client, csrf, Root(f) + "/preview", text: hierarchyAccount)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Upload(client, csrf, Root(f) + "/preview", text: retiredAccount)).StatusCode);

        await using (var proof = new AuditSphereDbContext(pg.Options))
        {
            Assert.Empty(await proof.SourceImportBatches.ToListAsync());
            Assert.Empty(await proof.GeneralLedgerTransactions.ToListAsync());
            Assert.Empty(await proof.GeneralLedgerLines.ToListAsync());
        }

        var validPostingLine = GeneralLedgerUploadSeed.Csv.Replace(",1000,", ",9100,");
        var preview = await Json(await Upload(client, csrf, Root(f) + "/preview", text: validPostingLine));
        Assert.True(preview.GetProperty("canImport").GetBoolean());
        var imported = await Json(await Upload(client, csrf, Root(f) + "/import", preview, text: validPostingLine));
        Assert.Equal("SEALED", imported.GetProperty("importState").GetString());
        await using var persisted = new AuditSphereDbContext(pg.Options);
        Assert.Single(await persisted.SourceImportBatches.ToListAsync());
        Assert.Equal(new[] { "3000", "9100" }, await persisted.GeneralLedgerLines.OrderBy(x => x.AccountCode)
            .Select(x => x.AccountCode).ToArrayAsync());
    }
    [Fact] public async Task ScopeBookStatusStaleContextAndEpochDenyWithoutHiddenReceipts() { await using var pg = await OwnedPostgresDatabase.CreateAsync("API-GL-UPLOAD-SCOPE"); var f = await PbcSeed.SeedAsync(pg); var other = await PbcSeed.SeedAsync(pg); GeneralLedgerUploadSeed.Context context; await using (var db = new AuditSphereDbContext(pg.Options)) { context = await GeneralLedgerUploadSeed.SeedAsync(db, f); await GeneralLedgerUploadSeed.SeedAsync(db, other); } using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c); foreach (var id in new[] { other.EngagementId, Guid.NewGuid() }) foreach (var action in new[] { "preview", "import", "reconcile" }) Assert.Equal(HttpStatusCode.Forbidden, (await Upload(c, csrf, $"/api/ui/engagements/{id}/general-ledger/{action}")).StatusCode); using var cf = Factory(pg, f.Client); using var client = cf.CreateClient(new() { AllowAutoRedirect = false }); var cc = await SignIn(client); Assert.Equal(HttpStatusCode.Forbidden, (await Upload(client, cc, Root(f) + "/preview")).StatusCode); var p = await Json(await Upload(c, csrf, Root(f) + "/preview")); await using (var db = new AuditSphereDbContext(pg.Options)) await db.ClientSafetyStates.Where(x => x.Id == f.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1)); Assert.Equal(HttpStatusCode.Conflict, (await Upload(c, csrf, Root(f) + "/import", p)).StatusCode); p = await Json(await Upload(c, csrf, Root(f) + "/preview")); await using (var db = new AuditSphereDbContext(pg.Options)) await db.ClientReportingBooks.Where(x => x.Id == context.BookId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "CLOSED")); var blocked = await Json(await Upload(c, csrf, Root(f) + "/preview")); Assert.False(blocked.GetProperty("canImport").GetBoolean()); Assert.Equal(HttpStatusCode.BadRequest, (await Upload(c, csrf, Root(f) + "/import", p)).StatusCode); await using (var db = new AuditSphereDbContext(pg.Options)) await db.Users.Where(x => x.Id == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1)); Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(c, csrf, Root(f) + "/reconcile")).StatusCode); await using var proof = new AuditSphereDbContext(pg.Options); Assert.Empty(await proof.SourceImportBatches.ToListAsync()); }
    [Fact]
    public async Task LateEpochChangeAfterSourceSaveRollsBackAllRows()
    {
        await using var pg = await OwnedPostgresDatabase.CreateAsync("API-GL-UPLOAD-LATE");
        var f = await PbcSeed.SeedAsync(pg);
        await using (var db = new AuditSphereDbContext(pg.Options)) await GeneralLedgerUploadSeed.SeedAsync(db, f);
        var actor = PbcSeed.Actor(f.Staff, "AccountingPreparer");
        var interceptor = new LateEpoch(f.FirmId, f.Staff.Id);
        await using (var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
            .AddInterceptors(interceptor).Options))
        {
            var bytes = Encoding.UTF8.GetBytes(GeneralLedgerUploadSeed.Csv);
            var p = await GeneralLedgerUploadWorkspace.PreviewAsync(db, actor, f.EngagementId, "source.csv", bytes);
            Assert.True(p.Succeeded, p.Message);
            var r = await GeneralLedgerUploadWorkspace.ImportAsync(db, actor, f.EngagementId, "source.csv", bytes,
                p.Value!.Revision, p.Value.FileSha256, true);
            Assert.True(interceptor.Applied);
            Assert.Equal(ErrorCodes.GenerationStale, r.ErrorCode);
        }
        await using var proof = new AuditSphereDbContext(pg.Options);
        Assert.Empty(await proof.SourceImportBatches.ToListAsync());
        Assert.Empty(await proof.GeneralLedgerTransactions.ToListAsync());
        Assert.Empty(await proof.GeneralLedgerLines.ToListAsync());
        Assert.Equal(actor.SessionEpoch, await proof.Users.Where(x => x.Id == actor.UserId)
            .Select(x => x.SessionEpoch).SingleAsync());
    }

    // External epoch updates serialize on the importer's user-row lock. Inject within the
    // same transaction after persistence to exercise the final authority fence and rollback;
    // this is not a claim that an external revocation can commit through that lock.
    private sealed class LateEpoch(Guid firmId, Guid userId) : SaveChangesInterceptor
    {
        public bool Applied { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e, int result,
            CancellationToken ct = default)
        {
            if (!Applied && e.Context is AuditSphereDbContext db && db.Database.CurrentTransaction is not null)
            {
                Assert.True(await db.SourceImportBatches.AnyAsync(x => x.FirmId == firmId, ct));
                Assert.True(await db.GeneralLedgerLines.AnyAsync(x => x.FirmId == firmId, ct));
                Assert.Equal(1, await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE users SET session_epoch = session_epoch + 1 WHERE firm_id = {firmId} AND id = {userId}", ct));
                Applied = true;
            }
            return result;
        }
    }
    private static string Root(PbcSeed.Fixture f) => $"/api/ui/engagements/{f.EngagementId}/general-ledger";
    private static async Task PublishChartWithRestrictedAccounts(OwnedPostgresDatabase pg, PbcSeed.Fixture f)
    {
        var preparer = PbcSeed.Actor(f.Staff, "AccountingPreparer");
        var reviewer = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
        Guid chartId;
        await using (var db = new AuditSphereDbContext(pg.Options))
        {
            db.RoleGrants.AddRange(
                PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer", clientId: f.ClientId),
                PbcSeed.Grant(f.FirmId, f.Reviewer, "AccountingReviewer", clientId: f.ClientId));
            await db.SaveChangesAsync();
            var created = await ClientAccountingService.CreateChartVersionAsync(db, preparer, f.ClientId,
                "GL-ELIGIBILITY", new DateOnly(2026, 1, 1));
            Assert.True(created.Succeeded, created.Message);
            chartId = created.Value;
            Assert.True((await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
                new("summary-v2", "9000", "Summary account", "ASSET", "DEBIT", false),
                new("detail-v2", "9100", "Cash detail", "ASSET", "DEBIT", true, "summary-v2"),
                new("retired-v2", "9200", "Retired account", "ASSET", "DEBIT", true),
                new("cash-v2", "1000", "Cash", "ASSET", "DEBIT", true),
                new("revenue-v2", "3000", "Revenue", "INCOME", "CREDIT", true)
            ])).Succeeded);
        }
        // Status is set only on this draft fixture; the production chart command has
        // no deactivation operation and published chart versions are immutable.
        await using (var db = new AuditSphereDbContext(pg.Options))
        {
            Assert.Equal(1, await db.ClientAccounts.Where(x => x.ChartVersionId == chartId && x.AccountCode == "9200")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AccountingWorkflowStates.Retired)));
            var published = await ClientAccountingService.PublishChartVersionAsync(db, reviewer, chartId);
            Assert.True(published.Succeeded, published.Message);
        }
    }
    private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser u) => new(new Dictionary<string, string?> { ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:TenantId"] = u.TenantId, ["DevelopmentIdentity:Subject"] = u.Subject, ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
    private static async Task<string> SignIn(HttpClient c) { await c.GetAsync("/auth/sign-in"); using var r = await c.GetAsync("/api/ui/session"); return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]); }
    private static async Task<JsonElement> Json(HttpResponseMessage r) { Assert.Equal(HttpStatusCode.OK, r.StatusCode); using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return doc.RootElement.TryGetProperty("value", out var v) ? v.Clone() : doc.RootElement.Clone(); }
    private static Task<HttpResponseMessage> Upload(HttpClient c, string csrf, string url, JsonElement? review = null, bool reviewed = true, string text = GeneralLedgerUploadSeed.Csv, string name = "source.csv") { var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(text)), "file", name); if (review is { } p) { form.Add(new StringContent(p.GetProperty("revision").GetString()!), "revision"); form.Add(new StringContent(p.GetProperty("fileSha256").GetString()!), "fileSha256"); form.Add(new StringContent(reviewed ? "true" : "false"), "reviewed"); } var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form }; request.Headers.Add("X-XSRF-TOKEN", csrf); return c.SendAsync(request); }
}
