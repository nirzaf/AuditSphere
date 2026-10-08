using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
namespace AuditSphereOps.E2E.Tests;
public sealed class AngularCurrencyConfigurationJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-CURRENCY-CONFIGURATION")]
  public async Task NativeExactPreparation_DraftRecovery_IndependentApproval_AndRevocation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-CURRENCY-CONFIGURATION");
    await using (var db = host.CreateDbContext()) { db.RoleGrants.Add(PbcSeed.AdminGrant(host.Fixture.FirmId, host.Fixture.Reviewer)); await db.SaveChangesAsync(); }
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Admin, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright); var page = await browser.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
    const string route = "/ui/app/accounting/currency-configuration";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "FX rates & policies", Exact = true })).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "New rate set", Exact = true }).ClickAsync();
    await page.GetByLabel("Code", new() { Exact = true }).FillAsync("SYN-NATIVE-RATES"); await page.GetByLabel("Rate source", new() { Exact = true }).FillAsync("Synthetic exact bank observation");
    await page.GetByLabel("Effective from", new() { Exact = true }).FillAsync("2026-01-01"); await page.GetByLabel("Effective to", new() { Exact = true }).FillAsync("2026-12-31");
    await page.GetByRole(AriaRole.Button, new() { Name = "Save tab draft", Exact = true }).ClickAsync(); await page.ReloadAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "New rate set", Exact = true }).ClickAsync(); await page.GetByRole(AriaRole.Button, new() { Name = "Recover tab draft", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByLabel("Code", new() { Exact = true })).ToHaveValueAsync("SYN-NATIVE-RATES");
    var assent = page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed the exact inputs and current persisted configuration.", Exact = true }); await Assertions.Expect(assent).Not.ToBeCheckedAsync(); await assent.CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Create draft", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Add observation", Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("From currency", new() { Exact = true }).FillAsync("USD"); await page.GetByLabel("To currency", new() { Exact = true }).FillAsync("QAR"); await page.GetByLabel("Observation date", new() { Exact = true }).FillAsync("2026-12-31");
    await page.GetByLabel("Exact DIRECT rate", new() { Exact = true }).FillAsync("3.7000001"); await assent.CheckAsync(); await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add observation", Exact = true })).ToBeDisabledAsync();
    await page.GetByLabel("Exact DIRECT rate", new() { Exact = true }).FillAsync("3.700001"); await Assertions.Expect(page.GetByTestId("currency-rate-intent")).ToContainTextAsync("DIRECT 3.700001"); await Assertions.Expect(assent).Not.ToBeCheckedAsync(); await assent.CheckAsync(); await page.GetByRole(AriaRole.Button, new() { Name = "Add observation", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Table).Last).ToContainTextAsync("3.700001"); await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Approve rate set", Exact = true })).ToHaveCountAsync(0);
    var checkerOrigin = await host.StartApiForIdentityAsync(host.Fixture.Reviewer, new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    var checker = await browser.NewPageAsync(); await checker.GotoAsync(checkerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await checker.GetByRole(AriaRole.Button, new() { Name = "Open SYN-NATIVE-RATES", Exact = true }).ClickAsync(); await checker.GetByRole(AriaRole.Checkbox, new() { Name = "I independently reviewed this exact rate set and every observation.", Exact = true }).CheckAsync(); await checker.GetByRole(AriaRole.Button, new() { Name = "Approve rate set", Exact = true }).ClickAsync();
    await Assertions.Expect(checker.GetByRole(AriaRole.Table).First).ToContainTextAsync("approved");
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh persisted state", Exact = true }).ClickAsync(); await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Add observation", Exact = true })).ToHaveCountAsync(0);
    await using (var db = host.CreateDbContext()) { var set = await db.ExchangeRateSetVersions.SingleAsync(); Assert.Equal("APPROVED", set.Status); Assert.Equal(host.Fixture.Reviewer.Id, set.ApprovedByUserId); Assert.Equal(3.700001m, (await db.ExchangeRates.SingleAsync()).Rate); await db.Users.Where(x => x.Id == host.Fixture.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1)); }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 }); Assert.DoesNotContain("3.700001", await page.Locator("body").InnerTextAsync()); Assert.Empty(errors);
  }
}
