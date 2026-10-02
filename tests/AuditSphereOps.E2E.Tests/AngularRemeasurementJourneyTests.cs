using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
namespace AuditSphereOps.E2E.Tests;
public sealed class AngularRemeasurementJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-FX-NATIVE-LIFECYCLE")]
  public async Task NativeDraftRecovery_ExactPreparation_IndependentApproval_StaleInputs_AndRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-FX-NATIVE-LIFECYCLE");
    RemeasurementFixture.Inputs i; await using (var db = host.CreateDbContext()) i = await RemeasurementFixture.SeedAsync(db, host.Fixture);
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff, settings); var reviewerOrigin = await host.StartApiForIdentityAsync(host.Fixture.Reviewer, settings);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright); await using var context = await browser.NewContextAsync(); await using var reviewContext = await browser.NewContextAsync();
    var page = await context.NewPageAsync(); var reviewer = await reviewContext.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e); reviewer.PageError += (_, e) => errors.Add(e);
    const string route = "/ui/app/accounting/remeasurement";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Currency remeasurement workpapers", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Combobox, new() { Name = "Client period and engagement", Exact = true })).ToHaveValueAsync(i.Period + "|" + host.Fixture.EngagementId);
    foreach (var (label, value) in new[] { ("Stable source reference", "open-001"), ("Evidence snapshot ID", i.Snapshot.ToString()), ("Foreign-currency amount", "100.123456"), ("Prior functional carrying amount", "370.123456") }) await page.GetByLabel(label, new() { Exact = true }).FillAsync(value);
    await page.GetByRole(AriaRole.Combobox, new() { Name = "Imported GL source line", Exact = true }).SelectOptionAsync(i.Line.ToString());
    await page.GetByRole(AriaRole.Button, new() { Name = "Save workpaper draft in tab", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Remeasurement tab draft", Exact = true })).ToContainTextAsync("saved in this tab only");
    await page.ReloadAsync(); await Assertions.Expect(page.GetByLabel("Stable source reference", new() { Exact = true })).ToHaveValueAsync("");
    await page.GetByRole(AriaRole.Button, new() { Name = "Recover workpaper draft", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByLabel("Foreign-currency amount", new() { Exact = true })).ToHaveValueAsync("100.123456");
    const string reviewLabel = "I reviewed this exact client, engagement, period, evidence and approved rate inputs.";
    await Assertions.Expect(page.GetByRole(AriaRole.Checkbox, new() { Name = reviewLabel, Exact = true })).Not.ToBeCheckedAsync();
    var prepare = page.GetByRole(AriaRole.Button, new() { Name = "Prepare reviewed workpaper", Exact = true }); await Assertions.Expect(prepare).ToBeDisabledAsync();
    await page.GetByRole(AriaRole.Checkbox, new() { Name = reviewLabel, Exact = true }).CheckAsync(); await prepare.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workpaper · SUBMITTED", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Approve independently", Exact = true })).ToHaveCountAsync(0);
    await reviewer.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await reviewer.GetByRole(AriaRole.Button, new() { Name = "Open / review", Exact = true }).ClickAsync();
    var approve = reviewer.GetByRole(AriaRole.Button, new() { Name = "Approve independently", Exact = true }); await Assertions.Expect(approve).ToBeDisabledAsync();
    await Assertions.Expect(reviewer.GetByRole(AriaRole.Table, new() { Name = "Currency remeasurement item calculations", Exact = true })).ToContainTextAsync("100.123456");
    await reviewer.GetByRole(AriaRole.Checkbox, new() { Name = "I independently reviewed this exact workpaper and current source revisions.", Exact = true }).CheckAsync(); await approve.ClickAsync();
    await Assertions.Expect(reviewer.GetByRole(AriaRole.Heading, new() { Name = "Workpaper · APPROVED", Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Stable source reference", new() { Exact = true }).FillAsync("stale-draft");
    await page.GetByRole(AriaRole.Navigation, new() { Name = "Breadcrumb", Exact = true }).GetByRole(AriaRole.Link, new() { Name = "Client accounting", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Keep editing", Exact = true }).ClickAsync(); await Assertions.Expect(page.GetByLabel("Stable source reference", new() { Exact = true })).ToHaveValueAsync("stale-draft");
    await using (var db = host.CreateDbContext()) await db.SourceImportBatches.Where(x => x.FirmId == host.Fixture.FirmId && x.PeriodId == i.Period).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "REJECTED"));
    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh approved inputs", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Remeasurement tab draft", Exact = true })).ToContainTextAsync("submission is blocked");
    await Assertions.Expect(page.GetByLabel("Stable source reference", new() { Exact = true })).ToHaveValueAsync("stale-draft");
    await using (var db = host.CreateDbContext()) { Assert.Equal("APPROVED", (await db.CurrencyRemeasurementSchedules.SingleAsync()).Status); Assert.Equal(100.123456m, (await db.CurrencyRemeasurementItems.SingleAsync()).ForeignCurrencyAmount); (await db.Users.SingleAsync(x => x.Id == host.Fixture.Staff.Id)).SessionEpoch++; await db.SaveChangesAsync(); }
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    Assert.DoesNotContain("stale-draft", await page.Locator("body").InnerTextAsync()); Assert.Empty(errors);
  }
}
