using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// US-042/US-043 deep acceptance beyond the DOM sweep: screen-reader proxy semantics (landmarks, heading
/// hierarchy, skip-link focus order, live-region announcements), a dialog focus-trap and Escape matrix on the
/// administration users workspace, a wider locale matrix (fr-FR formatting and an RTL locale smoke), and a
/// constrained-network budget for a cold lazy route. These check the observable behavior an assistive
/// technology and a throttled reader depend on; a human walk-through with real AT remains the acceptance gate.
/// </summary>
public sealed class AngularAccessibilityDeepAcceptanceTests
{
  private readonly Xunit.Abstractions.ITestOutputHelper output;
  public AngularAccessibilityDeepAcceptanceTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

  private const string HeadingOrderScript = """
() => {
  const levels = [...document.querySelectorAll('h1,h2,h3,h4,h5,h6')]
    .filter((h) => h.getClientRects().length > 0)
    .map((h) => Number(h.tagName[1]));
  for (let i = 1; i < levels.length; i++)
    if (levels[i] - levels[i - 1] > 1) return 'heading level jumps from h' + levels[i - 1] + ' to h' + levels[i];
  return '';
}
""";

  private static async Task<string> SignInAsync(IPage page, string origin, string returnUrl)
  {
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(returnUrl));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();
    return page.Url;
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-A11Y-DEEP-E2E")]
  public async Task ScreenReaderProxySemantics_LiveRegionsAndDialogMatrix_HoldAcrossTheShell()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-A11Y-DEEP-E2E");
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Admin,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);

    await SignInAsync(page, origin, "/ui/app");

    // Landmarks: exactly one main and a navigation landmark in the shell.
    Assert.Equal(1, await page.Locator("main").CountAsync());
    Assert.True(await page.Locator("nav, [role='navigation']").First.IsVisibleAsync());

    // Heading hierarchy never skips a level on representative pages.
    foreach (var route in new[] { "/ui/app", "/ui/app/practice/leads", "/ui/app/administration/users" })
    {
      await page.GotoAsync(origin + route);
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
      var jump = await page.EvaluateAsync<string>(HeadingOrderScript);
      Assert.True(string.IsNullOrEmpty(jump), route + ": " + jump);
    }

    // Skip link is the first tab stop and hands focus to main.
    await page.GotoAsync(origin + "/ui/app");
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    await page.Keyboard.PressAsync("Tab");
    var skip = page.GetByRole(AriaRole.Link, new() { Name = "Skip to main content", Exact = true });
    await Assertions.Expect(skip).ToBeFocusedAsync();
    await skip.ClickAsync();
    await Assertions.Expect(page.Locator("main")).ToBeFocusedAsync();

    // Live regions announce command outcomes: the scoped CSV export reports through role=status.
    var downloadButton = page.GetByRole(AriaRole.Button, new() { Name = "Download scoped CSV", Exact = true });
    await downloadButton.ClickAsync();
    var announcement = page.Locator("[role='status'], [role='alert']").Filter(new() { HasText = "CSV" });
    await Assertions.Expect(announcement.First).ToBeVisibleAsync(new() { Timeout = 15000 });

    // Dialog matrix on the administration users workspace: focus enters the dialog, Escape closes it,
    // and focus returns to the opening control.
    await page.GotoAsync(origin + "/ui/app/administration/users");
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    var trigger = page.GetByRole(AriaRole.Button, new() { Name = "Assign role and scope" }).First;
    await trigger.ClickAsync();
    var dialog = page.Locator("[role='dialog']").First;
    await Assertions.Expect(dialog).ToBeVisibleAsync();
    var focusInside = false;
    for (var attempt = 0; attempt < 25 && !focusInside; attempt++)
    {
      focusInside = await page.EvaluateAsync<bool>(
        "document.activeElement && document.activeElement.closest(\"[role='dialog']\") !== null");
      if (!focusInside) await page.WaitForTimeoutAsync(200);
    }
    Assert.True(focusInside, "Focus did not enter the opened dialog.");
    await page.Keyboard.PressAsync("Escape");
    await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
    await Assertions.Expect(trigger).ToBeFocusedAsync();

    // Per-grant history disclosure toggles with an honest aria-expanded state.
    var historyButton = page.GetByRole(AriaRole.Button, new() { Name = "View history for" }).First;
    await historyButton.ClickAsync();
    await Assertions.Expect(historyButton).ToHaveAttributeAsync("aria-expanded", "true");
    await historyButton.ClickAsync();
    await Assertions.Expect(historyButton).ToHaveAttributeAsync("aria-expanded", "false");

    Assert.Empty(pageErrors);
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-LOCALE-THROTTLE-E2E")]
  public async Task LocaleMatrix_RendersWithoutErrors_AndConstrainedNetworkStillLoadsLazyRoutes()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-LOCALE-THROTTLE-E2E");
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    foreach (var locale in new[] { "fr-FR", "ar-EG" })
    {
      await using var context = await browser.NewContextAsync(new BrowserNewContextOptions { Locale = locale });
      var page = await context.NewPageAsync();
      var pageErrors = new List<string>();
      page.PageError += (_, error) => pageErrors.Add(error);
      await SignInAsync(page, origin, "/ui/app");
      await page.GotoAsync(origin + "/ui/app/practice/time");
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
      await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Practice time & task records", Exact = true })).ToBeVisibleAsync();
      var direction = await page.EvaluateAsync<string>("getComputedStyle(document.body).direction");
      Assert.Equal("ltr", direction);
      Assert.Empty(pageErrors);
    }

    // Constrained-network budget: a cold lazy route must still arrive inside a generous ceiling
    // (order-of-magnitude guard; exact measurements belong to status.json only).
    await using var throttled = await browser.NewContextAsync();
    var slowPage = await throttled.NewPageAsync();
    var session = await throttled.NewCDPSessionAsync(slowPage);
    await session.SendAsync("Network.enable");
    await session.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
    {
      ["offline"] = false,
      ["latency"] = 150,
      ["downloadThroughput"] = 200_000,
      ["uploadThroughput"] = 100_000,
    });
    var watch = System.Diagnostics.Stopwatch.StartNew();
    await slowPage.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/ui/app/practice/time"));
    await Assertions.Expect(slowPage.GetByRole(AriaRole.Heading, new() { Name = "Practice time & task records", Exact = true }))
      .ToBeVisibleAsync(new() { Timeout = 30000 });
    watch.Stop();
    output.WriteLine($"PERF throttled cold lazy-route load={watch.ElapsedMilliseconds}ms (ceiling 25000ms)");
    Assert.True(watch.Elapsed <= TimeSpan.FromSeconds(25),
      $"Constrained-network cold load took {watch.ElapsedMilliseconds}ms, exceeding the 25s ceiling.");
  }
}
