using System.Net;
using System.Net.Http.Json;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

/// <summary>
/// Acceptance tests for the hardened HTTP boundary. HTTP authorization only establishes an
/// authenticated AuditSphere browser session before business handlers run; TrustedActorResolver
/// and Application AuthorizationDecision remain the business authority (revocation tests assert
/// refusals that the HTTP boundary deliberately cannot produce).
/// </summary>
public sealed class HttpBoundarySecurityTests
{
  [Fact]
  public async Task UnauthenticatedUiRequestsAreRefusedBeforeAnyBusinessHandler()
  {
    // No database is configured on purpose: any refusal produced by a business handler or the
    // workspace queries would require one, so a 401 here can only come from the HTTP boundary.
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = "boundary-anonymous-subject",
      ["DevelopmentIdentity:TenantId"] = Guid.NewGuid().ToString("D"),
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

    using var read = await client.GetAsync("/api/ui/portfolio");
    Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
    Assert.DoesNotContain("session.unavailable", await read.Content.ReadAsStringAsync());

    using var command = await client.PostAsJsonAsync("/api/ui/portfolio/export", new { search = "" });
    Assert.Equal(HttpStatusCode.Unauthorized, command.StatusCode);
    Assert.DoesNotContain("session.unavailable", await command.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task EstablishedCookieSessionsStillReachApplicationWorkspaces()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("HTTP-BOUNDARY-SESSION");
    var f = await PbcSeed.SeedAsync(pg);
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Staff.Subject, ["DevelopmentIdentity:TenantId"] = f.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/session")).StatusCode);
    await client.GetAsync("/auth/sign-in");

    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    var payload = await session.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
    Assert.NotNull(payload);
    Assert.True(payload!.ContainsKey("userId"));
    Assert.True(payload.ContainsKey("staff"));
  }

  [Fact]
  public async Task RevokedSessionsRemainRefusedByTheApplicationLayer()
  {
    // A valid cookie with a stale session epoch passes the HTTP boundary on purpose and is
    // refused by TrustedActorResolver inside the handler; the refusal body proves the source.
    await using var pg = await OwnedPostgresDatabase.CreateAsync("HTTP-BOUNDARY-REVOKE");
    var f = await PbcSeed.SeedAsync(pg);
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Staff.Subject, ["DevelopmentIdentity:TenantId"] = f.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/session")).StatusCode);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var user = await db.Users.SingleAsync(u => u.Id == f.Staff.Id);
      user.SessionEpoch++;
      await db.SaveChangesAsync();
    }

    using var revoked = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    var body = await revoked.Content.ReadAsStringAsync();
    Assert.Contains("session.unavailable", body);
  }

