using System.Net;
using System.Net.Http.Json;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;

namespace AuditSphereOps.Api.Tests;

public sealed class PortfolioSiblingIsolationApiTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task SameFirmSiblingCannotChangeScopedPortfolioCountsOrExport(bool engagementScoped)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("PORTFOLIO-SIBLING-ISOLATION");
    var own = await PbcSeed.SeedAsync(pg);
    var viewer = PbcSeed.User(own.FirmId, "Staff");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(viewer);
      db.RoleGrants.Add(PbcSeed.Grant(own.FirmId, viewer, "Staff", own.ClientId,
        engagementScoped ? own.EngagementId : null));
      await PortfolioWorkspaceSeed.PopulateAsync(db, own, 3);
    }

    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = viewer.Subject,
      ["DevelopmentIdentity:TenantId"] = viewer.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    var csrf = session.Headers.GetValues("Set-Cookie")
      .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))
      .Split(';')[0]["XSRF-TOKEN=".Length..];
    client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(csrf));

    async Task<(string Workspace, string Export)> ReadScopedProjectionAsync()
    {
      using var workspace = await client.GetAsync("/api/ui/portfolio/workspace");
      Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);
      var workspaceJson = await workspace.Content.ReadAsStringAsync();
      using var export = await client.PostAsJsonAsync("/api/ui/portfolio/export", new { search = "" });
      Assert.Equal(HttpStatusCode.OK, export.StatusCode);
      Assert.True(export.Headers.CacheControl?.NoStore == true);
      return (workspaceJson, await export.Content.ReadAsStringAsync());
    }

    var before = await ReadScopedProjectionAsync();
    var sibling = await SiblingClientSeed.SeedAsync(pg, own.FirmId, "API-HIDDEN-PORTFOLIO-B");
    try
    {
      await using (var db = new AuditSphereDbContext(pg.Options))
        await PortfolioWorkspaceSeed.PopulateAsync(db, sibling.Fixture, 3);

      var after = await ReadScopedProjectionAsync();
      Assert.Equal(before.Workspace, after.Workspace);
      Assert.Equal(before.Export, after.Export);
      Assert.DoesNotContain("API-HIDDEN-PORTFOLIO-B", after.Workspace, StringComparison.Ordinal);
      Assert.DoesNotContain("API-HIDDEN-PORTFOLIO-B", after.Export, StringComparison.Ordinal);
      Assert.DoesNotContain(sibling.Fixture.ClientId.ToString("D"), after.Workspace, StringComparison.Ordinal);
      Assert.DoesNotContain(sibling.Fixture.ClientId.ToString("D"), after.Export, StringComparison.Ordinal);
    }
    finally
    {
      PbcSeed.DeleteDirectory(sibling.StagingRoot);
    }
  }
}
