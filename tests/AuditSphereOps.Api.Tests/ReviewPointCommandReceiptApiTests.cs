using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class ReviewPointCommandReceiptApiTests
{
  public static TheoryData<string> Actions=>new(){ReviewPointCommandActions.Clear,ReviewPointCommandActions.Reopen};
  [Theory,MemberData(nameof(Actions))]
  public async Task ClearAndReopenPublishTypedExactBeforeAfterAndRecoverOriginalReceipt(string action)
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-REVIEW-POINT-RECEIPTS");var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;await using var db=new AuditSphereDbContext(pg.Options);
    if(action==ReviewPointCommandActions.Reopen)Assert.True((await AuditPlanningService.SetReviewPointDispositionAsync(db,w.Staff,w.ReviewPointId,true,1)).Succeeded);
    using var factory=Factory(pg,f.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);var root=$"/api/ui/reviews/{w.ReviewPointId}/commands";
    var state=await Read(client,root+"/state");Assert.False(state.TryGetProperty("stateJson",out _));Assert.True(state.GetProperty("canChangeDisposition").GetBoolean());
    var point=state.GetProperty("reviewPoint");Assert.Equal(JsonValueKind.String,point.GetProperty("dispositionRevision").ValueKind);Assert.Equal("9007199254740993",point.GetProperty("targetRevision").GetString());
    Assert.Contains("مراجعة",point.GetProperty("comment").GetString());
    var r=new ReviewPointCommandRequest(Guid.NewGuid(),point.GetProperty("dispositionRevision").GetString()!,state.GetProperty("reviewBasis").GetString()!,new(action));
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,"invalid",root+"/preview",r)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(client,csrf,root+"/actions",r)).StatusCode);
    using var previewResponse=await Post(client,csrf,root+"/preview",r);Assert.Equal(HttpStatusCode.OK,previewResponse.StatusCode);using var pj=JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());var p=pj.RootElement.GetProperty("value");
    Assert.True(p.GetProperty("canProceed").GetBoolean());Assert.Equal(action,p.GetProperty("canonicalInput").GetProperty("action").GetString());Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("summary").GetString()));
    r=r with{Reviewed=true,ExpectedRequestHash=p.GetProperty("requestHash").GetString()};
    using var response=await Post(client,csrf,root+"/actions",r);Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var receipt=json.RootElement.GetProperty("value");
    Assert.Equal(JsonValueKind.String,receipt.GetProperty("actorEpoch").ValueKind);var e=receipt.GetProperty("dispositionEvent");Assert.Equal(JsonValueKind.String,e.GetProperty("actorEpoch").ValueKind);
    Assert.Equal(p.GetProperty("before").GetRawText(),e.GetProperty("before").GetRawText());Assert.Equal(action==ReviewPointCommandActions.Clear,e.GetProperty("after").GetProperty("cleared").GetBoolean());
    var later=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,action==ReviewPointCommandActions.Clear?ReviewPointCommandActions.Reopen:ReviewPointCommandActions.Clear);
    Assert.True((await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,later)).Succeeded);await ReviewPointCommandReceiptSeed.FreezeAsync(db,w);
    using var replay=await Post(client,csrf,root+"/actions",r);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);using var rj=JsonDocument.Parse(await replay.Content.ReadAsStringAsync());Assert.Equal(receipt.GetRawText(),rj.RootElement.GetProperty("value").GetRawText());
    var lookup=await Read(client,root+$"/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}");Assert.True(lookup.GetProperty("found").GetBoolean());Assert.Equal(receipt.GetRawText(),lookup.GetProperty("receipt").GetRawText());
    Assert.Equal(HttpStatusCode.Conflict,(await Post(client,csrf,root+"/actions",r with{ReviewBasis=new('b',64)})).StatusCode);
    var legacy=await Read(client,$"/api/ui/reviews/{w.ReviewPointId}");Assert.False(legacy.GetProperty("canChangeDisposition").GetBoolean());Assert.Equal(JsonValueKind.String,legacy.GetProperty("dispositionRevision").ValueKind);
    Assert.Equal(2,await db.ReviewPointCommandReceipts.CountAsync());
  }

  [Fact]
  public async Task CompatibilityHttpWritesRequireExplicitRevisionAndRefuseStaleReversal()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-REVIEW-POINT-FENCE");var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    using var factory=Factory(pg,w.Fixture.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);var root=$"/api/ui/reviews/{w.ReviewPointId}";
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(client,csrf,root+"/disposition",new{cleared=true})).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await Post(client,csrf,root+"/disposition",new ReviewPointDispositionInput(true,"1"))).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict,(await Post(client,csrf,root+"/disposition",new ReviewPointDispositionInput(false,"1"))).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await Post(client,csrf,root+"/disposition",new ReviewPointDispositionInput(true,"2"))).StatusCode);
    Assert.Single(await db.ReviewPointDispositionEvents.ToListAsync());Assert.Empty(await db.ReviewPointCommandReceipts.ToListAsync());
    var record=await Read(client,root);Assert.Equal("2",record.GetProperty("dispositionRevision").GetString());Assert.True(record.GetProperty("canChangeDisposition").GetBoolean());
    Assert.Equal(HttpStatusCode.OK,(await Post(client,csrf,root+"/disposition",new ReviewPointDispositionInput(false,"2"))).StatusCode);
    Assert.Equal(2,await db.ReviewPointDispositionEvents.CountAsync());
  }
  [Theory,InlineData(false),InlineData(true)]
  public async Task AdministrativeDispositionPreservesHoldAuthorityButHonorsFrozenOriginal(bool freeze)
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-REVIEW-POINT-GATES");var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;await using var db=new AuditSphereDbContext(pg.Options);
    if(freeze)await ReviewPointCommandReceiptSeed.FreezeAsync(db,w);
    else
    {
      db.EngagementHolds.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,EngagementId=f.EngagementId,HoldKind="Independence",Reason="SYNTHETIC hold",CreatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();
      await db.Engagements.Where(x=>x.Id==f.EngagementId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ProfessionalWorkBlocked,true));
    }
    using var factory=Factory(pg,f.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);var root=$"/api/ui/reviews/{w.ReviewPointId}/commands";
    var state=await Read(client,root+"/state");Assert.Equal(!freeze,state.GetProperty("canChangeDisposition").GetBoolean());
    var r=new ReviewPointCommandRequest(Guid.NewGuid(),state.GetProperty("reviewPoint").GetProperty("dispositionRevision").GetString()!,state.GetProperty("reviewBasis").GetString()!,new(ReviewPointCommandActions.Clear));
    using var previewResponse=await Post(client,csrf,root+"/preview",r);Assert.Equal(HttpStatusCode.OK,previewResponse.StatusCode);using var pj=JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());var p=pj.RootElement.GetProperty("value");Assert.Equal(!freeze,p.GetProperty("canProceed").GetBoolean());
    using var response=await Post(client,csrf,root+"/actions",r with{Reviewed=true,ExpectedRequestHash=p.GetProperty("requestHash").GetString()});Assert.Equal(!freeze,response.IsSuccessStatusCode);
    Assert.Equal(freeze?0:1,await db.ReviewPointDispositionEvents.CountAsync());
  }
  [Fact]
  public async Task UnknownForeignOtherActorAndRevokedRecoveryRemainOpaque()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-REVIEW-POINT-SCOPE");var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);Assert.True((await ReviewPointCommandWorkspace.ExecuteAsync(db,w.Staff,w.ReviewPointId,r)).Succeeded);
    using var factory=Factory(pg,w.Fixture.Staff);using var client=factory.CreateClient();await SignIn(client);var tail=$"/commands/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}";
    var foreign=await ReviewPointCommandReceiptSeed.SeedAsync(pg);using var hidden=await client.GetAsync($"/api/ui/reviews/{foreign.ReviewPointId}"+tail);using var guessed=await client.GetAsync($"/api/ui/reviews/{Guid.NewGuid()}"+tail);
    Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode);Assert.Equal(hidden.StatusCode,guessed.StatusCode);Assert.Equal(await hidden.Content.ReadAsStringAsync(),await guessed.Content.ReadAsStringAsync());
    var root=$"/api/ui/reviews/{w.ReviewPointId}";var absent=await Read(client,root+$"/commands/receipts/{Guid.NewGuid()}?requestHash={new string('b',64)}");Assert.False(absent.GetProperty("found").GetBoolean());Assert.Equal(JsonValueKind.Null,absent.GetProperty("receipt").ValueKind);
    using var otherFactory=Factory(pg,w.Fixture.Admin);using var other=otherFactory.CreateClient();await SignIn(other);Assert.False((await Read(other,root+tail)).GetProperty("found").GetBoolean());
    await db.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(root+tail)).StatusCode);
  }
  [Fact]
  public async Task EpochsBeyondJavaScriptRangeStayExactStringsAndPriorLifetimeCannotRecover()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-REVIEW-POINT-EPOCH");var w=await ReviewPointCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;await using var db=new AuditSphereDbContext(pg.Options);
    const long epoch=9007199254740993;await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,epoch));f.Staff.SessionEpoch=epoch;
    using var factory=Factory(pg,f.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);var root=$"/api/ui/reviews/{w.ReviewPointId}/commands";
    var r=await ReviewPointCommandReceiptSeed.ReviewAsync(db,w.Staff,w.ReviewPointId,ReviewPointCommandActions.Clear);
    using var response=await Post(client,csrf,root+"/actions",r);Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var receipt=json.RootElement.GetProperty("value");
    Assert.Equal("9007199254740993",receipt.GetProperty("actorEpoch").GetString());Assert.Equal("9007199254740993",receipt.GetProperty("dispositionEvent").GetProperty("actorEpoch").GetString());
    await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,epoch+1));using var recovery=await client.GetAsync(root+$"/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}");Assert.False(recovery.IsSuccessStatusCode);
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");Assert.Equal(HttpStatusCode.OK,r.StatusCode);return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static Task<HttpResponseMessage> Post(HttpClient c,string token,string url,object body){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",token);return c.SendAsync(r);}
  private static async Task<JsonElement> Read(HttpClient c,string url){using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);using var j=JsonDocument.Parse(await r.Content.ReadAsStringAsync());return j.RootElement.Clone();}
}
