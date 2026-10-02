using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace AuditSphereOps.Api.Tests;

public sealed class SourceAcceptanceApiTests
{
  [Fact]
  public async Task ReviewedAcceptance_CsrfAssentConcurrencyAndReceipts_AdvanceGenerationOnce()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-SOURCE-ACCEPTANCE"); var f = await PbcSeed.SeedAsync(pg); var id = await Seed(pg, f);
    using var factory = Factory(pg, f.Admin); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c);
    var review = await Read(c, id); Assert.True(review.GetProperty("canAccept").GetBoolean()); var revision = review.GetProperty("revision").GetString()!;
    Assert.Equal(HttpStatusCode.Forbidden, (await Accept(c, "wrong", id, revision)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Accept(c, csrf, id, revision, reviewed: false)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Accept(c, csrf, id, new string('b',64))).StatusCode);
    var results = await Task.WhenAll(Accept(c, csrf, id, revision), Accept(c, csrf, id, revision));
    Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
    var observed = await Read(c, id); Assert.False(observed.GetProperty("canAccept").GetBoolean()); Assert.Equal(2, observed.GetProperty("inputGeneration").GetInt64());
    Assert.Equal(id, observed.GetProperty("receipt").GetProperty("trialBalanceDatasetId").GetGuid()); Assert.Equal(f.Admin.Id, observed.GetProperty("receipt").GetProperty("acceptedByUserId").GetGuid()); Assert.Equal("Independent source evidence", observed.GetProperty("receipt").GetProperty("evidenceReference").GetString());
    Assert.Equal(observed.GetProperty("receipt").GetProperty("id").GetGuid(), observed.GetProperty("selected").GetProperty("decisionId").GetGuid());
    await using var db = new AuditSphereDbContext(pg.Options); Assert.Single(await db.SourceAcceptanceDecisions.ToListAsync());
  }
  [Fact]
  public async Task SelfReview_StaleGeneration_ClosedPeriod_PendingSourceAndCrossScopeAreRefused()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-SOURCE-GATES"); var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg); var id = await Seed(pg, f); var fid = await Seed(pg, foreign);
    using var factory = Factory(pg, f.Admin); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); var csrf = await SignIn(c); var r = await Read(c,id); var revision = r.GetProperty("revision").GetString()!;
    await using (var db = new AuditSphereDbContext(pg.Options)) await db.ClientSafetyStates.Where(x => x.Id == f.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration+1));
    Assert.Equal(HttpStatusCode.Conflict, (await Accept(c,csrf,id,revision)).StatusCode);
    var self = await Seed(pg, f, f.Admin.Id); var selfReview = await Read(c,self); Assert.False(selfReview.GetProperty("canAccept").GetBoolean()); Assert.Contains("other than", selfReview.GetProperty("blocker").GetString()); Assert.Equal(HttpStatusCode.Forbidden,(await Accept(c,csrf,self,selfReview.GetProperty("revision").GetString()!)).StatusCode);
    var pending = await Seed(pg,f,accepted:false); Assert.False((await Read(c,pending)).GetProperty("canAccept").GetBoolean());
    foreach(var denied in new[]{fid,Guid.NewGuid()}) { Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(Path(denied))).StatusCode); Assert.Equal(HttpStatusCode.Forbidden,(await Accept(c,csrf,denied,revision)).StatusCode); }
    var sibling = Guid.NewGuid(); await using(var db = new AuditSphereDbContext(pg.Options)) { db.Engagements.Add(new() {Id=sibling,FirmId=f.FirmId,PracticeClientId=f.ClientId,Status="Active",CreatedAt=DateTimeOffset.UtcNow}); await db.SaveChangesAsync(); }
    var sid = await Seed(pg,f with { EngagementId=sibling }); using var sf=Factory(pg,f.Staff); using var staff=sf.CreateClient(new(){AllowAutoRedirect=false}); await SignIn(staff); Assert.Equal(HttpStatusCode.Forbidden,(await staff.GetAsync(Path(sid))).StatusCode);
    using var cf=Factory(pg,f.Client); using var client=cf.CreateClient(new(){AllowAutoRedirect=false}); await SignIn(client); Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(Path(id))).StatusCode);
    await using(var db = new AuditSphereDbContext(pg.Options)) { var source=await db.TrialBalanceDatasets.SingleAsync(x=>x.Id==id); await db.ClientReportingPeriods.Where(x=>x.Id==source.PeriodId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"CLOSED")); }
    Assert.False((await Read(c,id)).GetProperty("canAccept").GetBoolean()); await using var proof=new AuditSphereDbContext(pg.Options); Assert.Empty(await proof.SourceAcceptanceDecisions.ToListAsync());
  }
  [Fact]
  public async Task CoreIndependence_FinalRevocationRollBackDecisionAndGeneration()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-SOURCE-CORE"); var f=await PbcSeed.SeedAsync(pg); var self=await Seed(pg,f,f.Admin.Id); var id=await Seed(pg,f); var actor=PbcSeed.Actor(f.Admin,"Administrator");
    await using(var db=new AuditSphereDbContext(pg.Options)) { var denied=await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db,actor,new(f.ClientId,f.EngagementId,"TB",self,null,"evidence")); Assert.Equal(ErrorCodes.ScopeDenied,denied.ErrorCode); }
    var afterWrite=new AfterDecision(async()=>{await using var db=new AuditSphereDbContext(pg.Options); await db.Users.Where(x=>x.Id==f.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));});
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(afterWrite).Options)) { var result=await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db,actor,new(f.ClientId,f.EngagementId,"TB",id,null,"evidence")); Assert.Equal(ErrorCodes.GenerationStale,result.ErrorCode); }
    await using var proof=new AuditSphereDbContext(pg.Options); Assert.Empty(await proof.SourceAcceptanceDecisions.ToListAsync()); Assert.Equal(1,await proof.ClientSafetyStates.Where(x=>x.Id==f.ClientId).Select(x=>x.InputGeneration).SingleAsync());
  }
  [Fact]
  public async Task InvalidDigest_ProfessionalHold_AndGlImporterIndependenceFailClosed()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-SOURCE-CROSS-KIND"); var f=await PbcSeed.SeedAsync(pg); var bad=await Seed(pg,f,digest:new string('z',64)); var valid=await Seed(pg,f); var actor=PbcSeed.Actor(f.Admin,"Administrator");
    await using(var db=new AuditSphereDbContext(pg.Options)) { var denied=await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db,actor,new(f.ClientId,f.EngagementId,"TB",bad,null,"evidence")); Assert.Equal(ErrorCodes.GateBlocked,denied.ErrorCode); }
    var hold=Guid.NewGuid(); await using(var db=new AuditSphereDbContext(pg.Options)) { db.EngagementHolds.Add(new(){Id=hold,FirmId=f.FirmId,EngagementId=f.EngagementId,HoldKind="Independence",Reason="Synthetic hold",CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync(); }
    await using(var db=new AuditSphereDbContext(pg.Options)) { var denied=await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db,actor,new(f.ClientId,f.EngagementId,"TB",valid,null,"evidence"));Assert.Equal(ErrorCodes.GateBlocked,denied.ErrorCode); }
    Guid period;var batch=Guid.NewGuid(); await using(var db=new AuditSphereDbContext(pg.Options)) {await db.EngagementHolds.Where(x=>x.Id==hold).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Released,true).SetProperty(x=>x.ReleasedAt,DateTimeOffset.UtcNow));period=(await db.TrialBalanceDatasets.SingleAsync(x=>x.Id==valid)).PeriodId!.Value;var hash=batch.ToString("N")+batch.ToString("N");db.SourceImportBatches.Add(new(){Id=batch,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,PeriodId=period,SourceKind="GL",ProfileVersion="gl-v1",ParserVersion="parser-v1",RawFileSha256Hex=hash,NormalizedDatasetDigest=hash,LegalEntityKey="SYN",Currency="QAR",Status="SEALED",ReceiptReference="Synthetic batch",CreatedByUserId=f.Admin.Id,CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync(); }
    await using(var db=new AuditSphereDbContext(pg.Options)) { var denied=await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db,actor,new(f.ClientId,f.EngagementId,"GL",null,batch,"evidence"));Assert.Equal(ErrorCodes.ScopeDenied,denied.ErrorCode);var mismatched=await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db,actor,new(f.ClientId,f.EngagementId,"GL",valid,null,"evidence"));Assert.Equal(ErrorCodes.Accounting.ImportRejected,mismatched.ErrorCode); }
    await using var proof=new AuditSphereDbContext(pg.Options);Assert.Empty(await proof.SourceAcceptanceDecisions.ToListAsync());Assert.Equal(1,await proof.ClientSafetyStates.Where(x=>x.Id==f.ClientId).Select(x=>x.InputGeneration).SingleAsync());
  }
  private sealed class AfterDecision(Func<Task> change):DbCommandInterceptor
  {
    private bool done;
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken=default) { if(!done&&command.CommandText.Contains("INSERT INTO source_acceptance_decisions",StringComparison.Ordinal)){done=true; await change();} return result; }
  }
  private static string Path(Guid id)=>$"/api/ui/datasets/{id}/source/acceptance";
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");Assert.Equal(HttpStatusCode.OK,r.StatusCode);return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static async Task<JsonElement> Read(HttpClient c,Guid id){using var response=await c.GetAsync(Path(id));Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.Clone();}
  private static Task<HttpResponseMessage> Accept(HttpClient c,string csrf,Guid id,string revision,bool reviewed=true){var request=new HttpRequestMessage(HttpMethod.Post,Path(id)){Content=JsonContent.Create(new{revision,reviewed,evidenceReference="Independent source evidence"})};request.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(request);}
  private static async Task<Guid> Seed(OwnedPostgresDatabase pg,PbcSeed.Fixture f,Guid? importer=null,bool accepted=true,string? digest=null)
  {
    await using var db=new AuditSphereDbContext(pg.Options); var period=Guid.NewGuid(); db.ClientReportingPeriods.Add(new(){Id=period,FirmId=f.FirmId,ClientId=f.ClientId,PeriodCode=period.ToString("N"),Currency="QAR",Basis="IFRS",Status="ACTIVE",StartDate=new(2026,1,1),EndDate=new(2026,12,31),CreatedByUserId=f.Admin.Id,CreatedAt=DateTimeOffset.UtcNow}); var id=Guid.NewGuid(); var hash=id.ToString("N")+id.ToString("N");db.TrialBalanceDatasets.Add(new(){Id=id,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,PeriodId=period,Currency="QAR",LegalEntityKey="SYN",SourceKind="Raw",Balanced=true,ValidationStatus=accepted?"Accepted":"Pending",RawFileSha256Hex=hash,NormalizedDatasetDigest=digest??hash,Sha256Hex=hash,ImportedByUserId=importer??f.Staff.Id,ImportedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();await db.TrialBalanceDatasets.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ImportState,"SEALED"));return id;
  }
}
