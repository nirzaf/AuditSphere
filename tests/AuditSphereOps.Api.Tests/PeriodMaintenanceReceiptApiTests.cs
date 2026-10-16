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

namespace AuditSphereOps.Api.Tests;

public sealed class PeriodMaintenanceReceiptApiTests
{
  [Fact]
  public async Task AllThreeReviewedActionsPreserveLegacyRouteAndRecoverExactReceipts()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PERIOD-MAINTENANCE-RECEIPTS");
    var parent = await PbcSeed.SeedAsync(pg);
    var seed = await SiblingClientSeed.SeedAsync(pg, parent.FirmId, "SYNTHETIC-PERIOD-RECEIPTS"); var f = seed.Fixture;
    Guid original, revised; long revision;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Reuse the client-scoped accounting grants supplied by SiblingClientSeed.
      var period = await db.ClientReportingPeriods.SingleAsync(x => x.Id == seed.PeriodId);
      // Synthetic persisted closed fixture; no professional or live approval is represented.
      period.Status = AccountingWorkflowStates.Closed; period.ClosedByUserId = f.Reviewer.Id;
      period.ClosedAt = DateTimeOffset.UtcNow; period.CloseReason = "SYNTHETIC closed receipt fixture"; period.Revision++;
      revision = period.Revision;
      // SiblingClientSeed already contains a restatement for its two initial packages.
      // Use a fresh synthetic amendment; random UUID ordering must never decide whether this pair is a duplicate.
      var source = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == seed.PackageId);
      var amendment = JsonSerializer.Deserialize<FinancialPackage>(JsonSerializer.Serialize(source))!;
      amendment.Id = Guid.CreateVersion7();
      amendment.CalculationHash = Hashing.Sha256Hex($"SYNTHETIC receipt amendment {amendment.Id:D}");
      amendment.TemplateVersion = "SYNTHETIC-receipt-amendment";
      db.FinancialPackages.Add(amendment);
      original = source.Id; revised = amendment.Id; await db.SaveChangesAsync();
    }
    using var factory = Factory(pg, f.Staff); using var client = factory.CreateClient(); var csrf = await SignIn(client);
    using var reviewerFactory = Factory(pg, f.Reviewer); using var reviewer = reviewerFactory.CreateClient(); var reviewCsrf = await SignIn(reviewer);
    var root = $"/api/ui/accounting/period-maintenance/clients/{f.ClientId}";
    var legacy = await Read(client, root); Assert.Equal(JsonValueKind.Array, legacy.ValueKind);
    var context = await Read(client, root + "/context"); Assert.Equal(JsonValueKind.Object, context.ValueKind); Assert.True(context.TryGetProperty("periods", out _));
    var state = await Read(client, root + $"/state?action=ROLL_FORWARD&periodId={seed.PeriodId}");
    Assert.False(state.TryGetProperty("stateJson", out _));
    var forward = new PeriodMaintenanceRequest(Guid.NewGuid(), state.GetProperty("reviewBasis").GetString()!, PeriodMaintenanceActions.RollForward,
      new(f.ClientId, seed.PeriodId, "FY2027-RECEIPTS", new(2027, 1, 1), new(2027, 12, 31), "STATUTORY", "QAR", new('a', 64), 1m, 1m, "SYNTHETIC evidence", ExpectedPriorRevision: revision));
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "invalid", root + "/preview", forward)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, csrf, root + "/actions", forward)).StatusCode);
    var fRequest = await Review(client, csrf, root, forward);
    var fReceipt = await Execute(client, csrf, root, fRequest);
    Assert.Equal(JsonValueKind.String, fReceipt.GetProperty("actorEpoch").ValueKind);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, csrf, root + "/actions", fRequest with { RollForward = fRequest.RollForward! with { EvidenceReference = "changed" } })).StatusCode);
    state = await Read(client, root + $"/state?action=CREATE_RESTATEMENT&periodId={seed.PeriodId}");
    var create = await Review(client, csrf, root, new(Guid.NewGuid(), state.GetProperty("reviewBasis").GetString()!, PeriodMaintenanceActions.CreateRestatement,
      Restatement: new(f.ClientId, seed.PeriodId, original, revised, "IAS 8", "SYNTHETIC correction", "SYNTHETIC restatement evidence", PeriodRestatementChangeTypes.RestatedError, "FY2026")));
    var cReceipt = await Execute(client, csrf, root, create); var rid = cReceipt.GetProperty("result").GetProperty("restatementId").GetGuid();
    state = await Read(reviewer, root + $"/state?action=APPROVE_RESTATEMENT&restatementId={rid}");
    var approve = await Review(reviewer, reviewCsrf, root, new(Guid.NewGuid(), state.GetProperty("reviewBasis").GetString()!, PeriodMaintenanceActions.ApproveRestatement, RestatementId: rid));
    using (var detailResponse = await Post(reviewer, reviewCsrf, root + "/preview", approve))
    {
      Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
      using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
      var detail = detailJson.RootElement.GetProperty("value").GetProperty("approvalDetail");
      Assert.Equal(rid, detail.GetProperty("restatementId").GetGuid());
      Assert.Equal(original, detail.GetProperty("originalPackageId").GetGuid());
      Assert.Equal(revised, detail.GetProperty("revisedPackageId").GetGuid());
      await using var evidence = new AuditSphereDbContext(pg.Options);
      var submitted = await evidence.ClientPeriodRestatements.AsNoTracking().SingleAsync(x => x.Id == rid);
      Assert.Equal(submitted.OriginalPackageHash, detail.GetProperty("originalPackageHash").GetString());
      Assert.Equal(submitted.RevisedPackageHash, detail.GetProperty("revisedPackageHash").GetString());
      Assert.Equal(submitted.Reason, detail.GetProperty("reason").GetString());
      Assert.Equal(submitted.EvidenceReference, detail.GetProperty("evidenceReference").GetString());
      Assert.Equal(submitted.RevisedBasis, detail.GetProperty("revisedBasis").GetString());
      Assert.Equal(submitted.ChangeType, detail.GetProperty("changeType").GetString());
      Assert.Equal(submitted.AffectedPeriods, detail.GetProperty("affectedPeriods").GetString());
      Assert.Equal("SUBMITTED", detail.GetProperty("status").GetString());
    }
    var aReceipt = await Execute(reviewer, reviewCsrf, root, approve);
    Assert.Equal("APPROVED", aReceipt.GetProperty("result").GetProperty("status").GetString());
    foreach (var (http, token, request, receipt) in new[] { (client, csrf, fRequest, fReceipt), (client, csrf, create, cReceipt), (reviewer, reviewCsrf, approve, aReceipt) })
    {
      var replay = await Execute(http, token, root, request); Assert.Equal(receipt.GetProperty("id").GetGuid(), replay.GetProperty("id").GetGuid());
      var lookup = await Read(http, root + $"/receipts/{request.RequestId}?requestHash={request.ExpectedRequestHash}");
      Assert.True(lookup.GetProperty("found").GetBoolean()); Assert.Equal(receipt.GetProperty("id").GetGuid(), lookup.GetProperty("receipt").GetProperty("id").GetGuid());
    }
    Assert.Equal("SUBMITTED", cReceipt.GetProperty("result").GetProperty("status").GetString());
    var other = await Read(client, root + $"/receipts/{approve.RequestId}?requestHash={approve.ExpectedRequestHash}"); Assert.False(other.GetProperty("found").GetBoolean());
    await using var verify = new AuditSphereDbContext(pg.Options); Assert.Equal(3, await verify.PeriodMaintenanceReceipts.CountAsync());
  }

  [Fact]
  public async Task ReceiptEpochUsesExactStringBeyondJavaScriptIntegerRange()
  {
    var receipt = new PeriodMaintenanceReceiptView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 9_007_199_254_740_993L,
      Guid.NewGuid(), new('a',64), "ROLL_FORWARD", new('b',64), DateTimeOffset.UtcNow, new(Guid.NewGuid(), null, "DRAFT", "{}"));
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    Assert.Equal("9007199254740993", json.RootElement.GetProperty("actorEpoch").GetString()); await Task.CompletedTask;
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser u) => new(new Dictionary<string, string?> {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:TenantId"] = u.TenantId,
    ["DevelopmentIdentity:Subject"] = u.Subject, ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
  private static async Task<string> SignIn(HttpClient c) { await c.GetAsync("/auth/sign-in"); using var r = await c.GetAsync("/api/ui/session"); Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]); }
  private static async Task<JsonElement> Read(HttpClient c, string url) { using var r = await c.GetAsync(url); Assert.Equal(HttpStatusCode.OK, r.StatusCode); using var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return j.RootElement.Clone(); }
  private static Task<HttpResponseMessage> Post(HttpClient c, string token, string url, object body) { var r = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) }; r.Headers.Add("X-XSRF-TOKEN", token); return c.SendAsync(r); }
  private static async Task<PeriodMaintenanceRequest> Review(HttpClient c, string token, string root, PeriodMaintenanceRequest r) {
    using var response = await Post(c, token, root + "/preview", r); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var v = json.RootElement.GetProperty("value");
    Assert.True(v.GetProperty("state").GetProperty("canProceed").GetBoolean(), v.GetProperty("state").GetProperty("blockers").GetRawText()); Assert.False(string.IsNullOrWhiteSpace(v.GetProperty("summary").GetString()));
    Assert.True(v.TryGetProperty("canonicalInput", out _)); return r with { Reviewed = true, ExpectedRequestHash = v.GetProperty("requestHash").GetString() }; }
  private static async Task<JsonElement> Execute(HttpClient c, string token, string root, PeriodMaintenanceRequest r) {
    using var response = await Post(c, token, root + "/actions", r); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.GetProperty("value").Clone(); }
}