  [Fact]
  public async Task RateLimitRejectsExcessReadsWithASafeStructuredBody()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("HTTP-BOUNDARY-THROTTLE");
    var f = await PbcSeed.SeedAsync(pg);
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Staff.Subject, ["DevelopmentIdentity:TenantId"] = f.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false",
      ["HttpBoundary:RateLimit:NormalRead:PermitLimit"] = "2",
      ["HttpBoundary:RateLimit:NormalRead:WindowSeconds"] = "60" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");

    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/session")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/session")).StatusCode);
    using var rejected = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    Assert.Equal("15", rejected.Headers.RetryAfter?.ToString());
    var refusal = await rejected.Content.ReadFromJsonAsync<Dictionary<string, string?>>();
    Assert.Equal("request.throttled", refusal!["code"]);
    Assert.DoesNotContain("Exception", refusal["message"], StringComparison.OrdinalIgnoreCase);
    Assert.False(string.IsNullOrWhiteSpace(refusal["correlationId"]));
  }

  [Fact]
  public async Task SearchHasADedicatedBudgetIndependentOfWorkspaceReads()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("HTTP-BOUNDARY-SEARCH");
    var f = await PbcSeed.SeedAsync(pg);
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Staff.Subject, ["DevelopmentIdentity:TenantId"] = f.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false",
      ["HttpBoundary:RateLimit:Search:PermitLimit"] = "1",
      ["HttpBoundary:RateLimit:Search:WindowSeconds"] = "60" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");

    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/search?term=client")).StatusCode);
    Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/ui/search?term=client")).StatusCode);
    // The search budget is exhausted, yet ordinary workspace reads remain available.
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/session")).StatusCode);
  }

  [Fact]
  public async Task AnonymousAuthenticationEntryIsPartitionedByRemoteAddress()
  {
    // No identity configured: /auth/sign-in reports unavailability (503) after passing the
    // boundary. A second attempt from the same address is rate limited before the endpoint runs.
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["DevelopmentIdentity:Enabled"] = "false",
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false",
      ["HttpBoundary:RateLimit:Authentication:PermitLimit"] = "1",
      ["HttpBoundary:RateLimit:Authentication:WindowSeconds"] = "60" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

    using var first = await client.GetAsync("/auth/sign-in");
    Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
    using var second = await client.GetAsync("/auth/sign-in");
    Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    var refusal = await second.Content.ReadFromJsonAsync<Dictionary<string, string?>>();
    Assert.Equal("request.throttled", refusal!["code"]);
  }

  [Fact]
  public async Task ExportClassChainsAConcurrencyBudgetPerIdentity()
  {
    var stage = ApiRateLimiter.ConcurrencyStage(
      new ApiRateLimitOptions { ExportConcurrency = 2, Export = new ApiRateLimitOptions.ClassLimit(20, 60) });
    var request = new DefaultHttpContext();
    request.Request.Path = "/api/ui/portfolio/export";
    request.Request.Method = "POST";
    request.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new ApiRateClassAttribute(ApiRateClass.Export)), "export"));
    var first = await stage.AcquireAsync(request);
    var second = await stage.AcquireAsync(request);
    Assert.True(first.IsAcquired);
    Assert.True(second.IsAcquired);

    using var blocked = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => stage.AcquireAsync(request, 1, blocked.Token).AsTask());

    first.Dispose();
    second.Dispose();
    using var released = await stage.AcquireAsync(request);
    Assert.True(released.IsAcquired);
  }

  [Fact]
  public void RateClassesResolveFromMetadataMethodAndPath()
  {
    var read = new DefaultHttpContext();
    read.Request.Method = "GET";
    Assert.Equal(ApiRateClass.NormalRead, ApiRateLimiter.ResolveClass(read));

    var command = new DefaultHttpContext();
    command.Request.Method = "POST";
    Assert.Equal(ApiRateClass.Command, ApiRateLimiter.ResolveClass(command));

    var search = new DefaultHttpContext();
    search.Request.Method = "GET";
    search.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new ApiRateClassAttribute(ApiRateClass.Search)), "search"));
    Assert.Equal(ApiRateClass.Search, ApiRateLimiter.ResolveClass(search));

    var signIn = new DefaultHttpContext();
    signIn.Request.Method = "GET";
    signIn.Request.Path = "/auth/sign-in";
    Assert.Equal(ApiRateClass.Authentication, ApiRateLimiter.ResolveClass(signIn));

    var m365 = new DefaultHttpContext();
    m365.Request.Method = "POST";
    m365.Request.Path = "/api/ui/administration/microsoft365/verify";
    Assert.Equal(ApiRateClass.M365Administration, ApiRateLimiter.ResolveClass(m365));
  }

  [Fact]
  public async Task SecurityHeadersAreAppliedToEveryResponse()
  {
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = "boundary-headers-subject",
      ["DevelopmentIdentity:TenantId"] = Guid.NewGuid().ToString("D"),
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

    using var response = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single(), StringComparison.Ordinal);
    Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    Assert.True(response.Headers.CacheControl!.NoStore);
  }

  [Fact]
  public async Task ContentSecurityPolicyAppliesOnlyToDocumentsAndKeepsScriptsStrict()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("HTTP-BOUNDARY-CSP");
    var root = Path.Combine(pg.RunRoot, "csp-ui");
    Directory.CreateDirectory(root);
    const string inline = "document.querySelectorAll('link[data-beasties-media]').forEach(function(l){l.media=l.getAttribute('data-beasties-media')});";
    const string index = $"<html><head><base href=\"/ui/\"><link rel=\"stylesheet\" href=\"/ui/styles-CURRENT1.css\"></head>" +
      $"<body><app-root></app-root><script src=\"/ui/main-CURRENT1.js\"></script><script>{inline}</script></body></html>";
    await File.WriteAllTextAsync(Path.Combine(root, "index.html"), index);
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false",
      ["AngularUi:Enabled"] = "true", ["AngularUi:BuildPath"] = root, ["AngularUi:CanonicalRoutes"] = "false" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

    using var shell = await client.GetAsync("/ui/app");
    Assert.Equal(HttpStatusCode.OK, shell.StatusCode);
    var csp = shell.Headers.GetValues("Content-Security-Policy").Single();
    var expected = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(inline)));
    Assert.Contains($"'sha256-{expected}'", csp, StringComparison.Ordinal);
    Assert.Contains("style-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
    Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
    Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
    Assert.DoesNotContain("'unsafe-eval'", csp, StringComparison.Ordinal);
    Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);

    using var api = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    Assert.False(api.Headers.Contains("Content-Security-Policy"));
  }

  [Fact]
  public async Task FingerprintedAngularAssetsCacheImmutablyWhileTheShellNeverCaches()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("HTTP-BOUNDARY-CACHE");
    var root = Path.Combine(pg.RunRoot, "cache-ui");
    Directory.CreateDirectory(root);
    const string index = "<html><head><base href=\"/ui/\"><link rel=\"stylesheet\" href=\"/ui/styles-CURRENT1.css\"></head>" +
      "<body><app-root></app-root><script src=\"/ui/main-CURRENT1.js\"></script></body></html>";
    await File.WriteAllTextAsync(Path.Combine(root, "index.html"), index);
    await File.WriteAllTextAsync(Path.Combine(root, "main-CURRENT1.js"), "CURRENT-SYNTHETIC-ASSET");
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false",
      ["AngularUi:Enabled"] = "true", ["AngularUi:BuildPath"] = root, ["AngularUi:CanonicalRoutes"] = "false" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

    using var asset = await client.GetAsync("/ui/main-CURRENT1.js");
    Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
    var cache = asset.Headers.CacheControl!;
    Assert.True(cache.Public);
    Assert.Contains("immutable", cache.ToString(), StringComparison.OrdinalIgnoreCase);
    Assert.Equal(TimeSpan.FromDays(365), cache.MaxAge);

    using var shell = await client.GetAsync("/ui/index.html");
    Assert.True(shell.Headers.CacheControl!.NoStore);
  }

  [Fact]
  public async Task OversizedJsonCommandsAreRefusedBeforeBusinessProcessing()
  {
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = "boundary-size-subject",
      ["DevelopmentIdentity:TenantId"] = Guid.NewGuid().ToString("D"),
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false",
      ["HttpBoundary:BodyLimits:MaxJsonBodyBytes"] = "64" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

    using var oversized = await client.PostAsJsonAsync("/api/ui/portfolio/export", new { search = new string('x', 500) });
    Assert.Equal(413, (int)oversized.StatusCode);
    var refusal = await oversized.Content.ReadFromJsonAsync<Dictionary<string, string?>>();
    Assert.Equal("request.too-large", refusal!["code"]);
    Assert.False(string.IsNullOrWhiteSpace(refusal["correlationId"]));
    Assert.True(oversized.Headers.CacheControl!.NoStore);

    // A bounded body still reaches the normal boundary behavior (unauthenticated refusal).
    using var bounded = await client.PostAsJsonAsync("/api/ui/portfolio/export", new { search = "client" });
    Assert.Equal(HttpStatusCode.Unauthorized, bounded.StatusCode);
  }
}
