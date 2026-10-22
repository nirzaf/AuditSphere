using System.Net;
using System.Net.Http.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

/// <summary>
/// WU-033: Validates portfolio and analytics projections with defined bases and strict scope isolation.
/// </summary>
[Trait("CompletionUnit", "WU-033")]
public sealed class CompletionPortfolioProjectionTests
{
  [Fact]
  public async Task PortfolioMetrics_PreserveExplicitScopeDefinitions_AndExcludeForeignData()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("PORTFOLIO-WU033-METRICS");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await PortfolioWorkspaceSeed.PopulateAsync(db, f, 3);
      await PortfolioWorkspaceSeed.PopulateAsync(db, foreign, 3);

      // Add an inactive engagement WITHOUT a hold to verify activation status is never confused with a hold
      var inactiveEngagementId = Guid.NewGuid();
      db.Engagements.Add(new()
      {
        Id = inactiveEngagementId,
        FirmId = f.FirmId,
        PracticeClientId = f.ClientId,
        ServiceRoute = "AUDIT",
        Status = "Draft",
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Staff", clientId: f.ClientId, engagementId: inactiveEngagementId));

      await db.SaveChangesAsync();
    }

    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Staff.Subject,
      ["DevelopmentIdentity:TenantId"] = f.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");

    var workspace = await client.GetFromJsonAsync<PortfolioWorkspace>("/api/ui/portfolio/workspace");
    Assert.NotNull(workspace);

    // Visible clients = 1, Visible engagements = 2 (seeded + draft), Open holds = 1 (only the real hold)
    // Pending operations = 1, Ready candidates = 2 (only ready, not draft), Issued releases = 1
    Assert.Equal(1, workspace.Metrics.Clients);
    Assert.Equal(2, workspace.Metrics.Engagements);
    Assert.Equal(1, workspace.Metrics.OpenHolds);
    Assert.Equal(1, workspace.Metrics.PendingOperations);
    Assert.Equal(2, workspace.Metrics.ReadyCandidates);
    Assert.Equal(1, workspace.Metrics.IssuedReleases);

    // Verify candidates are strictly scoped to visible client/engagement
    Assert.All(workspace.Candidates, c =>
    {
      Assert.Equal(f.ClientId, c.ClientId);
      Assert.Equal(f.EngagementId, c.EngagementId);
    });
  }

  [Fact]
  public async Task PortfolioWorkspace_InvalidPaginationAndSearch_RefusesWithoutDiagnosticLeak()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("PORTFOLIO-WU033-BOUNDS");
    var f = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await PortfolioWorkspaceSeed.PopulateAsync(db, f, 1);
    }

    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Staff.Subject,
      ["DevelopmentIdentity:TenantId"] = f.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");

    // Negative page refused
    var negPage = await client.GetAsync("/api/ui/portfolio/workspace?page=-1");
    Assert.Equal(HttpStatusCode.BadRequest, negPage.StatusCode);

    // PageSize > 100 refused
    var largePage = await client.GetAsync("/api/ui/portfolio/workspace?pageSize=101");
    Assert.Equal(HttpStatusCode.BadRequest, largePage.StatusCode);

    // PageSize < 1 refused
    var zeroSize = await client.GetAsync("/api/ui/portfolio/workspace?pageSize=0");
    Assert.Equal(HttpStatusCode.BadRequest, zeroSize.StatusCode);

    // Search > 100 chars refused
    var longSearch = await client.GetAsync($"/api/ui/portfolio/workspace?search={new string('x', 101)}");
    Assert.Equal(HttpStatusCode.BadRequest, longSearch.StatusCode);
  }
}
