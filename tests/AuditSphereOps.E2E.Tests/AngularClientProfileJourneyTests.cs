using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularClientProfileJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", "ANGULAR-CLIENT-PROFILE-E2E")]
  public async Task ScopedMetadataIndependentPagesReloadMobileAndRevocation(bool canonical)
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-CLIENT-PROFILE-E2E", startLegacyBlazorHosts: false);
    var f = host.Fixture;
    await using (var db = host.CreateDbContext()) { await ClientProfileWorkspaceSeed.PopulateAsync(db, f); }
    var prefix = canonical ? "" : "/ui";
    var origin = await host.StartApiForIdentityAsync(f.Staff, new Dictionary<string,string> {
      ["AngularUi__Enabled"]="true", ["AngularUi__CanonicalRoutes"]=canonical.ToString() });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(); var page = await context.NewPageAsync();
    var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    var path = prefix + "/app/clients/" + f.ClientId;
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Client profile", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("Trading as: Synthetic trading name", new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("audit-client dl")).ToContainTextAsync("SYNTHETIC-REG-001");
    await Assertions.Expect(page.Locator("audit-client dl")).ToContainTextAsync("9007199254740993");
    await Assertions.Expect(page.Locator("audit-client dl")).ToContainTextAsync("2026-01-02 12:30 UTC");
    var engagements = page.GetByRole(AriaRole.Region, new() { Name = "Associated engagements", Exact = true });
    var contacts = page.GetByRole(AriaRole.Region, new() { Name = "Client contacts", Exact = true });
    await Assertions.Expect(engagements.Locator("tbody tr")).ToHaveCountAsync(10);
    await Assertions.Expect(contacts.Locator("tbody tr")).ToHaveCountAsync(10);
    await engagements.GetByRole(AriaRole.Button, new() {Name="Next engagements",Exact=true}).ClickAsync();
    await Assertions.Expect(engagements).ToContainTextAsync("Page 2");
    await Assertions.Expect(contacts).ToContainTextAsync("Page 1");
    await contacts.GetByRole(AriaRole.Button, new() {Name="Next contacts",Exact=true}).ClickAsync();
    await Assertions.Expect(contacts).ToContainTextAsync("Page 2");
    await page.ReloadAsync();
    await Assertions.Expect(engagements).ToContainTextAsync("Page 2"); await Assertions.Expect(contacts).ToContainTextAsync("Page 2");
    await contacts.GetByLabel("Contacts per page", new(){Exact=true}).SelectOptionAsync("25");
    await Assertions.Expect(contacts.Locator("tbody tr")).ToHaveCountAsync(25);
    await Assertions.Expect(engagements).ToContainTextAsync("Page 2");
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name="Client portal intent",Exact=true })).ToContainTextAsync("Acceptance and Partner activation are required");
    Assert.DoesNotContain("EXCLUDED PRIVATE PROFILE", await page.Locator("body").InnerTextAsync());
    await page.SetViewportSizeAsync(390,844); Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await page.GotoAsync(origin + prefix + "/app/clients/" + Guid.NewGuid());
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() {Name="Client unavailable",Exact=true})).ToBeVisibleAsync();
    Assert.DoesNotContain("SYNTHETIC-REG-001", await page.Locator("body").InnerTextAsync());
    await page.GotoAsync(origin + path); await Assertions.Expect(page.Locator("audit-client dl")).ToContainTextAsync("SYNTHETIC-REG-001");
    await using (var db = host.CreateDbContext())
    { await db.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.EngagementId == null).ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow)); }
    await page.GetByRole(AriaRole.Button, new() {Name="Refresh client",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() {Name="Client unavailable",Exact=true})).ToBeVisibleAsync();
    Assert.DoesNotContain("SYNTHETIC-REG-001", await page.Locator("body").InnerTextAsync()); Assert.Empty(errors);
  }
}
