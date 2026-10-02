using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Api;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Testing;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AuditSphereOps.Api.Tests;

public sealed class StandaloneApiHostTests
{
  [Fact]
  public async Task ApiHost_RunsWithoutBlazor_AndPreservesCookieScopeAndCsrfBoundaries()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-STANDALONE-API");
    var seed = await PbcSeed.SeedAsync(pg);
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = seed.Staff.Subject,
      ["DevelopmentIdentity:TenantId"] = seed.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/portfolio")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/_blazor/negotiate")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/app")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/unknown")).StatusCode);
    Assert.DoesNotContain(typeof(ApiHost).Assembly.GetReferencedAssemblies(), x => x.Name is "MudBlazor" or "AuditSphereOps.Web");
    using (var scope = factory.Services.CreateScope())
      Assert.Null(scope.ServiceProvider.GetService<AuthenticationStateProvider>());
    Assert.DoesNotContain(factory.Services.GetRequiredService<EndpointDataSource>().Endpoints,
      e => e is RouteEndpoint route && route.RoutePattern.RawText?.Contains("_blazor", StringComparison.Ordinal) == true);
    var signin = await client.GetAsync("/auth/sign-in");
    Assert.Equal(HttpStatusCode.Redirect, signin.StatusCode);
    Assert.Equal("/ui/app", signin.Headers.Location!.OriginalString);
    using var session = await client.GetAsync("/api/ui/session");
    var proof = Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("XSRF-TOKEN=")).Split(';')[0]["XSRF-TOKEN=".Length..]);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/portfolio")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/administration/microsoft365")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/ui/engagements/{Guid.NewGuid()}")).StatusCode);
    foreach (var path in new[] { "/api/ui/administration/access/assign", "/api/ui/administration/directory/bind" })
    {
      using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { request = (object?)null, reviewed = true, reviewDigest = new string('a', 64) }) };
      request.Headers.Add("X-XSRF-TOKEN", proof);
      Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }
    Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/ui/sign-out", new { })).StatusCode);
    using var signout = new HttpRequestMessage(HttpMethod.Post, "/api/ui/sign-out") { Content = JsonContent.Create(new { }) };
    signout.Headers.Add("X-XSRF-TOKEN", proof);
    Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(signout)).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/portfolio")).StatusCode);
  }

  [Fact]
  public void EndpointDecimals_KeepExactStringSerialization()
  {
    Assert.Equal("\"123456789012345678.123456\"", JsonSerializer.Serialize(123456789012345678.123456m, AuditSphereOps.Api.Ui.UiEndpoints.UiJson));
  }

  [Fact]
  public async Task MalformedAdministratorRoleRequests_AreTypedRefusals_WithoutMutations()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-ADMIN-REQUEST-VALIDATION");
    var seed = await PbcSeed.SeedAsync(pg);
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = seed.Admin.Subject,
      ["DevelopmentIdentity:TenantId"] = seed.Admin.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    var proof = Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("XSRF-TOKEN=")).Split(';')[0]["XSRF-TOKEN=".Length..]);
    foreach (var path in new[] { "/api/ui/administration/access/preview", "/api/ui/administration/access/assign" })
    {
      using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { request = (object?)null, reviewed = true, reviewDigest = new string('a', 64) }) };
      request.Headers.Add("X-XSRF-TOKEN", proof);
      using var response = await client.SendAsync(request);
      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
      using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
      Assert.Contains(body.RootElement.GetProperty("code").GetString(), new[] { "request.invalid", "review.required" });
    }
    await using var db = new AuditSphereOps.Infrastructure.Persistence.AuditSphereDbContext(pg.Options);
    Assert.Empty(db.RoleGrantChangeEvidences);
  }
}
