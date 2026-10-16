using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditSphereOps.Api.Tests;

public sealed class DirectoryBindingApiTests
{
  private sealed class Reader(DirectoryCandidate candidate) : IMicrosoftDirectoryReader
  {
    public DirectoryCandidate Current { get; set; } = candidate;
    public int Calls;
    public Task<DirectoryCandidate> GetByIdAsync(string tenantId, string objectId, CancellationToken ct)
    {
      Calls++;
      return Task.FromResult(Current);
    }
    public Task<DirectoryCandidatePage> SearchAsync(string tenantId, string prefix, string? pageToken, CancellationToken ct, string? domain = null) => throw new NotSupportedException();
    public Task<DirectoryCandidatePage> BrowseActiveAsync(string tenantId, string? domain, string? pageToken, CancellationToken ct) => throw new NotSupportedException();
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, PbcSeed.Fixture f, string tenant,
    Reader reader, bool enabled = true, bool staff = false) => new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = staff ? f.Staff.Subject : f.Admin.Subject,
      ["DevelopmentIdentity:TenantId"] = tenant,
      ["Identity:TenantId"] = tenant,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    }, services =>
    {
      services.RemoveAll<IMicrosoftDirectoryReader>();
      services.AddSingleton<IMicrosoftDirectoryReader>(reader);
      services.RemoveAll<TenantAdministrationSettings>();
      services.AddSingleton(new TenantAdministrationSettings(
        new TenantAdministrationOptions(tenant, DirectoryReadEnabled: enabled), false, false, null));
    });

  private static async Task<PbcSeed.Fixture> SeedAsync(OwnedPostgresDatabase pg, string tenant)
  {
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await db.Users.Where(x => x.FirmId == f.FirmId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TenantId, tenant));
    return f;
  }

  private static async Task<string> SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    using var response = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return Uri.UnescapeDataString(response.Headers.GetValues("Set-Cookie")
      .Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static Task<HttpResponseMessage> Bind(HttpClient client, string proof, Guid id, bool guest, bool reviewed = true)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/ui/administration/directory/bind")
    {
      Content = JsonContent.Create(new { objectId = id, guest, reviewed })
    };
    request.Headers.Add("X-XSRF-TOKEN", proof);
    return client.SendAsync(request);
  }

  private static string Status(string tenant, Guid id) =>
    $"/api/ui/administration/directory/binding?tenantId={tenant}&objectId={id}";

  [Theory, InlineData(false), InlineData(true)]
  public async Task ExactMemberOrGuestBindingCanBeReconciledWithoutRepeatingProviderReadOrMutation(bool guest)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-DIRECTORY-RECONCILE");
    var tenant = Guid.NewGuid().ToString("D");
    var f = await SeedAsync(pg, tenant);
    var id = Guid.NewGuid();
    var reader = new Reader(new(tenant, id.ToString("D"), "Synthetic directory identity", "directory@example.test", true, guest ? "Guest" : "Member"));
    using var factory = Factory(pg, f, tenant, reader);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var proof = await SignIn(client);
    using var absent = await client.GetAsync(Status(tenant, id));
    Assert.Equal(HttpStatusCode.OK, absent.StatusCode);
    var before = await absent.Content.ReadFromJsonAsync<JsonElement>();
    Assert.False(before.GetProperty("bound").GetBoolean());
    Assert.Equal(JsonValueKind.Null, before.GetProperty("userId").ValueKind);
    Assert.Equal(0, reader.Calls);
    Assert.Equal(HttpStatusCode.BadRequest, (await Bind(client, proof, id, guest, false)).StatusCode);
    using var bound = await Bind(client, proof, id, guest);
    Assert.Equal(HttpStatusCode.OK, bound.StatusCode);
    var userId = (await bound.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetGuid();
    using var recovered = await client.GetAsync(Status(tenant, id));
    Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    Assert.True(recovered.Headers.CacheControl!.NoStore);
    var value = await recovered.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(tenant, value.GetProperty("tenantId").GetString());
    Assert.Equal(id.ToString("D"), value.GetProperty("objectId").GetString());
    Assert.Equal(userId, value.GetProperty("userId").GetGuid());
    Assert.Equal(guest ? "Client" : "Staff", value.GetProperty("userKind").GetString());
    Assert.True(value.GetProperty("bound").GetBoolean());
    Assert.Equal("ENABLED", value.GetProperty("directoryEnabledState").GetString());
    Assert.False(value.TryGetProperty("receipt", out _));
    Assert.Equal(1, reader.Calls);
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.Single(await db.DirectoryUserObservations.ToListAsync());
    Assert.False(await db.RoleGrants.AnyAsync(x => x.UserId == userId));
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Status(Guid.NewGuid().ToString("D"), id))).StatusCode);
    using var unknown = await client.GetAsync(Status(tenant, Guid.NewGuid()));
    Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
    Assert.False((await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("bound").GetBoolean());
  }

  [Theory, InlineData(false), InlineData(true)]
  public async Task NegativeExactVerificationWithMalformedProfileInvalidatesPriorPositiveForRoleAssignment(bool typeChanged)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-DIRECTORY-NEGATIVE");
    var tenant = Guid.NewGuid().ToString("D");
    var f = await SeedAsync(pg, tenant);
    var id = Guid.NewGuid();
    var reader = new Reader(new(tenant, id.ToString("D"), "Original synthetic name", "directory@example.test", true, "Member"));
    using var factory = Factory(pg, f, tenant, reader);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var proof = await SignIn(client);
    using var bound = await Bind(client, proof, id, false);
    Assert.Equal(HttpStatusCode.OK, bound.StatusCode);
    var userId = (await bound.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetGuid();
    reader.Current = reader.Current with
    {
      AccountEnabled = typeChanged, UserType = typeChanged ? "Guest" : "Member",
      DisplayName = new string('x', 301), UserPrincipalName = "unsafe\0upn"
    };
    Assert.Equal(HttpStatusCode.BadRequest, (await Bind(client, proof, id, false)).StatusCode);
    using var status = await client.GetAsync(Status(tenant, id));
    var state = await status.Content.ReadFromJsonAsync<JsonElement>();
    Assert.True(state.GetProperty("bound").GetBoolean());
    Assert.Equal(typeChanged ? "ENABLED" : "DISABLED", state.GetProperty("directoryEnabledState").GetString());
    Assert.Equal(typeChanged ? "Guest" : "Member", state.GetProperty("directoryUserType").GetString());
    var request = new AuditSphereOps.Application.Security.RoleAssignmentRequest(userId, "Staff", "CLIENT", f.ClientId,
      Reason: "Refuse negative verification", ConfirmScopeExpansion: true);
    using var previewRequest = new HttpRequestMessage(HttpMethod.Post, "/api/ui/administration/access/preview") { Content = JsonContent.Create(request) };
    previewRequest.Headers.Add("X-XSRF-TOKEN", proof);
    using var preview = await client.SendAsync(previewRequest);
    Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
    var digest = (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("digest").GetString();
    using var assignRequest = new HttpRequestMessage(HttpMethod.Post, "/api/ui/administration/access/assign")
    {
      Content = JsonContent.Create(new { request, reviewDigest = digest, reviewed = true })
    };
    assignRequest.Headers.Add("X-XSRF-TOKEN", proof);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(assignRequest)).StatusCode);
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.Equal("Original synthetic name", await db.Users.Where(x => x.Id == userId).Select(x => x.DisplayName).SingleAsync());
    Assert.False(await db.RoleGrants.AnyAsync(x => x.UserId == userId));
    Assert.Equal(2, await db.DirectoryUserObservations.CountAsync());
  }

  [Theory, InlineData(false), InlineData(true)]
  public async Task ReconciliationRequiresEnabledConfigurationAndCurrentFirmAdministrator(bool staff)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-DIRECTORY-AUTHORITY");
    var tenant = Guid.NewGuid().ToString("D");
    var f = await SeedAsync(pg, tenant);
    var id = Guid.NewGuid();
    var reader = new Reader(new(tenant, id.ToString("D"), "Synthetic identity", "directory@example.test", true, "Member"));
    using var factory = Factory(pg, f, tenant, reader, enabled: staff, staff: staff);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var proof = await SignIn(client);
    Assert.Equal(staff ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest, (await client.GetAsync(Status(tenant, id))).StatusCode);
    Assert.Equal(staff ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest, (await Bind(client, proof, id, false)).StatusCode);
    Assert.Equal(0, reader.Calls);
  }

  [Theory, InlineData(false), InlineData(true)]
  public async Task ReconciliationRefusesRevokedOrExpiredSessionWithoutProviderCall(bool epoch)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-DIRECTORY-SESSION");
    var tenant = Guid.NewGuid().ToString("D");
    var f = await SeedAsync(pg, tenant);
    var id = Guid.NewGuid();
    var reader = new Reader(new(tenant, id.ToString("D"), "Synthetic identity", "directory@example.test", true, "Member"));
    using var factory = Factory(pg, f, tenant, reader);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);
    await using var db = new AuditSphereDbContext(pg.Options);
    if (epoch) await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    else await db.RoleGrants.Where(x => x.UserId == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    using var result = await client.GetAsync(Status(tenant, id));
    Assert.Equal(epoch ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, result.StatusCode);
    Assert.Equal(0, reader.Calls);
  }
}
