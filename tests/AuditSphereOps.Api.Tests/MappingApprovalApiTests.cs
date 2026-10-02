using System.Data.Common;
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

namespace AuditSphereOps.Api.Tests;

public sealed class MappingApprovalApiTests
{
  [Fact]
  public async Task FreshIndependentReviewSerializesConcurrentApprovalAndPreservesAllocations()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-MAPPING-REVIEW");
    var f=await PbcSeed.SeedAsync(pg); var seed=await Seed(pg,f);
    using var factory=Factory(pg,f.Reviewer); using var c=factory.CreateClient(new(){AllowAutoRedirect=false});
    var csrf=await SignIn(c); var url=Url(seed.MappingId); var plan=await Read(c,url);
    Assert.True(plan.GetProperty("canApprove").GetBoolean());
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,"wrong",url,plan)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,url,plan,false)).StatusCode);
    var nullInput=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create<object?>(null)};
    nullInput.Headers.Add("X-XSRF-TOKEN",csrf);
    Assert.Equal(HttpStatusCode.Conflict,(await c.SendAsync(nullInput)).StatusCode);
    var legacy=new HttpRequestMessage(HttpMethod.Post,$"/api/ui/accounting/mappings/{seed.MappingId}/approve") {Content=JsonContent.Create(new{version=1})};
    legacy.Headers.Add("X-XSRF-TOKEN",csrf);
    Assert.Equal(HttpStatusCode.Conflict,(await c.SendAsync(legacy)).StatusCode);
    var outcomes=await Task.WhenAll(Post(c,csrf,url,plan),Post(c,csrf,url,plan));
    Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.OK);
    Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.Conflict);
    var result=await Read(c,url);
    Assert.Equal("APPROVED",result.GetProperty("status").GetString());
    Assert.Equal(f.Reviewer.Id,result.GetProperty("reviewerId").GetGuid());
    Assert.False(result.GetProperty("canApprove").GetBoolean());
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,url,plan)).StatusCode);
    await using var db=new AuditSphereDbContext(pg.Options);
    Assert.Equal(2,await db.ClientSafetyStates.Where(x=>x.Id==f.ClientId).Select(x=>x.InputGeneration).SingleAsync());
    Assert.Single(await db.MappingVersions.ToListAsync());
    Assert.All(await db.MappingAllocations.ToListAsync(),x=>Assert.Equal(1m,x.Fraction));
    Assert.Empty(await db.SourceAcceptanceDecisions.ToListAsync());
  }

  [Fact]
  public async Task ChangedSourceGenerationTaxonomyChartAndClosedBookRefuseTheReviewedSnapshot()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-MAPPING-STALE");
    var f=await PbcSeed.SeedAsync(pg); var seed=await Seed(pg,f);
    using var factory=Factory(pg,f.Reviewer); using var c=factory.CreateClient();
    var csrf=await SignIn(c); var url=Url(seed.MappingId); var plan=await Read(c,url);
    plan=await Read(c,url);var chart=plan.GetProperty("chartVersionId").GetGuid();
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.MappingVersions.Where(x=>x.Id==seed.MappingId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ClientChartVersionId,(Guid?)null));
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,url,plan)).StatusCode);
    Assert.False((await Read(c,url)).GetProperty("canApprove").GetBoolean());
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.MappingVersions.Where(x=>x.Id==seed.MappingId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ClientChartVersionId,(Guid?)chart));
    plan=await Read(c,url);
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.MappingVersions.Where(x=>x.Id==seed.MappingId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.TaxonomyVersion,"unavailable-taxonomy"));
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,url,plan)).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.MappingVersions.Where(x=>x.Id==seed.MappingId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.TaxonomyVersion,"synthetic-map-v1"));
    plan=await Read(c,url);
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.ClientReportingBooks.Where(x=>x.Id==seed.BookId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"CLOSED"));
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,url,plan)).StatusCode);
    Assert.False((await Read(c,url)).GetProperty("canApprove").GetBoolean());
    await using(var db=new AuditSphereDbContext(pg.Options)) {
      await db.ClientReportingBooks.Where(x=>x.Id==seed.BookId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"DRAFT"));
      await db.ClientSafetyStates.Where(x=>x.Id==f.ClientId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.InputGeneration,x=>x.InputGeneration+1));
    }
    Assert.False((await Read(c,url)).GetProperty("canApprove").GetBoolean());
    await using var proof=new AuditSphereDbContext(pg.Options);
    Assert.Equal("DRAFT",await proof.MappingVersions.Where(x=>x.Id==seed.MappingId).Select(x=>x.Status).SingleAsync());
  }

  [Fact]
  public async Task ImmutableAllocationUpdatesAreRefusedAndNewInvalidSplitsStaleTheReview()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-MAPPING-SPLIT");
    var f=await PbcSeed.SeedAsync(pg);var seed=await Seed(pg,f);
    using var factory=Factory(pg,f.Reviewer);using var c=factory.CreateClient();var csrf=await SignIn(c);var url=Url(seed.MappingId);
    var plan=await Read(c,url);
    await using(var db=new AuditSphereDbContext(pg.Options)) {
      await Assert.ThrowsAsync<InvalidOperationException>(()=>db.MappingAllocations.Where(x=>x.MappingVersionId==seed.MappingId)
        .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Fraction,.5m)));
    }
    await using(var db=new AuditSphereDbContext(pg.Options)) {
      db.MappingAllocations.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,
        MappingVersionId=seed.MappingId,SourceAccountCode="1000",DestinationCode="REVENUE",StatementSection="INCOME",
        Fraction=.5m,Rationale="Synthetic appended incomplete split",CreatedAt=DateTimeOffset.UtcNow});
      await db.SaveChangesAsync();
    }
    Assert.Equal(HttpStatusCode.Conflict,(await Post(c,csrf,url,plan)).StatusCode);
    var invalid=await Read(c,url);Assert.False(invalid.GetProperty("canApprove").GetBoolean());
    Assert.Contains("100%",invalid.GetProperty("blocker").GetString());
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,url,invalid)).StatusCode);
    await using var proof=new AuditSphereDbContext(pg.Options);
    Assert.Equal("DRAFT",await proof.MappingVersions.Select(x=>x.Status).SingleAsync());
    Assert.Equal(1,await proof.ClientSafetyStates.Where(x=>x.Id==f.ClientId).Select(x=>x.InputGeneration).SingleAsync());
  }

  [Fact]
  public async Task UnsealedSourceCannotBecomeAnApprovedMapping()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-MAPPING-UNSEALED");
    var f=await PbcSeed.SeedAsync(pg);MappingApprovalSeed.Context seed;
    await using(var db=new AuditSphereDbContext(pg.Options)) seed=await MappingApprovalSeed.SeedAsync(db,f,seal:false);
    using var factory=Factory(pg,f.Reviewer);using var c=factory.CreateClient();var csrf=await SignIn(c);
    var plan=await Read(c,Url(seed.MappingId));Assert.False(plan.GetProperty("canApprove").GetBoolean());
    Assert.Contains("sealed",plan.GetProperty("blocker").GetString());
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url(seed.MappingId),plan)).StatusCode);
    await using var proof=new AuditSphereDbContext(pg.Options);
    Assert.Equal("DRAFT",await proof.MappingVersions.Select(x=>x.Status).SingleAsync());
    Assert.Equal(1,await proof.ClientSafetyStates.Where(x=>x.Id==f.ClientId).Select(x=>x.InputGeneration).SingleAsync());
  }

  [Fact]
  public async Task SelfReviewHiddenScopeNullAssentAndRevocationFailClosed()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-MAPPING-SCOPE");
    var f=await PbcSeed.SeedAsync(pg); var other=await PbcSeed.SeedAsync(pg);
    var seed=await Seed(pg,f); var foreign=await Seed(pg,other);
    using var factory=Factory(pg,f.Staff); using var c=factory.CreateClient(); var csrf=await SignIn(c);
    var plan=await Read(c,Url(seed.MappingId)); Assert.False(plan.GetProperty("canApprove").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,csrf,Url(seed.MappingId),plan)).StatusCode);
    foreach(var id in new[]{foreign.MappingId,Guid.NewGuid()}) {
      Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(Url(id))).StatusCode);
      Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,csrf,Url(id),plan)).StatusCode);
    }
    using var clientFactory=Factory(pg,f.Client); using var client=clientFactory.CreateClient(); await SignIn(client);
    Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(Url(seed.MappingId))).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options))
      await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(Url(seed.MappingId))).StatusCode);
  }

  [Fact]
  public async Task LateEpochChangeRollsBackApprovalAndInputGeneration()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-MAPPING-LATE");
    var f=await PbcSeed.SeedAsync(pg); var seed=await Seed(pg,f);
    var actor=PbcSeed.Actor(f.Reviewer,"AccountingReviewer");
    var interceptor=new LateEpoch(async()=>{
      await using var db=new AuditSphereDbContext(pg.Options);
      await db.Users.Where(x=>x.Id==f.Reviewer.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));
    });
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options)) {
      var plan=await MappingApprovalWorkspace.GetAsync(db,actor,seed.MappingId);
      Assert.True(plan.Succeeded,plan.Message);
      var result=await MappingApprovalWorkspace.ApproveAsync(db,actor,seed.MappingId,plan.Value!.Revision,true);
      Assert.Equal(ErrorCodes.GenerationStale,result.ErrorCode);
    }
    await using var proof=new AuditSphereDbContext(pg.Options);
    var retained=await proof.MappingVersions.SingleAsync();
    Assert.Equal("DRAFT",retained.Status); Assert.Null(retained.ApprovedByUserId); Assert.Null(retained.ApprovedAt);
    Assert.Equal(1,await proof.ClientSafetyStates.Where(x=>x.Id==f.ClientId).Select(x=>x.InputGeneration).SingleAsync());
  }

  private sealed class LateEpoch(Func<Task> change):DbCommandInterceptor {
    private bool done;
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData e,DbDataReader reader,CancellationToken ct=default) {
      if(!done && command.CommandText.Contains("UPDATE mapping_versions",StringComparison.Ordinal)) {done=true;await change();}
      return reader;
    }
  }
  private static async Task<MappingApprovalSeed.Context> Seed(OwnedPostgresDatabase pg,PbcSeed.Fixture f) {
    await using var db=new AuditSphereDbContext(pg.Options); return await MappingApprovalSeed.SeedAsync(db,f);
  }
  private static string Url(Guid id)=>$"/api/ui/accounting/mappings/{id}/approval";
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?> {
    ["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,
    ["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c) {
    await c.GetAsync("/auth/sign-in"); using var r=await c.GetAsync("/api/ui/session");
    return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }
  private static async Task<JsonElement> Read(HttpClient c,string url) {
    using var response=await c.GetAsync(url); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return doc.RootElement.Clone();
  }
  private static Task<HttpResponseMessage> Post(HttpClient c,string csrf,string url,JsonElement plan,bool reviewed=true) {
    var message=new HttpRequestMessage(HttpMethod.Post,url) {Content=JsonContent.Create(new{revision=plan.GetProperty("revision").GetString(),reviewed})};
    message.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(message);
  }
}
