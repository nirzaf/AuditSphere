using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class ClientProfileWorkspaceApiTests
{
  [Fact]
  public async Task NativeProfileRequiresCurrentClientScopeAndReturnsOnlyBoundedExactProjection()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("CLIENT-PROFILE-WORKSPACE-API");
    var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options)) { await ClientProfileWorkspaceSeed.PopulateAsync(db, f); }
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Staff.Subject, ["DevelopmentIdentity:TenantId"] = f.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
    using var c = factory.CreateClient(new() { AllowAutoRedirect = false });
    var path = "/api/ui/clients/" + f.ClientId;
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(path)).StatusCode); await c.GetAsync("/auth/sign-in");
    var retained = (await c.GetFromJsonAsync<ClientWorkspace>(path))!;
    Assert.Equal(27, retained.Engagements.Count); Assert.Equal(27, retained.Contacts.Count);
    Assert.Equal(100, retained.Paging.EngagementPageSize); Assert.Equal(100, retained.Paging.ContactPageSize);
    using var response = await c.GetAsync(path + "?contactPage=2&engagementPageSize=25");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
    var v = (await response.Content.ReadFromJsonAsync<ClientWorkspace>())!;
    Assert.Equal(27, v.Metrics.Engagements); Assert.Equal(27, v.Metrics.Contacts); Assert.Equal(25, v.Engagements.Count);
    Assert.Equal(7, v.Contacts.Count); Assert.Equal(new ClientWorkspacePaging(0,25,2,10), v.Paging);
    Assert.Equal("9007199254740993", v.SafetyGeneration); Assert.Equal("AWAITING_ACCEPTANCE", v.PortalIntent!.State);
    using var raw = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert.False(raw.RootElement.TryGetProperty("restrictedProfile", out _));
    Assert.DoesNotContain("EXCLUDED PRIVATE PROFILE", await response.Content.ReadAsStringAsync());
    foreach (var query in new[] { "contactPage=-1", "contactPage=10001", "contactPageSize=100", "engagementPageSize=0", "engagementPageSize=bad" })
      Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(path+"?"+query)).StatusCode);
    using var foreignResponse = await c.GetAsync("/api/ui/clients/" + foreign.ClientId);
    using var guessedResponse = await c.GetAsync("/api/ui/clients/" + Guid.NewGuid());
    Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
    Assert.Equal(await guessedResponse.Content.ReadAsStringAsync(), await foreignResponse.Content.ReadAsStringAsync());
    Assert.Empty((await c.GetFromJsonAsync<ClientWorkspace>(path + "?contactPage=10000"))!.Contacts);
    await using (var db = new AuditSphereDbContext(pg.Options))
    { await db.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.EngagementId == null).ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow)); }
    Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(path)).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    { await db.Users.Where(u => u.Id == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1)); }
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(path)).StatusCode);
  }
}
