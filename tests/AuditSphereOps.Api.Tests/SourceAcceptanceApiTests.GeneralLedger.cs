using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed partial class SourceAcceptanceApiTests
{
  [Fact]
  public async Task GeneralLedger_ReviewedConcurrentAcceptance_SelectsGlWithoutChangingTbOrClaimingCompleteness()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-GL-ACCEPTANCE");
    var f = await PbcSeed.SeedAsync(pg); var tb = await Seed(pg, f);
    Guid gl; await using (var db = new AuditSphereDbContext(pg.Options)) gl = await GeneralLedgerWorkspaceSeed.SeedAsync(db, f, journals: 1);
    using var factory = Factory(pg, f.Admin); using var c = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(c); var t = await Read(c, tb);
    Assert.Equal(HttpStatusCode.OK, (await Accept(c, csrf, tb, t.GetProperty("revision").GetString()!)).StatusCode);
    var r = await GlRead(c, gl); Assert.True(r.GetProperty("canAccept").GetBoolean());
    Assert.Equal(2, r.GetProperty("rowCount").GetInt32()); Assert.False(r.TryGetProperty("balanced", out _)); Assert.False(r.TryGetProperty("datasetId", out _));
    var revision = r.GetProperty("revision").GetString()!;
    Assert.Equal(HttpStatusCode.Forbidden, (await GlAccept(c, "wrong", gl, revision)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await GlAccept(c, csrf, gl, revision, reviewed: false)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await GlAccept(c, csrf, gl, new string('b', 64))).StatusCode);
    var results = await Task.WhenAll(GlAccept(c, csrf, gl, revision), GlAccept(c, csrf, gl, revision));
    Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
    var observed = await GlRead(c, gl); Assert.False(observed.GetProperty("canAccept").GetBoolean());
    Assert.Equal(3, observed.GetProperty("inputGeneration").GetInt64());
    var receipt = observed.GetProperty("receipt"); Assert.Equal(gl, receipt.GetProperty("importBatchId").GetGuid());
    Assert.Equal(JsonValueKind.Null, receipt.GetProperty("trialBalanceDatasetId").ValueKind);
    Assert.Equal(f.Admin.Id, receipt.GetProperty("acceptedByUserId").GetGuid()); Assert.Equal("GL", receipt.GetProperty("sourceKind").GetString());
    Assert.Equal(receipt.GetProperty("id").GetGuid(), observed.GetProperty("selected").GetProperty("decisionId").GetGuid());
    Assert.Equal(tb, (await Read(c, tb)).GetProperty("selected").GetProperty("trialBalanceDatasetId").GetGuid());
    await using var proof = new AuditSphereDbContext(pg.Options); Assert.Equal(2, await proof.SourceAcceptanceDecisions.CountAsync());
    Assert.Empty(await proof.GeneralLedgerCompletenessBridges.ToListAsync());
  }

  [Fact]
  public async Task GeneralLedger_ImporterUnsealedNonGlScopeStaleGenerationAndClosedPeriodAreRefused()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-GL-ACCEPTANCE-GATES");
    var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    var sibling = Guid.NewGuid(); Guid id, self, loading, notGl, foreignId, siblingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Engagements.Add(new() { Id = sibling, FirmId = f.FirmId, PracticeClientId = f.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
      id = await GeneralLedgerWorkspaceSeed.SeedAsync(db, f, journals: 1);
      self = await GeneralLedgerWorkspaceSeed.SeedAsync(db, f with { Staff = f.Admin }, journals: 1);
      loading = await GeneralLedgerWorkspaceSeed.SeedAsync(db, f, journals: 1, seal: false);
      notGl = await GeneralLedgerWorkspaceSeed.SeedAsync(db, f, journals: 0, kind: "TB");
      foreignId = await GeneralLedgerWorkspaceSeed.SeedAsync(db, foreign, journals: 1);
      siblingId = await GeneralLedgerWorkspaceSeed.SeedAsync(db, f with { EngagementId = sibling }, journals: 1);
    }
    using var factory = Factory(pg, f.Admin); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c);
    var r = await GlRead(c, id); var revision = r.GetProperty("revision").GetString()!;
    var blocked = await GlRead(c, self); Assert.False(blocked.GetProperty("canAccept").GetBoolean()); Assert.Contains("other than", blocked.GetProperty("blocker").GetString());
    Assert.Equal(HttpStatusCode.Forbidden, (await GlAccept(c, csrf, self, blocked.GetProperty("revision").GetString()!)).StatusCode);
    foreach (var denied in new[] { loading, notGl, foreignId, Guid.NewGuid() })
    {
      Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(GlPath(denied))).StatusCode);
      Assert.Equal(HttpStatusCode.Forbidden, (await GlAccept(c, csrf, denied, revision)).StatusCode);
    }
    using var staffFactory = Factory(pg, f.Staff); using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false }); await SignIn(staff);
    Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(GlPath(siblingId))).StatusCode);
    using var clientFactory = Factory(pg, f.Client); using var client = clientFactory.CreateClient(new() { AllowAutoRedirect = false }); await SignIn(client);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(GlPath(id))).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options)) await db.ClientSafetyStates.Where(x => x.Id == f.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
    Assert.Equal(HttpStatusCode.Conflict, (await GlAccept(c, csrf, id, revision)).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var batch = await db.SourceImportBatches.SingleAsync(x => x.Id == id);
      await db.ClientReportingPeriods.Where(x => x.Id == batch.PeriodId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "CLOSED"));
    }
    Assert.False((await GlRead(c, id)).GetProperty("canAccept").GetBoolean());
    await using var proof = new AuditSphereDbContext(pg.Options); Assert.Empty(await proof.SourceAcceptanceDecisions.ToListAsync());
  }

  [Fact]
  public async Task GeneralLedger_FinalRevocationRollsBackDecisionAndGeneration()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-GL-ACCEPTANCE-REVOCATION"); var f = await PbcSeed.SeedAsync(pg);
    Guid id; string revision; var actor = PbcSeed.Actor(f.Admin, "Administrator");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      id = await GeneralLedgerWorkspaceSeed.SeedAsync(db, f, journals: 1);
      var r = await SourceAcceptanceWorkspace.GetGeneralLedgerAsync(db, actor, id); Assert.True(r.Succeeded); revision = r.Value!.Revision;
    }
    var afterWrite = new AfterDecision(async () => { await using var db = new AuditSphereDbContext(pg.Options); await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1)); });
    await using (var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(afterWrite).Options))
    {
      var r = await SourceAcceptanceWorkspace.AcceptGeneralLedgerAsync(db, actor, id, revision, "Independent GL evidence", true);
      Assert.Equal(ErrorCodes.GenerationStale, r.ErrorCode);
    }
    await using var proof = new AuditSphereDbContext(pg.Options); Assert.Empty(await proof.SourceAcceptanceDecisions.ToListAsync());
    Assert.Equal(1, await proof.ClientSafetyStates.Where(x => x.Id == f.ClientId).Select(x => x.InputGeneration).SingleAsync());
  }

  private static string GlPath(Guid id) => $"/api/ui/gl-sources/{id}/acceptance";
  private static async Task<JsonElement> GlRead(HttpClient c, Guid id)
  {
    using var r = await c.GetAsync(GlPath(id)); Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return json.RootElement.Clone();
  }
  private static Task<HttpResponseMessage> GlAccept(HttpClient c, string csrf, Guid id, string revision, bool reviewed = true)
  {
    var r = new HttpRequestMessage(HttpMethod.Post, GlPath(id)) { Content = JsonContent.Create(new { revision, reviewed, evidenceReference = "Independent GL evidence" }) };
    r.Headers.Add("X-XSRF-TOKEN", csrf); return c.SendAsync(r);
  }
}
