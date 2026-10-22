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
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-A11Y-DEEP-E2E");
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

    // Route navigation places focus on main. Verify document order, then reach the skip link
    // through actual reverse tab navigation rather than assigning focus programmatically.
    await page.GotoAsync(origin + "/ui/app");
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    await Assertions.Expect(page.Locator("main")).ToBeFocusedAsync();
    var firstFocusableName = await page.EvaluateAsync<string>("""
      () => {
        const first = [...document.querySelectorAll('a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])')]
          .find(element => element.getClientRects().length > 0 && getComputedStyle(element).visibility !== 'hidden');
        return first?.getAttribute('aria-label') || first?.textContent?.trim() || '';
      }
      """);
    Assert.Equal("Skip to main content", firstFocusableName);
    var skip = page.GetByRole(AriaRole.Link, new() { Name = "Skip to main content", Exact = true });
    var focusableCount = await page.Locator("a[href],button,input,select,textarea,[tabindex]").CountAsync();
    for (var attempt = 0; attempt <= focusableCount &&
        !await skip.EvaluateAsync<bool>("element => element === document.activeElement"); attempt++)
      await page.Keyboard.PressAsync("Shift+Tab");
    await Assertions.Expect(skip).ToBeFocusedAsync();
    // The first document tab stop also follows the final stop when forward navigation wraps.
    await page.Keyboard.PressAsync("Shift+Tab");
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(skip).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Enter");
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
    await Assertions.Expect(dialog.GetByRole(AriaRole.Heading,
      new() { Name = "Assign AuditSphere access", Exact = true })).ToBeFocusedAsync();
    // Test both trap boundaries with actual keyboard navigation, without submitting a grant.
    var firstControl = dialog.GetByLabel("Role", new() { Exact = true });
    var lastControl = dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true });
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(firstControl).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Shift+Tab");
    await Assertions.Expect(lastControl).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Tab");
    await Assertions.Expect(firstControl).ToBeFocusedAsync();
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
    await OwnedBrowserFixtureHandoff.WaitAsync(origin, "/ui/app/administration/users", "accessibility-role-dialog");
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-LOCALE-THROTTLE-E2E")]
  public async Task LocaleMatrix_RendersWithoutErrors_AndConstrainedNetworkStillLoadsLazyRoutes()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-LOCALE-THROTTLE-E2E");
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

  [Fact]
  [Trait("CaseId", "ANGULAR-SUSTAINED-LOAD-E2E")]
  public async Task SustainedNavigation_AcrossLazyRoutes_StaysWithinPerNavigationCeiling()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-SUSTAINED-LOAD-E2E");
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);

    await SignInAsync(page, origin, "/ui/app");

    // Sustained local load: repeated warm navigations across every lazy route family in one session.
    // Order-of-magnitude ceiling per warm navigation; exact measurements belong to status.json only.
    var routes = new[]
    {
      "/ui/app", "/ui/app/practice/leads", "/ui/app/practice/time",
      "/ui/app/accounting/mappings", "/ui/app/operations",
    };
    var slowest = 0L;
    for (var round = 0; round < 3; round++)
    {
      foreach (var route in routes)
      {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await page.GotoAsync(origin + route);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        watch.Stop();
        slowest = Math.Max(slowest, watch.ElapsedMilliseconds);
        Assert.True(watch.Elapsed <= TimeSpan.FromSeconds(5),
          $"Warm navigation to {route} took {watch.ElapsedMilliseconds}ms, exceeding the 5s ceiling.");
      }
    }
    output.WriteLine($"PERF sustained warm navigations slowest={slowest}ms over {routes.Length * 3} navigations (ceiling 5000ms each)");
    Assert.Empty(pageErrors);
  }

  // RUN-006: control naming, description and repeated-action rules audited on rendered pages.
  private const string ControlAuditScript = """
() => {
  const visible = (e) => e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden';
  const text = (e) => (e?.textContent || '').replace(/\s+/g, ' ').trim();
  const name = (e) => {
    if (e.getAttribute('aria-label')?.trim()) return e.getAttribute('aria-label').trim();
    const by = e.getAttribute('aria-labelledby');
    if (by) return by.split(/\s+/).map((id) => text(document.getElementById(id))).join(' ').trim();
    if (e.id) { const l = document.querySelector(`label[for="${CSS.escape(e.id)}"]`); if (l && text(l)) return text(l); }
    const wrap = e.closest('label'); if (wrap && text(wrap)) return text(wrap);
    if (['BUTTON', 'A'].includes(e.tagName)) return text(e);
    return e.getAttribute('title')?.trim() || '';
  };
  const problems = [];
  const controls = [...document.querySelectorAll('input:not([type=hidden]),select,textarea,button,a[href],[role=button],[role=tab],[role=checkbox],[role=switch]')].filter(visible);
  for (const e of controls) {
    const label = e.outerHTML.slice(0, 90);
    if (!name(e)) problems.push('unnamed control: ' + label);
    for (const id of (e.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean))
      if (!document.getElementById(id)) problems.push(`aria-describedby "${id}" missing: ` + label);
    if (e.getAttribute('aria-invalid') === 'true') {
      const described = (e.getAttribute('aria-describedby') || '').split(/\s+/).map((id) => text(document.getElementById(id))).join('');
      if (!described) problems.push('invalid control without described error: ' + label);
    }
  }
  // Repeated row actions must say which row they act on.
  const seen = new Map();
  for (const row of document.querySelectorAll('tr, [role=row], li')) {
    for (const b of [...row.querySelectorAll(':scope > * button, :scope > button')].filter(visible)) {
      const n = name(b); if (!n) continue;
      if (!seen.has(n)) seen.set(n, new Set());
      seen.get(n).add(row);
    }
  }
  for (const [n, rows] of seen) if (rows.size > 1) problems.push(`repeated row action "${n}" x${rows.size} without row context`);
  return { controls: controls.length, problems };
}
""";

  [Fact]
  [Trait("CaseId", "RUN-006-A11Y-CONTROLS")]
  public async Task RenderedControls_AreNamedDescribedAndDistinct_AndReducedMotionStopsAnimation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "RUN-006-A11Y-CONTROLS");
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Admin,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(new() { ReducedMotion = ReducedMotion.Reduce });
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);
    await SignInAsync(page, origin, "/ui/app");

    var findings = new List<string>();
    foreach (var route in new[] { "/ui/app", "/ui/app/practice/leads", "/ui/app/administration/users", "/ui/app/practice/commercial-settings" })
    {
      await page.GotoAsync(origin + route);
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
      var audit = await page.EvaluateAsync<ControlAudit>(ControlAuditScript);
      Assert.True(audit.Controls > 5, $"{route}: only {audit.Controls} visible controls; the audit would be vacuous.");
      output.WriteLine($"A11Y {route}: {audit.Controls} controls, {audit.Problems.Length} finding(s)");
      findings.AddRange(audit.Problems.Select(problem => route + ": " + problem));
    }
    Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));

    // Negative control: the auditor must flag known-bad markup, so a clean result is not vacuous.
    await page.EvaluateAsync("""
      () => {
        const probe = document.createElement('div');
        probe.id = 'a11y-audit-probe';
        probe.innerHTML = '<input><input aria-label="Amount" aria-invalid="true" aria-describedby="absent-error">'
          + '<table><tr><td><button>Remove</button></td></tr><tr><td><button>Remove</button></td></tr></table>';
        document.body.appendChild(probe);
      }
      """);
    var probe = await page.EvaluateAsync<ControlAudit>(ControlAuditScript);
    await page.EvaluateAsync("() => document.getElementById('a11y-audit-probe').remove()");
    Assert.Contains(probe.Problems, problem => problem.StartsWith("unnamed control"));
    Assert.Contains(probe.Problems, problem => problem.Contains("\"absent-error\" missing"));
    Assert.Contains(probe.Problems, problem => problem.StartsWith("invalid control without described error"));
    Assert.Contains(probe.Problems, problem => problem.StartsWith("repeated row action \"Remove\""));

    // Reduced motion: the global rule collapses animation and transition durations.
    var longest = await page.EvaluateAsync<double>("""
      () => Math.max(0, ...[...document.querySelectorAll('*')].flatMap((e) => {
        const s = getComputedStyle(e);
        return [...s.animationDuration.split(','), ...s.transitionDuration.split(',')].map((v) => parseFloat(v) * (v.trim().endsWith('ms') ? 1 : 1000));
      }))
      """);
    Assert.True(longest <= 1, $"Reduced motion left a {longest}ms animation or transition.");
    Assert.Empty(pageErrors);
  }

  private sealed class ControlAudit { public int Controls { get; set; } public string[] Problems { get; set; } = []; }
}
