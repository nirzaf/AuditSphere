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
public sealed class WorkpaperCommandReceiptApiTests
{
  [Fact]
  public async Task PreparedSaveHasNoMutationAndMinimalCheckpointRecoversOriginalAfterSupersededSave()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-WORKPAPER-SAVE");var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    using var factory=Factory(pg,w.Fixture.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);var root=$"/api/ui/audit/workpapers/{w.WorkpaperId}/commands";
    var state=await Read(client,root+"/state");Assert.False(state.TryGetProperty("stateJson",out _));var s=state.GetProperty("workpaper");
    var r=new WorkpaperCommandRequest(Guid.NewGuid(),new(WorkpaperCommandActions.SaveDraft,Save:new("0",s.GetProperty("revision").GetString()!,s.GetProperty("inputGeneration").GetString()!,s.GetProperty("policyGeneration").GetString()!,"  عمل\r\n\tمنفذ  "," استنتاج ")));
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,"invalid",root+"/prepare-save",r)).StatusCode);
    using var preparedResponse=await Post(client,csrf,root+"/prepare-save",r);Assert.Equal(HttpStatusCode.OK,preparedResponse.StatusCode);using var pj=JsonDocument.Parse(await preparedResponse.Content.ReadAsStringAsync());var p=pj.RootElement.GetProperty("value");
    Assert.Equal(r.RequestId,p.GetProperty("requestId").GetGuid());Assert.Equal(64,p.GetProperty("requestHash").GetString()!.Length);Assert.Empty(await db.WorkpaperDrafts.ToListAsync());Assert.Empty(await db.WorkpaperCommandEvents.ToListAsync());Assert.Empty(await db.WorkpaperCommandReceipts.ToListAsync());
    r=r with{Fields=JsonSerializer.Deserialize<WorkpaperCommandFields>(p.GetProperty("canonicalInput").GetRawText(),new JsonSerializerOptions(JsonSerializerDefaults.Web))!,ExpectedRequestHash=p.GetProperty("requestHash").GetString()};
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(client,csrf,root+"/actions",r with{ExpectedRequestHash=null})).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict,(await Post(client,csrf,root+"/actions",r with{Fields=r.Fields with{Save=r.Fields.Save! with{Conclusion="Changed"}}})).StatusCode);
    using var response=await Post(client,csrf,root+"/actions",r);Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var j=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var receipt=j.RootElement.GetProperty("value");Assert.Equal("عمل\r\n\tمنفذ",receipt.GetProperty("commandEvent").GetProperty("after").GetProperty("draft").GetProperty("workPerformed").GetString());
    var later=await WorkpaperCommandReceiptSeed.SaveAsync(db,w,"Later work","Later conclusion");Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,later)).Succeeded);
    // A new browser/context needs only id/hash, never editor text, to recover the original save.
    using var freshFactory=Factory(pg,w.Fixture.Staff);using var fresh=freshFactory.CreateClient();await SignIn(fresh);
    var lookup=await Read(fresh,root+$"/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}");Assert.True(lookup.GetProperty("found").GetBoolean());Assert.Equal(receipt.GetRawText(),lookup.GetProperty("receipt").GetRawText());
    var detail=await Read(fresh,$"/api/ui/audit/workpapers/{w.WorkpaperId}");Assert.Equal(JsonValueKind.String,detail.GetProperty("revision").ValueKind);Assert.Equal(JsonValueKind.String,detail.GetProperty("draft").GetProperty("draftRevision").ValueKind);Assert.Equal("2",detail.GetProperty("draft").GetProperty("draftRevision").GetString());
  }
  [Theory,InlineData(WorkpaperCommandActions.DiscardDraft),InlineData(WorkpaperCommandActions.Submit)]
  public async Task ReviewedFinalActionsRetainOriginalBeforeAfterAndRecoveryAfterCurrentHold(string action)
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-WORKPAPER-FINAL");var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var save=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,save)).Succeeded);
    using var factory=Factory(pg,w.Fixture.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);var root=$"/api/ui/audit/workpapers/{w.WorkpaperId}/commands";
    var r=await WorkpaperCommandReceiptSeed.ReviewAsync(db,w,action);using var previewResponse=await Post(client,csrf,root+"/preview",r);Assert.Equal(HttpStatusCode.OK,previewResponse.StatusCode);using var pj=JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());var p=pj.RootElement.GetProperty("value");
    Assert.Equal(r.ExpectedRequestHash,p.GetProperty("requestHash").GetString());Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("summary").GetString()));
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(client,csrf,root+"/actions",r with{Reviewed=false})).StatusCode);
    using var response=await Post(client,csrf,root+"/actions",r);Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var j=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var receipt=j.RootElement.GetProperty("value");
    Assert.Equal(p.GetProperty("before").GetRawText(),receipt.GetProperty("commandEvent").GetProperty("before").GetRawText());Assert.Equal(action==WorkpaperCommandActions.Submit?"CONSUMED":"DISCARDED",receipt.GetProperty("commandEvent").GetProperty("after").GetProperty("draft").GetProperty("lifecycle").GetString());
    if(action==WorkpaperCommandActions.Submit){var detail=await Read(client,$"/api/ui/audit/workpapers/{w.WorkpaperId}");Assert.Equal(JsonValueKind.String,detail.GetProperty("submissions")[0].GetProperty("revision").ValueKind);}
    await db.Engagements.Where(x=>x.Id==w.Fixture.EngagementId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ProfessionalWorkBlocked,true));
    using var replay=await Post(client,csrf,root+"/actions",r);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);using var rj=JsonDocument.Parse(await replay.Content.ReadAsStringAsync());Assert.Equal(receipt.GetRawText(),rj.RootElement.GetProperty("value").GetRawText());
    Assert.Equal(receipt.GetRawText(),(await Read(client,root+$"/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}")).GetProperty("receipt").GetRawText());
  }
  [Fact]
  public async Task LegacyNumericSaveShapeAndSaveIdsRemainCompatibleWithPreparedExactSave()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-WORKPAPER-COMPAT");var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);using var factory=Factory(pg,w.Fixture.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);var root=$"/api/ui/audit/workpapers/{w.WorkpaperId}";
    using var response=await Post(client,csrf,root+"/draft",new{ExpectedDraftRevision=0L,BaseWorkpaperRevision=1L,BaseInputGeneration=1L,BasePolicyGeneration=1L,SaveId=r.RequestId,WorkPerformed=r.Fields.Save!.WorkPerformed,Conclusion=r.Fields.Save.Conclusion});Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(r.RequestId,json.RootElement.GetProperty("value").GetProperty("lastSaveId").GetGuid());Assert.Equal(JsonValueKind.Number,json.RootElement.GetProperty("value").GetProperty("draftRevision").ValueKind);
    using var replay=await Post(client,csrf,root+"/commands/actions",r);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);Assert.Single(await db.WorkpaperDrafts.ToListAsync());Assert.Single(await db.WorkpaperCommandReceipts.ToListAsync());
  }
  [Fact]
  public async Task LargeGenerationsAndEpochRemainExactInDetailStateIntentAndReceipt()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-WORKPAPER-LONGS");var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;await using var db=new AuditSphereDbContext(pg.Options);const long value=9007199254740993;
    await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,value));f.Staff.SessionEpoch=value;
    await db.Workpapers.Where(x=>x.Id==w.WorkpaperId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Revision,value));await db.ClientSafetyStates.Where(x=>x.Id==f.ClientId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.InputGeneration,value));await db.FirmSafetyStates.Where(x=>x.Id==f.FirmId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.PolicyGeneration,value));
    using var factory=Factory(pg,f.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);var root=$"/api/ui/audit/workpapers/{w.WorkpaperId}";
    var detail=await Read(client,root);Assert.Equal(value.ToString(),detail.GetProperty("revision").GetString());Assert.Equal(value.ToString(),detail.GetProperty("draft").GetProperty("baseInputGeneration").GetString());Assert.Equal(value.ToString(),detail.GetProperty("draft").GetProperty("basePolicyGeneration").GetString());
    var r=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);using var response=await Post(client,csrf,root+"/commands/actions",r);Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var j=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(value.ToString(),j.RootElement.GetProperty("value").GetProperty("actorEpoch").GetString());
  }
  [Fact]
  public async Task ForeignGuessedOtherActorAndRevokedRecoveryAreOpaque()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-WORKPAPER-SCOPE");var w=await WorkpaperCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);var r=await WorkpaperCommandReceiptSeed.SaveAsync(db,w);Assert.True((await WorkpaperCommandWorkspace.ExecuteAsync(db,w.Staff,w.WorkpaperId,r)).Succeeded);
    using var factory=Factory(pg,w.Fixture.Staff);using var client=factory.CreateClient();await SignIn(client);var foreign=await WorkpaperCommandReceiptSeed.SeedAsync(pg);var tail=$"/commands/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}";
    using var hidden=await client.GetAsync($"/api/ui/audit/workpapers/{foreign.WorkpaperId}"+tail);using var guessed=await client.GetAsync($"/api/ui/audit/workpapers/{Guid.NewGuid()}"+tail);Assert.Equal(HttpStatusCode.Forbidden,hidden.StatusCode);Assert.Equal(hidden.StatusCode,guessed.StatusCode);Assert.Equal(await hidden.Content.ReadAsStringAsync(),await guessed.Content.ReadAsStringAsync());
    using var otherFactory=Factory(pg,w.Fixture.Admin);using var other=otherFactory.CreateClient();await SignIn(other);Assert.False((await Read(other,$"/api/ui/audit/workpapers/{w.WorkpaperId}"+tail)).GetProperty("found").GetBoolean());
    await db.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/ui/audit/workpapers/{w.WorkpaperId}"+tail)).StatusCode);
  }
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");Assert.Equal(HttpStatusCode.OK,r.StatusCode);return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static Task<HttpResponseMessage> Post(HttpClient c,string token,string url,object body){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",token);return c.SendAsync(r);}
  private static async Task<JsonElement> Read(HttpClient c,string url){using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);using var j=JsonDocument.Parse(await r.Content.ReadAsStringAsync());return j.RootElement.Clone();}
}
