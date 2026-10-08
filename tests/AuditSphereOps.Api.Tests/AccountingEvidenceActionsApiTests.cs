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
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class AccountingEvidenceActionsApiTests
{
  [Fact]
  public async Task ExactLinkIndependentReviewAndRequestRecoveryRetainImmutableEvidence()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-EVIDENCE-ACTIONS");
    var seed = await AccountingAnalysisReviewSeed.SeedAsync(pg, retainReviews:false, linkEvidence:false); var f=seed.Fixture; var id=seed.Evidence["ECL"];
    using var staffFactory=Factory(pg,f.Staff);using var staff=staffFactory.CreateClient();var csrf=await SignIn(staff);
    using var reviewerFactory=Factory(pg,f.Reviewer);using var reviewer=reviewerFactory.CreateClient();var reviewerCsrf=await SignIn(reviewer);
    var v=await Read(staff,Url("ECL",id)+"/actions"); Assert.True(v.GetProperty("canLink").GetBoolean()); Assert.False(v.GetProperty("canReview").GetBoolean());
    var choices=await Read(staff,Url("ECL",id)+"/procedure-results");Assert.Equal(27,choices.GetProperty("count").GetInt32());Assert.Equal(25,choices.GetProperty("rows").GetArrayLength());
    var next=await Read(staff,Url("ECL",id)+"/procedure-results?page=1");Assert.Equal(2,next.GetProperty("rows").GetArrayLength());
    var candidate=choices.GetProperty("rows")[0];var request=Link(v,candidate);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(staff,"wrong",Url("ECL",id)+"/actions",request with{Reviewed=true})).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(staff,csrf,Url("ECL",id)+"/actions",request)).StatusCode);
    Assert.True((await Preview(staff,csrf,"ECL",id,request)).GetProperty("canProceed").GetBoolean());
    var receipt=await Execute(staff,csrf,"ECL",id,request);var replay=await Execute(staff,csrf,"ECL",id,request);
    Assert.Equal(receipt.GetProperty("id").GetGuid(),replay.GetProperty("id").GetGuid());
    Assert.Equal(HttpStatusCode.Conflict,(await Post(staff,csrf,Url("ECL",id)+"/actions",request with{Reviewed=true,Reason="Changed intent"})).StatusCode);
    var lookup=await Read(staff,Url("ECL",id)+"/receipts/"+request.RequestId+"?requestHash="+receipt.GetProperty("requestHash").GetString());Assert.True(lookup.GetProperty("found").GetBoolean());
    var otherActor=await Read(reviewer,Url("ECL",id)+"/receipts/"+request.RequestId+"?requestHash="+receipt.GetProperty("requestHash").GetString());Assert.False(otherActor.GetProperty("found").GetBoolean());
    var self=await Read(staff,Url("ECL",id)+"/actions");Assert.False((await Preview(staff,csrf,"ECL",id,Review(self,"APPROVED"))).GetProperty("canProceed").GetBoolean());
    var independent=await Read(reviewer,Url("ECL",id)+"/actions");Assert.Contains(independent.GetProperty("decisions").EnumerateArray(),x=>x.GetString()=="APPROVED");
    var reviewed=await Execute(reviewer,reviewerCsrf,"ECL",id,Review(independent,"APPROVED"));Assert.Equal("APPROVED",reviewed.GetProperty("decision").GetString());
    var final=await Read(reviewer,Url("ECL",id)+"/actions");Assert.False(final.GetProperty("canReview").GetBoolean());Assert.False(final.GetProperty("canLink").GetBoolean());Assert.Equal(2,final.GetProperty("count").GetInt32());
    await using var db=new AuditSphereDbContext(pg.Options);Assert.Equal(1,await db.AccountingEvidenceAuditLinks.CountAsync(x=>x.EvidenceId==id));
    Assert.Equal(2,await db.AccountingEvidenceActions.CountAsync(x=>x.EvidenceId==id));
    var action=await db.AccountingEvidenceActions.SingleAsync(x=>x.EvidenceId==id&&x.Action=="REVIEW");
    Assert.Contains("7.006173",action.BeforeJson);Assert.Contains("APPROVED",action.AfterJson);Assert.Equal(f.Reviewer.Id,action.ActorId);
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE accounting_evidence_actions SET reason='altered' WHERE id={action.Id}"));
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM accounting_evidence_actions WHERE id={action.Id}"));
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ecl_assessments SET management_overlay=999 WHERE id={id}"));
    var linkedId=receipt.GetProperty("linkId").GetGuid();
    var otherTarget=Guid.NewGuid();
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE accounting_evidence_audit_links SET evidence_id={otherTarget} WHERE id={linkedId}"));
    foreach(var kind in new[]{"INVENTORY","SPECIALIST","ANALYTICAL"})
    {
      var target=seed.Evidence[kind];var fields=await Read(staff,Url(kind,target)+"/actions");var eligible=await Read(staff,Url(kind,target)+"/procedure-results");
      await Execute(staff,csrf,kind,target,Link(fields,eligible.GetProperty("rows")[0]));
      var state=await Read(reviewer,Url(kind,target)+"/actions");await Execute(reviewer,reviewerCsrf,kind,target,Review(state,"APPROVED"));
      Assert.False((await Read(reviewer,Url(kind,target)+"/actions")).GetProperty("canReview").GetBoolean());
    }
  }

  [Fact]
  public async Task ConcurrentLocalReplayAndLateEpochRefusalNeverLeavePartialLinksOrReceipts()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-EVIDENCE-ATOMIC");var s=await AccountingAnalysisReviewSeed.SeedAsync(pg,false,false);var f=s.Fixture;
    var actor=PbcSeed.Actor(f.Staff,"AccountingPreparer");var id=s.Evidence["ECL"];
    await using var inspect=new AuditSphereDbContext(pg.Options);var v=(await AccountingEvidenceWorkspace.StateAsync(inspect,actor,"ECL",id)).Value!;
    var candidate=(await AccountingEvidenceWorkspace.ProceduresAsync(inspect,actor,"ECL",id)).Value!.Rows[0];
    var r=new EvidenceActionRequest(Guid.NewGuid(),v.ReviewBasis,"LINK",candidate.ResultId,candidate.ResultBasis,"","Synthetic parallel link","Synthetic evidence",true);
    async Task<EvidenceActionReceipt> Run()
    {
      // A serialization loser is a local rejected transaction. Reuse the exact retained intent only.
      for(var attempt=0;attempt<4;attempt++)
      {
        await using var db=new AuditSphereDbContext(pg.Options);
        try{var result=await AccountingEvidenceWorkspace.ExecuteAsync(db,actor,"ECL",id,r);Assert.True(result.Succeeded,result.Message);return result.Value!;}
        catch(Exception ex) when(IsSerializationFailure(ex)){}
      }
      throw new InvalidOperationException("Local serialized replay did not reconcile.");
    }
    var replies=await Task.WhenAll(Run(),Run());Assert.Equal(replies[0].Id,replies[1].Id);
    Assert.Equal(1,await inspect.AccountingEvidenceActions.CountAsync());Assert.Equal(1,await inspect.AccountingEvidenceAuditLinks.CountAsync(x=>x.EvidenceId==id));
    var inventory=s.Evidence["INVENTORY"];var state=(await AccountingEvidenceWorkspace.StateAsync(inspect,actor,"INVENTORY",inventory)).Value!;
    var candidates=(await AccountingEvidenceWorkspace.ProceduresAsync(inspect,actor,"INVENTORY",inventory)).Value!;
    var originalEpoch=await inspect.Users.Where(x=>x.Id==actor.UserId).Select(x=>x.SessionEpoch).SingleAsync();
    var late=new EpochAfterSave(actor.UserId);
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>().UseNpgsql(pg.ConnectionString).AddInterceptors(late).Options))
    {
      var rejected=await AccountingEvidenceWorkspace.ExecuteAsync(db,actor,"INVENTORY",inventory,
        r with{RequestId=Guid.NewGuid(),ReviewBasis=state.ReviewBasis,ResultId=candidates.Rows[0].ResultId,ResultBasis=candidates.Rows[0].ResultBasis});
      Assert.False(rejected.Succeeded);
    }
    Assert.Empty(await inspect.AccountingEvidenceAuditLinks.Where(x=>x.EvidenceId==inventory).ToListAsync());
    Assert.Empty(await inspect.AccountingEvidenceActions.Where(x=>x.EvidenceId==inventory).ToListAsync());
    Assert.Equal(originalEpoch,await inspect.Users.Where(x=>x.Id==actor.UserId).Select(x=>x.SessionEpoch).SingleAsync());
  }
  private static bool IsSerializationFailure(Exception exception)
  {
    // Npgsql's non-retrying EF execution strategy wraps a transient SaveChanges failure.
    // Match the actual PostgreSQL rollback code, never a generic transient-error message.
    for(Exception? current=exception;current is not null;current=current.InnerException)
      if(current is PostgresException {SqlState:PostgresErrorCodes.SerializationFailure}) return true;
    return false;
  }
  private sealed class EpochAfterSave(Guid userId):SaveChangesInterceptor
  {
    private bool fired;
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken ct=default)
    {if(!fired){fired=true;await data.Context!.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET session_epoch=session_epoch+1 WHERE id={userId}",ct);}return result;}
  }

  [Fact]
  public async Task ScopeSourceReplayPeriodAndLegacyRiskFencesNeverPublishAnUnsupportedDecision()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-EVIDENCE-ACTION-FENCES");var s=await AccountingAnalysisReviewSeed.SeedAsync(pg,false,false);var f=s.Fixture;
    using var factory=Factory(pg,f.Reviewer);using var c=factory.CreateClient();var csrf=await SignIn(c);var id=s.Evidence["SPECIALIST"];
    using var hidden=await c.GetAsync(Url("SPECIALIST",s.HiddenId)+"/actions");using var missing=await c.GetAsync(Url("SPECIALIST",Guid.NewGuid())+"/actions");
    Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode);Assert.Equal(await hidden.Content.ReadAsStringAsync(),await missing.Content.ReadAsStringAsync());
    using var portalFactory=Factory(pg,f.Client);using var portal=portalFactory.CreateClient();var portalCsrf=await SignIn(portal);
    foreach(var(kind,evidenceId) in s.Evidence)
      Assert.Equal(HttpStatusCode.Forbidden,(await portal.GetAsync(Url(kind,evidenceId)+"/actions")).StatusCode);
    var v=await Read(c,Url("SPECIALIST",id)+"/actions");var choices=await Read(c,Url("SPECIALIST",id)+"/procedure-results");
    var link=Link(v,choices.GetProperty("rows")[0]);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,csrf,Url("SPECIALIST",id)+"/preview",link with{ResultId=Guid.NewGuid()})).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,Url("SPECIALIST",id)+"/preview",link with{ResultBasis=new('f',64)})).StatusCode);
    var old=await Read(c,Url("ECL",s.Evidence["ECL"])+"/actions");
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.TrialBalanceDatasets.Where(x=>x.Id==s.SourceId).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.NormalizedDatasetDigest,new string('e',64)));
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,Url("ECL",s.Evidence["ECL"])+"/actions",Review(old,"APPROVED") with{Reviewed=true})).StatusCode);
    var risk=await Read(c,Url("JOURNAL_RISK",s.Evidence["JOURNAL_RISK"])+"/actions");Assert.Equal("ESCALATED",Assert.Single(risk.GetProperty("decisions").EnumerateArray()).GetString());
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url("JOURNAL_RISK",s.Evidence["JOURNAL_RISK"])+"/actions",Review(risk,"CLEARED") with{Reviewed=true})).StatusCode);
    await Execute(c,csrf,"JOURNAL_RISK",s.Evidence["JOURNAL_RISK"],Review(risk,"ESCALATED"));
    v=await Read(c,Url("SPECIALIST",id)+"/actions");link=Link(v,(await Read(c,Url("SPECIALIST",id)+"/procedure-results")).GetProperty("rows")[0]);
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.ClientReportingPeriods.Where(x=>x.Id==f.ClientId || x.ClientId==f.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.Status,"CLOSED"));
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url("SPECIALIST",id)+"/actions",link with{Reviewed=true})).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options))
    {Assert.Equal(1,await db.AccountingEvidenceActions.CountAsync());await db.Users.Where(x=>x.Id==f.Reviewer.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));}
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(Url("SPECIALIST",id)+"/actions")).StatusCode);
  }
  private static EvidenceActionRequest Link(JsonElement v,JsonElement r)=>new(Guid.NewGuid(),v.GetProperty("reviewBasis").GetString()!,"LINK",r.GetProperty("resultId").GetGuid(),r.GetProperty("resultBasis").GetString(),"","Synthetic exact procedure link","Synthetic link evidence");
  private static EvidenceActionRequest Review(JsonElement v,string decision)=>new(Guid.NewGuid(),v.GetProperty("reviewBasis").GetString()!,"REVIEW",null,null,decision,"Synthetic independent human conclusion","Synthetic corroboration");
  private static string Url(string kind,Guid id)=>$"/api/ui/accounting/evidence/{kind}/{id}";
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static async Task<JsonElement> Read(HttpClient c,string url){using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();}
  private static Task<HttpResponseMessage> Post(HttpClient c,string csrf,string url,object body){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(r);}
  private static async Task<JsonElement> Preview(HttpClient c,string csrf,string kind,Guid id,EvidenceActionRequest r){using var reply=await Post(c,csrf,Url(kind,id)+"/preview",r);Assert.Equal(HttpStatusCode.OK,reply.StatusCode);return JsonDocument.Parse(await reply.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
  private static async Task<JsonElement> Execute(HttpClient c,string csrf,string kind,Guid id,EvidenceActionRequest r){using var reply=await Post(c,csrf,Url(kind,id)+"/actions",r with{Reviewed=true});Assert.Equal(HttpStatusCode.OK,reply.StatusCode);return JsonDocument.Parse(await reply.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
}
