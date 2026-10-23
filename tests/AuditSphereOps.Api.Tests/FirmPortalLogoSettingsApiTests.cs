using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Testing;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class FirmPortalLogoSettingsApiTests
{
  private const string TestLogoPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAN0lEQVR4nO3QQREAMAgDQYoYvGGtJstUBZ+Ngcvsqb4vFpebcQcIECBAgAABAgQIECBAgACBLzDWWQJ4Rpw0uQAAAABJRU5ErkJggg==";
  private const string ProfilePath = "/api/ui/commercial-settings/profile";
  private const string SettingsPath = "/api/ui/commercial-settings";

  [Fact]
  public async Task ReviewedLogoPostReturnsIdAndSettingsExposeOnlyAdditiveLogoMetadata()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-FIRM-PORTAL-LOGO-SETTINGS");
    var fixture = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.User(fixture.FirmId, "Staff");
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.Users.Add(partner);
      seed.RoleGrants.Add(new AuditSphereOps.Domain.Security.RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, UserId = partner.Id, Role = "Partner",
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Admin.Id
      });
      await seed.SaveChangesAsync();
    }
    using var factory = Factory(pg, partner);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);

    using var created = await Post(client, csrf, ProfilePath, ProfileBody("0", "Synthetic logo settings firm",
      LogoPngBase64: TestLogoPngBase64));
    Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    using (var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
      Assert.True(createdJson.RootElement.GetProperty("id").GetString()!.Length > 0);

    using (var response = await client.GetAsync(SettingsPath))
    {
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
      var json = await response.Content.ReadAsStringAsync();
      using var document = JsonDocument.Parse(json);
      var profile = document.RootElement.GetProperty("profile");
      Assert.True(profile.GetProperty("hasLogo").GetBoolean());
      Assert.Matches("^[0-9a-f]{64}$", profile.GetProperty("logoSha256Hex").GetString()!);
      Assert.False(profile.TryGetProperty("logoPngBytes", out _));
      Assert.False(profile.TryGetProperty("logoPngBase64", out _));
      Assert.False(json.Contains(TestLogoPngBase64, StringComparison.Ordinal));
    }

    // Legacy clients omit the appended intent fields; the existing logo remains KEEP.
    using var retained = await Post(client, csrf, ProfilePath, ProfileBody("1", "Synthetic revised logo settings firm"));
    Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
    var current = await client.GetFromJsonAsync<JsonElement>(SettingsPath);
    Assert.Equal("2", current.GetProperty("profile").GetProperty("version").GetString());
    Assert.True(current.GetProperty("profile").GetProperty("hasLogo").GetBoolean());
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.Equal(2, await db.FirmCommercialProfiles.CountAsync(x => x.FirmId == fixture.FirmId));
  }

  [Fact]
  public async Task LogoSettingsPreserveAuthenticationCsrfValidationAndConflictStatuses()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-FIRM-PORTAL-LOGO-ERRORS");
    var fixture = await PbcSeed.SeedAsync(pg);
    using var noSessionFactory = Factory(pg, fixture.Admin);
    using (var noSession = noSessionFactory.CreateClient(new() { AllowAutoRedirect = false }))
    {
      using var response = await Post(noSession, "", ProfilePath, ProfileBody("0", "Synthetic firm"));
      Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    using var factory = Factory(pg, fixture.Admin);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    using (var badCsrf = await Post(client, "forged", ProfilePath, ProfileBody("0", "Synthetic firm")))
    {
      Assert.Equal(HttpStatusCode.Forbidden, badCsrf.StatusCode);
      Assert.Equal("csrf.invalid", await ErrorCode(badCsrf));
    }

    using (var invalid = await Post(client, csrf, ProfilePath, ProfileBody("0", "Synthetic firm", LogoPngBase64: "AQID")))
    {
      Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
      Assert.Equal("commercial.logo-invalid", await ErrorCode(invalid));
    }
    using (var ambiguous = await Post(client, csrf, ProfilePath,
      ProfileBody("0", "Synthetic firm", LogoPngBase64: TestLogoPngBase64, RemoveLogo: true)))
    {
      Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
      Assert.Equal("commercial.logo-invalid", await ErrorCode(ambiguous));
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.Empty(await db.FirmCommercialProfiles.ToListAsync());

    using (var initial = await Post(client, csrf, ProfilePath, ProfileBody("0", "Synthetic firm")))
      Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
    using (var stale = await Post(client, csrf, ProfilePath, ProfileBody("0", "Different stale firm")))
    {
      Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
      Assert.Equal("revision.stale", await ErrorCode(stale));
    }

    using var staffFactory = Factory(pg, fixture.Staff);
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    var staffCsrf = await SignIn(staff);
    using (var denied = await Post(staff, staffCsrf, ProfilePath, ProfileBody("1", "Denied staff update", LogoPngBase64: TestLogoPngBase64)))
    {
      Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
      Assert.Equal("scope.denied", await ErrorCode(denied));
    }

    using var clientFactory = Factory(pg, fixture.Client);
    using var portalClient = clientFactory.CreateClient(new() { AllowAutoRedirect = false });
    var clientCsrf = await SignIn(portalClient);
    using var clientDenied = await Post(portalClient, clientCsrf, ProfilePath,
      ProfileBody("1", "Denied client update", LogoPngBase64: TestLogoPngBase64));
    Assert.Equal(HttpStatusCode.Forbidden, clientDenied.StatusCode);
    Assert.Equal("scope.denied", await ErrorCode(clientDenied));
  }

  [Fact]
  public async Task PricingPolicyScopeDenialIsForbiddenWhileValidationSuccessAndCsrfStatusesRemainDistinct()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PRICING-POLICY-SCOPE-STATUS");
    var fixture = await PbcSeed.SeedAsync(pg);
    const string path = "/api/ui/commercial-settings/pricing-policy";

    using var staffFactory = Factory(pg, fixture.Staff);
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    var staffCsrf = await SignIn(staff);
    using (var denied = await Post(staff, staffCsrf, path, PricingPolicyBody()))
    {
      Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
      Assert.Equal("scope.denied", await ErrorCode(denied));
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.Empty(await db.FirmPricingPolicies.ToListAsync());

    using var adminFactory = Factory(pg, fixture.Admin);
    using var admin = adminFactory.CreateClient(new() { AllowAutoRedirect = false });
    var adminCsrf = await SignIn(admin);
    using (var invalid = await Post(admin, adminCsrf, path, PricingPolicyBody("50000", "1000")))
    {
      Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
      Assert.Equal("pricing-policy.invalid", await ErrorCode(invalid));
    }
    using (var missingCsrf = await Post(admin, "", path, PricingPolicyBody()))
    {
      Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
      Assert.Equal("csrf.invalid", await ErrorCode(missingCsrf));
    }
    using (var created = await Post(admin, adminCsrf, path, PricingPolicyBody()))
    {
      Assert.Equal(HttpStatusCode.OK, created.StatusCode);
      using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
      Assert.NotEqual(Guid.Empty, json.RootElement.GetProperty("id").GetGuid());
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.Single(await db.FirmPricingPolicies.ToListAsync());
  }

  private static object ProfileBody(string version, string legalName, string? LogoPngBase64 = null, bool RemoveLogo = false)
  {
    var body = new Dictionary<string, object?>
    {
      ["version"] = version, ["legalName"] = legalName, ["address"] = "Doha", ["email"] = "firm@example.test",
      ["phone"] = "123", ["accent"] = "#2B6CB0", ["closing"] = "Closing", ["history"] = "",
      ["credentials"] = "", ["methodology"] = "", ["reviewed"] = true
    };
    if (LogoPngBase64 is not null) body["logoPngBase64"] = LogoPngBase64;
    if (RemoveLogo) body["removeLogo"] = true;
    return body;
  }

  private static object PricingPolicyBody(string minimumFee = "1000", string maximumFee = "50000") => new
  {
    currency = "QAR", minimumFee, maximumFee, maxDiscount = "10", validityDays = "30",
    expectedRevision = (long?)null, reviewed = true
  };

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser user) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
    ["DevelopmentIdentity:Enabled"] = "true",
    ["DevelopmentIdentity:Subject"] = user.Subject,
    ["DevelopmentIdentity:TenantId"] = user.TenantId,
    ["ExternalEffects:Enabled"] = "false",
    ["Application:AllowSimulationAdapters"] = "true"
  });

  private static async Task<string> SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))
      .Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static Task<HttpResponseMessage> Post(HttpClient client, string csrf, string path, object body)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
    if (csrf.Length > 0) request.Headers.Add("X-XSRF-TOKEN", csrf);
    return client.SendAsync(request);
  }

  private static async Task<string?> ErrorCode(HttpResponseMessage response)
  {
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return json.RootElement.TryGetProperty("code", out var value) ? value.GetString() : null;
  }
}
