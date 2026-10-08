using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class JournalReviewApiTests
{
  [Fact]
  public async Task ExactEditorReturnResubmitAndIndependentPostingRetainRevisionHistory()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-LIFECYCLE");
    var (f, id) = await Seed(pg);
    using var staffFactory = Factory(pg, f.Staff); using var staff = staffFactory.CreateClient(); var csrf = await SignIn(staff);
    using var reviewerFactory = Factory(pg, f.Reviewer); using var reviewer = reviewerFactory.CreateClient(); var reviewerCsrf = await SignIn(reviewer);
    var view = await Read(staff, Url(id) + "/workspace");
    Assert.True(view.GetProperty("canEdit").GetBoolean()); Assert.False(view.GetProperty("canPost").GetBoolean());
    var edit = Request(view, "UPDATE", [new("1000","200.123456","0"),new("3000","0","200.123455")]);
    var preview = await Preview(staff, csrf, id, edit);
    Assert.False(preview.GetProperty("canProceed").GetBoolean()); Assert.Contains("Unbalanced", preview.GetProperty("errors")[0].GetString());
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(staff, csrf, Url(id)+"/actions", edit with { Reviewed=true })).StatusCode);
    edit = edit with { Lines=[new("1000","200.123456","0"),new("3000","0","200.123456")] };
    preview = await Preview(staff, csrf, id, edit); Assert.Equal("200.123456", preview.GetProperty("totalDebit").GetString());
    var receipt = await Execute(staff, csrf, id, edit); var replay = await Execute(staff, csrf, id, edit);
    Assert.Equal(receipt.GetProperty("id").GetGuid(), replay.GetProperty("id").GetGuid());
    Assert.Equal(HttpStatusCode.Conflict, (await Post(staff, csrf, Url(id)+"/actions", edit with { Reviewed=true, Reason="Another intent" })).StatusCode);
    view = await Read(staff, Url(id)+"/workspace");
    var submit = Request(view,"SUBMIT"); await Execute(staff,csrf,id,submit);
    var submitted = await Read(reviewer, Url(id)+"/workspace"); Assert.True(submitted.GetProperty("canPost").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(reviewer,reviewerCsrf,Url(id)+"/actions",Request(submitted,"RETURN") with { Reviewed=false })).StatusCode);
    await Execute(reviewer,reviewerCsrf,id,Request(submitted,"RETURN") with {Reason="Correct exact account evidence"});
    view = await Read(staff,Url(id)+"/workspace"); Assert.Equal("Returned",view.GetProperty("status").GetString());
    Assert.Equal("Correct exact account evidence",view.GetProperty("returnReason").GetString());
    await Execute(staff,csrf,id,Request(view,"UPDATE",[new("1000","201.123456","0"),new("3000","0","201.123456")]));
    view = await Read(staff,Url(id)+"/workspace"); await Execute(staff,csrf,id,Request(view,"SUBMIT"));
    submitted = await Read(reviewer,Url(id)+"/workspace"); await Execute(reviewer,reviewerCsrf,id,Request(submitted,"POST"));
    view = await Read(staff,Url(id)+"/workspace");
    Assert.Equal("Posted",view.GetProperty("status").GetString()); Assert.Equal(3,view.GetProperty("revision").GetInt64());
    Assert.Equal(6,view.GetProperty("historyCount").GetInt32()); Assert.False(view.GetProperty("canEdit").GetBoolean());
    var history = await Read(staff,Url(id)+"/history/"+receipt.GetProperty("id").GetGuid());
    Assert.Equal("100.123456",history.GetProperty("before").GetProperty("lines")[0].GetProperty("debit").GetString());
    Assert.Equal("200.123456",history.GetProperty("after").GetProperty("lines")[0].GetProperty("debit").GetString());
    Assert.Equal(1,history.GetProperty("before").GetProperty("revision").GetInt64());
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(staff,"wrong",Url(id)+"/preview",Request(view,"REVERSE") with{ReversalNumber="AJ-R"})).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(staff,csrf,Url(id)+"/actions",Request(view,"UPDATE",[new("1000","1","0"),new("3000","0","1")]) with{Reviewed=true})).StatusCode);
  }

  [Fact]
  public async Task ConcurrentReversalHasOneLinkedDraftAndImmutableRequestEvidence()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-REVERSE");var(f,id)=await Seed(pg);
    await using(var db=new AuditSphereDbContext(pg.Options)) Assert.True((await AdjustmentJournalService.PostAsync(db,PbcSeed.Actor(f.Reviewer,"AccountingReviewer"),id)).Succeeded);
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();var csrf=await SignIn(c);
    var v=await Read(c,Url(id)+"/workspace");var r=Request(v,"REVERSE") with{ReversalNumber="AJ-SYN-R",Reason="Reverse retained exact treatment",EvidenceReference="Synthetic reversal evidence"};
    var replies=await Task.WhenAll(Execute(c,csrf,id,r),Execute(c,csrf,id,r));
    Assert.Equal(replies[0].GetProperty("resultJournalId").GetGuid(),replies[1].GetProperty("resultJournalId").GetGuid());
    var lookup=await Read(c,Url(id)+"/receipts/"+r.RequestId+"?requestHash="+replies[0].GetProperty("requestHash").GetString());Assert.True(lookup.GetProperty("found").GetBoolean());
    var fresh=await Read(c,Url(id)+"/workspace");Assert.False(fresh.GetProperty("canReverse").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url(id)+"/actions",Request(fresh,"REVERSE") with{Reviewed=true,ReversalNumber="AJ-SYN-R2"})).StatusCode);
    await using var verify=new AuditSphereDbContext(pg.Options);
    var child=await verify.AdjustmentJournals.SingleAsync(x=>x.ReversalOfJournalId==id);
    Assert.Equal("Draft",child.Status);Assert.Equal(r.Reason,child.Reason);Assert.Equal(r.EvidenceReference,child.EvidenceReference);
    Assert.Equal(1,await verify.AdjustmentJournalActions.CountAsync());
    await Assert.ThrowsAsync<PostgresException>(()=>verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE adjustment_lines SET journal_id={child.Id} WHERE journal_id={id}"));
    await Assert.ThrowsAsync<PostgresException>(()=>verify.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM adjustment_lines WHERE journal_id={id}"));
    var evidence=await verify.AdjustmentJournalActions.SingleAsync();
    var error=await Assert.ThrowsAsync<PostgresException>(()=>verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE adjustment_journal_actions SET reason='altered' WHERE id={evidence.Id}"));
    Assert.Contains("append-only",error.MessageText);
    await Assert.ThrowsAsync<PostgresException>(()=>verify.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM adjustment_journal_actions WHERE id={evidence.Id}"));
    Assert.Equal(HttpStatusCode.Conflict,(await c.GetAsync(Url(id)+"/receipts/"+r.RequestId+"?requestHash="+new string('f',64))).StatusCode);
  }

  [Fact]
  public async Task StalePeriodAndInvalidLinePrecisionNeverWriteOrExposeSiblingHistory()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-FENCES");var(f,id)=await Seed(pg);
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();var csrf=await SignIn(c);var v=await Read(c,Url(id)+"/workspace");
    foreach(var lines in new[]{new JournalEditLine[]{new("1000","1e2","0"),new("3000","0","100")},[new("1000","1.1234567","0"),new("3000","0","1.1234567")],[new("UNKNOWN","1","0"),new("3000","0","1")]})
      Assert.False((await Preview(c,csrf,id,Request(v,"UPDATE",lines))).GetProperty("canProceed").GetBoolean());
    var submit=Request(v,"SUBMIT");
    await using(var db=new AuditSphereDbContext(pg.Options)) await db.ClientReportingPeriods.Where(x=>x.Id==v.GetProperty("periodId").GetGuid()).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Status,"CLOSED"));
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,Url(id)+"/actions",submit with{Reviewed=true})).StatusCode);
    using var clientFactory=Factory(pg,f.Client);using var client=clientFactory.CreateClient();await SignIn(client);
    foreach(var target in new[]{id,Guid.NewGuid()}) { using var denied=await client.GetAsync(Url(target)+"/workspace");Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.DoesNotContain("historyCount",await denied.Content.ReadAsStringAsync()); }
    await using var verify=new AuditSphereDbContext(pg.Options);Assert.Equal("Draft",await verify.AdjustmentJournals.Where(x=>x.Id==id).Select(x=>x.Status).SingleAsync());Assert.Empty(await verify.AdjustmentJournalActions.ToListAsync());
    Assert.Equal(ErrorCodes.ScopeDenied,(await AdjustmentJournalWorkspace.GetAsync(verify,verify,PbcSeed.Actor(f.Staff,"AccountingPreparer") with{FirmId=Guid.NewGuid()},id)).ErrorCode);
  }

  [Fact]
  public async Task LateEpochAfterServiceSaveRollsBackTreatmentAndReceiptTogether()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-LATE-EPOCH");var(f,id)=await Seed(pg);
    var actor=PbcSeed.Actor(f.Staff,"AccountingPreparer");JournalActionRequest request;
    await using(var db=new AuditSphereDbContext(pg.Options)) {var v=(await AdjustmentJournalWorkspace.GetAsync(db,db,actor,id)).Value!;request=new(Guid.NewGuid(),"SUBMIT",v.ReviewBasis,"Synthetic exact review","Synthetic evidence","",[],true);}
    var interceptor=new EpochAfterSave(async()=>{await using var db=new AuditSphereDbContext(pg.Options);await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));});
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options))
      Assert.False((await AdjustmentJournalWorkspace.ExecuteAsync(db,db,actor,id,request)).Succeeded);
    await using var verify=new AuditSphereDbContext(pg.Options);Assert.Equal("Draft",await verify.AdjustmentJournals.Where(x=>x.Id==id).Select(x=>x.Status).SingleAsync());Assert.Empty(await verify.AdjustmentJournalActions.ToListAsync());
  }

  [Fact]
  public async Task SourceReflectionAndManagementDispositionReconciliation()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-JOURNAL-REFLECTION");
    var (f, id) = await Seed(pg);
    using var staffFactory = Factory(pg, f.Staff); using var staff = staffFactory.CreateClient(); var staffCsrf = await SignIn(staff);
    using var reviewerFactory = Factory(pg, f.Reviewer); using var reviewer = reviewerFactory.CreateClient(); var reviewerCsrf = await SignIn(reviewer);

    var mgmtView = await Read(staff, Url(id) + "/management");
    var mgmtBasis = mgmtView.GetProperty("reviewBasis").GetString()!;
    var mgmtReq = new { RequestId = Guid.NewGuid(), ReviewBasis = mgmtBasis, Decision = "ACCEPTED", Reason = "Client approved in closing meeting", EvidenceReference = "Signed closing memo", Reviewed = true };
    var mgmtPost = await Post(staff, staffCsrf, Url(id) + "/management", mgmtReq);
    Assert.Equal(HttpStatusCode.OK, mgmtPost.StatusCode);

    var draftView = await Read(staff, Url(id) + "/workspace");
    Assert.False(draftView.GetProperty("canReconcileReflection").GetBoolean());
    var md = draftView.GetProperty("managementDecision");
    Assert.Equal("ACCEPTED", md.GetProperty("decision").GetString());
    Assert.Equal("OFFLINE", md.GetProperty("evidenceMode").GetString());
    Assert.Equal("Signed closing memo", md.GetProperty("evidenceReference").GetString());

    await Execute(staff, staffCsrf, id, Request(draftView, "SUBMIT"));
    var submitted = await Read(reviewer, Url(id) + "/workspace");
    await Execute(reviewer, reviewerCsrf, id, Request(submitted, "POST"));

    var postedStaff = await Read(staff, Url(id) + "/workspace");
    Assert.False(postedStaff.GetProperty("canReconcileReflection").GetBoolean());
    var staffReflection = await Post(staff, staffCsrf, Url(id) + "/source-reflection",
      new { RequestId = Guid.NewGuid(), ReviewBasis = postedStaff.GetProperty("reviewBasis").GetString(), State = "NOT_REFLECTED", Evidence = "Staff attempt" });
    Assert.Equal(HttpStatusCode.BadRequest, staffReflection.StatusCode);

    var postedReviewer = await Read(reviewer, Url(id) + "/workspace");
    Assert.True(postedReviewer.GetProperty("canReconcileReflection").GetBoolean());

    var invalid = await Post(reviewer, reviewerCsrf, Url(id) + "/source-reflection",
      new { RequestId = Guid.NewGuid(), ReviewBasis = postedReviewer.GetProperty("reviewBasis").GetString(), State = "REFLECTED", Evidence = "   " });
    Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

    var ok = await Post(reviewer, reviewerCsrf, Url(id) + "/source-reflection",
      new { RequestId = Guid.NewGuid(), ReviewBasis = postedReviewer.GetProperty("reviewBasis").GetString(), State = "NOT_REFLECTED", Evidence = "Not reflected in client trial balance" });
    Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

    var updated = await Read(reviewer, Url(id) + "/workspace");
    var sr = updated.GetProperty("sourceReflection");
    Assert.Equal("NOT_REFLECTED", sr.GetProperty("state").GetString());
    Assert.Equal("Not reflected in client trial balance", sr.GetProperty("evidence").GetString());
    Assert.True(sr.GetProperty("isExactRevision").GetBoolean());
    Assert.Equal(f.Reviewer.Id, sr.GetProperty("reviewedByUserId").GetGuid());
  }

  private sealed class EpochAfterSave(Func<Task> act):SaveChangesInterceptor
  {
    private bool fired;
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken ct=default)
    {if(!fired){fired=true;await act();}return result;}
  }
  private static async Task<(PbcSeed.Fixture,Guid)> Seed(OwnedPostgresDatabase pg)
  {
    var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    return(f,await JournalReviewSeed.SeedAsync(db,f));
  }
  private static JournalActionRequest Request(JsonElement v,string action,IReadOnlyList<JournalEditLine>? lines=null)=>new(Guid.NewGuid(),action,v.GetProperty("reviewBasis").GetString()!,"Synthetic reviewed action","Synthetic evidence","",lines??[],false);
  private static string Url(Guid id)=>$"/api/ui/accounting/journals/{id}";
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static async Task<JsonElement> Read(HttpClient c,string url){using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();}
  private static Task<HttpResponseMessage> Post(HttpClient c,string csrf,string url,object body){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(r);}
  private static async Task<JsonElement> Preview(HttpClient c,string csrf,Guid id,JournalActionRequest r){using var response=await Post(c,csrf,Url(id)+"/preview",r);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
  private static async Task<JsonElement> Execute(HttpClient c,string csrf,Guid id,JournalActionRequest r){using var response=await Post(c,csrf,Url(id)+"/actions",r with{Reviewed=true});Assert.Equal(HttpStatusCode.OK,response.StatusCode);return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
}
