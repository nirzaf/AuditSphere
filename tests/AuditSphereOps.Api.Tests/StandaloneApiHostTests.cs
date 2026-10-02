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
}
