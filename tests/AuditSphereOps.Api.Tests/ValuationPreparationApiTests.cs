using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class ValuationPreparationApiTests
{
  [Fact]
  public async Task SourceOnlyReconciliationBlocksValuationPreparationForBothMethods()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-VALUATION-PREPARE");var seed=await AccountingAnalysisReviewSeed.SeedAsync(pg,false,false);var f=seed.Fixture;
    await using var db=new AuditSphereDbContext(pg.Options);var rec=await db.EclAssessments.Where(x=>x.Id==seed.Evidence["ECL"]).Select(x=>x.ReconciliationId).SingleAsync();
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();var csrf=await SignIn(c);
    foreach(var kind in new[]{"ECL","INVENTORY"})
    {
      var s=await Read(c,Url(rec,kind));Assert.False(s.GetProperty("canPrepare").GetBoolean());
      Assert.Contains(s.GetProperty("blockers").EnumerateArray().Select(x=>x.GetString()),x=>x!.Contains("distinct independently evidenced control population",StringComparison.Ordinal));
      var request=Request(s,kind);
      Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,"invalid",Url(rec,kind),request with{Reviewed=true})).StatusCode);
      Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url(rec,kind),request)).StatusCode);
      using var p=await Post(c,csrf,Url(rec,kind)+"/preview",request);Assert.Equal(HttpStatusCode.OK,p.StatusCode);
      var preview=JsonDocument.Parse(await p.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();
      Assert.False(preview.GetProperty("canProceed").GetBoolean());
      Assert.Contains(preview.GetProperty("blockers").EnumerateArray().Select(x=>x.GetString()),x=>x!.Contains("distinct independently evidenced control population",StringComparison.Ordinal));
      using var execute=await Post(c,csrf,Url(rec,kind),request with{Reviewed=true});Assert.Equal(HttpStatusCode.BadRequest,execute.StatusCode);
      using var error=JsonDocument.Parse(await execute.Content.ReadAsStringAsync());Assert.Equal("gate.blocked",error.RootElement.GetProperty("code").GetString());
      var view=await Read(c,$"/api/ui/accounting/evidence/{kind}/{seed.Evidence[kind]}");
      Assert.Equal("DRAFT",view.GetProperty("status").GetString());Assert.Null(view.GetProperty("reviewedAt").GetString());
    }
    Assert.Empty(await db.ValuationPreparations.ToListAsync());
    Assert.Equal(100.123456m,await db.AccountingReconciliations.Where(x=>x.Id==rec).Select(x=>x.SourceTotal).SingleAsync());
  }
  [Fact]
  public async Task MissingUnsupportedStaleClosedAndRevokedInputsNeverCreateValuations()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-VALUATION-FENCES");var seed=await AccountingAnalysisReviewSeed.SeedAsync(pg,false,false);var f=seed.Fixture;
    await using var db=new AuditSphereDbContext(pg.Options);var rec=await db.EclAssessments.Where(x=>x.Id==seed.Evidence["ECL"]).Select(x=>x.ReconciliationId).SingleAsync();
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();var csrf=await SignIn(c);
    using var portalFactory=Factory(pg,f.Client);using var portal=portalFactory.CreateClient();await SignIn(portal);
    Assert.Equal(HttpStatusCode.Forbidden,(await portal.GetAsync(Url(rec,"ECL"))).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(Url(Guid.NewGuid(),"ECL"))).StatusCode);
    var r=Request(await Read(c,Url(rec,"ECL")),"ECL");
    foreach(var bad in new[]{r.Fields with{ProbabilityOfDefault=""},r.Fields with{Method="UNSUPPORTED"},r.Fields with{ProbabilityOfDefault="1.000001"},r.Fields with{BookedAmount="8.0000001"},r.Fields with{Quantity="0"}})
      Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url(rec,"ECL")+"/preview",r with{Fields=bad})).StatusCode);
    await db.TrialBalanceDatasets.Where(x=>x.Id==seed.SourceId).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.NormalizedDatasetDigest,new string('e',64)));
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,Url(rec,"ECL"),r with{Reviewed=true})).StatusCode);
    Assert.False((await Read(c,Url(rec,"ECL"))).GetProperty("canPrepare").GetBoolean());
    await db.TrialBalanceDatasets.Where(x=>x.Id==seed.SourceId).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.NormalizedDatasetDigest,new string('d',64)));
    await db.ClientReportingPeriods.Where(x=>x.ClientId==f.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.Status,"CLOSED"));
    Assert.False((await Read(c,Url(rec,"ECL"))).GetProperty("canPrepare").GetBoolean());Assert.Empty(await db.ValuationPreparations.ToListAsync());
    await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(Url(rec,"ECL"))).StatusCode);
  }
  [Fact]
  public async Task CurrentPreparationSchemaCannotBeDowngradedAndSourceRemainsUnchanged()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-VALUATION-MIGRATION");
    var seed=await AccountingAnalysisReviewSeed.SeedAsync(pg,false,false);
    await using var db=new AuditSphereDbContext(pg.Options);
    var existing=await db.EclAssessments.CountAsync();
    await PermanentFileFreezeMigrationAssertions.AssertDowngradeBlockedAsync(db,"_NativeValuationPreparation");
    Assert.Equal(existing,await db.EclAssessments.CountAsync());Assert.Equal(100.123456m,await db.TrialBalanceRows.Where(x=>x.DatasetId==seed.SourceId&&x.Amount>0).SumAsync(x=>x.Amount));
    Assert.Empty(await db.ValuationPreparations.ToListAsync());
  }
  private static ValuationFields Fields(string k)=>k=="ECL"?
    new("PROVISION_MATRIX_V1","synthetic.ecl.v1",new('a',64),"0.100000","0.500000","2.000000","8.000000","8.000000",null,null,null,null,null,"Synthetic source rationale","Synthetic assumptions evidence"):
    new("LOWER_COST_NRV_V1","synthetic.inventory.v1",new('a',64),null,null,null,null,null,"2.000001","6.000001","5.000001","0.100001","12.000001","Synthetic source rationale","Synthetic assumptions evidence");
  private static ValuationPreparationRequest Request(JsonElement s,string kind)=>new(Guid.NewGuid(),s.GetProperty("reviewBasis").GetString()!,Fields(kind));
  private static string Url(Guid id,string kind)=>$"/api/ui/accounting/reconciliations/{id}/valuation-preparation/{kind}";
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var s=await c.GetAsync("/api/ui/session");return Uri.UnescapeDataString(s.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static async Task<JsonElement> Read(HttpClient c,string url){using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();}
  private static Task<HttpResponseMessage> Post(HttpClient c,string csrf,string url,object input){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(input)};r.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(r);}
}
