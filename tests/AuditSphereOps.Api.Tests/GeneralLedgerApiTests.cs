using System.Data.Common;
using System.Net;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Api.Tests;
public sealed class GeneralLedgerApiTests
{
  [Fact]
  public async Task ScopedCatalogue_ServerFilters_Pages_ExactAmounts_AndCompleteJournal()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-GL-READ"); var f = await PbcSeed.SeedAsync(pg); Guid id;
    await using(var db = new AuditSphereDbContext(pg.Options)) id = await GeneralLedgerWorkspaceSeed.SeedAsync(db,f);
    using var factory = Factory(pg,f.Admin); using var c = factory.CreateClient(); await c.GetAsync("/auth/sign-in");
    var catalogue = await Read(c,Path(f.EngagementId)); Assert.Single(catalogue.GetProperty("items").EnumerateArray());
    var source = await Read(c,Path(f.EngagementId,id)); var rows = source.GetProperty("rows");
    Assert.Equal(150,rows.GetProperty("totalCount").GetInt32()); Assert.Equal(100,rows.GetProperty("items").GetArrayLength());
    Assert.Equal("100.123456",rows.GetProperty("items")[0].GetProperty("debit").GetString()); Assert.True(rows.GetProperty("balanced").GetBoolean());
    Assert.Equal(JsonValueKind.Null,source.GetProperty("context").GetProperty("selected").ValueKind);
    var second = await Read(c,Path(f.EngagementId,id)+"?page=2"); Assert.Equal(50,second.GetProperty("rows").GetProperty("items").GetArrayLength());
    var filtered = await Read(c,Path(f.EngagementId,id)+"?account=100&from=2026-06-01&to=2026-06-30&journal=J-000&counterparty=SYN-PARTNER");
    Assert.Equal(1,filtered.GetProperty("rows").GetProperty("totalCount").GetInt32()); Assert.False(filtered.GetProperty("rows").GetProperty("balanced").GetBoolean());
    var journal = await Read(c,Path(f.EngagementId,id)+"/journal?journal=J-000"); Assert.Equal(2,journal.GetProperty("journal").GetProperty("lineCount").GetInt32());
    Assert.True(journal.GetProperty("journal").GetProperty("balanced").GetBoolean());
    await using(var db = new AuditSphereDbContext(pg.Options)) for(var n=0;n<20;n++) await GeneralLedgerWorkspaceSeed.SeedAsync(db,f,journals:0);
    var page1=await Read(c,Path(f.EngagementId)); var page2=await Read(c,Path(f.EngagementId)+"?page=2");
    Assert.Equal(21,page1.GetProperty("totalCount").GetInt32());Assert.Equal(20,page1.GetProperty("items").GetArrayLength());Assert.Single(page2.GetProperty("items").EnumerateArray());
  }
  [Fact]
  public async Task NoHiddenCounts_Client_ExpiredScope_SiblingAndCrossFirmAreRefused()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-GL-SCOPE");var f=await PbcSeed.SeedAsync(pg);var foreign=await PbcSeed.SeedAsync(pg);Guid id;Guid foreignId;var sibling=Guid.NewGuid();
    await using(var db=new AuditSphereDbContext(pg.Options)){id=await GeneralLedgerWorkspaceSeed.SeedAsync(db,f);foreignId=await GeneralLedgerWorkspaceSeed.SeedAsync(db,foreign);db.Engagements.Add(new(){Id=sibling,FirmId=f.FirmId,PracticeClientId=f.ClientId,CreatedAt=DateTimeOffset.UtcNow});var grant=PbcSeed.Grant(f.FirmId,f.Staff,"AccountingPreparer",f.ClientId,f.EngagementId);grant.GrantedAt=DateTimeOffset.UtcNow.AddHours(-2);db.RoleGrants.Add(grant);await db.SaveChangesAsync();}
    using var af=Factory(pg,f.Admin);using var admin=af.CreateClient();await admin.GetAsync("/auth/sign-in");
    foreach(var url in new[]{Path(foreign.EngagementId),Path(foreign.EngagementId,foreignId),Path(sibling,id),Path(f.EngagementId,Guid.NewGuid())}){
      var r=await admin.GetAsync(url);Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);Assert.DoesNotContain("totalCount",await r.Content.ReadAsStringAsync());}
    foreach(var user in new[]{f.Client,f.Staff}){using var factory=Factory(pg,user);using var c=factory.CreateClient();await c.GetAsync("/auth/sign-in");
      if(user.Id==f.Staff.Id){Assert.Equal(HttpStatusCode.OK,(await c.GetAsync(Path(f.EngagementId))).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(Path(sibling))).StatusCode);
        await using var db=new AuditSphereDbContext(pg.Options);await db.RoleGrants.Where(x=>x.UserId==f.Staff.Id&&x.Role=="AccountingPreparer").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ExpiresAt,DateTimeOffset.UtcNow.AddMinutes(-1)));
        var stale=await c.GetAsync(Path(f.EngagementId));Assert.Equal(HttpStatusCode.Unauthorized,stale.StatusCode);Assert.DoesNotContain("totalCount",await stale.Content.ReadAsStringAsync());await c.GetAsync("/auth/sign-in");}
      var denied=await c.GetAsync(Path(f.EngagementId));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.DoesNotContain("totalCount",await denied.Content.ReadAsStringAsync());}
  }
  [Fact]
  public async Task BoundedInvalidRequests_OversizedJournalAndNonGlSourcesFailClosed()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-GL-BOUNDS");var f=await PbcSeed.SeedAsync(pg);Guid id;Guid large;Guid nonGl;Guid loading;
    await using(var db=new AuditSphereDbContext(pg.Options)){id=await GeneralLedgerWorkspaceSeed.SeedAsync(db,f);large=await GeneralLedgerWorkspaceSeed.SeedAsync(db,f,1,true);nonGl=await GeneralLedgerWorkspaceSeed.SeedAsync(db,f,1,kind:"SCHEDULE");loading=await GeneralLedgerWorkspaceSeed.SeedAsync(db,f,1,seal:false);}
    using var factory=Factory(pg,f.Admin);using var c=factory.CreateClient();await c.GetAsync("/auth/sign-in");
    foreach(var suffix in new[]{"?page=0","?page=2147483647","?from=2026-10-01&to=2026-01-01","?account="+new string('a',101),"?journal="+new string('a',201),"?counterparty="+new string('a',201)}) Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(Path(f.EngagementId,id)+suffix)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(Path(f.EngagementId)+"?page=2147483647")).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(Path(f.EngagementId,large)+"/journal?journal=J-000")).StatusCode);
    foreach(var hidden in new[]{nonGl,loading}) Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(Path(f.EngagementId,hidden))).StatusCode);
    var cat=await Read(c,Path(f.EngagementId));Assert.Equal(2,cat.GetProperty("totalCount").GetInt32());
    await using var proof=new AuditSphereDbContext(pg.Options);Assert.Empty(await proof.SourceAcceptanceDecisions.ToListAsync());Assert.Empty(await proof.GeneralLedgerCompletenessBridges.ToListAsync());
  }
  [Fact]
  public async Task LateRevocationAndSourceChangeReturnNoRows()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-GL-FENCES");var f=await PbcSeed.SeedAsync(pg);Guid id;await using(var db=new AuditSphereDbContext(pg.Options))id=await GeneralLedgerWorkspaceSeed.SeedAsync(db,f);
    var actor=PbcSeed.Actor(f.Admin,"Administrator");
    var changed=new BeforeRows(async()=>{await using var db=new AuditSphereDbContext(pg.Options);await db.SourceImportBatches.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"REJECTED"));});
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(changed).Options)){var r=await GeneralLedgerQuery.GetLinesAsync(db,actor,id);Assert.Equal(ErrorCodes.StaleRevision,r.ErrorCode);Assert.Null(r.Value);}
    await using(var db=new AuditSphereDbContext(pg.Options))await db.SourceImportBatches.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"SEALED"));
    var revoked=new BeforeRows(async()=>{await using var db=new AuditSphereDbContext(pg.Options);await db.Users.Where(x=>x.Id==f.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));});
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(revoked).Options)){var r=await GeneralLedgerWorkspace.JournalAsync(db,actor,f.EngagementId,id,"J-000");Assert.Equal(ErrorCodes.GenerationStale,r.ErrorCode);Assert.Null(r.Value);}
  }
  private sealed class BeforeRows(Func<Task> change):DbCommandInterceptor
  {private bool done;public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default){if(!done&&command.CommandText.Contains("general_ledger_lines",StringComparison.Ordinal)){done=true;await change();}return result;}}
  private static string Path(Guid engagement,Guid? batch=null)=>$"/api/ui/engagements/{engagement}/general-ledger"+(batch.HasValue?$"/{batch}":"");
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<JsonElement> Read(HttpClient c,string path){using var r=await c.GetAsync(path);Assert.Equal(HttpStatusCode.OK,r.StatusCode);using var json=JsonDocument.Parse(await r.Content.ReadAsStringAsync());return json.RootElement.Clone();}
}
