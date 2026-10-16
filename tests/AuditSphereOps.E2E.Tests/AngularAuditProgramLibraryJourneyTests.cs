using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularAuditProgramLibraryJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", "AS-PAR-002-ANG-AUDIT-LIBRARY-01")]
  public async Task LibraryReadActionsRecoverFromEmptyAndFailedSearchWithoutPublishing(bool published)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANG-AUDIT-LIBRARY-01");
    Guid grantId;
    Guid firstVersionId = Guid.Empty;
    await using (var db = host.CreateDbContext())
    {
      var grant = PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "Partner");
      grantId = grant.Id;
      db.RoleGrants.Add(grant);
      await db.SaveChangesAsync();
      if (published)
      {
        var actor = PbcSeed.Actor(host.Fixture.Staff, "Partner");
        var first = await AuditProgramService.PublishAsync(db, actor, new("2026.1", AuditProgramCatalog.SourceHash));
        Assert.True(first.Succeeded, first.Message);
        firstVersionId = first.Value!.ProgramVersionId;
        var second = await AuditProgramService.PublishAsync(db, actor, new("2026.2", AuditProgramCatalog.SourceHash));
        Assert.True(second.Succeeded, second.Message);
      }
    }
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    var mutations = 0;
    page.PageError += (_, error) => errors.Add(error);
    page.Request += (_, request) => { if (request.Method == "POST" && request.Url.Contains("/api/ui/", StringComparison.Ordinal)) mutations++; };
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl=%2Fapp%2Faudit%2Flibrary");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Audit program library", Exact = true })).ToBeVisibleAsync();
    if (!published)
    {
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "No published library version", Exact = true })).ToBeVisibleAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Refresh library", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "No published library version", Exact = true })).ToBeVisibleAsync();
    }
    else
    {
      await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/app/audit/library");
      var version = page.Locator("section[aria-labelledby='version-heading'] select[name='version']");
      await Assertions.Expect(version).ToBeVisibleAsync();
      await version.SelectOptionAsync(firstVersionId.ToString("D"));
      await Assertions.Expect(version).ToHaveValueAsync(firstVersionId.ToString("D"));
      await Assertions.Expect(version.Locator("option:checked")).ToHaveTextAsync(
        $"{AuditProgramCatalog.ProgramCode} v2026.1 · PUBLISHED · {AuditProgramCatalog.Items.Count} procedures");
      var metadata = page.Locator("section[aria-labelledby='version-heading']");
      await Assertions.Expect(metadata.Locator("dt", new() { HasText = "Source hash" }).Locator("+ dd")).ToHaveTextAsync(AuditProgramCatalog.SourceHash);
      await Assertions.Expect(metadata.Locator("dt", new() { HasText = "Procedures" }).Locator("+ dd")).ToHaveTextAsync(AuditProgramCatalog.Items.Count.ToString());
      await Assertions.Expect(metadata.Locator("dt", new() { HasText = "Sections" }).Locator("+ dd")).ToHaveTextAsync(AuditProgramCatalog.Items.Select(x => x.SectionNumber).Distinct().Count().ToString());
      await Assertions.Expect(page.GetByText(
        $"2 library versions · {AuditProgramCatalog.Items.Count} procedures and {AuditProgramCatalog.Items.Select(x => x.SectionNumber).Distinct().Count()} sections in the selected version", new() { Exact = true })).ToBeVisibleAsync();
      var sectionUrl = $"{origin}/api/ui/audit/library/{firstVersionId:D}/sections/1";
      var selectedSectionRead = page.WaitForResponseAsync(response => response.Url == sectionUrl && response.Status == 200);
      await page.GetByRole(AriaRole.Button, new() { Name = "Browse", Exact = true }).First.ClickAsync();
      await selectedSectionRead;
      var sectionCount = AuditProgramCatalog.Items.Count(x => x.SectionNumber == 1);
      await Assertions.Expect(page.GetByText($"Showing {sectionCount} of {sectionCount} procedures.", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByText(AuditProgramCatalog.Items.First(x => x.SourceProcedureId == "AWP-01-01").SourceWording, new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Section 1 — Planning & Risk Assessment", Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByText("AWP-01-01", new() { Exact = true })).ToBeVisibleAsync();
      var sectionPanel = page.Locator("section[aria-labelledby='procedures-heading']");
      await sectionPanel.GetByLabel("Search procedures", new() { Exact = true }).FillAsync("SYNTHETIC-NO-SOURCE-MATCH");
      await sectionPanel.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByText("No procedures in this section match the search term.", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByText("Showing 0 of 0 procedures.", new() { Exact = true })).ToBeVisibleAsync();
      await sectionPanel.GetByRole(AriaRole.Button, new() { Name = "Clear", Exact = true }).ClickAsync();
      await Assertions.Expect(sectionPanel.GetByLabel("Search procedures", new() { Exact = true })).ToHaveValueAsync("");
      await Assertions.Expect(page.GetByText("AWP-01-01", new() { Exact = true })).ToBeVisibleAsync();
      var sections = sectionUrl + "?search=AWP";
      await page.RouteAsync(sections, route => route.FulfillAsync(new() { Status = 403, ContentType = "application/json", Body = "{\"message\":\"Synthetic library read denied.\"}" }));
      await sectionPanel.GetByLabel("Search procedures", new() { Exact = true }).FillAsync("AWP");
      await sectionPanel.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByText("Synthetic library read denied.", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(page.GetByText("AWP-01-01", new() { Exact = true })).ToHaveCountAsync(0);
      await Assertions.Expect(page.Locator("section[aria-labelledby='version-heading'] select[name='version']")).ToHaveCountAsync(0);
      await Assertions.Expect(metadata).ToHaveCountAsync(0);
      await page.UnrouteAsync(sections);
      await page.GetByRole(AriaRole.Button, new() { Name = "Refresh library", Exact = true }).ClickAsync();
      await Assertions.Expect(page.Locator("section[aria-labelledby='version-heading'] select[name='version']")).ToBeVisibleAsync();
      await page.GetByRole(AriaRole.Button, new() { Name = "Browse", Exact = true }).First.ClickAsync();
      await Assertions.Expect(page.GetByText("AWP-01-01", new() { Exact = true })).ToBeVisibleAsync();
      await CheckResponsiveAsync();
      // Native version selection and section browsing remain reachable without a pointer.
      await page.Locator("section[aria-labelledby='version-heading'] select[name='version']").FocusAsync();
      await page.Keyboard.PressAsync("Tab");
      await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Browse", Exact = true }).First).ToBeFocusedAsync();
      await using (var db = host.CreateDbContext())
      {
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, PbcSeed.Actor(host.Fixture.Admin, "Administrator"),
          new RevokeRoleGrantRequest(grantId, Reason: "Synthetic library access ended"));
        Assert.True(revoked.Succeeded, revoked.Message);
      }
      var invalidation = page.WaitForResponseAsync(response => response.Url.EndsWith("/api/ui/session", StringComparison.Ordinal) && response.Status == 401);
      await page.EvaluateAsync("() => window.dispatchEvent(new FocusEvent('focus'))");
      await invalidation;
      await Assertions.Expect(page.GetByText("AWP-01-01", new() { Exact = true })).ToHaveCountAsync(0);
      await Assertions.Expect(page.Locator("section[aria-labelledby='version-heading'] select[name='version']")).ToHaveCountAsync(0);
    }
    if (!published) await CheckResponsiveAsync();
    async Task CheckResponsiveAsync()
    {
      foreach (var width in new[] { 320, 390, 768, 1024, 1440 })
      {
        await page.SetViewportSizeAsync(width, 900);
        await page.WaitForFunctionAsync("w => document.documentElement.clientWidth === w", width);
        var documentWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.True(documentWidth <= width + 1, $"Library is {documentWidth}px wide at {width}px.");
      }
    }
    Assert.Equal(0, mutations);
    Assert.Empty(errors);
  }
}
