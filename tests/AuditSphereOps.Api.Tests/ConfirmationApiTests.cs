using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;
public sealed class ConfirmationApiTests
{
  [Fact]
  public async Task ReviewedLifecycle_RequiresObservedDispatch_IndependentCurrentEvidence_AndReviewedAlternative()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-CONFIRMATIONS-LIFECYCLE");var f=await PbcSeed.SeedAsync(pg);
    using var sf=Factory(pg,f.Staff);using var rf=Factory(pg,f.Reviewer);using var af=Factory(pg,f.Admin);
    using var staff=sf.CreateClient(new(){AllowAutoRedirect=false});using var reviewer=rf.CreateClient(new(){AllowAutoRedirect=false});using var admin=af.CreateClient(new(){AllowAutoRedirect=false});
    var sc=await SignIn(staff);var rc=await SignIn(reviewer);var ac=await SignIn(admin);var path=Path(f.EngagementId);
    var register=await Get(staff,path);Assert.Equal(0,register.GetProperty("items").GetArrayLength());
    var body=new{reviewed=true,reviewToken=register.GetProperty("createReviewToken").GetString(),areaCode="CASH_BANK",sourceRecordId="bank-001",bookedAmount="123456789.123456",currency="QAR",confirmationDate="2026-10-02",respondent="Synthetic bank",contactValidationSource="Approved contact register"};
    Assert.Equal(HttpStatusCode.Forbidden,(await staff.PostAsJsonAsync(path,body)).StatusCode);
    var unsupported=JsonSerializer.Deserialize<Dictionary<string,object?>>(JsonSerializer.Serialize(body))!;unsupported["bookedAmount"]="123456789.12345678";
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(staff,path,sc,unsupported)).StatusCode);
    unsupported["bookedAmount"]="1.00000000000000000000000000001";Assert.Equal(HttpStatusCode.BadRequest,(await Post(staff,path,sc,unsupported)).StatusCode);
    using var created=await Post(staff,path,sc,body);Assert.Equal(HttpStatusCode.OK,created.StatusCode);var json=await Json(created);var id=json.GetProperty("value").GetProperty("confirmationCaseId").GetGuid();
    Assert.Equal(HttpStatusCode.Conflict,(await Post(staff,path,sc,body)).StatusCode);
    var detail=await Get(staff,path+"/"+id);Assert.Equal("NOT_DISPATCHED",detail.GetProperty("case").GetProperty("monitoring").GetString());Assert.Equal("123456789.123456",detail.GetProperty("case").GetProperty("bookedAmount").GetString());
    Assert.Equal(HttpStatusCode.BadRequest,(await Action(staff,sc,path,id,"DISPATCH",new{reference="not-sent"})).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await Action(staff,sc,path,id,"APPROVE")).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await Action(reviewer,rc,path,id,"APPROVE")).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await Action(admin,ac,path,id,"CRITICALITY",new{critical=true,rationale="Outstanding critical evidence"})).StatusCode);
    Assert.Equal(1,(await Get(staff,path)).GetProperty("outstandingCritical").GetInt32());
    Assert.Equal(HttpStatusCode.OK,(await Action(staff,sc,path,id,"DISPATCH",new{reference="observed-synthetic-dispatch"})).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await Action(staff,sc,path,id,"RESPONSE",new{origin="FOLLOW_UP",channel="CONTROLLED",reference="nonresponse-observation",authenticityAssessment="Observed no response",decision="NO_RESPONSE"})).StatusCode);
    detail=await Get(reviewer,path+"/"+id);var response=detail.GetProperty("responses")[0].GetProperty("id").GetGuid();
    Assert.Equal(HttpStatusCode.OK,(await Action(reviewer,rc,path,id,"REVIEW_RESPONSE",new{evidenceId=response})).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Action(reviewer,rc,path,id,"CLOSE",new{conclusion="Nonresponse is not enough"})).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await Action(staff,sc,path,id,"ALTERNATIVE",new{purpose="Inspect clearing",evidenceReferences=new[]{"immutable-evidence-001"},conclusion="Supports recorded amount"})).StatusCode);
    detail=await Get(reviewer,path+"/"+id);var alt=detail.GetProperty("alternatives")[0].GetProperty("id").GetGuid();
    Assert.Equal(HttpStatusCode.OK,(await Action(reviewer,rc,path,id,"REVIEW_ALTERNATIVE",new{evidenceId=alt})).StatusCode);
    // A late response appends a revision; the earlier reviewed nonresponse cannot approve it.
    var previousTag=detail.GetProperty("reviewToken").GetString();
    Assert.Equal(HttpStatusCode.OK,(await Action(staff,sc,path,id,"RESPONSE",new{origin="BANK",channel="DIRECT",reference="late-response",confirmedAmount="123456789.123457",authenticityAssessment="Validated direct response",decision="DIFFERENCE"})).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict,(await Post(reviewer,path+"/"+id+"/actions",rc,new{action="CLOSE",reviewed=true,reviewToken=previousTag,conclusion="Old evidence"})).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Action(reviewer,rc,path,id,"REVIEW_RESPONSE",new{evidenceId=response})).StatusCode);
    detail=await Get(reviewer,path+"/"+id);Assert.Equal(2,detail.GetProperty("responses").GetArrayLength());var latest=detail.GetProperty("responses")[0];Assert.Equal("0.000001",latest.GetProperty("differenceAmount").GetString());
    Assert.Equal(HttpStatusCode.OK,(await Action(reviewer,rc,path,id,"REVIEW_RESPONSE",new{evidenceId=latest.GetProperty("id").GetGuid()})).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await Action(reviewer,rc,path,id,"CLOSE",new{conclusion="Current independently reviewed evidence is sufficient"})).StatusCode);
    Assert.Equal(0,(await Get(staff,path)).GetProperty("outstandingCritical").GetInt32());
    detail=await Get(reviewer,path+"/"+id);var closure=detail.GetProperty("closure");Assert.Equal("Current independently reviewed evidence is sufficient",closure.GetProperty("conclusion").GetString());Assert.Equal(f.Reviewer.Id,closure.GetProperty("closedByUserId").GetGuid());Assert.Equal(64,closure.GetProperty("evidenceSha256").GetString()!.Length);
    Assert.Equal(HttpStatusCode.BadRequest,(await Action(reviewer,rc,path,id,"CLOSE",new{conclusion="A second close must not rewrite evidence"})).StatusCode);
    await using(var proofDb=new AuditSphereDbContext(pg.Options)){
      var proof=await proofDb.AuditConfirmationClosures.SingleAsync();Assert.Equal(closure.GetProperty("id").GetGuid(),proof.Id);
      Assert.Equal(proof.EvidenceSha256,Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(proof.EvidenceSnapshotJson))));
      using var snapshot=JsonDocument.Parse(proof.EvidenceSnapshotJson);Assert.Equal(2,snapshot.RootElement.GetProperty("CurrentResponse").GetProperty("Revision").GetInt64());Assert.Equal(latest.GetProperty("id").GetGuid(),snapshot.RootElement.GetProperty("CurrentResponse").GetProperty("Id").GetGuid());
      await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>proofDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE audit_confirmation_closures SET conclusion = 'tampered' WHERE id = {proof.Id}"));
      await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>proofDb.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_confirmation_closures WHERE id = {proof.Id}"));
    }

    await using var db=new AuditSphereDbContext(pg.Options);Assert.Single(await db.AuditConfirmationCases.ToListAsync());Assert.Equal(2,await db.AuditConfirmationResponses.CountAsync());
  }
  [Fact]
  public async Task ExactScope_GuessedIds_BoundedWindows_AndRevokedSessions_FailClosed()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-CONFIRMATIONS-SCOPE");var f=await PbcSeed.SeedAsync(pg);var other=await PbcSeed.SeedAsync(pg);
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient(new(){AllowAutoRedirect=false});var csrf=await SignIn(c);var path=Path(f.EngagementId);
    foreach(var id in new[]{other.EngagementId,Guid.NewGuid()})Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(Path(id))).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(path+"/"+Guid.NewGuid())).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(path+"?page=-1")).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(path+"?filter=ANY")).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options)){for(var n=0;n<27;n++)db.AuditConfirmationCases.Add(new AuditSphereOps.Domain.Audit.AuditConfirmationCase{Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,AreaCode="CASH_BANK",SourceRecordId=n.ToString(),ContactValidationSource="Approved synthetic contact",Respondent="Synthetic bank",Currency="QAR",ConfirmationDate=new(2026,10,2),CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();}
    var first=await Get(c,path);Assert.Equal(25,first.GetProperty("items").GetArrayLength());Assert.True(first.GetProperty("hasMore").GetBoolean());Assert.Equal(2,(await Get(c,path+"?page=1")).GetProperty("items").GetArrayLength());
    await using(var db=new AuditSphereDbContext(pg.Options)){(await db.Users.SingleAsync(x=>x.Id==f.Staff.Id)).SessionEpoch++;await db.SaveChangesAsync();}
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(path)).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await Post(c,path,csrf,new{})).StatusCode);
  }
  [Fact]
  public async Task ReviewedBatch_IsAtomic_Bounded_DuplicateSafe_AndScopeFenced()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-CONFIRMATIONS-BATCH");var f=await PbcSeed.SeedAsync(pg);var other=await PbcSeed.SeedAsync(pg);
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient(new(){AllowAutoRedirect=false});var csrf=await SignIn(c);var path=Path(f.EngagementId);
    var token=(await Get(c,path)).GetProperty("createReviewToken").GetString();
    object Body(string? t,object[] cases,bool reviewed=true)=>new{reviewToken=t,reviewed,areaCode="CASH_BANK",currency="QAR",confirmationDate="2026-10-02",cases};
    object Row(string source,string amount="123456789.123456")=>new{sourceRecordId=source,bookedAmount=amount,respondent="Synthetic "+source,contactValidationSource="Approved synthetic contact"};
    var first=Body(token,new[]{Row("batch-001"),Row("batch-002")});
    Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsJsonAsync(path+"/batch",first)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,Path(other.EngagementId)+"/batch",csrf,first)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,path+"/batch",csrf,Body(token,new[]{Row("batch-001")},false))).StatusCode);
    foreach(var invalid in new[]{Body(token,new[]{Row("batch-001"),Row("batch-001 ")}),Body(token,new[]{Row("batch-001"),Row("batch-002","1.1234567")}),Body(token,Array.Empty<object>()),Body(token,Enumerable.Range(0,101).Select(n=>Row("row-"+n)).ToArray())})
      Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,path+"/batch",csrf,invalid)).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options))Assert.Equal(0,await db.AuditConfirmationCases.CountAsync());
    using var created=await Post(c,path+"/batch",csrf,first);Assert.Equal(HttpStatusCode.OK,created.StatusCode);var result=await Json(created);
    Assert.Equal(2,result.GetProperty("value").GetProperty("createdCount").GetInt32());Assert.Equal("246913578.246912",result.GetProperty("value").GetProperty("totalBookedAmount").GetString());
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,path+"/batch",csrf,first)).StatusCode);
    token=(await Get(c,path)).GetProperty("createReviewToken").GetString();
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,path+"/batch",csrf,Body(token,new[]{Row("batch-003"),Row("batch-002")}))).StatusCode);
    var inapplicable=JsonSerializer.Deserialize<Dictionary<string,object?>>(JsonSerializer.Serialize(Body(token,new[]{Row("batch-003")})))!;inapplicable["procedureId"]=Guid.NewGuid();
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,path+"/batch",csrf,inapplicable)).StatusCode);
    // Two concurrent writes with the same reviewed register produce exactly one batch.
    var concurrent=Body(token,new[]{Row("batch-003"),Row("batch-004")});
    var responses=await Task.WhenAll(Post(c,path+"/batch",csrf,concurrent),Post(c,path+"/batch",csrf,concurrent));
    Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);foreach(var r in responses)r.Dispose();
    await using(var db=new AuditSphereDbContext(pg.Options)){var cases=await db.AuditConfirmationCases.OrderBy(x=>x.SourceRecordId).ToListAsync();Assert.Equal(4,cases.Count);Assert.All(cases,x=>{Assert.Equal("DRAFT",x.Status);Assert.Null(x.DispatchedAt);Assert.Equal(123456789.123456m,x.BookedAmount);Assert.Equal(f.Staff.Id,x.CreatedByUserId);});(await db.Users.SingleAsync(x=>x.Id==f.Staff.Id)).SessionEpoch++;await db.SaveChangesAsync();}
    Assert.Equal(HttpStatusCode.Unauthorized,(await Post(c,path+"/batch",csrf,concurrent)).StatusCode);
  }
  [Fact]
  public async Task Closure_UsesLatestAlternativeEvenAtEqualTimestamps_AndLabelsLegacyEvidence()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-CONFIRMATIONS-CLOSURE");var f=await PbcSeed.SeedAsync(pg);
    using var factory=Factory(pg,f.Reviewer);using var c=factory.CreateClient(new(){AllowAutoRedirect=false});var csrf=await SignIn(c);var path=Path(f.EngagementId);
    var caseId=Guid.NewGuid();var legacyId=Guid.NewGuid();var at=DateTimeOffset.UtcNow;
    var oldId=Guid.Parse("11111111-1111-4111-8111-111111111111");var latestId=Guid.Parse("22222222-2222-4222-8222-222222222222");
    await using(var db=new AuditSphereDbContext(pg.Options)){
      foreach(var id in new[]{caseId,legacyId})db.AuditConfirmationCases.Add(new AuditSphereOps.Domain.Audit.AuditConfirmationCase{Id=id,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,AreaCode="CASH_BANK",SourceRecordId=id.ToString(),ContactValidationSource="Approved synthetic source",Respondent="Synthetic bank",Currency="QAR",Status=id==legacyId?"CLOSED":"NO_RESPONSE",DispatchedAt=at.AddDays(-15),ConfirmationDate=new(2026,10,2),CreatedByUserId=f.Staff.Id,CreatedAt=at});
      foreach(var id in new[]{oldId,latestId})db.AuditAlternativeProcedures.Add(new AuditSphereOps.Domain.Audit.AuditAlternativeProcedure{Id=id,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,ConfirmationCaseId=caseId,Purpose="Synthetic alternative revision",EvidenceReferencesJson="[\"immutable-synthetic-evidence\"]",Conclusion=id==oldId?"Old evidence":"Current evidence",Status=id==oldId?"REVIEWED":"SUBMITTED",CreatedByUserId=f.Staff.Id,ReviewedByUserId=id==oldId?f.Reviewer.Id:null,ReviewedAt=id==oldId?at:null,CreatedAt=at});
      await db.SaveChangesAsync();}
    Assert.Equal(JsonValueKind.Null,(await Get(c,path+"/"+legacyId)).GetProperty("closure").ValueKind);
    Assert.Equal(HttpStatusCode.BadRequest,(await Action(c,csrf,path,caseId,"CLOSE",new{conclusion="Old alternative must not suffice"})).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Action(c,csrf,path,caseId,"REVIEW_ALTERNATIVE",new{evidenceId=oldId})).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await Action(c,csrf,path,caseId,"REVIEW_ALTERNATIVE",new{evidenceId=latestId})).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await Action(c,csrf,path,caseId,"CLOSE",new{conclusion="Current alternative independently reviewed"})).StatusCode);
    var detail=await Get(c,path+"/"+caseId);Assert.Equal("Current alternative independently reviewed",detail.GetProperty("closure").GetProperty("conclusion").GetString());
    await using(var db=new AuditSphereDbContext(pg.Options)){var proof=await db.AuditConfirmationClosures.SingleAsync();using var snapshot=JsonDocument.Parse(proof.EvidenceSnapshotJson);Assert.Equal(latestId,snapshot.RootElement.GetProperty("CurrentAlternative").GetProperty("Id").GetGuid());}
  }
  private static string Path(Guid e)=>"/api/ui/engagements/"+e+"/confirmations";
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AuditSphereOps.Domain.Security.AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");Assert.Equal(HttpStatusCode.OK,r.StatusCode);return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static Task<HttpResponseMessage> Post(HttpClient c,string path,string csrf,object body){var r=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(r);}
  private static async Task<JsonElement> Json(HttpResponseMessage r){using var j=JsonDocument.Parse(await r.Content.ReadAsStringAsync());return j.RootElement.Clone();}
  private static async Task<JsonElement> Get(HttpClient c,string path){using var r=await c.GetAsync(path);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return await Json(r);}
  private static async Task<HttpResponseMessage> Action(HttpClient c,string csrf,string path,Guid id,string action,object? fields=null){var d=await Get(c,path+"/"+id);var b=fields==null?new Dictionary<string,object?>():JsonSerializer.Deserialize<Dictionary<string,object?>>(JsonSerializer.Serialize(fields))!;b["action"]=action;b["reviewToken"]=d.GetProperty("reviewToken").GetString();b["reviewed"]=true;return await Post(c,path+"/"+id+"/actions",csrf,b);}
}
