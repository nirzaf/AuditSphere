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

public sealed class AuditPlanningCommandReceiptApiTests
{
  public static TheoryData<string> Actions => new() { "CALCULATE_MATERIALITY", "ROUND_MATERIALITY", "APPROVE_MATERIALITY", "CREATE_RISK", "ASSESS_RISK", "ASSIGN_RISK_OWNER", "CLEAR_RISK", "LINK_PROCEDURE_RISK" };
  [Theory, MemberData(nameof(Actions))]
  public async Task EightActionsExposeExactPreviewAndActorOwnedRecovery(string action)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-AUDIT-PLANNING-RECEIPTS");
    var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var (actor, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, action);
    await using var db = new AuditSphereDbContext(pg.Options);
    var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId);
    using var factory = Factory(pg, user); using var client = factory.CreateClient(); var csrf = await SignIn(client);
    var root = $"/api/ui/engagements/{f.EngagementId}/audit-plan/commands";
    var query = Query(fields);
    var state = await Read(client, root + "/state" + query);
    Assert.True(state.GetProperty("canProceed").GetBoolean()); Assert.False(state.TryGetProperty("stateJson", out _));
    var r = new AuditPlanningCommandRequest(Guid.NewGuid(), state.GetProperty("reviewBasis").GetString()!, fields);
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "invalid", root + "/preview", r)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, csrf, root + "/actions", r)).StatusCode);
    using var previewResponse = await Post(client, csrf, root + "/preview", r); Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
    using var previewJson = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync()); var preview = previewJson.RootElement.GetProperty("value");
    Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("summary").GetString()));
    Assert.Equal(action, preview.GetProperty("canonicalInput").GetProperty("action").GetString());
    var source = preview.GetProperty("state").GetProperty("source");
    Assert.Equal(JsonValueKind.String, source.GetProperty("mappingVersion").ValueKind); Assert.Equal(64, source.GetProperty("datasetDigest").GetString()!.Length);
    if (action == AuditPlanningActions.ApproveMateriality)
    {
      var detail = preview.GetProperty("state").GetProperty("materiality");
      Assert.Equal(JsonValueKind.String, detail.GetProperty("planningMateriality").ValueKind);
      Assert.Equal(64, detail.GetProperty("datasetDigest").GetString()!.Length);
      Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("rationale").GetString()));
      Assert.Equal(fields.Approve!.AssessmentId, detail.GetProperty("assessmentId").GetGuid());
    }
    if (action == AuditPlanningActions.ClearRisk)
    {
      var detail = preview.GetProperty("state").GetProperty("risk");
      Assert.Equal(fields.ClearRisk!.ExpectedAssessmentId, detail.GetProperty("assessmentId").GetGuid());
      Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("assessmentRationale").GetString()));
      Assert.Equal("RED", detail.GetProperty("band").GetString());
    }
    r = r with { Fields = JsonSerializer.Deserialize<AuditPlanningCommandFields>(preview.GetProperty("canonicalInput").GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!,
      Reviewed = true, ExpectedRequestHash = preview.GetProperty("requestHash").GetString() };
    using var response = await Post(client, csrf, root + "/actions", r); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var receipt = json.RootElement.GetProperty("value");
    Assert.Equal(JsonValueKind.String, receipt.GetProperty("actorEpoch").ValueKind);
    var receiptId = receipt.GetProperty("id").GetGuid();
    using var replay = await Post(client, csrf, root + "/actions", r); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()); Assert.Equal(receiptId, replayJson.RootElement.GetProperty("value").GetProperty("id").GetGuid());
    var lookup = await Read(client, root + $"/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}");
    Assert.True(lookup.GetProperty("found").GetBoolean()); Assert.Equal(receiptId, lookup.GetProperty("receipt").GetProperty("id").GetGuid());
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, csrf, root + "/actions", r with { ReviewBasis = new('a',64) })).StatusCode);
    Assert.Single(await db.AuditPlanningCommandReceipts.ToListAsync());
    // Legacy plan route remains a distinct usable endpoint.
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/ui/engagements/{f.EngagementId}/audit-plan")).StatusCode);
  }

  [Fact]
  public async Task ForeignGuessedAndRevokedScopesNeverExposeReceiptDetails()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-AUDIT-PLANNING-SCOPE"); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var (a, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, AuditPlanningActions.CreateRisk);
    await using var db = new AuditSphereDbContext(pg.Options); var r = await AuditPlanningReceiptSeed.ReviewAsync(db, a, f.EngagementId, fields);
    var saved = await AuditPlanningCommandWorkspace.ExecuteAsync(db, a, f.EngagementId, r); Assert.True(saved.Succeeded, saved.Message);
    using var factory = Factory(pg, f.Staff); using var client = factory.CreateClient(); await SignIn(client);
    var foreign = await PbcSeed.SeedAsync(pg);
    var foreignPath = $"/api/ui/engagements/{foreign.EngagementId}/audit-plan/commands/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}";
    var guessedPath = $"/api/ui/engagements/{Guid.NewGuid()}/audit-plan/commands/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}";
    using var hidden = await client.GetAsync(foreignPath); using var guessed = await client.GetAsync(guessedPath);
    Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode); Assert.Equal(hidden.StatusCode, guessed.StatusCode);
    Assert.Equal(await hidden.Content.ReadAsStringAsync(), await guessed.Content.ReadAsStringAsync());
    await db.RoleGrants.Where(x => x.UserId == a.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/ui/engagements/{f.EngagementId}/audit-plan/commands/receipts/{r.RequestId}?requestHash={r.ExpectedRequestHash}")).StatusCode);
  }

  [Fact]
  public async Task ArabicMultilineCanonicalPreviewAndReceiptRemainExactOverHttp()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-PLANNING-ARABIC");var w=await AuditPlanningReceiptSeed.SeedAsync(pg);var f=w.Fixture;
    using var factory=Factory(pg,f.Staff);using var client=factory.CreateClient();var csrf=await SignIn(client);
    var root=$"/api/ui/engagements/{f.EngagementId}/audit-plan/commands";
    var state=await Read(client,root+"/state?action=CREATE_RISK");var text="مراجعة\r\n\t"+new string('ع',3991);
    var fields=new AuditPlanningCommandFields(AuditPlanningActions.CreateRisk,CreateRisk:new("النقد","الوجود",text,text,"SIGNIFICANT",text,text));
    var request=new AuditPlanningCommandRequest(Guid.NewGuid(),state.GetProperty("reviewBasis").GetString()!,fields);
    using var previewResponse=await Post(client,csrf,root+"/preview",request);Assert.Equal(HttpStatusCode.OK,previewResponse.StatusCode);
    using var previewJson=JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());var p=previewJson.RootElement.GetProperty("value");
    Assert.Equal(text,p.GetProperty("canonicalInput").GetProperty("createRisk").GetProperty("description").GetString());
    request=request with{Reviewed=true,ExpectedRequestHash=p.GetProperty("requestHash").GetString()};
    using var resultResponse=await Post(client,csrf,root+"/actions",request);Assert.Equal(HttpStatusCode.OK,resultResponse.StatusCode);
    using var resultJson=JsonDocument.Parse(await resultResponse.Content.ReadAsStringAsync());var result=resultJson.RootElement.GetProperty("value");
    Assert.Equal(text,result.GetProperty("result").GetProperty("risk").GetProperty("description").GetString());
    Assert.Equal(text,result.GetProperty("preview").GetProperty("canonicalInput").GetProperty("createRisk").GetProperty("controlsConsidered").GetString());
  }

  private static string Query(AuditPlanningCommandFields f)
  {
    var query = "?action=" + f.Action;
    if ((f.Round?.AssessmentId ?? f.Approve?.AssessmentId) is { } assessment) query += "&assessmentId=" + assessment;
    if ((f.AssessRisk?.RiskId ?? f.AssignRiskOwner?.RiskId ?? f.ClearRisk?.RiskId ?? f.LinkProcedureRisk?.RiskId) is { } risk) query += "&riskId=" + risk;
    if (f.LinkProcedureRisk is { } link) query += "&procedureId=" + link.ProcedureId;
    return query;
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser u) => new(new Dictionary<string,string?> {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:TenantId"] = u.TenantId,
    ["DevelopmentIdentity:Subject"] = u.Subject, ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
  private static async Task<string> SignIn(HttpClient c) { await c.GetAsync("/auth/sign-in"); using var r = await c.GetAsync("/api/ui/session"); Assert.Equal(HttpStatusCode.OK,r.StatusCode);
    return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]); }
  private static Task<HttpResponseMessage> Post(HttpClient c, string token, string url, object body) { var r = new HttpRequestMessage(HttpMethod.Post,url) { Content = JsonContent.Create(body) }; r.Headers.Add("X-XSRF-TOKEN",token); return c.SendAsync(r); }
  private static async Task<JsonElement> Read(HttpClient c, string url) { using var r = await c.GetAsync(url); Assert.Equal(HttpStatusCode.OK,r.StatusCode); using var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return j.RootElement.Clone(); }
}
