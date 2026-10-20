using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class EngagementActivityApiTests
{
  [Fact]
  public async Task ActivityRouteEnforcesSessionScopeValidationPagingNoStoreAndReadOnlyBehavior()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-ENGAGEMENT-ACTIVITY");
    var seed = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.User(seed.FirmId, "Staff");
    var siblingEngagementId = Guid.NewGuid();
    var requestId = Guid.NewGuid();
    var olderId = Guid.NewGuid();
    var newerId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(manager);
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, manager, "Manager", seed.ClientId, seed.EngagementId));
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
      {
        Id = siblingEngagementId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
      });
      db.PbcRequests.Add(new PbcRequest
      {
        Id = requestId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        Objective = "API activity fixture", EntityScope = "Test entity", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", Area = "Cash", RequestedFormat = "PDF", ControlTotals = "12 months",
        ClientOwnerUserId = seed.Client.Id, FirmOwnerUserId = manager.Id, ReviewerUserId = seed.Reviewer.Id,
        DueDate = "2026-12-31", Confidentiality = "Confidential", AcceptanceCriteria = "Readable source evidence.",
        State = PbcStates.Sent, Revision = 1, CreatedAt = now.AddMinutes(-5),
        CreatedByUserId = manager.Id, UpdatedAt = now.AddMinutes(-5)
      });
      PbcCommunication Communication(Guid id, DateTimeOffset createdAt, string subject) => new()
      {
        Id = id, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        PbcRequestId = requestId, AuthorUserId = manager.Id, Kind = PbcCommunicationKinds.Email,
        Body = "Private request body", RecipientEmail = "client@example.test", Subject = subject,
        PortalUrl = "private-capability", DeliveryState = PbcDeliveryStates.Sent, CreatedAt = createdAt
      };
      db.PbcCommunications.AddRange(
        Communication(olderId, now.AddMinutes(-2), "Older recorded request"),
        Communication(newerId, now.AddMinutes(-1), "Newer recorded request"));
      await db.SaveChangesAsync();
    }

    var path = $"/api/ui/engagements/{seed.EngagementId:D}/activity";
    using var managerFactory = Factory(pg, manager);
    using var managerClient = managerFactory.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal(HttpStatusCode.Unauthorized, (await managerClient.GetAsync(path)).StatusCode);
    await SignIn(managerClient);

    var before = await ReadStateAsync(pg, seed.EngagementId);
    using (var defaultResponse = await managerClient.GetAsync(path))
    {
      Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
      Assert.True(defaultResponse.Headers.CacheControl?.NoStore == true);
      var defaultPage = (await defaultResponse.Content.ReadFromJsonAsync<EngagementActivityPage>())!;
      Assert.Equal(2, defaultPage.Items.Count);
      Assert.False(defaultPage.HasMore);
    }
    using var firstResponse = await managerClient.GetAsync(path + "?pageSize=1");
    Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    Assert.True(firstResponse.Headers.CacheControl?.NoStore == true);
    var first = (await firstResponse.Content.ReadFromJsonAsync<EngagementActivityPage>())!;
    Assert.Equal(newerId, Assert.Single(first.Items).Id);
    Assert.True(first.HasMore);
    Assert.NotNull(first.NextCursor);

    var cursor = first.NextCursor!;
    var nextPath = path + "?" + string.Join("&", new[]
    {
      "from=" + Uri.EscapeDataString(first.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
      "to=" + Uri.EscapeDataString(first.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
      "snapshotAtUtc=" + Uri.EscapeDataString(first.SnapshotAtUtc.ToString("O", CultureInfo.InvariantCulture)),
      "afterAtUtc=" + Uri.EscapeDataString(cursor.AtUtc.ToString("O", CultureInfo.InvariantCulture)),
      "afterSource=" + cursor.Source.ToString(CultureInfo.InvariantCulture),
      "afterId=" + cursor.Id.ToString("D"),
      "pageSize=1"
    });
    using var secondResponse = await managerClient.GetAsync(nextPath);
    Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
    var second = (await secondResponse.Content.ReadFromJsonAsync<EngagementActivityPage>())!;
    Assert.Equal(first.SnapshotAtUtc, second.SnapshotAtUtc);
    Assert.Equal(olderId, Assert.Single(second.Items).Id);
    Assert.False(second.HasMore);
    Assert.Null(second.NextCursor);

    var partial = await managerClient.GetAsync(path + "?afterAtUtc=" + Uri.EscapeDataString(now.ToString("O", CultureInfo.InvariantCulture)));
    Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
    Assert.Equal("activity.cursor-invalid", await ErrorCodeAsync(partial));
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var longWindow = await managerClient.GetAsync(path + $"?from={today.AddDays(-90):yyyy-MM-dd}&to={today:yyyy-MM-dd}");
    Assert.Equal(HttpStatusCode.BadRequest, longWindow.StatusCode);
    Assert.Equal("activity.range-invalid", await ErrorCodeAsync(longWindow));

    using var sibling = await managerClient.GetAsync($"/api/ui/engagements/{siblingEngagementId:D}/activity");
    using var unknown = await managerClient.GetAsync($"/api/ui/engagements/{Guid.NewGuid():D}/activity");
    Assert.Equal(HttpStatusCode.Forbidden, sibling.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
    Assert.Equal(await sibling.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());

    using (var staffFactory = Factory(pg, seed.Staff))
    using (var staff = staffFactory.CreateClient())
    {
      await SignIn(staff);
      Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(path)).StatusCode);
    }
    using (var clientFactory = Factory(pg, seed.Client))
    using (var client = clientFactory.CreateClient())
    {
      await SignIn(client);
      Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }

    using (var post = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { value = "unsupported" }) })
    using (var response = await managerClient.SendAsync(post))
      Assert.False(response.IsSuccessStatusCode);
    Assert.Equal(before, await ReadStateAsync(pg, seed.EngagementId));

    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Users.Where(x => x.Id == manager.Id)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    Assert.Equal(HttpStatusCode.Unauthorized, (await managerClient.GetAsync(path)).StatusCode);
    Assert.Equal(before, await ReadStateAsync(pg, seed.EngagementId));
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser user) =>
    new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = user.Subject,
      ["DevelopmentIdentity:TenantId"] = user.TenantId,
      ["ExternalEffects:Enabled"] = "false",
      ["Application:AllowSimulationAdapters"] = "true"
    });

  private static async Task SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
  }

  private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
  {
    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return body.RootElement.GetProperty("code").GetString()!;
  }

  private static async Task<string> ReadStateAsync(OwnedPostgresDatabase pg, Guid engagementId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var state = new
    {
      Communications = await db.PbcCommunications.Where(x => x.EngagementId == engagementId).CountAsync(),
      Uploads = await db.PbcUploadIntents.Where(x => x.EngagementId == engagementId).CountAsync(),
      Notes = await db.ProcedureReviewNotes.Where(x => x.EngagementId == engagementId).CountAsync(),
      DurableOperations = await db.DurableOperations.CountAsync(x => x.EngagementId == engagementId),
      Decisions = await db.AuditProcedureReviews.Where(x => x.EngagementId == engagementId).CountAsync(),
      Approvals = await db.MaterialityApprovals.Where(x => x.EngagementId == engagementId).CountAsync(),
      Tasks = await db.WorkTasks.Where(x => x.EngagementId == engagementId)
        .OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.CompletedAt }).ToArrayAsync()
    };
    return JsonSerializer.Serialize(state);
  }
}
