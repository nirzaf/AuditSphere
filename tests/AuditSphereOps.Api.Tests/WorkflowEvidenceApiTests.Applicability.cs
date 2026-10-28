using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Api.Ui;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed partial class WorkflowEvidenceApiTests
{
  [Fact]
  public async Task ApplicabilityBoundaryPreservesExactInt64CookieCsrfScopeAndMandatoryTaskProtection()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-WORKFLOW-APPLICABILITY");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    const long revision = 9007199254740993L;
    Guid taskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PracticeTimeService.CreateTaskAsync(db, PbcSeed.Actor(f.Admin, "Administrator"),
        new("Synthetic mandatory task", f.ClientId, f.EngagementId, null));
      Assert.True(created.Succeeded, created.Message);
      taskId = created.Value;
      // Initial synthetic transport fixture: never disable the exact +1 update guard to reach an Int64 boundary.
      db.WorkflowTaskBindings.Add(new WorkflowTaskBinding
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        WorkTaskId = taskId, Phase = WorkflowTaskPhases.AuditPlanning, Ordinal = 1, RequiredTask = true,
        SourceKey = $"MANUAL:{Guid.NewGuid():N}", CreationRequestDigest = new string('a', 64),
        Revision = revision, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = f.Admin.Id
      });
      await db.SaveChangesAsync();
    }
    var path = $"/api/ui/engagements/{f.EngagementId}/workflow/tasks/{taskId}/applicability";
    using var factory = Factory(pg, f.Admin);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    var csrf = await SignIn(client);
    using var read = await client.GetAsync(path);
    Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    Assert.True(read.Headers.CacheControl!.NoStore);
    var workspace = await read.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(JsonValueKind.String, workspace.GetProperty("taskRevision").ValueKind);
    Assert.Equal("9007199254740993", workspace.GetProperty("taskRevision").GetString());
    Assert.False(workspace.GetProperty("canPropose").GetBoolean());
    var proposal = new UiEndpoints.WorkflowApplicabilityProposalInput(Guid.NewGuid(), "9007199254740993", null,
      new string('a', 64), "Synthetic exact mandatory-task protection.", Guid.NewGuid(), true);
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "forged", path + "/proposals", proposal)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, csrf, path + "/proposals", proposal with { Reviewed = false })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, csrf, path + "/proposals", proposal with { ExpectedTaskRevision = "09007199254740993" })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, csrf, path + "/proposals", proposal with { ExpectedTaskRevision = "9.007199254740993e15" })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, csrf, path + "/proposals", new
    {
      proposal.SourceReceiptId, ExpectedTaskRevision = revision, proposal.ExpectedProposalId,
      proposal.BasisHash, proposal.Reason, proposal.IdempotencyKey, proposal.Reviewed
    })).StatusCode);
    using var adjacent = await Post(client, csrf, path + "/proposals", proposal with { ExpectedTaskRevision = "9007199254740992" });
    Assert.Equal(HttpStatusCode.Conflict, adjacent.StatusCode);
    Assert.Equal(ErrorCodes.StaleRevision, (await adjacent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    using var exact = await Post(client, csrf, path + "/proposals", proposal);
    Assert.Equal(HttpStatusCode.BadRequest, exact.StatusCode);
    Assert.Equal(ErrorCodes.GateBlocked, (await exact.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    // Reaching the mandatory-template gate, rather than revision.stale, distinguishes adjacent values above 2^53.
    var history = await client.GetFromJsonAsync<UiEndpoints.WorkflowApplicabilityHistoryResponse>(path + "/history");
    Assert.Empty(history!.Items);
    Assert.False(history.HasMore);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "/history?afterId=" + Guid.NewGuid())).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "/receipts/" + proposal.IdempotencyKey)).StatusCode);

    using var staffFactory = Factory(pg, f.Staff);
    using var staff = staffFactory.CreateClient();
    var staffCsrf = await SignIn(staff);
    Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(path)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(path + "/receipts/" + proposal.IdempotencyKey)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(staff, staffCsrf, path + "/proposals", new
    {
      proposal.SourceReceiptId, proposal.ExpectedTaskRevision, proposal.ExpectedProposalId, proposal.BasisHash,
      proposal.Reason, proposal.IdempotencyKey, proposal.Reviewed, ActorUserId = f.Admin.Id, Roles = new[] { "Administrator" }
    })).StatusCode);
    using var outsideFactory = Factory(pg, foreign.Admin);
    using var outside = outsideFactory.CreateClient();
    await SignIn(outside);
    using var known = await outside.GetAsync(path);
    using var unknown = await outside.GetAsync($"/api/ui/engagements/{Guid.NewGuid()}/workflow/tasks/{Guid.NewGuid()}/applicability");
    Assert.Equal(HttpStatusCode.Forbidden, known.StatusCode);
    Assert.Equal(await unknown.Content.ReadAsStringAsync(), await known.Content.ReadAsStringAsync());

    using var disabledFactory = Factory(pg, f.Admin, false);
    using var disabled = disabledFactory.CreateClient();
    var disabledCsrf = await SignIn(disabled);
    foreach (var suffix in new[] { "", "/history", "/receipts/" + proposal.IdempotencyKey })
      Assert.Equal(HttpStatusCode.NotFound, (await disabled.GetAsync(path + suffix)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await Post(disabled, disabledCsrf, path + "/proposals", proposal)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await Post(disabled, disabledCsrf, path + "/decisions",
      new UiEndpoints.WorkflowApplicabilityDecisionInput(Guid.NewGuid(), null, "APPROVE", proposal.ExpectedTaskRevision,
        proposal.BasisHash, proposal.Reason, Guid.NewGuid(), true))).StatusCode);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await verify.WorkflowTaskApplicabilityProposals.ToListAsync());
    Assert.Empty(await verify.WorkflowTaskApplicabilityDecisions.ToListAsync());
    Assert.Equal(revision, await verify.WorkflowTaskBindings.Where(x => x.WorkTaskId == taskId).Select(x => x.Revision).SingleAsync());
    Assert.Equal(PracticeTimeStates.TaskOpen, (await verify.WorkTasks.SingleAsync(x => x.Id == taskId)).Status);
    await verify.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(x => x.SetProperty(y => y.SessionEpoch, y => y.SessionEpoch + 1));
    foreach (var suffix in new[] { "", "/history", "/receipts/" + proposal.IdempotencyKey })
      Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path + suffix)).StatusCode);
  }
}
