using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AuditSphereOps.Api.Tests;

public sealed class InstallationApiTests
{
  private const string Tenant = "11111111-1111-4111-8111-111111111111";
  private const string ObjectId = "22222222-2222-4222-8222-222222222222";
  // Synthetic test proof, never a deployment credential.
  private const string Proof = "synthetic-installation-proof-for-tests";

  [Fact]
  public async Task Bootstrap_DeniesDuplicateClaimsAndIdentityAlreadyBoundToAnotherFirm()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-API-INSTALLATION-ISOLATION");
    using var factory = Factory(pg, Guid.NewGuid());
    using var duplicate = CookieClient(factory, Tenant, ObjectId, duplicateObjectClaim: true);
    Assert.Equal(HttpStatusCode.Forbidden, (await duplicate.GetAsync("/api/setup/session")).StatusCode);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(new AppUser { Id = Guid.NewGuid(), FirmId = Guid.NewGuid(), TenantId = Tenant, Subject = ObjectId,
      Email = "other-firm@example.test", DisplayName = "Other firm private identity", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    using var approved = CookieClient(factory, Tenant, ObjectId);
    using var response = await approved.GetAsync("/api/setup/session");
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    Assert.DoesNotContain("Other firm private identity", await response.Content.ReadAsStringAsync());
    Assert.Empty(await db.Microsoft365SetupSessions.ToListAsync());
    Assert.Empty(await db.RoleGrants.ToListAsync());
  }

  [Fact]
  public async Task Bootstrap_ExactIdentity_Csrf_Proof_AndFreshEpochAreRequired()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-API-INSTALLATION");
    var firm = Guid.NewGuid();
    using var factory = Factory(pg, firm);
    using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/setup/session")).StatusCode);
    using var wrongTenant = CookieClient(factory, Guid.NewGuid().ToString(), ObjectId);
    using var wrongObject = CookieClient(factory, Tenant, Guid.NewGuid().ToString());
    foreach (var denied in new[] { wrongTenant, wrongObject })
    {
      using var response = await denied.GetAsync("/api/setup/session");
      Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
      Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }
    using var client = CookieClient(factory, Tenant, ObjectId);
    using var session = await client.GetAsync("/api/setup/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    Assert.True(session.Headers.CacheControl!.NoStore);
    var body = await session.Content.ReadAsStringAsync();
    Assert.DoesNotContain(Tenant, body);
    Assert.DoesNotContain(Proof, body);
    Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("canBootstrap").GetBoolean());
    var csrf = Csrf(session);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/setup/bootstrap", new { proof = Proof, reviewed = true })).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/api/setup/bootstrap", "forged", new { proof = Proof, reviewed = true })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/api/setup/bootstrap", csrf, new { proof = "wrong", reviewed = true })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/api/setup/bootstrap", csrf, new { proof = Proof, reviewed = false })).StatusCode);
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await db.Users.ToListAsync());
    Assert.Empty(await db.Microsoft365SetupSessions.ToListAsync());
    using var accepted = await Post(client, "/api/setup/bootstrap", csrf, new { proof = Proof, reviewed = true });
    Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    Assert.Equal("{\"completed\":true,\"requiresFreshSignIn\":true}", await accepted.Content.ReadAsStringAsync());
    var user = Assert.Single(await db.Users.AsNoTracking().ToListAsync());
    Assert.Equal(firm, user.FirmId);
    Assert.Equal(ObjectId, user.Subject);
    Assert.Single(await db.RoleGrants.ToListAsync());
    Assert.Single(await db.RoleGrantChangeEvidences.ToListAsync());
    var persisted = Assert.Single(await db.Microsoft365SetupSessions.AsNoTracking().ToListAsync());
    Assert.NotEqual(Proof, persisted.BootstrapProofHash);
    Assert.Equal(64, persisted.CapabilityHash.Length);
    Assert.DoesNotContain(Proof, JsonSerializer.Serialize(persisted));
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/session")).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/api/setup/bootstrap", csrf, new { proof = Proof, reviewed = true })).StatusCode);
    Assert.Single(await db.RoleGrants.ToListAsync());
    using var fresh = CookieClient(factory, Tenant, ObjectId, user.SessionEpoch);
    Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/ui/session")).StatusCode);
    var grant = await db.RoleGrants.SingleAsync();
    grant.RevokedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    using var closed = await client.GetAsync("/api/setup/session");
    Assert.False(JsonDocument.Parse(await closed.Content.ReadAsStringAsync()).RootElement.GetProperty("canBootstrap").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/api/setup/bootstrap", csrf, new { proof = Proof, reviewed = true })).StatusCode);
    Assert.NotNull((await db.RoleGrants.AsNoTracking().SingleAsync()).RevokedAt);
  }

  [Fact]
  public async Task TenantPreparation_UsesDeploymentTenant_RevisionFence_AndDoesNotAssertConsent()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-API-PREPARE-TENANT");
    using var factory = Factory(pg, Guid.NewGuid());
    using var initial = CookieClient(factory, Tenant, ObjectId);
    using var bootstrapSession = await initial.GetAsync("/api/setup/session");
    Assert.Equal(HttpStatusCode.OK, (await Post(initial, "/api/setup/bootstrap", Csrf(bootstrapSession), new { proof = Proof, reviewed = true })).StatusCode);
    await using var db = new AuditSphereDbContext(pg.Options);
    var user = await db.Users.AsNoTracking().SingleAsync();
    using var admin = CookieClient(factory, Tenant, ObjectId, user.SessionEpoch);
    using var session = await admin.GetAsync("/api/ui/session");
    var csrf = Csrf(session);
    var draft = await db.Microsoft365SetupDrafts.AsNoTracking().SingleAsync();
    const string path = "/api/ui/administration/microsoft365/prepare";
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, path, csrf, new { draftId = draft.Id, expectedRevision = draft.Revision.ToString(), reviewed = false })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, path, csrf, new { draftId = draft.Id, expectedRevision = "999", reviewed = true })).StatusCode);
    using var prepared = await Post(admin, path, csrf, new { draftId = draft.Id, expectedRevision = draft.Revision.ToString(), reviewed = true });
    Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
    var revision = Assert.Single(await db.Microsoft365ConnectionRevisions.AsNoTracking().ToListAsync());
    Assert.Equal(Tenant, revision.TenantId);
    Assert.Equal("REQUIRED", revision.ConsentState);
    Assert.Null(revision.VerifiedAt);
    Assert.Empty(await db.TenantCapabilityVerifications.ToListAsync());
    Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, path, csrf, new { draftId = draft.Id, expectedRevision = draft.Revision.ToString(), reviewed = true })).StatusCode);
    Assert.Single(await db.Microsoft365ConnectionRevisions.ToListAsync());
    using var workspace = await admin.GetAsync("/api/ui/administration/microsoft365");
    Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);
    var body = await workspace.Content.ReadAsStringAsync();
    Assert.Contains("\"configuredTenantId\":\"" + Tenant, body);
    Assert.DoesNotContain(Proof, body);
    Assert.DoesNotContain(revision.RuntimeCredentialReference, body);
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, Guid firm) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
    ["DevelopmentIdentity:Enabled"] = "false", ["Identity:TenantId"] = Tenant,
    ["Identity:ClientId"] = "33333333-3333-4333-8333-333333333333", ["Identity:ClientSecret"] = "synthetic-test-placeholder",
    ["Setup:FirmId"] = firm.ToString(), ["Setup:InstallationId"] = "synthetic-installation",
    ["Setup:BootstrapProofHash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Proof))).ToLowerInvariant(),
    ["Setup:InitialAdministratorTenantId"] = Tenant, ["Setup:InitialAdministratorObjectId"] = ObjectId,
    ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
  });

  private static HttpClient CookieClient(StandaloneApiApplicationFactory factory, string tenant, string objectId, long? epoch = null, bool duplicateObjectClaim = false)
  {
    var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
    var claims = new List<Claim> { new("tid", tenant), new("oid", objectId), new("name", "Synthetic administrator"), new("email", "admin@example.test") };
    if (duplicateObjectClaim) claims.Add(new("oid", Guid.NewGuid().ToString()));
    if (epoch is { } e) claims.Add(new(TrustedActorResolver.SessionEpochClaimType, e.ToString()));
    var properties = new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) };
    var ticket = options.TicketDataFormat.Protect(new(new ClaimsPrincipal(new ClaimsIdentity(claims, "fixture")), properties, CookieAuthenticationDefaults.AuthenticationScheme));
    var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    client.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" + ticket);
    return client;
  }

  private static string Csrf(HttpResponseMessage session) => Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie")
    .Single(h => h.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  private static Task<HttpResponseMessage> Post(HttpClient client, string path, string csrf, object input)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(input) };
    request.Headers.Add("X-XSRF-TOKEN", csrf);
    return client.SendAsync(request);
  }
}
