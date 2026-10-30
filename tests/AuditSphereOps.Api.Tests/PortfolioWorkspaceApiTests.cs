using System.Net;
using System.Net.Http.Json;
using AuditSphereOps.Api.Ui;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditSphereOps.Api.Tests;

public sealed class PortfolioWorkspaceApiTests
{
  [Fact]
  public async Task ContextChoiceContinuationIsProtectedAndBoundToActorClientAndPageSize()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("PORTFOLIO-CONTEXT-CURSOR-API");
    var f = await PbcSeed.SeedAsync(pg);
    var otherClientId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.PracticeClients.Add(new PracticeClient { Id = otherClientId, FirmId = f.FirmId,
        LegalName = "Other context client", CreatedAt = DateTimeOffset.UtcNow });
      db.Engagements.Add(new Engagement { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        ServiceRoute = "AUDIT", Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Staff", f.ClientId));
      await db.SaveChangesAsync();
    }
    var protection = new EphemeralDataProtectionProvider();
    StandaloneApiApplicationFactory Factory(string subject, string tenant) => new(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = subject, ["DevelopmentIdentity:TenantId"] = tenant,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" }, services =>
    {
      services.RemoveAll<IDataProtectionProvider>();
      services.AddSingleton<IDataProtectionProvider>(protection);
    });
    using var adminFactory = Factory(f.Admin.Subject, f.Admin.TenantId);
    using var staffFactory = Factory(f.Staff.Subject, f.Staff.TenantId);
    using var admin = adminFactory.CreateClient(new() { AllowAutoRedirect = false });
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    await admin.GetAsync("/auth/sign-in");
    await staff.GetAsync("/auth/sign-in");
    var path = $"/api/ui/portfolio/context-choices?clientId={f.ClientId}&pageSize=1";
    using var first = await admin.GetAsync(path);
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    Assert.True(first.Headers.CacheControl?.NoStore == true);
    var firstPage = await first.Content.ReadFromJsonAsync<UiEndpoints.PortfolioContextChoicesResponse>();
    Assert.NotNull(firstPage);
    Assert.Equal(f.ClientId, firstPage.ClientId);
    Assert.Equal(1, firstPage.PageSize);
    Assert.Single(firstPage.Items);
    Assert.True(firstPage.HasMore);
    Assert.InRange(firstPage.ValidForMs, 1, 30000);
    var token = Assert.IsType<string>(firstPage.NextCursor);
    Assert.StartsWith("pc1.", token);
    var continuation = $"&cursor={Uri.EscapeDataString(token)}";
    using var second = await admin.GetAsync(path + continuation);
    Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    Assert.True(second.Headers.CacheControl?.NoStore == true);
    var secondPage = await second.Content.ReadFromJsonAsync<UiEndpoints.PortfolioContextChoicesResponse>();
    Assert.NotNull(secondPage);
    Assert.Equal(f.ClientId, secondPage.ClientId);
    Assert.Equal(1, secondPage.PageSize);
    Assert.Single(secondPage.Items);
    Assert.NotEqual(firstPage.Items[0].EngagementId, secondPage.Items[0].EngagementId);
    Assert.False(secondPage.HasMore);
    Assert.Null(secondPage.NextCursor);

    // Both actors can independently access this client; a shared protector isolates actor binding.
    using var staffFirst = await staff.GetAsync(path);
    Assert.Equal(HttpStatusCode.OK, staffFirst.StatusCode);
    var tampered = token.ToCharArray();
    tampered[tampered.Length / 2] = tampered[tampered.Length / 2] == 'A' ? 'B' : 'A';
    var invalidRequests = new[] {
      (admin, path + "&cursor=" + Uri.EscapeDataString(new string(tampered))),
      (admin, $"/api/ui/portfolio/context-choices?clientId={otherClientId}&pageSize=1" + continuation),
      (admin, $"/api/ui/portfolio/context-choices?clientId={f.ClientId}&pageSize=2" + continuation),
      (staff, path + continuation)
    };
    foreach (var (client, request) in invalidRequests)
    {
      using var rejected = await client.GetAsync(request);
      Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
      Assert.True(rejected.Headers.CacheControl?.NoStore == true);
      var failure = await rejected.Content.ReadFromJsonAsync<Dictionary<string, string>>();
      Assert.Equal("request.invalid", failure!["code"]);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Users.Where(u => u.Id == f.Admin.Id)
        .ExecuteUpdateAsync(update => update.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    using var renewedAdmin = adminFactory.CreateClient(new() { AllowAutoRedirect = false });
    await renewedAdmin.GetAsync("/auth/sign-in");
    using var renewedFirst = await renewedAdmin.GetAsync(path);
    Assert.Equal(HttpStatusCode.OK, renewedFirst.StatusCode);
    using var priorEpoch = await renewedAdmin.GetAsync(path + continuation);
    Assert.Equal(HttpStatusCode.BadRequest, priorEpoch.StatusCode);
    Assert.True(priorEpoch.Headers.CacheControl?.NoStore == true);
    var epochFailure = await priorEpoch.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    Assert.Equal("request.invalid", epochFailure!["code"]);
  }

  [Fact]
  public async Task ExportLimitReturnsTypedRefusalWithoutCsvOrInternalDiagnostics()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("PORTFOLIO-EXPORT-LIMIT-API");
    var f = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var now = DateTimeOffset.UtcNow;
      db.PracticeClients.AddRange(Enumerable.Range(0, PortfolioQuery.ExportClientLimit).Select(i => new PracticeClient
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, LegalName = $"PORTFOLIO EXPORT LIMIT CLIENT {i:D4}", CreatedAt = now
      }));
      await db.SaveChangesAsync();
    }

    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Admin.Subject, ["DevelopmentIdentity:TenantId"] = f.Admin.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    var csrf = session.Headers.GetValues("Set-Cookie")
      .Single(v => v.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..];
    client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(csrf));

    using var response = await client.PostAsJsonAsync("/api/ui/portfolio/export", new { search = "" });
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    Assert.Null(response.Content.Headers.ContentDisposition);
    var refusal = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
    Assert.Equal("export.limit", refusal!["code"]);
    Assert.Equal("Narrow the search before exporting. The client export limit was exceeded.", refusal["message"]);
    Assert.DoesNotContain("Exception", refusal["message"], StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("text/csv", response.Content.Headers.ContentType.MediaType, StringComparison.OrdinalIgnoreCase);
  }

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
    var progressPath = $"/api/ui/clients/{f.ClientId}/progress";
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(progressPath)).StatusCode);
    await c.GetAsync("/auth/sign-in");
    using var session = await c.GetAsync("/api/ui/session");
    var csrf = session.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..];
    var v = await c.GetFromJsonAsync<PortfolioWorkspace>("/api/ui/portfolio/workspace");
    Assert.Equal(new PortfolioMetrics(1, 1, 1, 1, 2, 1), v!.Metrics);
    Assert.Equal(3, v.Candidates.Count); Assert.False(v.HasActiveMicrosoftConfiguration);
    Assert.All(v.Candidates, r => Assert.Equal("9007199254740993", r.TargetRevision));
    var firstProgress = await c.GetFromJsonAsync<EngagementProgressList>(progressPath);
    Assert.Equal((1, 0, 50), (firstProgress!.Total, firstProgress.Page, firstProgress.PageSize));
    Assert.Equal(f.EngagementId, Assert.Single(firstProgress.Items).EngagementId);
    var nextProgress = await c.GetFromJsonAsync<EngagementProgressList>(progressPath + "?page=1");
    Assert.Equal((1, 1, 50), (nextProgress!.Total, nextProgress.Page, nextProgress.PageSize));
    Assert.Empty(nextProgress.Items);
    Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(progressPath + "?page=-1")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/api/ui/clients/{foreign.ClientId}/progress")).StatusCode);
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
    Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(progressPath + "?page=1")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/ui/portfolio/export", new { search = "" })).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    { var user = await db.Users.SingleAsync(u => u.Id == f.Staff.Id); user.SessionEpoch++; await db.SaveChangesAsync(); }
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/ui/portfolio/workspace")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(progressPath)).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/ui/portfolio/export", new { search = "" })).StatusCode);
  }
}
