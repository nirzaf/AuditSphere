using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuditSphereOps.Api.Tests;

/// <summary>
/// AS-PM8-01B: the optional queue summary is requested through the existing <c>/api/ui/work/tasks</c> read. It keeps the
/// established cookie session, feature flag and grant-scope boundaries and never writes a business event.
/// </summary>
public sealed class WorkflowDashboardApiTests
{
  private const string QueuePath = "/api/ui/work/tasks";
  private const string QueueInvalidMessage = "Choose a supported queue view, page size from 1 to 100, and a valid task cursor.";
  private static readonly string[] SummaryViewOrder =
    ["MY_TASKS", "DUE_TODAY", "OVERDUE", "WAITING_FOR_CLIENT", "UNDER_REVIEW", "CHANGES_REQUIRED", "UPCOMING"];
  private static readonly Regex CountText = new("^(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);

  [Fact]
  public async Task SummaryIsOptionalCompleteScopedAndReadOnlyOnTheExistingQueueEndpoint()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-WORKFLOW-DASHBOARD");
    var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    var siblingClientId = Guid.NewGuid(); var siblingEngagementId = Guid.NewGuid();
    await SeedSiblingScopeAsync(pg, f.FirmId, siblingClientId, siblingEngagementId);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var admin = PbcSeed.Actor(f.Admin, "Administrator");
    var foreignAdmin = PbcSeed.Actor(foreign.Admin, "Administrator");
    Guid[] scoped, sibling, foreignTasks;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      scoped =
      [
        await CreateTaskAsync(db, admin, f.EngagementId, "Synthetic due today, assigned", 1, today, f.Staff.Id),
        await CreateTaskAsync(db, admin, f.EngagementId, "Synthetic due today, second", 2, today),
        await CreateTaskAsync(db, admin, f.EngagementId, "Synthetic due today, third", 3, today),
        await CreateTaskAsync(db, admin, f.EngagementId, "Synthetic overdue", 4, today.AddDays(-1)),
        await CreateTaskAsync(db, admin, f.EngagementId, "Synthetic upcoming", 5, today.AddDays(3))
      ];
      // Real tasks of another client in the same firm and of another firm: due today, overdue and upcoming.
      sibling =
      [
        await CreateTaskAsync(db, admin, siblingEngagementId, "Sibling due today", 1, today),
        await CreateTaskAsync(db, admin, siblingEngagementId, "Sibling overdue", 2, today.AddDays(-2)),
        await CreateTaskAsync(db, admin, siblingEngagementId, "Sibling upcoming", 3, today.AddDays(1))
      ];
      foreignTasks =
      [
        await CreateTaskAsync(db, foreignAdmin, foreign.EngagementId, "Foreign due today", 1, today),
        await CreateTaskAsync(db, foreignAdmin, foreign.EngagementId, "Foreign overdue", 2, today.AddDays(-1))
      ];
    }
    var outsideIds = sibling.Concat(foreignTasks).ToArray();
    string[] staffCounts = ["1", "3", "1", "0", "0", "0", "1"];
    var expiredUser = PbcSeed.User(f.FirmId, "Staff");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var expired = PbcSeed.Grant(f.FirmId, expiredUser, "Staff", f.ClientId, f.EngagementId);
      expired.GrantedAt = DateTimeOffset.UtcNow.AddDays(-30);
      expired.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
      db.Users.Add(expiredUser);
      db.RoleGrants.Add(expired);
      await db.SaveChangesAsync();
    }

    using var staffFactory = Factory(pg, f.Staff);
    using var anonymous = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    // No cookie: the existing authenticated-session challenge refuses the read before any queue data is computed.
    var (anonymousStatus, anonymousBody) = await GetAsync(anonymous, "?view=DUE_TODAY&pageSize=50&includeSummary=true");
    Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatus);
    Assert.Equal(string.Empty, anonymousBody);

    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false }); await SignIn(staff);
    using var clientFactory = Factory(pg, f.Client); using var client = clientFactory.CreateClient(); await SignIn(client);
    using var reviewerFactory = Factory(pg, f.Reviewer); using var reviewer = reviewerFactory.CreateClient(); await SignIn(reviewer);
    using var adminFactory = Factory(pg, f.Admin); using var firmAdmin = adminFactory.CreateClient(); await SignIn(firmAdmin);
    using var outsiderFactory = Factory(pg, foreign.Admin); using var outsider = outsiderFactory.CreateClient(); await SignIn(outsider);

    string before;
    await using (var db = new AuditSphereDbContext(pg.Options)) before = await SideEffectFingerprintAsync(db);

    // Omitted (and explicitly false) keeps the established queue DTO with a null summary.
    foreach (var omitted in new[] { "?view=DUE_TODAY&pageSize=50", "?view=DUE_TODAY&pageSize=50&includeSummary=false" })
    {
      var (status, body) = await GetAsync(staff, omitted);
      Assert.Equal(HttpStatusCode.OK, status);
      using var doc = JsonDocument.Parse(body);
      Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("summary").ValueKind);
      Assert.Equal("DUE_TODAY", doc.RootElement.GetProperty("view").GetString());
      Assert.Equal(scoped[..3].Order(), TaskIds(doc).Order());
    }

    using var summaryResponse = await staff.GetAsync(QueuePath + "?view=DUE_TODAY&pageSize=50&includeSummary=true");
    Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
    Assert.True(summaryResponse.Headers.CacheControl!.NoStore);
    var summaryBody = await summaryResponse.Content.ReadAsStringAsync();
    using (var doc = JsonDocument.Parse(summaryBody))
    {
      Assert.Equal(scoped[..3].Order(), TaskIds(doc).Order());
      Assert.False(doc.RootElement.GetProperty("hasMore").GetBoolean());
      Assert.Equal(staffCounts, SummaryCounts(doc, today));
    }
    var typed = (await staff.GetFromJsonAsync<WorkflowTaskQueue>(QueuePath + "?view=DUE_TODAY&pageSize=50&includeSummary=true"))!;
    Assert.Equal(today, typed.Summary!.AsOfDateUtc);
    Assert.Equal(SummaryViewOrder, typed.Summary.Counts.Select(x => x.View));

    // A one-row page still reports the complete authorized population, whatever the selected view.
    var (pageStatus, pageBody) = await GetAsync(staff, "?view=DUE_TODAY&pageSize=1&includeSummary=true");
    Assert.Equal(HttpStatusCode.OK, pageStatus);
    using (var doc = JsonDocument.Parse(pageBody))
    {
      Assert.Contains(Assert.Single(TaskIds(doc)), scoped[..3]);
      Assert.True(doc.RootElement.GetProperty("hasMore").GetBoolean());
      Assert.Equal(staffCounts, SummaryCounts(doc, today));
    }
    var (teamStatus, teamBody) = await GetAsync(staff, "?view=TEAM&pageSize=100&includeSummary=true");
    Assert.Equal(HttpStatusCode.OK, teamStatus);
    using (var doc = JsonDocument.Parse(teamBody))
    {
      Assert.Equal(scoped.Order(), TaskIds(doc).Order());
      Assert.Equal(staffCounts, SummaryCounts(doc, today));
    }
    // Another client's and another firm's real task identities never appear, and their records do not alter counts.
    foreach (var body in new[] { summaryBody, pageBody, teamBody })
    foreach (var id in outsideIds)
      Assert.DoesNotContain(id.ToString(), body, StringComparison.OrdinalIgnoreCase);

    // The firm-wide grant does count the sibling client's tasks, so they are real in-firm records outside staff scope.
    var (adminStatus, adminBody) = await GetAsync(firmAdmin, "?view=DUE_TODAY&pageSize=50&includeSummary=true");
    Assert.Equal(HttpStatusCode.OK, adminStatus);
    using (var doc = JsonDocument.Parse(adminBody))
    {
      Assert.Equal(scoped[..3].Append(sibling[0]).Order(), TaskIds(doc).Order());
      Assert.Equal(["0", "4", "2", "0", "0", "0", "2"], SummaryCounts(doc, today));
    }
    foreach (var id in foreignTasks) Assert.DoesNotContain(id.ToString(), adminBody, StringComparison.OrdinalIgnoreCase);

    var (outsiderStatus, outsiderBody) = await GetAsync(outsider, "?view=TEAM&pageSize=100&includeSummary=true");
    Assert.Equal(HttpStatusCode.OK, outsiderStatus);
    using (var doc = JsonDocument.Parse(outsiderBody))
    {
      Assert.Equal(foreignTasks.Order(), TaskIds(doc).Order());
      Assert.Equal(["0", "1", "1", "0", "0", "0", "0"], SummaryCounts(doc, today));
    }
    foreach (var id in scoped.Concat(sibling)) Assert.DoesNotContain(id.ToString(), outsiderBody, StringComparison.OrdinalIgnoreCase);

    // Client and no-queue-role identities keep the existing 403 boundary and receive no counts.
    foreach (var denied in new[] { client, reviewer })
    {
      var (status, body) = await GetAsync(denied, "?view=DUE_TODAY&pageSize=50&includeSummary=true");
      Assert.Equal(HttpStatusCode.Forbidden, status);
      using var doc = JsonDocument.Parse(body);
      Assert.Equal("scope.denied", doc.RootElement.GetProperty("code").GetString());
      Assert.DoesNotContain("summary", body, StringComparison.OrdinalIgnoreCase);
      foreach (var id in scoped) Assert.DoesNotContain(id.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    // Unsupported input combinations keep the existing queue validation code and message.
    foreach (var invalid in new[]
      {
        "?view=NOT_A_VIEW&includeSummary=true",
        "?view=DUE_TODAY&pageSize=0&includeSummary=true",
        "?view=DUE_TODAY&pageSize=101&includeSummary=true",
        $"?view=DUE_TODAY&afterDueDate={today:yyyy-MM-dd}&includeSummary=true",
        "?view=MY_TASKS&unassignedOnly=true&includeSummary=true"
      })
    {
      var (status, body) = await GetAsync(staff, invalid);
      Assert.Equal(HttpStatusCode.BadRequest, status);
      AssertFailure(body, "workflow.queue-invalid", QueueInvalidMessage);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // HTTP reads create no workflow activity, payment, acceptance, approval or worker job, and change no task state.
      Assert.Equal(before, await SideEffectFingerprintAsync(db));
      await db.Users.Where(x => x.Id == f.Staff.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    }

    // A revoked session epoch is refused before any queue data, including the previously served summary, is released.
    var (revokedStatus, revokedBody) = await GetAsync(staff, "?view=DUE_TODAY&pageSize=50&includeSummary=true");
    Assert.Equal(HttpStatusCode.Unauthorized, revokedStatus);
    AssertFailure(revokedBody, "session.unavailable", "Sign in again.");
    foreach (var leaked in new[] { "summary", "asOfDateUtc", "counts", "DUE_TODAY" })
      Assert.DoesNotContain(leaked, revokedBody, StringComparison.OrdinalIgnoreCase);
    foreach (var id in scoped) Assert.DoesNotContain(id.ToString(), revokedBody, StringComparison.OrdinalIgnoreCase);

    // An expired engagement grant keeps the existing boundary: the trusted actor resolver revokes it and ends the
    // session before the queue runs, so no rows or counts are released.
    using var expiredFactory = Factory(pg, expiredUser);
    using var expiredClient = expiredFactory.CreateClient(new() { AllowAutoRedirect = false }); await SignIn(expiredClient);
    var (expiredStatus, expiredBody) = await GetAsync(expiredClient, "?view=DUE_TODAY&pageSize=50&includeSummary=true");
    Assert.Equal(HttpStatusCode.Unauthorized, expiredStatus);
    AssertFailure(expiredBody, "session.unavailable", "Sign in again.");
    Assert.DoesNotContain("summary", expiredBody, StringComparison.OrdinalIgnoreCase);
    foreach (var id in scoped) Assert.DoesNotContain(id.ToString(), expiredBody, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task DisabledExperienceKeepsTheExistingDenialAndConfigurationUnchanged()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-WORKFLOW-DASHBOARD-OFF");
    var f = await PbcSeed.SeedAsync(pg);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    Guid task;
    await using (var db = new AuditSphereDbContext(pg.Options))
      task = await CreateTaskAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.EngagementId,
        "Synthetic due today while disabled", 1, today, f.Staff.Id);

    using var factory = Factory(pg, f.Staff, enabled: false);
    var configuration = factory.Services.GetRequiredService<IConfiguration>();
    Assert.False(configuration.GetValue<bool>("WorkflowExperience:Enabled"));
    using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
    var (anonymousStatus, anonymousBody) = await GetAsync(anonymous, "?view=DUE_TODAY&includeSummary=true");
    Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatus);
    Assert.Equal(string.Empty, anonymousBody);

    using var staff = factory.CreateClient(new() { AllowAutoRedirect = false }); await SignIn(staff);
    string before;
    await using (var db = new AuditSphereDbContext(pg.Options)) before = await SideEffectFingerprintAsync(db);
    foreach (var query in new[]
      {
        "?view=DUE_TODAY&pageSize=50&includeSummary=true", "?view=DUE_TODAY&pageSize=50",
        "?view=DUE_TODAY&pageSize=50&includeSummary=true"
      })
    {
      var (status, body) = await GetAsync(staff, query);
      Assert.Equal(HttpStatusCode.Forbidden, status);
      AssertFailure(body, "scope.denied", "Workflow task experience is not enabled on this server.");
      Assert.DoesNotContain(task.ToString(), body, StringComparison.OrdinalIgnoreCase);
      Assert.False(configuration.GetValue<bool>("WorkflowExperience:Enabled"));
    }
    await using (var db = new AuditSphereDbContext(pg.Options)) Assert.Equal(before, await SideEffectFingerprintAsync(db));
  }

  private static async Task SeedSiblingScopeAsync(OwnedPostgresDatabase pg, Guid firmId, Guid clientId, Guid engagementId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "SIBLING DASHBOARD CLIENT", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState { Id = clientId, FirmId = firmId });
    await db.SaveChangesAsync();
  }

  private static async Task<Guid> CreateTaskAsync(AuditSphereDbContext db, ActorContext actor, Guid engagementId,
    string title, int ordinal, DateOnly dueDate, Guid? assigneeUserId = null)
  {
    var created = await EngagementWorkflowTaskService.CreateAsync(db, actor, engagementId,
      new WorkflowTaskCreateRequest(title, WorkflowTaskPhases.AuditFieldwork, ordinal, dueDate,
        AssigneeUserId: assigneeUserId, IdempotencyKey: Guid.NewGuid()));
    Assert.True(created.Succeeded, created.Message);
    return created.Value;
  }

  private static async Task<(HttpStatusCode Status, string Body)> GetAsync(HttpClient c, string query)
  {
    using var response = await c.GetAsync(QueuePath + query);
    return (response.StatusCode, await response.Content.ReadAsStringAsync());
  }

  private static Guid[] TaskIds(JsonDocument doc) =>
    doc.RootElement.GetProperty("tasks").EnumerateArray().Select(x => x.GetProperty("taskId").GetGuid()).ToArray();

  /// <summary>Asserts the summary contract (UTC date, seven ordered {view,count} rows, counts as JSON text) and returns the counts.</summary>
  private static string[] SummaryCounts(JsonDocument doc, DateOnly today)
  {
    var summary = doc.RootElement.GetProperty("summary");
    Assert.Equal(JsonValueKind.Object, summary.ValueKind);
    Assert.Equal(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), summary.GetProperty("asOfDateUtc").GetString());
    var counts = summary.GetProperty("counts").EnumerateArray().ToArray();
    Assert.Equal(SummaryViewOrder, counts.Select(x => x.GetProperty("view").GetString()));
    foreach (var count in counts)
    {
      Assert.Equal(["view", "count"], count.EnumerateObject().Select(x => x.Name));
      Assert.Equal(JsonValueKind.String, count.GetProperty("count").ValueKind);
      Assert.Matches(CountText, count.GetProperty("count").GetString()!);
    }
    return counts.Select(x => x.GetProperty("count").GetString()!).ToArray();
  }

  private static void AssertFailure(string body, string code, string message)
  {
    using var doc = JsonDocument.Parse(body);
    Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    Assert.Equal(message, doc.RootElement.GetProperty("message").GetString());
  }

  private static async Task<string> SideEffectFingerprintAsync(AuditSphereDbContext db)
  {
    var tasks = await db.WorkTasks.AsNoTracking()
      .Select(x => new { x.Id, x.Status, x.DueDate, x.AssigneeUserId }).ToListAsync();
    var bindings = await db.WorkflowTaskBindings.AsNoTracking()
      .Select(x => new { x.WorkTaskId, x.Revision, x.Generation, x.PbcRequestId }).ToListAsync();
    var engagements = await db.Engagements.AsNoTracking().Select(x => new { x.Id, x.Generation, x.Status }).ToListAsync();
    return string.Join("|",
      string.Join(";", tasks.OrderBy(x => x.Id).Select(x => $"{x.Id}:{x.Status}:{x.DueDate}:{x.AssigneeUserId}")),
      string.Join(";", bindings.OrderBy(x => x.WorkTaskId).Select(x => $"{x.WorkTaskId}:{x.Revision}:{x.Generation}:{x.PbcRequestId}")),
      string.Join(";", engagements.OrderBy(x => x.Id).Select(x => $"{x.Id}:{x.Generation}:{x.Status}")),
      await db.WorkflowTaskActivities.CountAsync(),
      await db.Receipts.CountAsync(), await db.ReceiptAllocations.CountAsync(),
      await db.AcceptanceDecisions.CountAsync(),
      await db.Approvals.CountAsync(), await db.MaterialityApprovals.CountAsync(),
      await db.DurableOperations.CountAsync(), await db.OperationAttempts.CountAsync(),
      await db.RoleGrants.CountAsync());
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser user, bool enabled = true) => new(new Dictionary<string, string?> {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
    ["DevelopmentIdentity:Subject"] = user.Subject, ["DevelopmentIdentity:TenantId"] = user.TenantId,
    ["ExternalEffects:Enabled"] = "false", ["Application:AllowSimulationAdapters"] = "true", ["WorkflowExperience:Enabled"] = enabled.ToString()
  });

  private static async Task SignIn(HttpClient c)
  {
    using var signIn = await c.GetAsync("/auth/sign-in");
    using var session = await c.GetAsync("/api/ui/session");
  }
}
