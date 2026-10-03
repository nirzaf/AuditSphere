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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class AdjustmentPlanCommandApiTests
{
  [Fact]
  public async Task ReviewedCreationAndCalculationAreIdempotentExactAndImmutable()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PLAN-COMMAND-EXACT");
    var (f,s) = await Seed(pg); using var factory = Factory(pg,f.Staff); using var c = factory.CreateClient(); var csrf = await SignIn(c);
    var source = await Read(c,CreateUrl(s.SourceId));
    var request = Request("CREATE",source,[new(s.EligibleId,1)]);
    Assert.True((await Preview(c,csrf,CreateUrl(s.SourceId),request)).GetProperty("canProceed").GetBoolean());
    var replies = await Task.WhenAll(Save(c,csrf,CreateUrl(s.SourceId),request),Save(c,csrf,CreateUrl(s.SourceId),request));
    var retained = replies[0]; Assert.Equal(retained.GetProperty("id").GetGuid(),replies[1].GetProperty("id").GetGuid());
    var planId = retained.GetProperty("planId").GetGuid(); Assert.Equal(s.SourceId,retained.GetProperty("datasetId").GetGuid());
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,CreateUrl(s.SourceId),request with {Reason="Different intent",Reviewed=true})).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Single(await db.AdjustmentPlanActions.ToListAsync());
      Assert.False((await AdjustmentPlanService.FinalizeAsync(db,PbcSeed.Actor(f.Staff,"AccountingPreparer"),planId)).Succeeded);
      await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE adjustment_plan_actions SET reason='Changed' WHERE plan_id={planId}"));
      await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM adjustment_plan_actions WHERE plan_id={planId}"));
      await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE adjustment_plan_lines SET layer='AUDIT' WHERE plan_id={planId}"));
      await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO adjustment_plan_lines(id,plan_id,logical_journal_number,journal_revision,layer,reflection_state) VALUES({Guid.NewGuid()},{planId},'FORGED',1,'REPORTING','NOT_REFLECTED')"));
      await using var tx = await db.Database.BeginTransactionAsync();
      var forgedHash = new string('a',64);
      await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE adjustment_plans SET status='Finalized',result_hash={forgedHash} WHERE id={planId}");
      await Assert.ThrowsAsync<PostgresException>(()=>tx.CommitAsync());
    }
    var before = await Read(c,PlanUrl(planId)); var finalize = Request("FINALIZE",before,[]);
    var preview = await Preview(c,csrf,FinalUrl(planId),finalize);
    Assert.Equal("200.246912",preview.GetProperty("debits").GetString()); Assert.Equal("200.246912",preview.GetProperty("credits").GetString());
    Assert.Equal(1,preview.GetProperty("appliedCount").GetInt32());
    var saved = await Save(c,csrf,FinalUrl(planId),finalize);
    Assert.Equal(saved.GetProperty("id").GetGuid(),(await Save(c,csrf,FinalUrl(planId),finalize)).GetProperty("id").GetGuid());
    Assert.Equal(preview.GetProperty("resultHash").GetString(),saved.GetProperty("resultHash").GetString());
    var lookup = await Read(c,FinalUrl(planId)+$"/receipts/{finalize.RequestId}?requestHash="+saved.GetProperty("requestHash").GetString());
    Assert.True(lookup.GetProperty("found").GetBoolean());
    Assert.Equal(2,(await Read(c,PlanUrl(planId)+"/history")).GetProperty("items").GetArrayLength());
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(2,await verify.AdjustmentPlanActions.CountAsync()); Assert.Single(await verify.AdjustmentPlanLines.Where(l=>l.PlanId==planId).ToListAsync());
    Assert.Equal(100.123456m,await verify.TrialBalanceRows.Where(r=>r.DatasetId==s.SourceId && r.AccountCode=="1000").Select(r=>r.Amount).SingleAsync());
    Assert.Empty(await verify.FinancialPackages.ToListAsync());
    await Assert.ThrowsAsync<PostgresException>(()=>verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE adjustment_plans SET applied_debits=0 WHERE id={planId}"));
    await Assert.ThrowsAsync<PostgresException>(()=>verify.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM adjustment_plans WHERE id={planId}"));
    Assert.True((await SourceReconciliationService.ResolveAsync(verify,PbcSeed.Actor(f.Reviewer,"AccountingReviewer"),s.SourceId,"AJ-SYN",1,ReflectionStates.Reflected,"Changed synthetic exact bridge")).Succeeded);
    var stale = await Read(c,PlanUrl(planId)); Assert.Equal(1,stale.GetProperty("blockedCount").GetInt32()); Assert.Equal(saved.GetProperty("resultHash").GetString(),stale.GetProperty("resultHash").GetString());
    await Assert.ThrowsAsync<PostgresException>(() => verify.GetService<IMigrator>().MigrateAsync("20261002232511_NativeJournalManagementEvidence"));
    Assert.Equal(2, await verify.AdjustmentPlanActions.CountAsync());
    Assert.Contains("20261003004940_NativeAdjustmentPlanEvidence", await verify.Database.GetAppliedMigrationsAsync());
  }
  [Fact]
  public async Task EmptyNativeEvidenceMigrationCanRollbackAndReapplyWithoutChangingExistingSource()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PLAN-COMMAND-MIGRATION");
    var (f,s) = await Seed(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var migrator = db.GetService<IMigrator>();
    await migrator.MigrateAsync("20261002232511_NativeJournalManagementEvidence");
    Assert.DoesNotContain("20261003004940_NativeAdjustmentPlanEvidence", await db.Database.GetAppliedMigrationsAsync());
    Assert.Equal(100.123456m, await db.TrialBalanceRows.Where(r=>r.DatasetId==s.SourceId && r.Amount>0).SumAsync(r=>r.Amount));
    await migrator.MigrateAsync();
    Assert.Empty(await db.AdjustmentPlanActions.ToListAsync());
    Assert.Single(await db.AdjustmentPlans.ToListAsync());
    Assert.Equal(f.FirmId, await db.AdjustmentPlans.Select(p=>p.FirmId).SingleAsync());
  }

  [Fact]
  public async Task ForgedContextCsrfMissingAssentClientAndReviewerCannotCreatePlans()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PLAN-COMMAND-AUTH");var(f,s)=await Seed(pg);
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();var csrf=await SignIn(c);var v=await Read(c,CreateUrl(s.SourceId));
    var r=Request("CREATE",v,[new(s.EligibleId,1)]);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,"forged",CreateUrl(s.SourceId),r with{Reviewed=true})).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,CreateUrl(s.SourceId),r)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,CreateUrl(s.SourceId),r with{Action="FINALIZE",Reviewed=true})).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,CreateUrl(s.SourceId),r with{Journals=[new(s.EligibleId,1),new(s.EligibleId,1)],Reviewed=true})).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,csrf,CreateUrl(s.SourceId),r with{Journals=[new(Guid.NewGuid(),1)],Reviewed=true})).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(CreateUrl(Guid.NewGuid()))).StatusCode);
    using var clientFactory=Factory(pg,f.Client);using var client=clientFactory.CreateClient();var clientCsrf=await SignIn(client);
    Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(CreateUrl(s.SourceId))).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,clientCsrf,CreateUrl(s.SourceId),r with{Reviewed=true})).StatusCode);
    using var rf=Factory(pg,f.Reviewer);using var reviewer=rf.CreateClient();var reviewerCsrf=await SignIn(reviewer);
    var rv=await Read(reviewer,CreateUrl(s.SourceId));Assert.False(rv.GetProperty("source").GetProperty("canCreate").GetBoolean());
    var rr=Request("CREATE",rv,[new(s.EligibleId,1)]);
    Assert.False((await Preview(reviewer,reviewerCsrf,CreateUrl(s.SourceId),rr)).GetProperty("canProceed").GetBoolean());
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(reviewer,reviewerCsrf,CreateUrl(s.SourceId),rr with{Reviewed=true})).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options))await db.Users.Where(u=>u.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    Assert.Equal(HttpStatusCode.Unauthorized,(await Post(c,csrf,CreateUrl(s.SourceId),r with{Reviewed=true})).StatusCode);
    await using var verify=new AuditSphereDbContext(pg.Options);Assert.Empty(await verify.AdjustmentPlanActions.ToListAsync());Assert.Single(await verify.AdjustmentPlans.ToListAsync());
  }

  [Fact]
  public async Task ChangedOrMissingReflectionAndClosedPeriodNeverDefaultToZeroCalculation()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-PLAN-COMMAND-REFLECTION");var(f,s)=await Seed(pg);
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();var csrf=await SignIn(c);var source=await Read(c,CreateUrl(s.SourceId));var r=Request("CREATE",source,[new(s.EligibleId,1)]);
    await using(var db=new AuditSphereDbContext(pg.Options))Assert.True((await SourceReconciliationService.ResolveAsync(db,PbcSeed.Actor(f.Reviewer,"AccountingReviewer"),s.SourceId,"AJ-SYN",1,ReflectionStates.Reflected,"Replacement exact bridge")).Succeeded);
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,CreateUrl(s.SourceId),r with{Reviewed=true})).StatusCode);
    var current=await Read(c,CreateUrl(s.SourceId)); var unknown=current.GetProperty("journals").EnumerateArray().Single(j=>j.GetProperty("journalNumber").GetString()=="AJ-UNKNOWN");
    Assert.False(unknown.GetProperty("canInclude").GetBoolean());
    Assert.False((await Preview(c,csrf,CreateUrl(s.SourceId),Request("CREATE",current,[new(unknown.GetProperty("journalId").GetGuid(),1)]))).GetProperty("canProceed").GetBoolean());
    var plan=await Read(c,PlanUrl(s.PlanId)); var blocked=await Preview(c,csrf,FinalUrl(s.PlanId),Request("FINALIZE",plan,[]));
    Assert.False(blocked.GetProperty("canProceed").GetBoolean());Assert.Equal(JsonValueKind.Null,blocked.GetProperty("debits").ValueKind);
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      await db.JournalSourceReconciliations.Where(x=>x.BaseDatasetId==s.SourceId && x.LogicalJournalNumber=="AJ-SYN").ExecuteDeleteAsync();
      var d=await db.TrialBalanceDatasets.SingleAsync(x=>x.Id==s.SourceId);await db.ClientReportingPeriods.Where(p=>p.Id==d.PeriodId).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.Status,"CLOSED"));
    }
    Assert.False((await Read(c,CreateUrl(s.SourceId))).GetProperty("source").GetProperty("canCreate").GetBoolean());
    await using var verify=new AuditSphereDbContext(pg.Options);Assert.Empty(await verify.AdjustmentPlanActions.ToListAsync());
  }

  [Fact]
  public async Task ExplicitSourceOnlyPlanAndActorOwnedRecoveryRemainExact()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-PLAN-COMMAND-EMPTY");var(f,s)=await Seed(pg);
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();var csrf=await SignIn(c);var source=await Read(c,CreateUrl(s.SourceId));var r=Request("CREATE",source,[]);
    var saved=await Save(c,csrf,CreateUrl(s.SourceId),r);var planId=saved.GetProperty("planId").GetGuid();var hash=saved.GetProperty("requestHash").GetString();
    Assert.True((await Read(c,CreateUrl(s.SourceId)+$"/receipts/{r.RequestId}?requestHash="+hash)).GetProperty("found").GetBoolean());
    Assert.Equal(HttpStatusCode.Conflict,(await c.GetAsync(CreateUrl(s.SourceId)+$"/receipts/{r.RequestId}?requestHash="+new string('f',64))).StatusCode);
    using var rf=Factory(pg,f.Reviewer);using var reviewer=rf.CreateClient();await SignIn(reviewer);
    Assert.False((await Read(reviewer,CreateUrl(s.SourceId)+$"/receipts/{r.RequestId}?requestHash="+hash)).GetProperty("found").GetBoolean());
    var final=await Save(c,csrf,FinalUrl(planId),Request("FINALIZE",await Read(c,PlanUrl(planId)),[]));
    Assert.Equal("100.123456",final.GetProperty("debits").GetString());Assert.Equal(0,final.GetProperty("appliedCount").GetInt32());
    var actor=PbcSeed.Actor(f.Staff,"AccountingPreparer");PlanCommandRequest native;
    await using(var db=new AuditSphereDbContext(pg.Options)){var options=(await AdjustmentPlanWorkspace.GetCreationOptionsAsync(db,actor,s.SourceId)).Value!;native=new(Guid.NewGuid(),"CREATE",options.ReviewBasis,"Late epoch test","Synthetic local evidence",[],true);}
    var interceptor=new EpochAfterSave();
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options))
      Assert.False((await AdjustmentPlanWorkspace.ExecuteCommandAsync(db,actor,s.SourceId,native)).Succeeded);
    await using var verify=new AuditSphereDbContext(pg.Options);Assert.Equal(2,await verify.AdjustmentPlanActions.CountAsync());Assert.Equal(2,await verify.AdjustmentPlans.CountAsync());
  }
  private sealed class EpochAfterSave:SaveChangesInterceptor
  {
    private bool fired;
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken ct=default)
    {
      if(!fired){fired=true;await data.Context!.Database.ExecuteSqlRawAsync("UPDATE users SET session_epoch=session_epoch+1",ct);}return result;
    }
  }
  private static async Task<(PbcSeed.Fixture,AdjustmentPlanReviewSeed.Result)> Seed(OwnedPostgresDatabase pg){var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);return(f,await AdjustmentPlanReviewSeed.SeedAsync(db,f));}
  private static string CreateUrl(Guid id)=>$"/api/ui/datasets/{id}/adjustment-plans";
  private static string PlanUrl(Guid id)=>$"/api/ui/accounting/adjustment-plans/{id}";
  private static string FinalUrl(Guid id)=>PlanUrl(id)+"/finalization";
  private static PlanCommandRequest Request(string action,JsonElement view,IReadOnlyList<PlanJournalSelection> selection)=>new(Guid.NewGuid(),action,view.GetProperty("reviewBasis").GetString()!,"Synthetic exact plan rationale","Synthetic source and journal evidence",selection,false);
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static async Task<JsonElement> Read(HttpClient c,string url){using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();}
  private static Task<HttpResponseMessage> Post(HttpClient c,string csrf,string url,object body){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(r);}
  private static async Task<JsonElement> Preview(HttpClient c,string csrf,string url,PlanCommandRequest r){using var response=await Post(c,csrf,url+"/preview",r);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
  private static async Task<JsonElement> Save(HttpClient c,string csrf,string url,PlanCommandRequest r){using var response=await Post(c,csrf,url,r with{Reviewed=true});Assert.Equal(HttpStatusCode.OK,response.StatusCode);return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();}
}
