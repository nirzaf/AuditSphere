using System.Net;
using System.Net.Http.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class EngagementDeadlineReminderApiTests
{
  [Fact]
  public async Task AuthorizedStaffReadsScopedRemindersWithoutCreatingWritesAndSessionRevocationReturns401()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-DEADLINE-REMINDERS");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
    Guid taskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var task = await EngagementWorkflowTaskService.CreateAsync(db, actor, f.EngagementId,
        new("API deadline reminder", WorkflowTaskPhases.AuditPlanning, 1,
          dueDate, IdempotencyKey: Guid.NewGuid()));
      Assert.True(task.Succeeded, task.Message);
      taskId = task.Value;
    }

    using var factory = Factory(pg, f.Admin);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var path = PathFor(f.EngagementId);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    await SignIn(client);
    using var first = await client.GetAsync(path);
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    Assert.True(first.Headers.CacheControl!.NoStore);
    var firstBody = await first.Content.ReadFromJsonAsync<EngagementDeadlineReminders>();
    Assert.Equal(f.EngagementId, firstBody!.EngagementId);
    var reminder = Assert.Single(firstBody.Items);
    Assert.Equal(taskId, reminder.SourceId);
    Assert.Equal("TASK_DUE", reminder.Kind);
    Assert.Equal(dueDate, reminder.DueDate);
    Assert.Equal("UPCOMING", reminder.Tone);
    Assert.Equal("/app/work", reminder.SourceHref);
    using var repeat = await client.GetAsync(path);
    Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
    Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(path, JsonContent.Create(new { }))).StatusCode);

    using var clientFactory = Factory(pg, f.Client);
    using var clientUser = clientFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(clientUser);
    Assert.Equal(HttpStatusCode.Forbidden, (await clientUser.GetAsync(path)).StatusCode);
    using var foreignFactory = Factory(pg, foreign.Admin);
    using var foreignUser = foreignFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(foreignUser);
    Assert.Equal(HttpStatusCode.Forbidden, (await foreignUser.GetAsync(path)).StatusCode);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(1, await db.WorkflowTaskActivities.CountAsync(x => x.WorkTaskId == taskId));
      Assert.Empty(await db.PbcReminderReceipts.ToListAsync());
      Assert.Empty(await db.PbcCommunications.ToListAsync());
      Assert.Empty(await db.DurableOperations.ToListAsync());
      await db.Users.Where(x => x.Id == f.Admin.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    }
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
  }

  [Fact]
  public async Task DisabledWorkflowExperienceReturnsTheQueueScopeDenial()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-DEADLINE-REMINDERS-OFF");
    var f = await PbcSeed.SeedAsync(pg);
    using var factory = Factory(pg, f.Admin, enabled: false);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);
    using var response = await client.GetAsync(PathFor(f.EngagementId));
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("scope.denied", body, StringComparison.Ordinal);
    Assert.Contains("Workflow task experience is not enabled on this server.", body, StringComparison.Ordinal);
  }

  private static string PathFor(Guid engagementId) => $"/api/ui/engagements/{engagementId}/deadline-reminders";

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,
    AuditSphereOps.Domain.Security.AppUser user, bool enabled = true) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
    ["DevelopmentIdentity:Enabled"] = "true",
    ["DevelopmentIdentity:Subject"] = user.Subject,
    ["DevelopmentIdentity:TenantId"] = user.TenantId,
    ["ExternalEffects:Enabled"] = "false",
    ["Application:AllowSimulationAdapters"] = "true",
    ["WorkflowExperience:Enabled"] = enabled.ToString()
  });

  private static async Task<string> SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x =>
      x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }
}
