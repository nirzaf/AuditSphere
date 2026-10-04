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
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class AccountingPreparationCreationApiTests
{
  [Fact]
  public async Task ReviewedReconciliationAndSpecialistRevisionAreScopedIdempotentAndImmutable()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-ANALYSIS-CREATION");
    var seed=await ReconciliationReviewSeed.SeedAsync(pg);var f=seed.Fixture;
    using var factory=Factory(pg,f.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);
    await using var db=new AuditSphereDbContext(pg.Options);
    var source=await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x=>x.Id==seed.SourceId);var periodId=source.PeriodId!.Value;
    var priorSchedule=await AccountingAnalysisService.RecordSpecialistScheduleAsync(db,PbcSeed.Actor(f.Staff,"AccountingPreparer"),
      new(f.ClientId,f.EngagementId,periodId,"ASSETS","SYNTHETIC-ASSETS-V1",100m,20m,0m,10m,0m,0m,0m,0m,0m,0m,0m,110m,
        new('c',64),"SYNTHETIC-ASSET-EVIDENCE-V1",DepreciationMethod:"STRAIGHT_LINE",UsefulLifeMonths:120));
    Assert.True(priorSchedule.Succeeded,priorSchedule.Message);

    var reconUrl=$"/api/ui/engagements/{f.EngagementId}/reconciliation-preparation";
    var reconState=await Read(client,$"{reconUrl}/state?sourceKind=TRIAL_BALANCE&sourceId={seed.SourceId}");
    Assert.True(reconState.GetProperty("canPrepare").GetBoolean());
    var recon=new ReconciliationPreparationRequest(Guid.NewGuid(),reconState.GetProperty("reviewBasis").GetString()!,
      new("TRIAL_BALANCE",seed.SourceId,periodId,source.BookId,"CASH",["1000"],new DateOnly(2026,12,31)),
      "Prepare synthetic reconciliation revision.","SYNTHETIC-RECONCILIATION-REF");
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,"invalid",reconUrl+"/preview",recon)).StatusCode);
    using var previewResponse=await Post(client,csrf,reconUrl+"/preview",recon);Assert.Equal(HttpStatusCode.OK,previewResponse.StatusCode);
    using var previewJson=JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());var preview=previewJson.RootElement.GetProperty("value");
    Assert.Equal("100.123456",preview.GetProperty("sourceTotal").GetString());Assert.Equal("100.123456",preview.GetProperty("glTotal").GetString());
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(client,csrf,reconUrl,recon)).StatusCode);
    var receipt=await Execute(client,csrf,reconUrl,recon);var replay=await Execute(client,csrf,reconUrl,recon);
    Assert.Equal(receipt.GetProperty("id").GetGuid(),replay.GetProperty("id").GetGuid());
    Assert.Equal(2,receipt.GetProperty("result").GetProperty("revision").GetInt64());
    Assert.Equal(seed.ReconciliationId,receipt.GetProperty("result").GetProperty("supersedesId").GetGuid());
    Assert.Equal(HttpStatusCode.Conflict,(await Post(client,csrf,reconUrl,recon with{Reviewed=true,Fields=recon.Fields with{Area="PAYABLES"}})).StatusCode);
    var readReceipt=await Read(client,$"{reconUrl}/receipts/{recon.RequestId}?requestHash={receipt.GetProperty("requestHash").GetString()}");
    Assert.True(readReceipt.GetProperty("found").GetBoolean());
    var storedRecon=await db.AccountingReconciliationPreparations.SingleAsync(x=>x.RequestId==recon.RequestId);
    Assert.Contains("1000",storedRecon.InputJson);Assert.Equal(f.Staff.Id,storedRecon.ActorId);
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE accounting_reconciliation_preparations SET reason='changed' WHERE id={storedRecon.Id}"));
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM accounting_reconciliation_preparations WHERE id={storedRecon.Id}"));

    var specialistUrl=$"/api/ui/engagements/{f.EngagementId}/specialist-preparation";
    var specialistState=await Read(client,$"{specialistUrl}/state?periodId={periodId}&area=ASSETS");
    Assert.True(specialistState.GetProperty("canPrepare").GetBoolean());
    var schedule=new SpecialistScheduleRequest(f.ClientId,f.EngagementId,periodId,"ASSETS","SYNTHETIC-ASSETS-V2",
      100.123456m,20m,0m,10m,0m,0m,0m,0m,0m,0m,0m,110m,new('d',64),"SYNTHETIC-ASSET-EVIDENCE-V2",
      DepreciationMethod:"STRAIGHT_LINE",UsefulLifeMonths:120);
    var specialist=new SpecialistPreparationRequest(Guid.NewGuid(),specialistState.GetProperty("reviewBasis").GetString()!,
      schedule,specialistState.GetProperty("currentScheduleId").GetGuid(),"Prepare synthetic asset schedule revision.");
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,"invalid",specialistUrl+"/preview",specialist)).StatusCode);
    using var specialistPreviewResponse=await Post(client,csrf,specialistUrl+"/preview",specialist);Assert.Equal(HttpStatusCode.OK,specialistPreviewResponse.StatusCode);
    using var specialistPreviewJson=JsonDocument.Parse(await specialistPreviewResponse.Content.ReadAsStringAsync());
    Assert.Equal("110.123456",specialistPreviewJson.RootElement.GetProperty("value").GetProperty("calculatedAmount").GetString());
    var scheduleReceipt=await ExecuteSpecialist(client,csrf,specialistUrl,specialist);
    var scheduleReplay=await ExecuteSpecialist(client,csrf,specialistUrl,specialist);
    Assert.Equal(scheduleReceipt.GetProperty("id").GetGuid(),scheduleReplay.GetProperty("id").GetGuid());
    Assert.Equal(2,scheduleReceipt.GetProperty("result").GetProperty("revision").GetInt64());
    Assert.Equal(HttpStatusCode.Conflict,(await Post(client,csrf,specialistUrl,specialist with{Reviewed=true,Reason="Changed intent"})).StatusCode);
    var specialistLookup=await Read(client,$"{specialistUrl}/receipts/{specialist.RequestId}?requestHash={scheduleReceipt.GetProperty("requestHash").GetString()}");
    Assert.True(specialistLookup.GetProperty("found").GetBoolean());
    var storedSchedule=await db.SpecialistSchedulePreparations.SingleAsync(x=>x.RequestId==specialist.RequestId);
    Assert.Contains("STRAIGHT_LINE",storedSchedule.InputJson);
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE specialist_schedule_preparations SET reason='changed' WHERE id={storedSchedule.Id}"));
    await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM specialist_schedule_preparations WHERE id={storedSchedule.Id}"));
    Assert.Single(await db.AccountingReconciliationPreparations.Where(x=>x.RequestId==recon.RequestId).ToListAsync());
    Assert.Single(await db.SpecialistSchedulePreparations.Where(x=>x.RequestId==specialist.RequestId).ToListAsync());
  }

  [Fact]
  public async Task WrongScopeClosedPeriodAndRevokedSessionCannotPreviewOrCreate()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-ANALYSIS-CREATION-SCOPE");var seed=await ReconciliationReviewSeed.SeedAsync(pg);var f=seed.Fixture;
    using var factory=Factory(pg,f.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);
    using var portalFactory=Factory(pg,f.Client);using var portal=portalFactory.CreateClient();await SignIn(portal);
    var reconUrl=$"/api/ui/engagements/{f.EngagementId}/reconciliation-preparation";
    Assert.Equal(HttpStatusCode.Forbidden,(await portal.GetAsync(reconUrl)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/ui/engagements/{Guid.NewGuid()}/reconciliation-preparation")).StatusCode);
    await using var db=new AuditSphereDbContext(pg.Options);var dataset=await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x=>x.Id==seed.SourceId);var periodId=dataset.PeriodId!.Value;
    var state=await Read(client,$"{reconUrl}/state?sourceKind=TRIAL_BALANCE&sourceId={seed.SourceId}");
    var request=new ReconciliationPreparationRequest(Guid.NewGuid(),state.GetProperty("reviewBasis").GetString()!,
      new("TRIAL_BALANCE",seed.SourceId,periodId,dataset.BookId,"CASH",["1000"],new DateOnly(2026,12,31)),"Synthetic reason","SYNTHETIC-REF");
    await db.ClientReportingPeriods.Where(x=>x.Id==periodId).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Status,"CLOSED"));
    Assert.Equal(HttpStatusCode.Conflict,(await Post(client,csrf,reconUrl+"/preview",request)).StatusCode);
    Assert.Empty(await db.AccountingReconciliationPreparations.ToListAsync());
    await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(reconUrl)).StatusCode);
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{
    ["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,
    ["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");Assert.Equal(HttpStatusCode.OK,r.StatusCode);
    return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static async Task<JsonElement> Read(HttpClient c,string url){using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();}
  private static Task<HttpResponseMessage> Post(HttpClient c,string token,string url,object body){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",token);return c.SendAsync(r);}
  private static async Task<JsonElement> Execute(HttpClient c,string csrf,string url,ReconciliationPreparationRequest request){using var r=await Post(c,csrf,url,request with{Reviewed=true});Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
  private static async Task<JsonElement> ExecuteSpecialist(HttpClient c,string csrf,string url,SpecialistPreparationRequest request){using var r=await Post(c,csrf,url,request with{Reviewed=true});Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
}
