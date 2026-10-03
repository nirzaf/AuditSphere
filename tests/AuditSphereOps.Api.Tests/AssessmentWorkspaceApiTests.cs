using System.Net;
using System.Net.Http.Json;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class AssessmentWorkspaceApiTests
{
  [Fact]
  public async Task AssessmentWorkspaceProjectsBoundedSpecialistTimelineFromImmutableReceipts()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-ASSESSMENT-TIMELINE");
    var f = await PbcSeed.SeedAsync(pg);
    Guid reviewId;
    await using (var seedDb = new AuditSphereDbContext(pg.Options))
      await AssessmentParitySeed.PopulateAsync(seedDb, f);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var actor = PbcSeed.Actor(f.Admin, "Partner");
      var request = await RunReviewedAsync(db, actor, f.ClientId,
        new("REQUEST_REVIEW", "2", Area: "AML", Specialist: "Synthetic specialist"));
      reviewId = request.ResourceId;
      await RunReviewedAsync(db, actor, f.ClientId,
        new("RECORD_REVIEW", "2", Evidence: "SYNTHETIC-EVIDENCE", ReviewId: reviewId,
          Status: "HOLD", ExpectedStatus: "PENDING"));
    }
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = f.Admin.Subject,
      ["DevelopmentIdentity:TenantId"] = f.Admin.TenantId, ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");
    using var response = await client.GetAsync("/api/ui/clients/" + f.ClientId + "/assessment");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var projection = (await response.Content.ReadFromJsonAsync<AssessmentWorkspace>())!;
    Assert.Equal(new[] { "Review requested", "Result recorded" }, projection.SpecialistTimeline.Select(x => x.Action));
    Assert.Equal(new[] { "PENDING", "HOLD" }, projection.SpecialistTimeline.Select(x => x.Status));
    Assert.All(projection.SpecialistTimeline, e => {
      Assert.Equal(reviewId, e.ReviewId);
      Assert.Equal("AML", e.Area);
      Assert.Equal(f.Admin.DisplayName, e.Actor);
    });
    Assert.True(projection.SpecialistTimeline[0].OccurredAt <= projection.SpecialistTimeline[1].OccurredAt);
    Assert.Equal("SYNTHETIC-EVIDENCE", projection.SpecialistTimeline[1].Evidence);
    Assert.True(projection.Checklist.Clearances.Single(x => x.Id == reviewId).CreatedAt <= projection.SpecialistTimeline[0].OccurredAt);
    Assert.Null(projection.Checklist.Clearances.Single(x => x.Id == reviewId).ClearedAt);
  }

  private static async Task<AssessmentReceiptView> RunReviewedAsync(AuditSphereDbContext db,
    AuditSphereOps.Application.Abstractions.ActorContext actor, Guid clientId, AssessmentCommandFields fields)
  {
    var requestId = Guid.CreateVersion7();
    var previewResult = await AssessmentCommandWorkspace.PreviewAsync(db, actor, clientId,
      new AssessmentCommandRequest(requestId, fields));
    Assert.True(previewResult.Succeeded, previewResult.Message);
    var preview = previewResult.Value!;
    var result = await AssessmentCommandWorkspace.ExecuteAsync(db, actor, clientId,
      new AssessmentCommandRequest(requestId, fields, preview.ReviewBasis, preview.RequestHash, Reviewed: true));
    Assert.True(result.Succeeded, result.Message);
    return result.Value!;
  }

  [Fact]
  public async Task AssessmentReadIsNoStoreScopedAndPreservesExactHistoricalSelection()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-ASSESSMENT-PARITY");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    Guid decisionId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      decisionId = await AssessmentParitySeed.PopulateAsync(db, f);
    }
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = f.Admin.Subject,
      ["DevelopmentIdentity:TenantId"] = f.Admin.TenantId, ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var url = "/api/ui/clients/" + f.ClientId + "/assessment?decisionId=" + decisionId;
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    await client.GetAsync("/auth/sign-in");
    using var response = await client.GetAsync(url);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.True(response.Headers.CacheControl!.NoStore);
    var projection = (await response.Content.ReadFromJsonAsync<AssessmentWorkspace>())!;
    Assert.True(projection.Historical);
    Assert.Equal(decisionId, projection.SelectedDecision!.Id);
    Assert.Equal(f.ClientId, projection.Client.Id);
    Assert.False(projection.Checklist.CanEdit);
    var route = (await client.GetFromJsonAsync<AssessmentRoute>("/api/ui/assessments/" + decisionId))!;
    Assert.Equal(decisionId, route.DecisionId);
    Assert.Equal(f.ClientId, route.ClientId);
    using var denied = await client.GetAsync("/api/ui/clients/" + foreign.ClientId + "/assessment");
    using var missing = await client.GetAsync("/api/ui/clients/" + Guid.NewGuid() + "/assessment");
    Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    Assert.Equal(denied.StatusCode, missing.StatusCode);
    Assert.Equal(await denied.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Users.Where(u => u.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
  }
}
