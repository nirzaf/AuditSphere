using System.Net;
using System.Net.Http.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class PortfolioWorkspaceApiTests
{
  [Fact]
  public async Task CurrentCookieScopeCsrfExportAndRevocationFenceEveryProjection()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("PORTFOLIO-WORKSPACE-API");
    var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    { await PortfolioWorkspaceSeed.PopulateAsync(db, f, 3); await PortfolioWorkspaceSeed.PopulateAsync(db, foreign, 3); }
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Staff.Subject, ["DevelopmentIdentity:TenantId"] = f.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
    using var c = factory.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/ui/portfolio/workspace")).StatusCode);
    await c.GetAsync("/auth/sign-in");
    using var session = await c.GetAsync("/api/ui/session");
    var csrf = session.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..];
    var v = await c.GetFromJsonAsync<PortfolioWorkspace>("/api/ui/portfolio/workspace");
    Assert.Equal(new PortfolioMetrics(1, 1, 1, 1, 2, 1), v!.Metrics);
    Assert.Equal(3, v.Candidates.Count); Assert.False(v.HasActiveMicrosoftConfiguration);
    Assert.All(v.Candidates, r => Assert.Equal("9007199254740993", r.TargetRevision));
    Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/ui/portfolio/workspace?pageSize=101")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/ui/portfolio/export", new { search = "" })).StatusCode);
    c.DefaultRequestHeaders.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(csrf));
    using var export = await c.PostAsJsonAsync("/api/ui/portfolio/export", new { search = "" });
    Assert.Equal(HttpStatusCode.OK, export.StatusCode); Assert.True(export.Headers.CacheControl!.NoStore);
    Assert.Equal("text/csv", export.Content.Headers.ContentType!.MediaType);
    var text = await export.Content.ReadAsStringAsync(); Assert.Contains(f.ClientId.ToString(), text); Assert.DoesNotContain(foreign.ClientId.ToString(), text);
    Assert.Equal(text, await (await c.PostAsJsonAsync("/api/ui/portfolio/export", new { search = "" })).Content.ReadAsStringAsync());
    using var guessed = await c.PostAsJsonAsync("/api/ui/portfolio/export", new { search = foreign.ClientId.ToString(), firmId = foreign.FirmId });
    Assert.Equal("record_type,client,identifier,status,period,details", await guessed.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/ui/portfolio/export", new { search = new string('a',101) })).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var grant = await db.RoleGrants.SingleAsync(g => g.UserId == f.Staff.Id); grant.RevokedAt = DateTimeOffset.UtcNow;
      await db.SaveChangesAsync();
    }
    Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/ui/portfolio/workspace")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/ui/portfolio/export", new { search = "" })).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    { var user = await db.Users.SingleAsync(u => u.Id == f.Staff.Id); user.SessionEpoch++; await db.SaveChangesAsync(); }
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/ui/portfolio/workspace")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/ui/portfolio/export", new { search = "" })).StatusCode);
  }
}
