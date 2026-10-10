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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class AnalyticalPreparationApiTests
{
  [Fact]
  public async Task ReviewedAnalyticalPreparationIsScopedIdempotentAndKeepsIndependentReviewWritable()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-ANALYTICAL-PREPARE");
    var seed=await AccountingAnalysisReviewSeed.SeedAsync(pg,false,true);var f=seed.Fixture;
    using var factory=Factory(pg,f.Staff);using var client=factory.CreateClient(new(){AllowAutoRedirect=false});var csrf=await SignIn(client);
    var engagement=$"/api/ui/engagements/{f.EngagementId}/analytical-preparation";
    using var contextResponse=await client.GetAsync(engagement);Assert.Equal(HttpStatusCode.OK,contextResponse.StatusCode);
    using var contextJson=JsonDocument.Parse(await contextResponse.Content.ReadAsStringAsync());var period=contextJson.RootElement.GetProperty("periods").EnumerateArray().First(x=>x.GetProperty("canPrepare").GetBoolean());
    var periodId=period.GetProperty("id").GetGuid();var state=await Read(client,$"{engagement}/state?periodId={periodId}");Assert.True(state.GetProperty("canPrepare").GetBoolean());
    var fields=new AnalyticalPreparationFields(periodId,null,"Revenue","Annual movement","120.123456","100.000000",null,
      "prior-year total","synthetic.analytical.v1","Synthetic source rationale.","Seasonality reviewed.");
    var request=new AnalyticalPreparationRequest(Guid.NewGuid(),state.GetProperty("reviewBasis").GetString()!,fields,
      "Prepare synthetic analytical review.","SYNTHETIC-ANALYTICAL-REF");
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,"wrong",engagement+"/preview",request)).StatusCode);
    using var previewResponse=await Post(client,csrf,engagement+"/preview",request);Assert.Equal(HttpStatusCode.OK,previewResponse.StatusCode);
    using var previewJson=JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());var preview=previewJson.RootElement.GetProperty("value");
    Assert.Equal("0.201235",preview.GetProperty("varianceRatio").GetString());Assert.True(preview.GetProperty("canProceed").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(client,csrf,engagement,request)).StatusCode);
    var receipt=await Execute(client,csrf,engagement,request);
    var replay=await Execute(client,csrf,engagement,request);Assert.Equal(receipt.GetProperty("id").GetGuid(),replay.GetProperty("id").GetGuid());
    Assert.Equal(HttpStatusCode.Conflict,(await Post(client,csrf,engagement,request with{Reviewed=true,Fields=fields with{CurrentAmount="121.000000"}})).StatusCode);
    var lookup=await Read(client,$"{engagement}/receipts/{request.RequestId}?requestHash={receipt.GetProperty("requestHash").GetString()}");
    Assert.True(lookup.GetProperty("found").GetBoolean());

    await using var db=new AuditSphereDbContext(pg.Options);var stored=await db.AccountingAnalysisPreparations.SingleAsync(x=>x.RequestId==request.RequestId);
    Assert.Equal(f.Staff.Id,stored.ActorId);Assert.Contains("prior-year total",stored.InputJson);Assert.Contains("SYNTHETIC-ANALYTICAL-REF",stored.EvidenceReference);
    var evidence=await db.AnalyticalReviews.SingleAsync(x=>x.Id==stored.EvidenceId);Assert.Equal("DRAFT",evidence.Status);Assert.Null(evidence.ReviewedAt);
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE accounting_analysis_preparations SET reason='changed' WHERE id={stored.Id}"));
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM accounting_analysis_preparations WHERE id={stored.Id}"));
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE analytical_reviews SET current_amount=999 WHERE id={stored.EvidenceId}"));
    var linked=await AccountingAnalysisService.LinkAccountingEvidenceToProcedureAsync(db,PbcSeed.Actor(f.Staff,"AccountingPreparer"),
      new("ANALYTICAL",stored.EvidenceId,seed.ProcedureResultIds[0]));Assert.True(linked.Succeeded,linked.Message);
    var reviewer=PbcSeed.Actor(f.Reviewer,"AccountingReviewer");
    var reviewed=await AccountingAnalysisService.ReviewAccountingEvidenceAsync(db,reviewer,new("ANALYTICAL",stored.EvidenceId,"APPROVED",
      Disposition:"Synthetic independent review.",Conclusion:"Synthetic human conclusion.",CorroborationReference:"SYNTHETIC-REVIEW-REF"));
    Assert.True(reviewed.Succeeded,reviewed.Message);var after=await db.AnalyticalReviews.SingleAsync(x=>x.Id==stored.EvidenceId);
    Assert.Equal(f.Reviewer.Id,after.ReviewedByUserId);Assert.Equal("Synthetic human conclusion.",after.ReviewConclusion);

    var zeroFields=fields with{Area="Cash",Measure="No prior data",PriorAmount="0.000000",CurrentAmount="5.000000"};
    var zeroRequest=request with{RequestId=Guid.NewGuid(),Fields=zeroFields};
    using var zeroPreviewResponse=await Post(client,csrf,engagement+"/preview",zeroRequest);Assert.Equal(HttpStatusCode.OK,zeroPreviewResponse.StatusCode);
    using var zeroPreviewJson=JsonDocument.Parse(await zeroPreviewResponse.Content.ReadAsStringAsync());var zeroPreview=zeroPreviewJson.RootElement.GetProperty("value");
    Assert.Null(zeroPreview.GetProperty("varianceRatio").GetString());Assert.Equal("INSUFFICIENT_DATA",zeroPreview.GetProperty("resultStatus").GetString());
    var zeroReceipt=await Execute(client,csrf,engagement,zeroRequest);
    var zeroEvidence=await db.AnalyticalReviews.SingleAsync(x=>x.Id==zeroReceipt.GetProperty("evidenceId").GetGuid());
    Assert.Null(zeroEvidence.Ratio);Assert.Equal("INSUFFICIENT_DATA",zeroEvidence.Status);
    Assert.Equal(2,await db.AccountingAnalysisPreparations.CountAsync());
    await PermanentFileFreezeMigrationAssertions.AssertDowngradeBlockedAsync(db,"_NativeAnalyticalReviewPreparation");
  }

  [Fact]
  public async Task PeriodScopeAndClosedOrRevokedSessionsFailClosed()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-ANALYTICAL-SCOPE");var seed=await AccountingAnalysisReviewSeed.SeedAsync(pg,false,false);var f=seed.Fixture;
    using var factory=Factory(pg,f.Staff);using var client=factory.CreateClient(new(){AllowAutoRedirect=false});var csrf=await SignIn(client);
    var endpoint=$"/api/ui/engagements/{f.EngagementId}/analytical-preparation";
    Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/ui/engagements/{Guid.NewGuid()}/analytical-preparation")).StatusCode);
    await using var db=new AuditSphereDbContext(pg.Options);var period=await db.ClientReportingPeriods.FirstAsync(x=>x.ClientId==f.ClientId&&x.Status=="ACTIVE");
    var state=await Read(client,$"{endpoint}/state?periodId={period.Id}");var request=new AnalyticalPreparationRequest(Guid.NewGuid(),state.GetProperty("reviewBasis").GetString()!,
      new(period.Id,null,"Cash","Balance","10.000000","5.000000",null,"prior balance","synthetic.v1","Synthetic explanation.",""),"Synthetic reason.","SYNTHETIC-REF");
    await db.ClientReportingPeriods.Where(x=>x.Id==period.Id).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Status,"CLOSED"));
    using var closed=await Post(client,csrf,endpoint+"/preview",request);Assert.Equal(HttpStatusCode.Conflict,closed.StatusCode);
    Assert.Equal(HttpStatusCode.Conflict,(await Post(client,csrf,endpoint,request with{Reviewed=true})).StatusCode);
    Assert.Empty(await db.AccountingAnalysisPreparations.ToListAsync());
    await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(endpoint)).StatusCode);
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{
    ["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,
    ["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");Assert.Equal(HttpStatusCode.OK,r.StatusCode);
    return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static async Task<JsonElement> Read(HttpClient c,string path){using var r=await c.GetAsync(path);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();}
  private static Task<HttpResponseMessage> Post(HttpClient c,string token,string path,object body){var r=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",token);return c.SendAsync(r);}
  private static async Task<JsonElement> Execute(HttpClient c,string csrf,string path,AnalyticalPreparationRequest request){using var r=await Post(c,csrf,path,request with{Reviewed=true});
    Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
}
