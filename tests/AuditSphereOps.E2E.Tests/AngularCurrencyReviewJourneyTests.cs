using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
namespace AuditSphereOps.E2E.Tests;
public sealed class AngularCurrencyReviewJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-INTAKE-CURRENCY")]
  public async Task NativeClosingReview_Identity_MissingRate_StaleFilters_AndRevokedSession()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-INTAKE-CURRENCY", startLegacyBlazorHosts: false); CurrencyReviewFixture.Inputs i;
    await using (var db = host.CreateDbContext()) i = await CurrencyReviewFixture.SeedAsync(db, host.Fixture, hiddenPrior: true);
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright); var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    var route = $"/ui/app/engagements/{host.Fixture.EngagementId}/tb-intake"; await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Trial balance intake", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Combobox, new() { Name = "Dataset", Exact = true }).SelectOptionAsync(i.Dataset.ToString());
    await page.GetByLabel("Presentation currency", new() { Exact = true }).FillAsync("QAR"); await page.GetByLabel("Highlight percent", new() { Exact = true }).FillAsync("10.123456");
    var request = page.GetByRole(AriaRole.Button, new() { Name = "Review currency and movements", Exact = true }); await request.ClickAsync();
    var result = page.GetByRole(AriaRole.Region, new() { Name = "Currency review result", Exact = true }); await Assertions.Expect(result).ToBeVisibleAsync(); await Assertions.Expect(result).ToContainTextAsync("No authorized prior dataset"); await Assertions.Expect(result).ToContainTextAsync("10.123456%"); await Assertions.Expect(result).ToContainTextAsync(i.RateSet.ToString()); await Assertions.Expect(result).ToContainTextAsync("Observation"); await Assertions.Expect(page.GetByRole(AriaRole.Table, new() { Name = "Currency review", Exact = true })).ToContainTextAsync("100.123456"); Assert.DoesNotContain("HIDDEN-PRIOR-MARKER", await page.Locator("body").InnerTextAsync());
    await page.GetByLabel("Highlight percent", new() { Exact = true }).FillAsync("20"); await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Request a fresh review");
    await page.GetByLabel("Presentation currency", new() { Exact = true }).FillAsync("USD"); await request.ClickAsync(); await Assertions.Expect(result).ToContainTextAsync("Same-currency identity"); await Assertions.Expect(result).ToContainTextAsync("No market observation");
    await page.GetByLabel("Presentation currency", new() { Exact = true }).FillAsync("EUR"); await request.ClickAsync(); await Assertions.Expect(result).ToHaveCountAsync(0); await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("No valid approved DIRECT closing rate");
    await page.GetByLabel("Presentation currency", new() { Exact = true }).FillAsync("QAR"); await request.ClickAsync(); await Assertions.Expect(result).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext()) { (await db.Users.SingleAsync(x => x.Id == host.Fixture.Staff.Id)).SessionEpoch++; await db.SaveChangesAsync(); }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 }); Assert.DoesNotContain("100.123456", await page.Locator("body").InnerTextAsync()); Assert.Empty(errors);
  }
}
