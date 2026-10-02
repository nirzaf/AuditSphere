using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularNavigationJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-API-NAVIGATION-E2E")]
  public async Task AuthorizedSearch_OpensNativeRoutes_AndSkipLinkPreservesContext()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-API-NAVIGATION-E2E");
    var f = host.Fixture;
    await using (var db = host.CreateDbContext())
      foreach (var objective in new[] { "Navigation bank statements", "Navigation bank reconciliation" })
      {
        var result = await PbcService.CreateRequestAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
          new(f.EngagementId, objective, "TEST", "2026-01-01", "2026-12-31", "Cash", "PDF", "Totals",
            f.Client.Id, f.Staff.Id, f.Reviewer.Id, "2027-01-31", "Confidential", "Complete readable documents"));
        Assert.True(result.Succeeded, result.Message);
      }
    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fui%2Fapp");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();
    Assert.Equal(0, await page.Locator("aside a[href='/app'],aside a[href='/portal']").CountAsync());
    var search = page.Locator("audit-global-search");
    await search.GetByRole(AriaRole.Combobox, new() { Name = "Search your workspace", Exact = true }).FillAsync("Navigation bank");
    await Assertions.Expect(search.Locator("li")).ToHaveCountAsync(2);
    var link = search.GetByRole(AriaRole.Link, new() { Name = "Navigation bank statements", Exact = true });
    Assert.Equal($"/ui/app/engagements/{f.EngagementId}/pbc", await link.GetAttributeAsync("href"));
    await link.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Prepared-by-client requests", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("audit-pbc-inbox")).ToContainTextAsync("Navigation bank reconciliation");
    var url = page.Url;
    var skip = page.GetByRole(AriaRole.Link, new() { Name = "Skip to main content", Exact = true });
    await skip.FocusAsync(); await skip.ClickAsync();
    await Assertions.Expect(page.Locator("main")).ToBeFocusedAsync();
    Assert.Equal(url, page.Url);
    await page.ReloadAsync();
    await Assertions.Expect(page.Locator("audit-pbc-inbox")).ToContainTextAsync("Navigation bank statements");
    Assert.Empty(errors);
  }
}
