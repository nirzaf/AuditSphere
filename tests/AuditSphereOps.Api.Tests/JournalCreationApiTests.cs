using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class JournalCreationApiTests
{
  [Fact]
  public async Task ConcurrentCreationReconcilesOneOriginalDraftAndNeverOverwritesItsCreationLines()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-CREATION");var(f,source,_)=await Seed(pg);
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();var csrf=await SignIn(c);
    var context=await Read(c,Url(source));var request=Request(context);
    Assert.True((await Preview(c,csrf,source,request)).GetProperty("canProceed").GetBoolean());
    var results=await Task.WhenAll(Create(c,csrf,source,request),Create(c,csrf,source,request));
    var id=results[0].GetProperty("resultJournalId").GetGuid();Assert.Equal(id,results[1].GetProperty("resultJournalId").GetGuid());
    Assert.Equal("CREATE",results[0].GetProperty("action").GetString());Assert.Equal("NOT_CREATED",results[0].GetProperty("oldStatus").GetString());Assert.Equal(0,results[0].GetProperty("oldRevision").GetInt64());
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,Url(source),request with{Reviewed=true,JournalNumber="CHANGED"})).StatusCode);
    var duplicate=Request(context) with{JournalNumber=request.JournalNumber};Assert.False((await Preview(c,csrf,source,duplicate)).GetProperty("canProceed").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url(source),duplicate with{Reviewed=true})).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(1,await db.AdjustmentJournalActions.CountAsync());
      Assert.True((await AdjustmentJournalService.UpdateDraftAsync(db,PbcSeed.Actor(f.Staff,"AccountingPreparer"),id,
        [("1000",201.123456m,0m),("3000",0m,201.123456m)],"Later correction","Later evidence",1)).Succeeded);
      Assert.Equal(2,await db.AdjustmentJournals.Where(x=>x.Id==id).Select(x=>x.Revision).SingleAsync());
      var e=await db.AdjustmentJournalActions.SingleAsync();Assert.Contains("200.123456",e.AfterJson);Assert.DoesNotContain("201.123456",e.AfterJson);
      await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE adjustment_journal_actions SET after_json='altered' WHERE id={e.Id}"));
    }
    var lookup=await Read(c,Url(source)+"/receipts/"+request.RequestId+"?requestHash="+results[0].GetProperty("requestHash").GetString());Assert.True(lookup.GetProperty("found").GetBoolean());
    var history=await Read(c,$"/api/ui/accounting/journals/{id}/history/{results[0].GetProperty("id").GetGuid()}");
    Assert.Empty(history.GetProperty("before").GetProperty("lines").EnumerateArray());Assert.Equal("NOT_CREATED",history.GetProperty("before").GetProperty("status").GetString());
    Assert.Equal("200.123456",history.GetProperty("after").GetProperty("lines")[0].GetProperty("debit").GetString());
  }

  [Fact]
  public async Task ExactCreationRefusesBadLinesClosedPeriodMissingReviewAndUnauthorizedSource()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-CREATE-FENCES");var(f,source,_)=await Seed(pg);
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();var csrf=await SignIn(c);var context=await Read(c,Url(source));var request=Request(context);
    foreach(var lines in new[]{new JournalEditLine[]{new("1000","1e2","0"),new("3000","0","100")},[new("1000","1.1234567","0"),new("3000","0","1.1234567")],[new("1000","1","0"),new("3000","0","0.9")],[new("UNKNOWN","1","0"),new("3000","0","1")]})
      Assert.False((await Preview(c,csrf,source,request with{Lines=lines})).GetProperty("canProceed").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url(source),request)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url(source),request with{Reviewed=true,Purpose=AdjustmentJournalPurposes.GroupOnlyElimination})).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,"forged",Url(source),request with{Reviewed=true})).StatusCode);
    using var clientFactory=Factory(pg,f.Client);using var client=clientFactory.CreateClient();await SignIn(client);
    foreach(var id in new[]{source,Guid.NewGuid()}) {using var denied=await client.GetAsync(Url(id));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.DoesNotContain("datasetDigest",await denied.Content.ReadAsStringAsync());}
    await using(var db=new AuditSphereDbContext(pg.Options))await db.ClientReportingPeriods.Where(x=>x.Id==context.GetProperty("periodId").GetGuid()).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Status,"CLOSED"));
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url(source),request with{Reviewed=true})).StatusCode);
    await using var verify=new AuditSphereDbContext(pg.Options);Assert.Equal(1,await verify.AdjustmentJournals.CountAsync());Assert.Empty(await verify.AdjustmentJournalActions.ToListAsync());
    Assert.False((await AdjustmentJournalWorkspace.GetCreationAsync(verify,PbcSeed.Actor(f.Staff,"AccountingPreparer") with{FirmId=Guid.NewGuid()},source)).Succeeded);
  }

  [Fact]
  public async Task SupersedingDraftRequiresExactPriorRevisionAndPreservesTheReturnedJournal()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-SUPERSEDES");var(f,source,old)=await Seed(pg);
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await AdjustmentJournalService.SubmitDraftAsync(db,PbcSeed.Actor(f.Staff,"AccountingPreparer"),old)).Succeeded);
      Assert.True((await AdjustmentJournalService.ReturnSubmissionAsync(db,PbcSeed.Actor(f.Reviewer,"AccountingReviewer"),old,"Correct evidence")).Succeeded);
    }
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();var csrf=await SignIn(c);var context=await Read(c,Url(source));var r=Request(context) with{SupersedesId=old,SupersedesRevision=1};
    Assert.True((await Preview(c,csrf,source,r)).GetProperty("canProceed").GetBoolean());
    await using(var db=new AuditSphereDbContext(pg.Options))Assert.True((await AdjustmentJournalService.UpdateDraftAsync(db,PbcSeed.Actor(f.Staff,"AccountingPreparer"),old,[("1000",111.123456m,0m),("3000",0m,111.123456m)],"Corrected original","Original evidence",1)).Succeeded);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,csrf,Url(source),r with{Reviewed=true})).StatusCode);
    var receipt=await Create(c,csrf,source,r with{SupersedesRevision=2});
    await using var verify=new AuditSphereDbContext(pg.Options);var created=await verify.AdjustmentJournals.SingleAsync(x=>x.Id==receipt.GetProperty("resultJournalId").GetGuid());Assert.Equal(old,created.SupersedesJournalId);Assert.Equal("Draft",created.Status);
    Assert.Equal("Returned",await verify.AdjustmentJournals.Where(x=>x.Id==old).Select(x=>x.Status).SingleAsync());Assert.Contains(await verify.AdjustmentLines.Where(x=>x.JournalId==old).ToListAsync(),x=>x.Debit==111.123456m);
  }

  [Fact]
  public async Task LateEpochRollsBackNewJournalAndCreationEvidenceTogether()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-CREATE-EPOCH");var(f,source,_)=await Seed(pg);var actor=PbcSeed.Actor(f.Staff,"AccountingPreparer");JournalCreationRequest request;
    await using(var db=new AuditSphereDbContext(pg.Options)) {var context=(await AdjustmentJournalWorkspace.GetCreationAsync(db,actor,source)).Value!;request=new(Guid.NewGuid(),context.ReviewBasis,"AJ-NEW",AdjustmentJournalPurposes.ReportingAdjustment,AdjustmentJournalOrigins.AuditProposed,"Synthetic reason","Synthetic evidence",null,null,[new("1000","200.123456","0"),new("3000","0","200.123456")],true);}
    var interceptor=new EpochAfterSave(async()=>{await using var db=new AuditSphereDbContext(pg.Options);await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));});
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options))Assert.False((await AdjustmentJournalWorkspace.CreateAsync(db,db,actor,source,request)).Succeeded);
    await using var verify=new AuditSphereDbContext(pg.Options);Assert.Equal(1,await verify.AdjustmentJournals.CountAsync());Assert.Empty(await verify.AdjustmentJournalActions.ToListAsync());
  }
  private sealed class EpochAfterSave(Func<Task> action):SaveChangesInterceptor
  {private bool fired;public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken ct=default){if(!fired){fired=true;await action();}return result;}}
  private static async Task<(PbcSeed.Fixture,Guid,Guid)> Seed(OwnedPostgresDatabase pg){var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);var old=await JournalReviewSeed.SeedAsync(db,f);return(f,await db.AdjustmentJournals.Where(x=>x.Id==old).Select(x=>x.BaseDatasetId).SingleAsync(),old);}
  private static JournalCreationRequest Request(JsonElement c)=>new(Guid.NewGuid(),c.GetProperty("reviewBasis").GetString()!,"AJ-CREATE",AdjustmentJournalPurposes.ReportingAdjustment,AdjustmentJournalOrigins.AuditProposed,"Synthetic new treatment","Synthetic creation evidence",null,null,[new("1000","200.123456","0"),new("3000","0","200.123456")],false);
  private static string Url(Guid source)=>$"/api/ui/datasets/{source}/journal-drafts";
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static async Task<JsonElement> Read(HttpClient c,string url){using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();}
  private static Task<HttpResponseMessage> Post(HttpClient c,string csrf,string url,object body){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(r);}
  private static async Task<JsonElement> Preview(HttpClient c,string csrf,Guid source,JournalCreationRequest r){using var response=await Post(c,csrf,Url(source)+"/preview",r);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
  private static async Task<JsonElement> Create(HttpClient c,string csrf,Guid source,JournalCreationRequest r){using var response=await Post(c,csrf,Url(source),r with{Reviewed=true});Assert.Equal(HttpStatusCode.OK,response.StatusCode);return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
}
