using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// US-012 source search and shortcut acceptance beyond request deduplication: the global slash shortcut
/// focuses authorized search from page content, Escape dismisses results without leaving stale state,
/// and the shortcut refuses to hijack slash typed inside a form field. A real screen-reader and wider
/// locale acceptance remain separate gates.
/// </summary>
public sealed class AngularSearchShortcutAcceptanceTests
{
  private readonly Xunit.Abstractions.ITestOutputHelper output;
  public AngularSearchShortcutAcceptanceTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

  [Fact]
  [Trait("CaseId", "ANGULAR-SEARCH-SHORTCUT-E2E")]
  public async Task SlashShortcutFocusesSearch_EscapeDismisses_AndFieldsKeepTheirSlash()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-SEARCH-SHORTCUT-E2E");
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, e) => errors.Add(e);

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/ui/app"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();

    var searchField = page.GetByRole(AriaRole.Combobox, new() { Name = "Search your workspace", Exact = true });
    var body = page.Locator("main");

    // Slash from page content focuses authorized search without touching the pointer.
    await body.ClickAsync(new LocatorClickOptions { Position = new() { X = 5, Y = 5 } });
    await page.Keyboard.PressAsync("/");
    await Assertions.Expect(searchField).ToBeFocusedAsync();

    // Results arrive through the authorized contract and Escape dismisses them in place.
    var searchRegion = page.GetByRole(AriaRole.Region, new() { Name = "Global search", Exact = true });
    await searchField.FillAsync("PBC");
    var status = searchRegion.GetByRole(AriaRole.Status);
    await Assertions.Expect(status).ToContainTextAsync("results", new() { Timeout = 15000 });
    await searchField.PressAsync("Escape");
    await Assertions.Expect(status).ToHaveCountAsync(0);
    await Assertions.Expect(searchField).ToBeFocusedAsync();

    // A slash typed inside a real field belongs to that field: search is not hijacked.
    var filter = page.GetByRole(AriaRole.Textbox, new() { Name = "Search client name or ID", Exact = true });
    await filter.ClickAsync();
    await filter.PressSequentiallyAsync("/");
    await Assertions.Expect(filter).ToHaveValueAsync("/");
    await Assertions.Expect(searchField).Not.ToBeFocusedAsync();

    Assert.Empty(errors);
    output.WriteLine("US-012 search shortcut journey completed with zero page errors.");
  }
}
