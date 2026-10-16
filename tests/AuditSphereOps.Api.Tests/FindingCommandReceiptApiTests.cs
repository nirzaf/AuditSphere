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

public sealed class FindingCommandReceiptApiTests
{
  public static TheoryData<string> Actions=>new(){FindingCommandActions.RecordResponse,FindingCommandActions.Designate,FindingCommandActions.Undesignate};
  [Theory,MemberData(nameof(Actions))]
  public async Task ThreeActionsExposeTypedExactPreviewImmutableEventAndIdenticalRecovery(string action)
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-FINDING-RECEIPTS");var w=await FindingCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;
    await using var db=new AuditSphereDbContext(pg.Options);
    if(action==FindingCommandActions.Undesignate)Assert.True((await AuditPlanningService.DesignateManagementLetterFindingAsync(db,w.Manager,new(w.FindingId,true,"Prior recommendation"))).Succeeded);
    using var factory=Factory(pg,f.Reviewer);using var client=factory.CreateClient();var csrf=await SignIn(client);var root=$"/api/ui/findings/{w.FindingId}/commands";
    var state=await Read(client,root+"/state");Assert.False(state.TryGetProperty("stateJson",out _));Assert.True(state.GetProperty("canDesignate").GetBoolean());
    var before=state.GetProperty("finding");Assert.Equal(JsonValueKind.String,before.GetProperty("revision").ValueKind);Assert.Equal("123.456789",before.GetProperty("monetaryAmount").GetString());
    var r=new FindingCommandRequest(Guid.NewGuid(),before.GetProperty("revision").GetString()!,state.GetProperty("reviewBasis").GetString()!,FindingCommandReceiptSeed.Fields(action));
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,"invalid",root+"/preview",r)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,(await Post(client,csrf,root+"/actions",r)).StatusCode);
    using var previewResponse=await Post(client,csrf,root+"/preview",r);Assert.Equal(HttpStatusCode.OK,previewResponse.StatusCode);
    using var previewJson=JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());var preview=previewJson.RootElement.GetProperty("value");
    Assert.True(preview.GetProperty("canProceed").GetBoolean());Assert.Equal(action,preview.GetProperty("canonicalInput").GetProperty("action").GetString());
    Assert.Equal(before.GetProperty("findingId").GetGuid(),preview.GetProperty("before").GetProperty("findingId").GetGuid());
    Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("summary").GetString()));
    r=r with{Reviewed=true,ExpectedRequestHash=preview.GetProperty("requestHash").GetString(),Fields=JsonSerializer.Deserialize<FindingCommandFields>(preview.GetProperty("canonicalInput").GetRawText(),new JsonSerializerOptions(JsonSerializerDefaults.Web))!};
    using var response=await Post(client,csrf,root+"/actions",r);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var receipt=json.RootElement.GetProperty("value");
    Assert.Equal(JsonValueKind.String,receipt.GetProperty("actorEpoch").ValueKind);Assert.Equal(JsonValueKind.String,receipt.GetProperty("amendment").GetProperty("actorEpoch").ValueKind);
    Assert.Equal(r.ExpectedRevision,receipt.GetProperty("amendment").GetProperty("before").GetProperty("revision").GetString());
    Assert.Equal(receipt.GetProperty("preview").GetProperty("before").GetRawText(), receipt.GetProperty("amendment").GetProperty("before").GetRawText());
    using var replay=await Post(client,csrf,root+"/actions",r);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
    using var replayJson=JsonDocument.Parse(await replay.Content.ReadAsStringAsync());Assert.Equal(receipt.GetRawText(),replayJson.RootElement.GetProperty("value").GetRawText());
    var lookup=await Read(client,root+$"/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}");Assert.True(lookup.GetProperty("found").GetBoolean());Assert.Equal(receipt.GetRawText(),lookup.GetProperty("receipt").GetRawText());
    Assert.Equal(HttpStatusCode.Conflict,(await Post(client,csrf,root+"/actions",r with{ReviewBasis=new('b',64)})).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync(root+$"/receipts/{r.RequestId}?requestHash={new string('c',64)}")).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/api/ui/findings/{w.FindingId}")).StatusCode);
    Assert.Single(await db.FindingCommandReceipts.ToListAsync());
  }

  [Fact]
  public async Task HttpMixedScopeManagerDeniedWhileResponseAndLegacyRoutesRemainUsable()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-FINDING-MIXED-SCOPE");var w=await FindingCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;
    await using var db=new AuditSphereDbContext(pg.Options);var sibling=Guid.NewGuid();db.Engagements.Add(new(){Id=sibling,FirmId=f.FirmId,PracticeClientId=f.ClientId,Status="Active",ProfessionalWorkBlocked=false,CreatedAt=DateTimeOffset.UtcNow});
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"Manager",f.ClientId,sibling));await db.SaveChangesAsync();
    using var factory=Factory(pg,f.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);var root=$"/api/ui/findings/{w.FindingId}";
    var state=await Read(client,root+"/commands/state");Assert.True(state.GetProperty("canRecordResponse").GetBoolean());Assert.False(state.GetProperty("canDesignate").GetBoolean());
    var record=await Read(client,root);Assert.True(record.GetProperty("canRecordResponse").GetBoolean());Assert.False(record.GetProperty("canDesignate").GetBoolean());
    var r=new FindingCommandRequest(Guid.NewGuid(),state.GetProperty("finding").GetProperty("revision").GetString()!,state.GetProperty("reviewBasis").GetString()!,FindingCommandReceiptSeed.Fields(FindingCommandActions.Designate));
    using var previewResponse=await Post(client,csrf,root+"/commands/preview",r);Assert.Equal(HttpStatusCode.OK,previewResponse.StatusCode);using var json=JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());var p=json.RootElement.GetProperty("value");Assert.False(p.GetProperty("canProceed").GetBoolean());
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,csrf,root+"/commands/actions",r with{Reviewed=true,ExpectedRequestHash=p.GetProperty("requestHash").GetString()})).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,csrf,root+"/management-letter-designation",new RecordManagementLetterDesignationRequest(w.FindingId,true,"Recommendation"))).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await Post(client,csrf,root+"/response",new RecordFindingResponseRequest(w.FindingId,"Legacy response",false))).StatusCode);
    Assert.Empty(await db.FindingCommandReceipts.ToListAsync());Assert.Single(await db.FindingAmendments.ToListAsync());
  }

  [Fact]
  public async Task UnknownForeignOtherActorAndRevokedReceiptRecoveryRemainOpaque()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-FINDING-OPAQUE");var w=await FindingCommandReceiptSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);
    var r=await FindingCommandReceiptSeed.ReviewAsync(db,w.Staff,w.FindingId,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));Assert.True((await FindingCommandWorkspace.ExecuteAsync(db,w.Staff,w.FindingId,r)).Succeeded);
    using var factory=Factory(pg,w.Fixture.Staff);using var client=factory.CreateClient();await SignIn(client);
    var foreign=await FindingCommandReceiptSeed.SeedAsync(pg);var tail=$"/commands/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}";
    using var denied=await client.GetAsync($"/api/ui/findings/{foreign.FindingId}"+tail);using var guessed=await client.GetAsync($"/api/ui/findings/{Guid.NewGuid()}"+tail);
    Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(denied.StatusCode,guessed.StatusCode);Assert.Equal(await denied.Content.ReadAsStringAsync(),await guessed.Content.ReadAsStringAsync());
    var root=$"/api/ui/findings/{w.FindingId}/commands";var absent=await Read(client,root+$"/receipts/{Guid.NewGuid()}?requestHash={new string('a',64)}");Assert.False(absent.GetProperty("found").GetBoolean());Assert.Equal(JsonValueKind.Null,absent.GetProperty("receipt").ValueKind);
    using var managerFactory=Factory(pg,w.Fixture.Reviewer);using var manager=managerFactory.CreateClient();await SignIn(manager);var other=await Read(manager,$"/api/ui/findings/{w.FindingId}"+tail);Assert.False(other.GetProperty("found").GetBoolean());
    await db.RoleGrants.Where(x=>x.UserId==w.Staff.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/ui/findings/{w.FindingId}"+tail)).StatusCode);
    Assert.Single(await db.FindingAmendments.ToListAsync());
  }

  [Fact]
  public async Task ArabicMultilineResponseRetainsTypedCanonicalTextOverHttp()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-FINDING-ARABIC");var w=await FindingCommandReceiptSeed.SeedAsync(pg);using var factory=Factory(pg,w.Fixture.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);
    var root=$"/api/ui/findings/{w.FindingId}/commands";var state=await Read(client,root+"/state");var text="مراجعة\r\n\t"+new string('ع',19991);
    var r=new FindingCommandRequest(Guid.NewGuid(),state.GetProperty("finding").GetProperty("revision").GetString()!,state.GetProperty("reviewBasis").GetString()!,new(FindingCommandActions.RecordResponse,Response:new(text,true)));
    using var previewResponse=await Post(client,csrf,root+"/preview",r);Assert.Equal(HttpStatusCode.OK,previewResponse.StatusCode);using var previewJson=JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());var p=previewJson.RootElement.GetProperty("value");Assert.Equal(text,p.GetProperty("canonicalInput").GetProperty("response").GetProperty("managementResponse").GetString());
    using var response=await Post(client,csrf,root+"/actions",r with{Reviewed=true,ExpectedRequestHash=p.GetProperty("requestHash").GetString()});Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(text,json.RootElement.GetProperty("value").GetProperty("amendment").GetProperty("after").GetProperty("managementResponse").GetString());
  }


  [Theory,InlineData("epoch"),InlineData("hold"),InlineData("freeze")]
  public async Task HttpLifetimeAndWriteGatesDoNotChangeOriginalReceipt(string gate)
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-FINDING-LIFETIME");var w=await FindingCommandReceiptSeed.SeedAsync(pg);var f=w.Fixture;await using var db=new AuditSphereDbContext(pg.Options);
    // Epochs beyond JavaScript's exact-number range remain strings on both receipt and event.
    const long epoch=9007199254740993;await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,epoch));f.Staff.SessionEpoch=epoch;
    using var factory=Factory(pg,f.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);var root=$"/api/ui/findings/{w.FindingId}/commands";
    var state=await Read(client,root+"/state");var r=new FindingCommandRequest(Guid.NewGuid(),state.GetProperty("finding").GetProperty("revision").GetString()!,state.GetProperty("reviewBasis").GetString()!,FindingCommandReceiptSeed.Fields(FindingCommandActions.RecordResponse));
    using var previewResponse=await Post(client,csrf,root+"/preview",r);Assert.Equal(HttpStatusCode.OK,previewResponse.StatusCode);using var pj=JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());r=r with{Reviewed=true,ExpectedRequestHash=pj.RootElement.GetProperty("value").GetProperty("requestHash").GetString()};
    using var response=await Post(client,csrf,root+"/actions",r);Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var receipt=json.RootElement.GetProperty("value");Assert.Equal("9007199254740993",receipt.GetProperty("actorEpoch").GetString());Assert.Equal("9007199254740993",receipt.GetProperty("amendment").GetProperty("actorEpoch").GetString());
    if(gate=="epoch")
    {
      await db.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,epoch+1));
      var recovery=await client.GetAsync(root+$"/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}");Assert.False(recovery.IsSuccessStatusCode);Assert.DoesNotContain(receipt.GetProperty("id").GetGuid().ToString(),await recovery.Content.ReadAsStringAsync());
    }
    else
    {
      if(gate=="hold")db.EngagementHolds.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,EngagementId=f.EngagementId,HoldKind="Independence",Reason="SYNTHETIC hold",CreatedAt=DateTimeOffset.UtcNow});
      else
      {
        var report=Guid.NewGuid();db.AuditDeliverables.Add(new(){Id=report,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,Kind="MANAGEMENT_LETTER",Version=1,TemplateVersion="SYNTHETIC",InputDigest=new('a',64),ContentSha256=new('b',64),Content=[1],CreatedByUserId=f.Admin.Id,CreatedAt=DateTimeOffset.UtcNow});
        var signed=DateTimeOffset.UtcNow.AddDays(-61);db.EngagementFileFreezes.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,ReportDeliverableId=report,ReportSignedAt=signed,DueAt=signed.AddDays(60),State="FROZEN",FrozenAt=signed.AddDays(60),UpdatedAt=signed.AddDays(60)});
      }
      await db.SaveChangesAsync();var blocked=await Read(client,root+"/state");Assert.False(blocked.GetProperty("canRecordResponse").GetBoolean());
      var replay=await Post(client,csrf,root+"/actions",r);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);using var replayJson=JsonDocument.Parse(await replay.Content.ReadAsStringAsync());Assert.Equal(receipt.GetRawText(),replayJson.RootElement.GetProperty("value").GetRawText());
      var recovered=await Read(client,root+$"/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}");Assert.Equal(receipt.GetRawText(),recovered.GetProperty("receipt").GetRawText());
      Assert.False((await Post(client,csrf,$"/api/ui/findings/{w.FindingId}/response",new RecordFindingResponseRequest(w.FindingId,"Blocked",true))).IsSuccessStatusCode);
    }
    Assert.Single(await db.FindingAmendments.ToListAsync());Assert.Single(await db.FindingCommandReceipts.ToListAsync());
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");Assert.Equal(HttpStatusCode.OK,r.StatusCode);return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static Task<HttpResponseMessage> Post(HttpClient c,string token,string url,object body){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",token);return c.SendAsync(r);}
  private static async Task<JsonElement> Read(HttpClient c,string url){using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);using var j=JsonDocument.Parse(await r.Content.ReadAsStringAsync());return j.RootElement.Clone();}
}
