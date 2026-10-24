using System.Net;
using System.Text.Json;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Testing;

namespace AuditSphereOps.Api.Tests;

public sealed class ScheduleSourceIntakeApiTests
{
  [Fact]
  public async Task SourceCatalogAndIndependentQueueRoutesEnforceTheirStaffScopes()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-SCHEDULE-SOURCE-CATALOG");
    var f = await PbcSeed.SeedAsync(pg);
    var root = $"/api/ui/engagements/{f.EngagementId}/fieldwork/schedule-intake";

    using var staffFactory = Factory(pg.ConnectionString, f.Staff);
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync(root + "/options")).StatusCode);
    await SignIn(staff);

    using var optionsResponse = await staff.GetAsync(root + "/options?pbcOffset=0&trialBalanceOffset=0&limit=25");
    var options = await ReadValueAsync(optionsResponse);
    Assert.Empty(options.GetProperty("pbcSources").EnumerateArray());
    Assert.Equal(0, options.GetProperty("nextPbcOffset").GetInt32());
    Assert.False(options.GetProperty("hasMorePbcSources").GetBoolean());
    Assert.Empty(options.GetProperty("trialBalances").EnumerateArray());
    Assert.Equal(0, options.GetProperty("nextTrialBalanceOffset").GetInt32());
    Assert.False(options.GetProperty("hasMoreTrialBalances").GetBoolean());
    Assert.Equal(HttpStatusCode.Forbidden,
      (await staff.GetAsync(root + "/review-candidates?offset=0&limit=25")).StatusCode);

    using var reviewerFactory = Factory(pg.ConnectionString, f.Reviewer);
    using var reviewer = reviewerFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(reviewer);
    using var candidatesResponse = await reviewer.GetAsync(root + "/review-candidates?offset=0&limit=25");
    var candidates = await ReadValueAsync(candidatesResponse);
    Assert.Equal(0, candidates.GetProperty("offset").GetInt32());
    Assert.Equal(25, candidates.GetProperty("limit").GetInt32());
    Assert.False(candidates.GetProperty("hasMore").GetBoolean());
    Assert.Empty(candidates.GetProperty("items").EnumerateArray());

    using var clientFactory = Factory(pg.ConnectionString, f.Client);
    using var client = clientFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(root + "/options")).StatusCode);
  }

  private static StandaloneApiApplicationFactory Factory(string connection, AppUser user) => new(
    new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = connection,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = user.Subject,
      ["DevelopmentIdentity:TenantId"] = user.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    });

  private static async Task SignIn(HttpClient client)
  {
    using var signIn = await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
  }

  private static async Task<JsonElement> ReadValueAsync(HttpResponseMessage response)
  {
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.TryGetProperty("value", out var value)
      ? value.Clone()
      : document.RootElement.Clone();
  }
}
