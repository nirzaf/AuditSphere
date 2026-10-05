using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Text.Json;

namespace AuditSphereOps.E2E.Tests;

/// <summary>Requires the Angular production build; see the Angular development guide before running.</summary>
public sealed class AngularPortfolioJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-PORT-FORGED-01")]
  public async Task ClientIdentityWithErroneousFirmWideStaffGrantCannotReadPortfolio()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-PORT-FORGED-01");
    var f = host.Fixture;
    const string hiddenClientName = "SYN-PAR-002-HIDDEN-PORTFOLIO-CLIENT";
    await using (var db = host.CreateDbContext())
    {
      var hiddenClientId = Guid.NewGuid();
      var hiddenEngagementId = Guid.NewGuid();
      var now = DateTimeOffset.UtcNow;
      db.PracticeClients.Add(new PracticeClient
      {
        Id = hiddenClientId, FirmId = f.FirmId, LegalName = hiddenClientName, CreatedAt = now
      });
      db.Engagements.Add(new Engagement
      {
        Id = hiddenEngagementId, FirmId = f.FirmId, PracticeClientId = hiddenClientId,
        Status = "Active", CreatedAt = now
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = hiddenClientId, FirmId = f.FirmId });
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Client, "Staff"));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Client,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
    Assert.DoesNotContain("Portfolio", await page.Locator("aside").InnerTextAsync());
    Assert.DoesNotContain(hiddenClientName, await page.Locator("body").InnerTextAsync());

    await using var denied = await context.APIRequest.GetAsync(origin +
      "/api/ui/portfolio/workspace?search=&page=0&pageSize=25");
    Assert.Equal(403, denied.Status);
    var body = await denied.TextAsync();
    Assert.DoesNotContain(hiddenClientName, body, StringComparison.Ordinal);
    Assert.DoesNotContain(f.Client.Email, body, StringComparison.Ordinal);
    Assert.Empty(errors);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANG-PORT-EXPORT-REVOKE-01")]
  public async Task ExportRevocationClearsPortfolioAndDoesNotProduceAnotherDownload()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-PORT-EXPORT-REVOKE-01");
    var f = host.Fixture;
    var staff = PbcSeed.User(f.FirmId, "Staff");
    var clientId = Guid.NewGuid();
    const string privateClientName = "=SYN-PAR-002-PORTFOLIO-REVOKED-CLIENT";
    var staffGrantId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(staff);
      db.PracticeClients.Add(new PracticeClient
      {
        Id = clientId, FirmId = f.FirmId, LegalName = privateClientName, CreatedAt = DateTimeOffset.UtcNow
      });
      var grant = PbcSeed.Grant(f.FirmId, staff, "Staff", clientId: clientId);
      grant.Id = staffGrantId;
      db.RoleGrants.Add(grant);
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(new() { AcceptDownloads = true });
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    var downloadCount = 0;
    page.PageError += (_, error) => errors.Add(error);
    page.Download += (_, _) => downloadCount++;
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fapp");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = privateClientName, Exact = true })).ToBeVisibleAsync();

    var firstDownloadTask = page.WaitForDownloadAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Download scoped CSV", Exact = true }).ClickAsync();
    var firstDownload = await firstDownloadTask;
    Assert.Equal("auditsphere-portfolio.csv", firstDownload.SuggestedFilename);
    var csv = await File.ReadAllTextAsync((await firstDownload.PathAsync())!);
    Assert.Contains($"\"'{privateClientName}\"", csv);
    Assert.Equal(1, downloadCount);
    var documentToken = await page.EvaluateAsync<string>("window.__portfolioRevocationToken = crypto.randomUUID()");

    await using (var db = host.CreateDbContext())
    {
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(f.Admin, "Administrator"),
        new RevokeRoleGrantRequest(staffGrantId, Reason: "Migration parity export revocation"));
      Assert.True(revoked.Succeeded, revoked.Message);
    }

    var refusedExport = page.WaitForResponseAsync(response =>
      response.Url.EndsWith("/api/ui/portfolio/export", StringComparison.Ordinal) && response.Status == 401);
    await page.GetByRole(AriaRole.Button, new() { Name = "Download scoped CSV", Exact = true }).ClickAsync();
    await refusedExport;
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    Assert.DoesNotContain(privateClientName, await page.Locator("body").InnerTextAsync());
    Assert.Equal(0, await page.GetByRole(AriaRole.Button,
      new() { Name = "Download scoped CSV", Exact = true }).CountAsync());
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__portfolioRevocationToken"));
    Assert.Equal(1, downloadCount);
    Assert.Empty(errors);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", "ANGULAR-PORTFOLIO-E2E-02")]
  public async Task ScopedSummaryRecordsCsvAndReturnFilters(bool canonical)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-PORTFOLIO-E2E-02");
    var a = await AuditSphereOps.Domain.Tests.SiblingClientSeed.SeedAsync(host.Database, host.Fixture.FirmId, "PORTFOLIO-A");
    var b = await AuditSphereOps.Domain.Tests.SiblingClientSeed.SeedAsync(host.Database, host.Fixture.FirmId, "HIDDEN-PORTFOLIO-B");
    try
    {
      await using (var db = host.CreateDbContext())
      {
        await AuditSphereOps.Domain.Tests.PortfolioWorkspaceSeed.PopulateAsync(db, a.Fixture, 26);
        await AuditSphereOps.Domain.Tests.PortfolioWorkspaceSeed.PopulateAsync(db, b.Fixture, 3);
      }
      var prefix = canonical ? "" : "/ui";
      var origin = await host.StartApiForIdentityAsync(a.Fixture.Staff, new Dictionary<string,string> {
        ["AngularUi__Enabled"] = "true", ["AngularUi__CanonicalRoutes"] = canonical.ToString() });
      using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
      await using var context = await browser.NewContextAsync(new() { AcceptDownloads = true }); var page = await context.NewPageAsync();
      var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
      await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(prefix + "/app"));
      await Assertions.Expect(page.Locator(".portfolio-clients tbody tr")).ToHaveCountAsync(1);

      // The API projection must scope aggregate counts as well as visible client rows.
      await using (var response = await context.APIRequest.GetAsync(origin +
        "/api/ui/portfolio/workspace?search=&page=0&pageSize=25"))
      {
        Assert.Equal(200, response.Status);
        var payloadText = await response.TextAsync();
        using var payload = JsonDocument.Parse(payloadText);
        var root = payload.RootElement;
        Assert.Equal(1, root.GetProperty("clients").GetProperty("total").GetInt32());
        Assert.Equal(a.Fixture.ClientId.ToString("D"), root.GetProperty("clients").GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(1, root.GetProperty("metrics").GetProperty("clients").GetInt32());
        Assert.Equal(1, root.GetProperty("metrics").GetProperty("engagements").GetInt32());
        Assert.Equal(25, root.GetProperty("metrics").GetProperty("readyCandidates").GetInt32());
        Assert.Equal(1, root.GetProperty("metrics").GetProperty("issuedReleases").GetInt32());
        Assert.Equal(26, root.GetProperty("candidateTotal").GetInt32());
        Assert.DoesNotContain("HIDDEN-PORTFOLIO-B", payloadText, StringComparison.Ordinal);
      }

      await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Recent financial packages" }).Locator("tbody tr")).ToHaveCountAsync(2);
      var candidates = page.GetByRole(AriaRole.Region, new() { Name = "Recent release candidates" });
      await Assertions.Expect(candidates.Locator("tbody tr")).ToHaveCountAsync(10);
      await candidates.GetByRole(AriaRole.Button, new() { Name = "Next candidates", Exact = true }).ClickAsync();
      await Assertions.Expect(candidates.Locator("tbody tr")).ToHaveCountAsync(10);
      await candidates.GetByRole(AriaRole.Button, new() { Name = "Next candidates", Exact = true }).ClickAsync();
      await Assertions.Expect(candidates.Locator("tbody tr")).ToHaveCountAsync(5);
      await candidates.GetByLabel("Candidates per page", new() { Exact = true }).SelectOptionAsync("25");
      await Assertions.Expect(candidates.Locator("tbody tr")).ToHaveCountAsync(25);
      Assert.DoesNotContain("HIDDEN-PORTFOLIO-B", await page.Locator("body").InnerTextAsync());
      await page.GetByRole(AriaRole.Textbox, new() { Name = "Search client name or ID" }).FillAsync("PORTFOLIO-A");
      await page.Locator("audit-portfolio").GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
      await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("search=PORTFOLIO-A"));
      var downloadTask = page.WaitForDownloadAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Download scoped CSV", Exact = true }).ClickAsync();
      var download = await downloadTask; Assert.Equal("auditsphere-portfolio.csv", download.SuggestedFilename);
      var text = await File.ReadAllTextAsync((await download.PathAsync())!);
      Assert.Contains("9007199254740993", text); Assert.Contains("FINANCIAL_PACKAGE", text); Assert.DoesNotContain("HIDDEN-PORTFOLIO-B", text);
      await page.GetByRole(AriaRole.Link, new() { Name = "PORTFOLIO-A Holdings", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Client unavailable", Exact = true })).ToBeVisibleAsync();
      await page.Locator("audit-client").GetByRole(AriaRole.Link, new() { Name = "Portfolio", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Textbox, new() { Name = "Search client name or ID" })).ToHaveValueAsync("PORTFOLIO-A");
      await Assertions.Expect(page.Locator(".portfolio-clients tr.selected")).ToHaveCountAsync(1);
      await page.ReloadAsync(); await Assertions.Expect(page.Locator(".portfolio-clients tr.selected")).ToHaveCountAsync(1);
      await page.SetViewportSizeAsync(390,844); Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
      await using (var db = host.CreateDbContext()) { var user = await db.Users.SingleAsync(u => u.Id == a.Fixture.Staff.Id); user.SessionEpoch++; await db.SaveChangesAsync(); }
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
      Assert.DoesNotContain("PORTFOLIO-A", await page.Locator("body").InnerTextAsync()); Assert.Empty(errors);
    }
    finally { Directory.Delete(a.StagingRoot, true); Directory.Delete(b.StagingRoot, true); }
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", "ANGULAR-PORTFOLIO-E2E-01")]
  public async Task DirectLink_Scope_Revocation_AndCanonicalFallback(bool canonical)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-PORTFOLIO-E2E-01");
    var prefix=canonical?"":"/ui";
    var settings=new Dictionary<string,string>{["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString()};
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff, settings);
    await using (var db = host.CreateDbContext())
    {
      db.Engagements.Add(new Engagement { Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
    }
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl="+Uri.EscapeDataString(prefix+"/app"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() {Name = "Portfolio", Exact = true})).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator(".portfolio-clients tbody tr")).ToHaveCountAsync(1);
    await Assertions.Expect(page.Locator(".portfolio-clients tbody td.number")).ToHaveTextAsync("1");
    await page.ReloadAsync();
    await Assertions.Expect(page.Locator(".portfolio-clients tbody tr")).ToHaveCountAsync(1);
    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await page.GetByRole(AriaRole.Textbox, new() {Name = "Search client name or ID"}).FillAsync("NO MATCH");
    await page.Locator("audit-portfolio").GetByRole(AriaRole.Button, new() {Name = "Search", Exact = true}).ClickAsync();
    await Assertions.Expect(page.GetByText("No clients match this search in your current scope.")).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Textbox, new() {Name = "Search client name or ID"}).FillAsync("");
    await page.Locator("audit-portfolio").GetByRole(AriaRole.Button, new() {Name = "Search", Exact = true}).ClickAsync();
    await Assertions.Expect(page.Locator(".portfolio-clients tbody tr")).ToHaveCountAsync(1);
    await using (var db = host.CreateDbContext())
    {
      var user = await db.Users.SingleAsync(u => u.Id == host.Fixture.Staff.Id);
      user.SessionEpoch++;
      await db.SaveChangesAsync();
    }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() {Name = "Access unavailable"})).ToBeVisibleAsync(new() {Timeout = 15000});
    Assert.DoesNotContain("PBC TEST CLIENT", await page.Locator("body").InnerTextAsync());
    await page.GotoAsync(host.StaffUrl + "/auth/sign-in?returnUrl=%2Fapp");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() {Name = "Portfolio", Exact = true})).ToBeVisibleAsync();
    using (var http = new HttpClient())
    {
      Assert.DoesNotContain("<app-root", await http.GetStringAsync(origin + "/health/live"));
      using var api = await http.GetAsync(origin + "/api/ui/session");
      Assert.Equal(System.Net.HttpStatusCode.Unauthorized, api.StatusCode);
      Assert.DoesNotContain("<app-root", await api.Content.ReadAsStringAsync());
      var previewUi = await http.GetAsync(host.StaffUrl + "/ui/app");
      Assert.Equal(System.Net.HttpStatusCode.OK, previewUi.StatusCode);
      Assert.Contains("<app-root", await previewUi.Content.ReadAsStringAsync());
    }
    var clientOrigin = await host.StartApiForIdentityAsync(host.Fixture.Client, settings);
    await using var restricted = await browser.NewContextAsync();
    var clientPage = await restricted.NewPageAsync();
    await clientPage.GotoAsync(clientOrigin + "/auth/sign-in?returnUrl="+Uri.EscapeDataString(prefix+"/app"));
    await Assertions.Expect(clientPage.GetByRole(AriaRole.Heading, new() {Name = "Client portal", Exact = true})).ToBeVisibleAsync();
    Assert.DoesNotContain("Portfolio", await clientPage.Locator("aside").InnerTextAsync());
    await Assertions.Expect(clientPage.Locator("audit-portfolio")).ToHaveCountAsync(0);
    await using var logoutContext = await browser.NewContextAsync();
    var logoutPage = await logoutContext.NewPageAsync();
    await logoutPage.GotoAsync(origin + "/auth/sign-in?returnUrl="+Uri.EscapeDataString(prefix+"/app"));
    await Assertions.Expect(logoutPage.GetByRole(AriaRole.Heading, new() {Name = "Portfolio", Exact = true})).ToBeVisibleAsync();
    var signedOut = await logoutPage.RunAndWaitForResponseAsync(
      () => logoutPage.GetByRole(AriaRole.Button, new() {Name = "Sign out", Exact = true}).ClickAsync(),
      response => response.Url.EndsWith("/api/ui/sign-out", StringComparison.Ordinal)
        && response.Request.Method == "POST");
    Assert.Equal(204, signedOut.Status);
    await signedOut.FinishedAsync();
    await Assertions.Expect(logoutPage.GetByRole(AriaRole.Heading, new() {Name = "Access unavailable"})).ToBeVisibleAsync();
    Assert.Equal(401, (await logoutContext.APIRequest.GetAsync(origin + "/api/ui/session")).Status);
    Assert.Empty(errors);
  }
}
