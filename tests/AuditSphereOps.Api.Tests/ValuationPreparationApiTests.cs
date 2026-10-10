using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class ValuationPreparationApiTests
{
  [Fact]
  public async Task ReviewedPreparationPublishesOnceAndRetainsImmutableInputsForBothMethods()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-VALUATION-PREPARE");var seed=await AccountingAnalysisReviewSeed.SeedAsync(pg,false,false);var f=seed.Fixture;
    await using var db=new AuditSphereDbContext(pg.Options);var rec=await db.EclAssessments.Where(x=>x.Id==seed.Evidence["ECL"]).Select(x=>x.ReconciliationId).SingleAsync();
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();var csrf=await SignIn(c);
    foreach(var kind in new[]{"ECL","INVENTORY"})
    {
      var s=await Read(c,Url(rec,kind));Assert.True(s.GetProperty("canPrepare").GetBoolean());var request=Request(s,kind);
      Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,"invalid",Url(rec,kind),request with{Reviewed=true})).StatusCode);
      Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url(rec,kind),request)).StatusCode);
      using var p=await Post(c,csrf,Url(rec,kind)+"/preview",request);Assert.Equal(HttpStatusCode.OK,p.StatusCode);
      var preview=JsonDocument.Parse(await p.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();
      Assert.Equal(kind=="ECL"?"7.006173":"9.900006",preview.GetProperty("calculatedAmount").GetString());
      var receipt=await Execute(c,csrf,rec,kind,request);var replay=await Execute(c,csrf,rec,kind,request);Assert.Equal(receipt.GetProperty("id").GetGuid(),replay.GetProperty("id").GetGuid());
      Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,Url(rec,kind),request with{Reviewed=true,Fields=request.Fields with{Reason="Changed intent"}})).StatusCode);
      Assert.True((await Read(c,Url(rec,kind)+"/receipts/"+request.RequestId+"?requestHash="+receipt.GetProperty("requestHash").GetString())).GetProperty("found").GetBoolean());
      var row=await db.ValuationPreparations.SingleAsync(x=>x.RequestId==request.RequestId);Assert.Contains("100.123456",row.ContextJson);Assert.Contains("Synthetic source rationale",row.InputJson);
      await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE valuation_preparations SET reason='changed' WHERE id={row.Id}"));
      await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM valuation_preparations WHERE id={row.Id}"));
      if(kind=="ECL")await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ecl_assessments SET management_overlay=999 WHERE id={row.EvidenceId}"));
      else await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE inventory_valuation_assessments SET quantity=999 WHERE id={row.EvidenceId}"));
      var view=await Read(c,$"/api/ui/accounting/evidence/{kind}/{row.EvidenceId}");Assert.Equal("DRAFT",view.GetProperty("status").GetString());Assert.Null(view.GetProperty("reviewedAt").GetString());
    }
    Assert.Equal(2,await db.ValuationPreparations.CountAsync());Assert.Equal(100.123456m,await db.AccountingReconciliations.Where(x=>x.Id==rec).Select(x=>x.SourceTotal).SingleAsync());
    await PermanentFileFreezeMigrationAssertions.AssertDowngradeBlockedAsync(db,"_NativeValuationPreparation");
    Assert.Equal(2,await db.ValuationPreparations.CountAsync());
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
  public async Task LateEpochRefusalRollsBackNewAssessmentAndReceiptTogether()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-VALUATION-ATOMIC");var seed=await AccountingAnalysisReviewSeed.SeedAsync(pg,false,false);var f=seed.Fixture;
    var actor=PbcSeed.Actor(f.Staff,"AccountingPreparer");await using var inspect=new AuditSphereDbContext(pg.Options);
    var rec=await inspect.EclAssessments.Where(x=>x.Id==seed.Evidence["ECL"]).Select(x=>x.ReconciliationId).SingleAsync();
    var s=(await ValuationPreparationWorkspace.StateAsync(inspect,actor,"ECL",rec)).Value!;var before=await inspect.EclAssessments.CountAsync(x=>x.ReconciliationId==rec);
    var epoch=await inspect.Users.Where(x=>x.Id==actor.UserId).Select(x=>x.SessionEpoch).SingleAsync();
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>().UseNpgsql(pg.ConnectionString).AddInterceptors(new EpochAfterSave(actor.UserId)).Options))
      Assert.False((await ValuationPreparationWorkspace.ExecuteAsync(db,actor,"ECL",rec,new(Guid.NewGuid(),s.ReviewBasis,Fields("ECL"),true))).Succeeded);
    Assert.Equal(before,await inspect.EclAssessments.CountAsync(x=>x.ReconciliationId==rec));Assert.Empty(await inspect.ValuationPreparations.ToListAsync());
    Assert.Equal(epoch,await inspect.Users.Where(x=>x.Id==actor.UserId).Select(x=>x.SessionEpoch).SingleAsync());
  }
  [Fact]
  public async Task ConcurrentExactIntentCreatesOneAnalysisAndRecoversOneReceipt()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-VALUATION-CONCURRENT");
    var seed=await AccountingAnalysisReviewSeed.SeedAsync(pg,false,false);var actor=PbcSeed.Actor(seed.Fixture.Staff,"AccountingPreparer");
    await using var inspect=new AuditSphereDbContext(pg.Options);var ecl=seed.Evidence["ECL"];
    var rec=await inspect.EclAssessments.Where(x=>x.Id==ecl).Select(x=>x.ReconciliationId).SingleAsync();
    var state=(await ValuationPreparationWorkspace.StateAsync(inspect,actor,"ECL",rec)).Value!;
    var request=new ValuationPreparationRequest(Guid.NewGuid(),state.ReviewBasis,Fields("ECL"),true);
    var before=await inspect.EclAssessments.CountAsync(x=>x.ReconciliationId==rec);
    async Task<ValuationPreparationReceipt> Run()
    {
      // Test-owned manual reconciliation of an exact rejected local transaction only.
      // The product never automatically retries an unknown acknowledgement.
      for(var attempt=0;attempt<4;attempt++)
      {
        await using var db=new AuditSphereDbContext(pg.Options);
        try{var r=await ValuationPreparationWorkspace.ExecuteAsync(db,actor,"ECL",rec,request);Assert.True(r.Succeeded,r.Message);return r.Value!;}
        catch(Exception ex) when(IsSerializationFailure(ex)){}
      }
      throw new InvalidOperationException("Concurrent exact preparation did not reconcile.");
    }
    var replies=await Task.WhenAll(Run(),Run());Assert.Equal(replies[0].Id,replies[1].Id);Assert.Equal(replies[0].EvidenceId,replies[1].EvidenceId);
    Assert.Single(await inspect.ValuationPreparations.ToListAsync());Assert.Equal(before+1,await inspect.EclAssessments.CountAsync(x=>x.ReconciliationId==rec));
    var other=PbcSeed.Actor(seed.Fixture.Reviewer,"AccountingReviewer");
    var hidden=await ValuationPreparationWorkspace.LookupAsync(inspect,other,"ECL",rec,request.RequestId,replies[0].RequestHash);
    Assert.True(hidden.Succeeded);Assert.False(hidden.Value!.Found);Assert.Null(hidden.Value.Receipt);
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
  private static bool IsSerializationFailure(Exception ex)
  {
    for(Exception? current=ex;current is not null;current=current.InnerException)
      if(current is PostgresException {SqlState:PostgresErrorCodes.SerializationFailure})return true;
    return false;
  }
  private sealed class EpochAfterSave(Guid user):SaveChangesInterceptor
  {
    private bool fired;
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData d,int result,CancellationToken ct=default)
    {if(!fired){fired=true;await d.Context!.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET session_epoch=session_epoch+1 WHERE id={user}",ct);}return result;}
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
  private static async Task<JsonElement> Execute(HttpClient c,string csrf,Guid id,string kind,ValuationPreparationRequest input){using var r=await Post(c,csrf,Url(id,kind),input with{Reviewed=true});Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
}
