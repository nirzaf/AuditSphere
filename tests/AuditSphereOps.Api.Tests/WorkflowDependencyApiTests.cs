using System.Net;
using System.Net.Http.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class WorkflowDependencyApiTests
{
  [Fact]
  public async Task DependencyChangesRequireCookieCsrfScopeRevisionsAndOwnedRecovery()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-WORKFLOW-ORDERING");
    var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg); Guid task; Guid predecessor;
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      var made = await EngagementWorkflowTaskService.CreateAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId,
        new("Synthetic evidence coordination", WorkflowTaskPhases.AuditPlanning, 1, IdempotencyKey: Guid.NewGuid()));
      Assert.True(made.Succeeded, made.Message); task = made.Value;
      var prior = await EngagementWorkflowTaskService.CreateAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId,
        new("Synthetic prerequisite task", WorkflowTaskPhases.AuditPlanning, 1, IdempotencyKey: Guid.NewGuid()));
      Assert.True(prior.Succeeded, prior.Message); predecessor = prior.Value;
    }
    using var factory = Factory(pg, f.Admin); using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var path = PathFor(f.EngagementId, task);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    var csrf = await SignIn(client);
    using var read = await client.GetAsync(path); Assert.True(read.Headers.CacheControl!.NoStore);
    Assert.Empty((await read.Content.ReadFromJsonAsync<WorkflowDependencyWorkspace>())!.References);
    var change = new WorkflowDependencyChangeRequest(predecessor, true, 1, 1, "Require prior operational completion.", Guid.NewGuid());
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "forged", path, change)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, csrf, path, change with { PredecessorTaskId = task })).StatusCode);
    using var created = await Post(client, csrf, path, change); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    var result = (await created.Content.ReadFromJsonAsync<CommandResult<WorkflowDependencyReceipt>>())!.Value!;
    using var replay = await Post(client, csrf, path, change); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    Assert.Equal(result, (await replay.Content.ReadFromJsonAsync<CommandResult<WorkflowDependencyReceipt>>())!.Value);
    Assert.Equal(result, await client.GetFromJsonAsync<WorkflowDependencyReceipt>(path + "/receipts/" + change.IdempotencyKey));
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, csrf, path, change with { Required = false, IdempotencyKey = Guid.NewGuid() })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "/receipts/" + Guid.NewGuid())).StatusCode);
    using var staffFactory = Factory(pg, f.Staff); using var staff = staffFactory.CreateClient(); var staffCsrf = await SignIn(staff);
    Assert.False((await staff.GetFromJsonAsync<WorkflowDependencyWorkspace>(path))!.CanManage);
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(staff, staffCsrf, path, change)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(path + "/receipts/" + change.IdempotencyKey)).StatusCode);
    using var outsiderFactory = Factory(pg, foreign.Admin); using var outsider = outsiderFactory.CreateClient(); await SignIn(outsider);
    using var known = await outsider.GetAsync(path); using var guessed = await outsider.GetAsync(PathFor(Guid.NewGuid(), Guid.NewGuid()));
    Assert.Equal(HttpStatusCode.Forbidden, known.StatusCode); Assert.Equal(await known.Content.ReadAsStringAsync(), await guessed.Content.ReadAsStringAsync());
    await using (var db = new AuditSphereDbContext(pg.Options)) {
      await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
      Assert.Single(await db.WorkflowTaskDependencyRevisions.ToListAsync()); Assert.Empty(await db.MaterialityApprovals.ToListAsync());
    }
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path + "/receipts/" + change.IdempotencyKey)).StatusCode);
    await using var retained = new AuditSphereDbContext(pg.Options);
    var migrations = retained.Database.GetMigrations().ToArray();
    var index = Array.FindIndex(migrations, x => x.EndsWith("_WorkflowDependencyAmendments", StringComparison.Ordinal));
    Assert.True(index > 0);
    var refused = await Assert.ThrowsAsync<PostgresException>(() => retained.GetService<IMigrator>().MigrateAsync(migrations[index - 1]));
    Assert.Equal("P0001", refused.SqlState);
    Assert.True(refused.MessageText.Contains("Retained workflow dependency", StringComparison.Ordinal) ||
      refused.MessageText.Contains("PermanentFileFreeze", StringComparison.Ordinal), refused.MessageText);
    Assert.Contains(migrations[index], await retained.Database.GetAppliedMigrationsAsync());
    Assert.Single(await retained.WorkflowTaskDependencyRevisions.ToListAsync());
  }

  [Fact]
  public async Task DisabledExperienceDoesNotExposePrerequisiteOrReceiptRoutes()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-WORKFLOW-ORDERING-OFF"); var f = await PbcSeed.SeedAsync(pg);
    using var factory = Factory(pg, f.Admin, false); using var c = factory.CreateClient(); var csrf = await SignIn(c);
    var path = PathFor(f.EngagementId, Guid.NewGuid());
    Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(path)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(path + "/receipts/" + Guid.NewGuid())).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await Post(c, csrf, path, new WorkflowDependencyChangeRequest(Guid.NewGuid(), true, 1, 1, "No feature access", Guid.NewGuid()))).StatusCode);
  }

  private static string PathFor(Guid engagement, Guid task) => $"/api/ui/engagements/{engagement}/workflow/tasks/{task}/dependencies";
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser user, bool enabled = true) => new(new Dictionary<string, string?> {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
    ["DevelopmentIdentity:Subject"] = user.Subject, ["DevelopmentIdentity:TenantId"] = user.TenantId,
    ["ExternalEffects:Enabled"] = "false", ["Application:AllowSimulationAdapters"] = "true", ["WorkflowExperience:Enabled"] = enabled.ToString()
  });
  private static async Task<string> SignIn(HttpClient c) {
    await c.GetAsync("/auth/sign-in"); using var session = await c.GetAsync("/api/ui/session");
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }
  private static Task<HttpResponseMessage> Post(HttpClient c, string csrf, string url, object body) {
    var r = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) }; r.Headers.Add("X-XSRF-TOKEN", csrf); return c.SendAsync(r);
  }
}
