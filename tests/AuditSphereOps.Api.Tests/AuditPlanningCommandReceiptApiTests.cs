using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
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

  [Fact]
  public async Task MilestonePreviewAndSaveShareTightCutoffDatesAndRejectMalformedOrBoundaryDates()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-MILESTONE-PREVIEW-SAVE");
    var w = await AuditPlanningReceiptSeed.SeedAsync(pg);
    var f = w.Fixture;
    await using (var seedDb = new AuditSphereDbContext(pg.Options))
    {
      var changed = await seedDb.Engagements.Where(x => x.Id == f.EngagementId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.PeriodEnd, "2026-12-31"));
      Assert.Equal(1, changed);
    }

    using var factory = Factory(pg, f.Reviewer);
    using var client = factory.CreateClient();
    var csrf = await SignIn(client);
    var calculatePath = $"/api/ui/engagements/{f.EngagementId}/milestones/calculate";
    var savePath = $"/api/ui/engagements/{f.EngagementId}/milestones";
    const string cutoff = "2027-01-12";
    const string overrideReason = "SYNTHETIC compressed-window scheduling override for API regression";

    async Task<long> PlanCount()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await db.EngagementMilestonePlans.LongCountAsync(x => x.EngagementId == f.EngagementId);
    }

    static async Task<string?> FailureCode(HttpResponseMessage response)
    {
      using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
      return error.RootElement.GetProperty("code").GetString();
    }

    static void AssertExpectedDates(JsonElement value)
    {
      Assert.Equal("2026-12-31", value.GetProperty("periodEnd").GetString());
      Assert.Equal("2027-01-12", value.GetProperty("statutoryFilingCutoff").GetString());
      Assert.Equal("2027-01-02", value.GetProperty("fieldworkStartDate").GetString());
      Assert.Equal("2027-01-08", value.GetProperty("draftReportDate").GetString());
      Assert.Equal("2027-01-11", value.GetProperty("finalReportDate").GetString());
      Assert.Equal("2027-03-12", value.GetProperty("archiveDeadlineDate").GetString());
    }

    Assert.Equal(0L, await PlanCount());

    // Each optional date must be rejected on both real routes rather than silently treated as omitted.
    foreach (var route in new[] { calculatePath, savePath })
    foreach (var field in new[] { "fieldworkStartDate", "draftReportDate", "finalReportDate" })
    {
      var malformed = new Dictionary<string, object?>
      {
        ["statutoryFilingCutoff"] = cutoff,
        ["warningOverrideReason"] = overrideReason,
        [field] = "not-a-date"
      };
      using var response = await Post(client, csrf, route, malformed);
      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
      Assert.Equal("request.invalid", await FailureCode(response));
      Assert.Equal(0L, await PlanCount());
    }

    var standardRequest = new Dictionary<string, object?> { ["statutoryFilingCutoff"] = cutoff };
    using var standardResponse = await Post(client, csrf, calculatePath, standardRequest);
    Assert.Equal(HttpStatusCode.OK, standardResponse.StatusCode);
    using var standardJson = JsonDocument.Parse(await standardResponse.Content.ReadAsStringAsync());
    var standardPreview = standardJson.RootElement.GetProperty("value").Clone();

    // 12 days from period end: fieldwork is +2; the 1-day filing buffer puts final at Jan 11;
    // the 9-day fieldwork span puts draft at +6; archive is exactly 60 days after final.
    AssertExpectedDates(standardPreview);
    Assert.True(standardPreview.GetProperty("warningOverrideRequired").GetBoolean());
    Assert.True(standardPreview.GetProperty("warnings").GetArrayLength() > 0);

    using (var missingOverride = await Post(client, csrf, savePath, standardRequest))
    {
      Assert.Equal(HttpStatusCode.BadRequest, missingOverride.StatusCode);
      Assert.Equal("milestone.override-required", await FailureCode(missingOverride));
    }
    Assert.Equal(0L, await PlanCount());

    var approvedScheduleRequest = new Dictionary<string, object?>
    {
      ["statutoryFilingCutoff"] = cutoff,
      ["warningOverrideReason"] = overrideReason
    };
    using var acceptedPreviewResponse = await Post(client, csrf, calculatePath, approvedScheduleRequest);
    Assert.Equal(HttpStatusCode.OK, acceptedPreviewResponse.StatusCode);
    using var acceptedPreviewJson = JsonDocument.Parse(await acceptedPreviewResponse.Content.ReadAsStringAsync());
    var acceptedPreview = acceptedPreviewJson.RootElement.GetProperty("value").Clone();
    AssertExpectedDates(acceptedPreview);
    Assert.False(acceptedPreview.GetProperty("warningOverrideRequired").GetBoolean());

    using var saveResponse = await Post(client, csrf, savePath, approvedScheduleRequest);
    Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
    using var saveJson = JsonDocument.Parse(await saveResponse.Content.ReadAsStringAsync());
    var saved = saveJson.RootElement.GetProperty("value").Clone();
    AssertExpectedDates(saved);
    Assert.Equal(acceptedPreview.GetProperty("fieldworkStartDate").GetString(), saved.GetProperty("fieldworkStartDate").GetString());
    Assert.Equal(acceptedPreview.GetProperty("draftReportDate").GetString(), saved.GetProperty("draftReportDate").GetString());
    Assert.Equal(acceptedPreview.GetProperty("finalReportDate").GetString(), saved.GetProperty("finalReportDate").GetString());
    Assert.Equal(acceptedPreview.GetProperty("archiveDeadlineDate").GetString(), saved.GetProperty("archiveDeadlineDate").GetString());
    Assert.Equal(overrideReason, saved.GetProperty("warningOverrideReason").GetString());

    await using (var verifyDb = new AuditSphereDbContext(pg.Options))
    {
      var persisted = await verifyDb.EngagementMilestonePlans.AsNoTracking().SingleAsync(x => x.EngagementId == f.EngagementId);
      Assert.Equal(f.FirmId, persisted.FirmId);
      Assert.Equal(f.ClientId, persisted.ClientId);
      Assert.Equal(new DateOnly(2026, 12, 31), persisted.PeriodEnd);
      Assert.Equal(new DateOnly(2027, 1, 12), persisted.StatutoryFilingCutoff);
      Assert.Equal(new DateOnly(2027, 1, 2), persisted.FieldworkStartDate);
      Assert.Equal(new DateOnly(2027, 1, 8), persisted.DraftReportDate);
      Assert.Equal(new DateOnly(2027, 1, 11), persisted.FinalReportDate);
      Assert.Equal(new DateOnly(2027, 3, 12), persisted.ArchiveDeadlineDate);
      Assert.Equal(overrideReason, persisted.WarningOverrideReason);
      Assert.Equal(f.Reviewer.Id, persisted.ScheduledByUserId);
      Assert.Equal(1L, persisted.Revision);
    }

    var boundaries = new (string Cutoff, string? Final, string ErrorCode)[]
    {
      ("0001-01-01", null, "milestone.chronology-error"),
      ("9999-12-31", null, "milestone.invalid"),
      (cutoff, "0001-01-01", "milestone.chronology-error"),
      (cutoff, "9999-12-31", "milestone.invalid")
    };
    foreach (var boundary in boundaries)
    foreach (var route in new[] { calculatePath, savePath })
    {
      var boundaryRequest = new Dictionary<string, object?>
      {
        ["statutoryFilingCutoff"] = boundary.Cutoff,
        ["warningOverrideReason"] = overrideReason
      };
      if (boundary.Final is not null) boundaryRequest["finalReportDate"] = boundary.Final;
      using var response = await Post(client, csrf, route, boundaryRequest);
      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
      Assert.Equal(boundary.ErrorCode, await FailureCode(response));
      Assert.Equal(1L, await PlanCount());
    }
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
