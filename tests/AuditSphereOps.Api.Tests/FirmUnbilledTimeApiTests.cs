using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class FirmUnbilledTimeApiTests
{
  [Fact]
  public async Task AuthorizedGetReturnsExactDecimalsAndStableFullReportSubtotalsAcrossPages()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("FIRM-UNBILLED-TIME-API");
    var fixture = await PbcSeed.SeedAsync(pg);
    await SeedApprovedTimeAsync(pg, fixture, 11);
    using var managerFactory = Factory(pg, fixture.Admin);
    using var manager = managerFactory.CreateClient(new() { AllowAutoRedirect = false });
    var path = "/api/ui/finance/unbilled-time?from=2026-04-15&to=2026-04-15&page=0&pageSize=10";
    Assert.Equal(HttpStatusCode.Unauthorized, (await manager.GetAsync(path)).StatusCode);
    await SignInAsync(manager);

    using var firstResponse = await manager.GetAsync(path);
    Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    using var first = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
    var firstRoot = first.RootElement;
    Assert.Equal("11", firstRoot.GetProperty("totalEntries").GetString());
    Assert.Equal(0, firstRoot.GetProperty("page").GetInt32());
    Assert.Equal(10, firstRoot.GetProperty("rows").GetArrayLength());
    Assert.Equal("3.333333", firstRoot.GetProperty("rows")[0].GetProperty("standardValue").GetString());

    using var secondResponse = await manager.GetAsync(path.Replace("page=0", "page=1", StringComparison.Ordinal));
    Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
    using var second = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
    Assert.Equal(1, second.RootElement.GetProperty("page").GetInt32());
    Assert.Single(second.RootElement.GetProperty("rows").EnumerateArray());
    Assert.Equal(firstRoot.GetProperty("subtotals").GetRawText(),
      second.RootElement.GetProperty("subtotals").GetRawText());
    Assert.Equal("36.666663", Assert.Single(firstRoot.GetProperty("subtotals").EnumerateArray())
      .GetProperty("standardValue").GetString());

    using var staffFactory = Factory(pg, fixture.Staff);
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(staff);
    Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(path)).StatusCode);
    using var foreignFilter = await manager.GetAsync(path + "&clientId=" + Guid.NewGuid().ToString("D"));
    Assert.Equal(HttpStatusCode.Forbidden, foreignFilter.StatusCode);
    Assert.DoesNotContain("PBC TEST CLIENT", await foreignFilter.Content.ReadAsStringAsync());

    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Equal(11, await proof.TimeEntries.CountAsync());
    Assert.Empty(await proof.Invoices.ToListAsync());
    Assert.Empty(await proof.BillingSourceAllocations.ToListAsync());
  }

  [Fact]
  public async Task InvalidDatesReturnTheExactQueryErrorAndClientUsersRemainDenied()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("FIRM-UNBILLED-TIME-API-INPUT");
    var fixture = await PbcSeed.SeedAsync(pg);
    using var factory = Factory(pg, fixture.Admin);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(client);
    var invalidPaths = new[]
    {
      "/api/ui/finance/unbilled-time?to=2026-04-15",
      "/api/ui/finance/unbilled-time?from=15-04-2026&to=2026-04-15",
      "/api/ui/finance/unbilled-time?from=2026-04-16&to=2026-04-15",
      "/api/ui/finance/unbilled-time?from=2025-04-14&to=2026-04-15",
      "/api/ui/finance/unbilled-time?from=2026-04-15&to=2026-04-15&pageSize=20",
      "/api/ui/finance/unbilled-time?from=2026-04-15&to=2026-04-15&page=-1",
      "/api/ui/finance/unbilled-time?from=2026-04-15&to=2026-04-15&clientId=00000000-0000-0000-0000-000000000000",
    };
    foreach (var path in invalidPaths)
    {
      using var response = await client.GetAsync(path);
      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
      var error = (await response.Content.ReadFromJsonAsync<JsonElement>())!;
      Assert.Equal("wip.query-invalid", error.GetProperty("code").GetString());
      Assert.Equal("Choose a valid work-date range of at most 366 days and a supported page.",
        error.GetProperty("message").GetString());
    }

    using var clientFactory = Factory(pg, fixture.Client);
    using var clientUser = clientFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(clientUser);
    using var denied = await clientUser.GetAsync("/api/ui/finance/unbilled-time?from=2026-04-15&to=2026-04-15");
    Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    Assert.Equal("scope.denied", (await denied.Content.ReadFromJsonAsync<JsonElement>())!.GetProperty("code").GetString());
  }

  private static async Task SeedApprovedTimeAsync(OwnedPostgresDatabase pg, PbcSeed.Fixture fixture, int count)
  {
    var now = DateTimeOffset.UtcNow;
    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, fixture.Admin, "FinanceManager"));
    var task = new WorkTask
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
      EngagementId = fixture.EngagementId, Title = "Synthetic approved time", CreatedAt = now
    };
    var card = new RateCardVersion
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, Role = "Staff", Activity = "Synthetic WIP",
      Currency = "QAR", RatePerHour = 200m, Status = PracticeTimeStates.RateApproved,
      CreatedByUserId = fixture.Admin.Id, ApprovedByUserId = fixture.Reviewer.Id,
      ApprovedAt = now, CreatedAt = now
    };
    db.WorkTasks.Add(task);
    db.RateCardVersions.Add(card);
    for (var index = 0; index < count; index++)
      db.TimeEntries.Add(new TimeEntry
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
        EngagementId = fixture.EngagementId, TaskId = task.Id, UserId = fixture.Staff.Id,
        WorkDate = new DateOnly(2026, 4, 15), StartMinute = index * 2, DurationMinutes = 1,
        Role = "Staff", Activity = "Synthetic WIP", BillableClassification = PracticeTimeStates.Billable,
        Narrative = "Synthetic exact-value row", NarrativeVisibility = PracticeTimeStates.NarrativeInternal,
        Status = PracticeTimeStates.TimeApproved, Currency = "QAR", Revision = 1,
        RateCardVersionId = card.Id, RatePerHour = 200m, SubmittedAt = now,
        ApprovedByUserId = fixture.Admin.Id, ApprovedAt = now, CreatedAt = now
      });
    await db.SaveChangesAsync();
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser user) =>
    new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = user.Subject,
      ["DevelopmentIdentity:TenantId"] = user.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    });

  private static async Task SignInAsync(HttpClient client)
  {
    using var signIn = await client.GetAsync("/auth/sign-in?returnUrl=%2Fui%2Fapp");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
  }
}
