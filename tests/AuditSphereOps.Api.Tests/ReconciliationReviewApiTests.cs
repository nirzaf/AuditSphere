using System.Net;
using System.Text.Json;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class ReconciliationReviewApiTests
{
  [Fact]
  public async Task RetainedApprovalAndAmountsAreSeparateFromCurrentSourceEligibility()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-RECONCILIATION-REVIEW");
    var s = await ReconciliationReviewSeed.SeedAsync(pg); var f=s.Fixture;
    using var factory = Factory(pg,f.Staff); using var c=factory.CreateClient(); await c.GetAsync("/auth/sign-in");
    using var response=await c.GetAsync(Url(s.ReconciliationId)); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    Assert.True(response.Headers.CacheControl?.NoStore);
    var first=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("100.123456",first.GetProperty("sourceTotal").GetString());
    Assert.Equal("APPROVED",first.GetProperty("status").GetString());
    Assert.True(first.GetProperty("canReuseApprovedEvidence").GetBoolean());
    Assert.Equal(27,first.GetProperty("itemCount").GetInt32());
    Assert.Equal(25,first.GetProperty("items").GetArrayLength()); Assert.True(first.GetProperty("hasMore").GetBoolean());
    var next=await Read(c,Url(s.ReconciliationId)+"?page=1"); Assert.Equal(2,next.GetProperty("items").GetArrayLength());
    Assert.False(next.GetProperty("hasMore").GetBoolean());
    var all=first.GetProperty("items").EnumerateArray().Concat(next.GetProperty("items").EnumerateArray()).Select(x=>x.GetProperty("id").GetGuid()).ToArray();
    Assert.Equal(27,all.Distinct().Count());
    using var hidden=await c.GetAsync(Url(s.SiblingId)); using var missing=await c.GetAsync(Url(Guid.NewGuid()));
    Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode); Assert.Equal(hidden.StatusCode,missing.StatusCode);
    Assert.Equal(await missing.Content.ReadAsStringAsync(),await hidden.Content.ReadAsStringAsync());
    var queue=await c.GetStringAsync("/api/ui/accounting/evidence"); Assert.Contains(s.ReconciliationId.ToString(),queue);
    Assert.DoesNotContain("HIDDEN SYNTHETIC",queue);
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.TrialBalanceDatasets.Where(x=>x.Id==s.SourceId).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.NormalizedDatasetDigest,new string('e',64)));
    var stale=await Read(c,Url(s.ReconciliationId));
    Assert.True(stale.GetProperty("isStale").GetBoolean()); Assert.False(stale.GetProperty("canReuseApprovedEvidence").GetBoolean());
    Assert.Equal("APPROVED",stale.GetProperty("status").GetString());
    Assert.Equal("100.123456",stale.GetProperty("sourceTotal").GetString());
    Assert.NotEmpty(stale.GetProperty("blockers").EnumerateArray());
    Assert.NotEqual(first.GetProperty("reviewBasis").GetString(),stale.GetProperty("reviewBasis").GetString());
    await using var verify=new AuditSphereDbContext(pg.Options);
    Assert.Equal("APPROVED",(await verify.AccountingReconciliations.SingleAsync(x=>x.Id==s.ReconciliationId)).Status);
    Assert.Equal(1,await verify.AccountingReconciliationProofs.CountAsync());
  }

  [Fact]
  public async Task GenerationChangesMissingProofAndUnauthorizedIdentitiesFailClosed()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-RECONCILIATION-ACCESS");var s=await ReconciliationReviewSeed.SeedAsync(pg);var f=s.Fixture;
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();await c.GetAsync("/auth/sign-in");
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.ClientSafetyStates.Where(x=>x.Id==f.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.InputGeneration,v=>v.InputGeneration+1));
    var stale=await Read(c,Url(s.ReconciliationId));Assert.True(stale.GetProperty("isStale").GetBoolean());
    Assert.False(stale.GetProperty("canReuseApprovedEvidence").GetBoolean());
    using var clientFactory=Factory(pg,f.Client);using var portal=clientFactory.CreateClient();await portal.GetAsync("/auth/sign-in");
    Assert.Equal(HttpStatusCode.Forbidden,(await portal.GetAsync(Url(s.ReconciliationId))).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.SessionEpoch,v=>v.SessionEpoch+1));
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(Url(s.ReconciliationId))).StatusCode);
    using var adminFactory=Factory(pg,f.Admin);using var admin=adminFactory.CreateClient();await admin.GetAsync("/auth/sign-in");
    var legacy=await Read(admin,Url(s.SiblingId));Assert.Equal(JsonValueKind.Null,legacy.GetProperty("latestProof").ValueKind);
    Assert.Equal(JsonValueKind.Null,legacy.GetProperty("currentGeneration").ValueKind);
    Assert.False(legacy.GetProperty("canReuseApprovedEvidence").GetBoolean());Assert.NotEmpty(legacy.GetProperty("blockers").EnumerateArray());
  }
  private static string Url(Guid id)=>"/api/ui/accounting/reconciliations/"+id;
  private static async Task<JsonElement> Read(HttpClient c,string url) { using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);using var d=JsonDocument.Parse(await r.Content.ReadAsStringAsync());return d.RootElement.Clone(); }
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AuditSphereOps.Domain.Security.AppUser user)=>new(new Dictionary<string,string?> {
    ["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=user.TenantId,
    ["DevelopmentIdentity:Subject"]=user.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false" });
}
