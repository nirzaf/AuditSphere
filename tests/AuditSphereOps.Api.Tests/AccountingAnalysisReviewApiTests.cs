using System.Net;
using System.Text.Json;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class AccountingAnalysisReviewApiTests
{
  [Fact]
  public async Task TypedRetainedInputsSourceAndProcedureLinksStayIndependentOfCurrentVerification()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-ANALYSIS-REVIEW");var s=await AccountingAnalysisReviewSeed.SeedAsync(pg);var f=s.Fixture;
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();await c.GetAsync("/auth/sign-in");
    foreach(var(kind,id) in s.Evidence)
    {
      using var response=await c.GetAsync(Url(kind,id));Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl?.NoStore);
      var r=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
      Assert.Equal(kind,r.GetProperty("kind").GetString());Assert.Equal(f.ClientId,r.GetProperty("clientId").GetGuid());
      Assert.Equal(kind!="JOURNAL_RISK",r.GetProperty("inputsCurrent").GetBoolean());Assert.True(r.GetProperty("hasCurrentReviewedProcedure").GetBoolean());
      Assert.Equal(kind=="JOURNAL_RISK" ? "CLEARED" : "APPROVED",r.GetProperty("status").GetString());
      Assert.All(r.GetProperty("amounts").EnumerateArray(),x=>Assert.True(x.GetProperty("value").ValueKind is JsonValueKind.String or JsonValueKind.Null));
      if(kind=="ECL")Assert.Equal("7.006173",Amount(r,"CALCULATED"));
      if(kind=="INVENTORY")Assert.Equal("109.000000",Amount(r,"CALCULATED"));
      if(kind=="SPECIALIST")Assert.Equal("110.123456",Amount(r,"CALCULATED"));
      if(kind=="ANALYTICAL")Assert.True(r.GetProperty("replayMatchesInputs").GetBoolean());
      if(kind=="JOURNAL_RISK")
      {
        Assert.Equal(JsonValueKind.Null,r.GetProperty("inputGeneration").ValueKind);Assert.Equal(JsonValueKind.Null,r.GetProperty("source").GetProperty("retainedDigest").ValueKind);
        Assert.False(r.GetProperty("source").GetProperty("currentChecksPass").GetBoolean());Assert.Contains("no retained source digest",r.GetRawText());
      }
    }
    var a=await Read(c,Url("SPECIALIST",s.Evidence["SPECIALIST"]));var b=await Read(c,Url("SPECIALIST",s.Evidence["SPECIALIST"])+"?page=1");
    Assert.Equal(27,a.GetProperty("linkCount").GetInt32());Assert.Equal(25,a.GetProperty("links").GetArrayLength());Assert.Equal(2,b.GetProperty("links").GetArrayLength());
    Assert.Equal(27,a.GetProperty("links").EnumerateArray().Concat(b.GetProperty("links").EnumerateArray()).Select(x=>x.GetProperty("id").GetGuid()).Distinct().Count());
    var zero=await Read(c,Url("ANALYTICAL",s.ZeroAnalyticalId));Assert.Equal(JsonValueKind.Null,zero.GetProperty("amounts").EnumerateArray().Single(x=>x.GetProperty("key").GetString()=="RATIO").GetProperty("value").ValueKind);
    Assert.False(zero.GetProperty("inputsCurrent").GetBoolean());Assert.Equal("INSUFFICIENT_DATA",zero.GetProperty("status").GetString());
    var queue=JsonDocument.Parse(await c.GetStringAsync("/api/ui/accounting/evidence")).RootElement;
    var riskRow=queue.EnumerateArray().Single(x=>x.GetProperty("kind").GetString()=="JOURNAL_RISK");
    Assert.Equal(JsonValueKind.Null,riskRow.GetProperty("inputGeneration").ValueKind);Assert.True(riskRow.GetProperty("isStale").GetBoolean());
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.TrialBalanceDatasets.Where(x=>x.Id==s.SourceId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.NormalizedDatasetDigest,new string('e',64)));
    var stale=await Read(c,Url("ECL",s.Evidence["ECL"]));Assert.False(stale.GetProperty("inputsCurrent").GetBoolean());Assert.Equal("APPROVED",stale.GetProperty("status").GetString());
    Assert.Equal("7.006173",Amount(stale,"CALCULATED"));Assert.NotEmpty(stale.GetProperty("blockers").EnumerateArray());
  }

  [Fact]
  public async Task MissingReplayMalformedMethodsScopeAndSessionChangesFailClosedWithoutFallbacks()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-ANALYSIS-ISOLATION");var s=await AccountingAnalysisReviewSeed.SeedAsync(pg);var f=s.Fixture;
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();await c.GetAsync("/auth/sign-in");
    using var hidden=await c.GetAsync(Url("SPECIALIST",s.HiddenId));using var missing=await c.GetAsync(Url("SPECIALIST",Guid.NewGuid()));
    Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode);Assert.Equal(hidden.StatusCode,missing.StatusCode);Assert.Equal(await missing.Content.ReadAsStringAsync(),await hidden.Content.ReadAsStringAsync());
    using var portalFactory=Factory(pg,f.Client);using var portal=portalFactory.CreateClient();await portal.GetAsync("/auth/sign-in");
    foreach(var(kind,id) in s.Evidence)Assert.Equal(HttpStatusCode.Forbidden,(await portal.GetAsync(Url(kind,id))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(Url("UNKNOWN",s.Evidence["ECL"]))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(Url("ECL",s.Evidence["ECL"])+"?page=20")).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      await db.AnalyticalReviews.Where(x=>x.Id==s.Evidence["ANALYTICAL"]).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.InputHash,new string('f',64)));
      await db.EclAssessments.Where(x=>x.Id==s.Evidence["ECL"]).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.Method,"UNSUPPORTED"));
      await db.SpecialistAccountingSchedules.Where(x=>x.Id==s.Evidence["SPECIALIST"]).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.UsefulLifeMonths,(int?)null));
    }
    foreach(var kind in new[]{"ECL","SPECIALIST","ANALYTICAL"})
    {var r=await Read(c,Url(kind,s.Evidence[kind]));Assert.False(r.GetProperty("inputsCurrent").GetBoolean());Assert.Equal("APPROVED",r.GetProperty("status").GetString());Assert.NotEmpty(r.GetProperty("blockers").EnumerateArray());}
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.ClientSafetyStates.Where(x=>x.Id==f.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.InputGeneration,v=>v.InputGeneration+1));
    var changed=await Read(c,Url("INVENTORY",s.Evidence["INVENTORY"]));Assert.False(changed.GetProperty("inputsCurrent").GetBoolean());Assert.False(changed.GetProperty("hasCurrentReviewedProcedure").GetBoolean());
    var queue=await c.GetStringAsync("/api/ui/accounting/evidence");Assert.DoesNotContain("HIDDEN SYNTHETIC",queue);Assert.DoesNotContain(s.HiddenId.ToString(),queue);
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.SessionEpoch,v=>v.SessionEpoch+1));
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(Url("ECL",s.Evidence["ECL"]))).StatusCode);
  }
  private static string Url(string kind,Guid id)=>$"/api/ui/accounting/evidence/{kind}/{id}";
  private static string? Amount(JsonElement r,string key)=>r.GetProperty("amounts").EnumerateArray().Single(x=>x.GetProperty("key").GetString()==key).GetProperty("value").GetString();
  private static async Task<JsonElement> Read(HttpClient c,string url){using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);using var d=JsonDocument.Parse(await r.Content.ReadAsStringAsync());return d.RootElement.Clone();}
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AuditSphereOps.Domain.Security.AppUser user)=>new(new Dictionary<string,string?>{
    ["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=user.TenantId,["DevelopmentIdentity:Subject"]=user.Subject,
    ["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
}
