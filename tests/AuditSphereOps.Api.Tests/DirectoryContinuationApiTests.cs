using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.Ui;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditSphereOps.Api.Tests;

public sealed class DirectoryContinuationApiTests
{
  private sealed class Reader(string tenant) : IMicrosoftDirectoryReader
  {
    public List<(bool Browse, string? Prefix, string? Domain, string? Token)> Calls { get; } = [];
    public string RawToken { get; set; } = "raw-provider-token";
    public Task<DirectoryCandidatePage> SearchAsync(string tenantId, string prefix, string? pageToken, CancellationToken ct, string? domain = null)
    {
      Calls.Add((false, prefix, domain, pageToken));
      return Page(pageToken);
    }
    public Task<DirectoryCandidatePage> BrowseActiveAsync(string tenantId, string? domain, string? pageToken, CancellationToken ct)
    {
      Calls.Add((true, null, domain, pageToken));
      return Page(pageToken);
    }
    private Task<DirectoryCandidatePage> Page(string? token) => Task.FromResult(new DirectoryCandidatePage(
      [new(tenant, Guid.NewGuid().ToString("D"), "Ada", "ada@example.test", true, "Member")], token is null ? RawToken : null));
    public Task<DirectoryCandidate> GetByIdAsync(string tenantId, string objectId, CancellationToken ct) => throw new NotSupportedException();
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, PbcSeed.Fixture f,
    string tenant, Reader reader, IDataProtectionProvider protection, bool otherActor = false) => new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = otherActor ? f.Staff.Subject : f.Admin.Subject,
      ["DevelopmentIdentity:TenantId"] = tenant,
      ["Identity:TenantId"] = tenant,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    }, services =>
    {
      services.RemoveAll<IMicrosoftDirectoryReader>();
      services.AddSingleton<IMicrosoftDirectoryReader>(reader);
      services.RemoveAll<IDataProtectionProvider>();
      services.AddSingleton(protection);
      services.RemoveAll<TenantAdministrationSettings>();
      services.AddSingleton(new TenantAdministrationSettings(new TenantAdministrationOptions(tenant, DirectoryReadEnabled: true), false, false, null));
    });

  private static async Task<PbcSeed.Fixture> Seed(OwnedPostgresDatabase pg, string tenant)
  {
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await db.Users.Where(x => x.FirmId == f.FirmId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TenantId, tenant));
    db.RoleGrants.Add(PbcSeed.AdminGrant(f.FirmId, f.Staff));
    await db.SaveChangesAsync();
    return f;
  }

  private static async Task<string> SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie")
      .Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static async Task<HttpResponseMessage> Search(HttpClient client, string csrf, string? token,
    string prefix = "Ad", string? domain = "example.test", bool browse = false)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ui/administration/directory/search")
    {
      Content = JsonContent.Create(new { prefix, domain, pageToken = token, browse })
    };
    request.Headers.Add("X-XSRF-TOKEN", csrf);
    return await client.SendAsync(request);
  }

  private static async Task<string?> Next(HttpResponseMessage response)
  {
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    return json.GetProperty("value").GetProperty("nextPageToken").GetString();
  }

  [Theory, InlineData(false), InlineData(true)]
  public async Task SameInheritedQueryUnwrapsOpaqueCursorAndProviderReceivesOnlyRawToken(bool browse)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-DIRECTORY-CONTINUE");
    var tenant = Guid.NewGuid().ToString("D");
    var f = await Seed(pg, tenant);
    var reader = new Reader(tenant);
    var protection = new EphemeralDataProtectionProvider();
    using var factory = Factory(pg, f, tenant, reader, protection);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    using var first = await Search(client, csrf, null, " Ad ", " @Example.Test ", browse);
    var token = await Next(first);
    Assert.NotNull(token);
    Assert.NotEqual(reader.RawToken, token);
    using var second = await Search(client, csrf, token, "Ad", "example.test", browse);
    Assert.Null(await Next(second));
    Assert.Equal(2, reader.Calls.Count);
    Assert.Null(reader.Calls[0].Token);
    Assert.Equal(reader.RawToken, reader.Calls[1].Token);
    Assert.Equal("example.test", reader.Calls[1].Domain);
    Assert.Equal(browse, reader.Calls[1].Browse);
  }

  [Theory]
  [InlineData("prefix")]
  [InlineData("domain")]
  [InlineData("mode")]
  [InlineData("tamper")]
  [InlineData("expired")]
  [InlineData("tenant")]
  [InlineData("firm")]
  [InlineData("raw")]
  [InlineData("oversized")]
  public async Task InvalidContinuationRefusesBeforeProviderCall(string change)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-DIRECTORY-CURSOR-REFUSAL");
    var tenant = Guid.NewGuid().ToString("D");
    var f = await Seed(pg, tenant);
    var reader = new Reader(tenant);
    var protection = new EphemeralDataProtectionProvider();
    using var factory = Factory(pg, f, tenant, reader, protection);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    using var first = await Search(client, csrf, null);
    var token = await Next(first);
    if (change == "tamper")
    {
      var chars = token!.ToCharArray();
      chars[chars.Length / 2] = chars[chars.Length / 2] == 'A' ? 'B' : 'A';
      token = new string(chars);
    }
    if (change == "expired")
    {
      var helper = new DirectoryContinuation(protection);
      var context = new DirectoryContinuationContext(f.FirmId, f.Admin.Id, f.Admin.SessionEpoch, Guid.Parse(tenant), false, "Ad", "example.test");
      Assert.True(helper.TryWrite(reader.RawToken, context, DateTimeOffset.UtcNow.AddMinutes(-16), out token));
    }
    if (change is "tenant" or "firm")
    {
      var context = new DirectoryContinuationContext(f.FirmId, f.Admin.Id, f.Admin.SessionEpoch, Guid.Parse(tenant), false, "Ad", "example.test");
      context = change == "tenant" ? context with { TenantId = Guid.NewGuid() } : context with { FirmId = Guid.NewGuid() };
      Assert.True(new DirectoryContinuation(protection).TryWrite(reader.RawToken, context, DateTimeOffset.UtcNow, out token));
    }
    if (change == "raw") token = reader.RawToken;
    if (change == "oversized") token = new string('x', DirectoryContinuation.MaximumEnvelopeLength + 1);
    using var refused = await Search(client, csrf, token, change == "prefix" ? "Bo" : "Ad",
      change == "domain" ? "other.test" : "example.test", change == "mode");
    Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    Assert.Single(reader.Calls);
  }

  [Theory, InlineData(false), InlineData(true)]
  public async Task AnotherCurrentAdministratorOrNewEpochCannotReuseCursor(bool epoch)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-DIRECTORY-CURSOR-OWNER");
    var tenant = Guid.NewGuid().ToString("D");
    var f = await Seed(pg, tenant);
    var reader = new Reader(tenant);
    var protection = new EphemeralDataProtectionProvider();
    using var factory = Factory(pg, f, tenant, reader, protection);
    using var firstClient = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(firstClient);
    using var first = await Search(firstClient, csrf, null);
    var token = await Next(first);
    if (epoch)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    }
    using var secondFactory = Factory(pg, f, tenant, reader, protection, otherActor: !epoch);
    using var secondClient = secondFactory.CreateClient(new() { AllowAutoRedirect = false });
    var newCsrf = await SignIn(secondClient);
    using var refused = await Search(secondClient, newCsrf, token);
    Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    Assert.Single(reader.Calls);
  }

  [Fact]
  public async Task MaximumProviderCursorFitsEnvelopeAndOversizedProviderCursorIsNeverReturned()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-DIRECTORY-CURSOR-MAXIMUM");
    var tenant = Guid.NewGuid().ToString("D");
    var f = await Seed(pg, tenant);
    var reader = new Reader(tenant) { RawToken = new string('ع', DirectoryContinuation.MaximumProviderTokenLength) };
    var protection = new EphemeralDataProtectionProvider();
    using var factory = Factory(pg, f, tenant, reader, protection);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    using var first = await Search(client, csrf, null);
    var token = await Next(first);
    Assert.NotNull(token);
    Assert.True(token.Length <= 20000);
    using var second = await Search(client, csrf, token);
    Assert.Null(await Next(second));
    Assert.Equal(reader.RawToken, reader.Calls[1].Token);
    reader.RawToken = new string('x', DirectoryContinuation.MaximumProviderTokenLength + 1);
    using var refused = await Search(client, csrf, null);
    Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    Assert.DoesNotContain(reader.RawToken, await refused.Content.ReadAsStringAsync());
  }
}
