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

public sealed class JournalManagementApiTests
{
  [Fact]
  public async Task SignedInClientDecisionIsExactIdempotentImmutableAndSeparateFromTechnicalPosting()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-MANAGEMENT");var(f,id)=await Seed(pg);
    using var factory=Factory(pg,f.Client);using var c=factory.CreateClient();var csrf=await SignIn(c);var v=await Read(c,Url(id,true));var r=Request(v);
    var p=await Preview(c,csrf,id,true,r);Assert.Equal("SIGNED_IN",p.GetProperty("evidenceMode").GetString());
    var replies=await Task.WhenAll(Save(c,csrf,id,true,r),Save(c,csrf,id,true,r));Assert.Equal(replies[0].GetProperty("action").GetProperty("id").GetGuid(),replies[1].GetProperty("action").GetProperty("id").GetGuid());
    var action=replies[0].GetProperty("action");var d=replies[0].GetProperty("management");Assert.Equal(f.Client.Id,d.GetProperty("decidedByUserId").GetGuid());Assert.Equal("Draft",action.GetProperty("newStatus").GetString());
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,Url(id,true),r with{Decision="REJECTED",Reviewed=true})).StatusCode);
    var lookup=await Read(c,Url(id,true)+$"/receipts/{r.RequestId}?requestHash="+action.GetProperty("requestHash").GetString());Assert.True(lookup.GetProperty("found").GetBoolean());
    using var staffFactory=Factory(pg,f.Staff);using var staff=staffFactory.CreateClient();await SignIn(staff);
    var history=await Read(staff,$"/api/ui/accounting/journals/{id}/history/{action.GetProperty("id").GetGuid()}");Assert.Equal("ACCEPTED",history.GetProperty("management").GetProperty("decision").GetString());Assert.Equal("100.123456",history.GetProperty("after").GetProperty("lines")[0].GetProperty("debit").GetString());
    await using var db=new AuditSphereDbContext(pg.Options);Assert.Equal(1,await db.AdjustmentJournalManagementDecisions.CountAsync());Assert.Equal(1,await db.AdjustmentJournalActions.CountAsync());Assert.Equal("Draft",await db.AdjustmentJournals.Where(x=>x.Id==id).Select(x=>x.Status).SingleAsync());Assert.Empty(await db.JournalSourceReconciliations.ToListAsync());
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE adjustment_journal_management_decisions SET decision='REJECTED' WHERE journal_id={id}"));
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM adjustment_journal_management_decisions WHERE journal_id={id}"));
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE adjustment_lines SET debit=1 WHERE journal_id={id} AND debit>0"));
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE adjustment_journals SET revision=revision+1 WHERE id={id}"));
    Assert.False((await AdjustmentJournalService.UpdateDraftAsync(db,PbcSeed.Actor(f.Staff,"AccountingPreparer"),id,[("1000",2m,0m),("3000",0m,2m)],"Changed","Changed",1)).Succeeded);
    Assert.True((await AdjustmentJournalService.SubmitDraftAsync(db,PbcSeed.Actor(f.Staff,"AccountingPreparer"),id)).Succeeded);
    Assert.True((await AdjustmentJournalService.PostAsync(db,PbcSeed.Actor(f.Reviewer,"AccountingReviewer"),id)).Succeeded);
  }
  [Fact]
  public async Task OfflineRecordingCannotClaimClientAuthenticationAndScopeCsrfAndEpochFailClosed()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-OFFLINE");var(f,id)=await Seed(pg);
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();var csrf=await SignIn(c);var v=await Read(c,Url(id,false));var r=Request(v) with{Decision="PARTIAL"};
    Assert.Equal("OFFLINE",(await Preview(c,csrf,id,false,r)).GetProperty("evidenceMode").GetString());
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,"forged",Url(id,false),r with{Reviewed=true})).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url(id,false),r)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(Url(id,true))).StatusCode);
    using var cf=Factory(pg,f.Client);using var client=cf.CreateClient();var token=await SignIn(client);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(Url(id,false))).StatusCode);
    foreach(var target in new[]{id,Guid.NewGuid()}){using var denied=await client.GetAsync($"/api/ui/accounting/journals/{target}/workspace");Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.DoesNotContain("datasetDigest",await denied.Content.ReadAsStringAsync());}
    var saved=await Save(c,csrf,id,false,r);Assert.Equal(JsonValueKind.Null,saved.GetProperty("management").GetProperty("decidedByUserId").ValueKind);Assert.Equal(f.Staff.Id,saved.GetProperty("action").GetProperty("actorId").GetGuid());
    var q=await Read(client,"/api/ui/portal/accounting/journals?page=0");Assert.Single(q.GetProperty("items").EnumerateArray());
    await using(var db=new AuditSphereDbContext(pg.Options)){await db.RoleGrants.Where(x=>x.UserId==f.Client.Id).ExecuteUpdateAsync(x=>x.SetProperty(g=>g.RevokedAt,DateTimeOffset.UtcNow));Assert.False((await JournalManagementWorkspace.GetAsync(db,db,PbcSeed.Actor(f.Client,"ClientUser") with{FirmId=Guid.NewGuid()},id,true)).Succeeded);}
    Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(Url(id,true))).StatusCode);Assert.Empty((await Read(client,"/api/ui/portal/accounting/journals?page=0")).GetProperty("items").EnumerateArray());
    await using(var db=new AuditSphereDbContext(pg.Options))await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(Url(id,false))).StatusCode);
  }
  [Fact]
  public async Task StaleDraftClosedPeriodAndMissingFirstSignInCannotPublishManagementEvidence()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-MANAGEMENT-FENCES");var(f,id)=await Seed(pg);using var factory=Factory(pg,f.Client);using var c=factory.CreateClient();var csrf=await SignIn(c);var v=await Read(c,Url(id,true));var r=Request(v);
    await using(var db=new AuditSphereDbContext(pg.Options))Assert.True((await AdjustmentJournalService.UpdateDraftAsync(db,PbcSeed.Actor(f.Staff,"AccountingPreparer"),id,[("1000",201.123456m,0m),("3000",0m,201.123456m)],"New rationale","New evidence",1)).Succeeded);
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,Url(id,true),r with{Reviewed=true})).StatusCode);
    var current=await Read(c,Url(id,true));Assert.Equal(2,current.GetProperty("revision").GetInt64());
    var notOnboarded = PbcSeed.User(f.FirmId, "Client");
    await using(var db=new AuditSphereDbContext(pg.Options)) { db.Users.Add(notOnboarded); db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,notOnboarded,"ClientUser",f.ClientId,f.EngagementId)); await db.SaveChangesAsync(); }
    using var missingFactory=Factory(pg,notOnboarded);using var missing=missingFactory.CreateClient();var missingToken=await SignIn(missing);
    var missingView=await Read(missing,Url(id,true));Assert.False(missingView.GetProperty("canDecide").GetBoolean());
    Assert.False((await Preview(missing,missingToken,id,true,Request(missingView))).GetProperty("canProceed").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(missing,missingToken,Url(id,true),Request(missingView) with{Reviewed=true})).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options)){var j=await db.AdjustmentJournals.SingleAsync(x=>x.Id==id);await db.ClientReportingPeriods.Where(x=>x.Id==j.PeriodId).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Status,"CLOSED"));}
    Assert.False((await Read(c,Url(id,true))).GetProperty("canDecide").GetBoolean());await using var verify=new AuditSphereDbContext(pg.Options);Assert.Empty(await verify.AdjustmentJournalActions.ToListAsync());Assert.Empty(await verify.AdjustmentJournalManagementDecisions.ToListAsync());
  }
  [Fact]
  public async Task LateEpochRollsBackDecisionAndEventTogether()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-MANAGEMENT-EPOCH");var(f,id)=await Seed(pg);var actor=PbcSeed.Actor(f.Client,"ClientUser");JournalManagementRequest r;
    await using(var db=new AuditSphereDbContext(pg.Options)){var v=(await JournalManagementWorkspace.GetAsync(db,db,actor,id,true)).Value!;r=new(Guid.NewGuid(),v.ReviewBasis,"ACCEPTED","Synthetic reason","Synthetic evidence",true);}
    var interceptor=new EpochAfterSave(async()=>{await using var db=new AuditSphereDbContext(pg.Options);await db.Users.Where(x=>x.Id==f.Client.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));});
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options))Assert.False((await JournalManagementWorkspace.ExecuteAsync(db,db,actor,id,true,r)).Succeeded);
    await using var verify=new AuditSphereDbContext(pg.Options);Assert.Empty(await verify.AdjustmentJournalActions.ToListAsync());Assert.Empty(await verify.AdjustmentJournalManagementDecisions.ToListAsync());
  }
  [Fact]
  public async Task SiblingClientEngagementAndHeldWorkAreExcludedFromQueueAndExactCommands()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-MANAGEMENT-ISOLATION");var(f,id)=await Seed(pg);
    var siblingId=Guid.NewGuid();var siblingClient=Guid.NewGuid();var otherId=Guid.NewGuid();
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      db.PracticeClients.Add(new(){Id=siblingClient,FirmId=f.FirmId,LegalName="PRIVATE-SIBLING-CLIENT",CreatedAt=DateTimeOffset.UtcNow});
      db.Engagements.AddRange(new(){Id=siblingId,FirmId=f.FirmId,PracticeClientId=f.ClientId,Status="Active",CreatedAt=DateTimeOffset.UtcNow},
        new(){Id=otherId,FirmId=f.FirmId,PracticeClientId=siblingClient,Status="Active",CreatedAt=DateTimeOffset.UtcNow});
      await db.SaveChangesAsync();
      var j=await db.AdjustmentJournals.AsNoTracking().SingleAsync(x=>x.Id==id);
      // Includes a legacy row whose client disagrees with its otherwise authorized engagement.
      foreach(var(e,c) in new[]{(siblingId,f.ClientId),(otherId,siblingClient),(f.EngagementId,siblingClient)})
        db.AdjustmentJournals.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=c,EngagementId=e,BaseDatasetId=j.BaseDatasetId,JournalNumber="PRIVATE-SIBLING-JOURNAL",Status="Draft",Purpose=j.Purpose,Revision=1,CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow});
      db.AdjustmentJournals.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,BaseDatasetId=j.BaseDatasetId,JournalNumber="PRIVATE-GROUP-ONLY",Status="Draft",Purpose=AdjustmentJournalPurposes.GroupOnlyElimination,Revision=1,CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow});
      await db.SaveChangesAsync();
    }
    using var factory=Factory(pg,f.Client);using var client=factory.CreateClient();var csrf=await SignIn(client);
    var own=await Read(client,Url(id,true));var request=Request(own) with{Reviewed=true};
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      var targets=await db.AdjustmentJournals.Where(x=>x.Id!=id).Select(x=>x.Id).ToListAsync();targets.Add(Guid.NewGuid());
      foreach(var target in targets)
      {
        using var denied=await client.GetAsync(Url(target,true));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.DoesNotContain("PRIVATE-SIBLING",await denied.Content.ReadAsStringAsync());
        using var command=await Post(client,csrf,Url(target,true),request);Assert.Equal(HttpStatusCode.Forbidden,command.StatusCode);
      }
      Assert.Single((await Read(client,"/api/ui/portal/accounting/journals?page=0")).GetProperty("items").EnumerateArray());
      await db.Engagements.Where(x=>x.Id==f.EngagementId).ExecuteUpdateAsync(x=>x.SetProperty(e=>e.ProfessionalWorkBlocked,true));
    }
    Assert.Empty((await Read(client,"/api/ui/portal/accounting/journals?page=0")).GetProperty("items").EnumerateArray());
    Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(Url(id,true))).StatusCode);
  }
  private sealed class EpochAfterSave(Func<Task> action):SaveChangesInterceptor{private bool fired;public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData d,int result,CancellationToken ct=default){if(!fired){fired=true;await action();}return result;}}
  private static async Task<(PbcSeed.Fixture,Guid)> Seed(OwnedPostgresDatabase pg){var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);return(f,await JournalReviewSeed.SeedAsync(db,f));}
  private static string Url(Guid id,bool client)=>client?$"/api/ui/portal/accounting/journals/{id}":$"/api/ui/accounting/journals/{id}/management";
  private static JournalManagementRequest Request(JsonElement v)=>new(Guid.NewGuid(),v.GetProperty("reviewBasis").GetString()!,"ACCEPTED","Synthetic disposition rationale","Synthetic accepted/rejected line evidence",false);
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static async Task<JsonElement> Read(HttpClient c,string url){using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();}
  private static Task<HttpResponseMessage> Post(HttpClient c,string csrf,string url,object body){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(r);}
  private static async Task<JsonElement> Preview(HttpClient c,string csrf,Guid id,bool client,JournalManagementRequest r){using var response=await Post(c,csrf,Url(id,client)+"/preview",r);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
  private static async Task<JsonElement> Save(HttpClient c,string csrf,Guid id,bool client,JournalManagementRequest r){using var response=await Post(c,csrf,Url(id,client),r with{Reviewed=true});Assert.Equal(HttpStatusCode.OK,response.StatusCode);return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
}
