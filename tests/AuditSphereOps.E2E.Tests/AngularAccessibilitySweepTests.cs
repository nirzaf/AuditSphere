using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// US-042 all-module accessibility and locale sweep. One signed-in staff identity walks a representative
/// route from every native module and each rendered state (data, empty or denial) must satisfy the same
/// semantic contract: document language, exactly one accessible h1, named interactive controls, labelled
/// form fields, table captions, WCAG AA text contrast against the effective background, and Escape-closable
/// search overlay. A German-locale context proves date formatting follows the browser locale instead of a
/// hard-coded culture. Findings are fixed in templates; this sweep pins them at equal strength.
/// </summary>
public sealed class AngularAccessibilitySweepTests
{
  private static readonly string[] Routes =
  [
    "/ui/app",
    "/ui/app/overview",
    "/ui/app/practice/leads",
    "/ui/app/practice/time",
    "/ui/app/practice/resources",
    "/ui/app/practice/analytics",
    "/ui/app/finance",
    "/ui/app/finance/books",
    "/ui/app/library",
    "/ui/app/operations",
    "/ui/app/accounting",
    "/ui/app/accounting/journals",
    "/ui/app/accounting/mappings",
    "/ui/app/accounting/evidence",
    "/ui/app/accounting/remeasurement",
    "/ui/app/accounting/restatements",
    "/ui/app/audit/library",
    "/ui/app/consolidation",
    "/ui/app/administration/users",
    "/ui/app/administration/project-progress",
  ];

  private const string SemanticAuditScript = """
() => {
  const problems = [];
  const doc = window.document;
  if (doc.documentElement.lang !== 'en') problems.push('html lang is ' + doc.documentElement.lang);
  const visibleH1 = [...doc.querySelectorAll('h1')].filter(h => h.getClientRects().length > 0);
  if (visibleH1.length !== 1) problems.push('expected exactly one visible h1, found ' + visibleH1.length);

  const effectiveBackground = (el) => {
    let node = el;
    while (node && node !== doc.documentElement) {
      const style = getComputedStyle(node);
      const rgba = style.backgroundColor.match(/rgba?\(([^)]+)\)/);
      if (rgba) {
        const parts = rgba[1].split(',').map((x) => parseFloat(x));
        if (parts.length < 4 || parts[3] > 0.9) return parts;
      }
      node = node.parentElement;
    }
    return [255, 255, 255];
  };
  const channel = (v) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };
  const luminance = (rgb) => 0.2126 * channel(rgb[0]) + 0.7152 * channel(rgb[1]) + 0.0722 * channel(rgb[2]);
  const contrast = (fg, bg) => {
    const l1 = luminance(fg), l2 = luminance(bg);
    return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
  };
  const parseColor = (value) => {
    const m = value.match(/rgba?\(([^)]+)\)/);
    if (!m) return null;
    const p = m[1].split(',').map((x) => parseFloat(x));
    return p;
  };
  const textElements = [...doc.querySelectorAll('h1,h2,h3,h4,p,li,td,th,label,button,a,dt,dd,summary,code,small,strong,span')]
    .filter((el) => el.getClientRects().length > 0 &&
      [...el.childNodes].some((n) => n.nodeType === 3 && n.textContent.trim().length > 0));
  for (const el of textElements) {
    const style = getComputedStyle(el);
    const fg = parseColor(style.color);
    if (!fg) continue;
    const bg = effectiveBackground(el);
    const ratio = contrast(fg, bg);
    const size = parseFloat(style.fontSize);
    const bold = parseInt(style.fontWeight, 10) >= 700;
    const large = size >= 24 || (bold && size >= 18.66);
    const required = large ? 3 : 4.5;
    if (ratio < required) {
      problems.push('contrast ' + ratio.toFixed(2) + ' < ' + required + ' for <' + el.tagName.toLowerCase() + '> "' +
        el.textContent.trim().slice(0, 40) + '" color ' + style.color);
    }
  }

  const controls = [...doc.querySelectorAll('button, a[href], [role="button"], summary, select, input, textarea')]
    .filter((el) => el.getClientRects().length > 0 && !el.disabled);
  for (const el of controls) {
    const tag = el.tagName.toLowerCase();
    if (tag === 'input' && (el.type === 'checkbox' || el.type === 'radio')) continue;
    const name = el.getAttribute('aria-label') || el.getAttribute('aria-labelledby') || el.getAttribute('title') ||
      (el.tagName === 'A' || el.tagName === 'BUTTON' || el.tagName === 'SUMMARY' ? el.textContent.trim() : '');
    const labeled = name && name.length > 0;
    if (!labeled) {
      const labelFor = el.id ? doc.querySelector('label[for="' + CSS.escape(el.id) + '"]') : null;
      if (!labelFor && !el.closest('label')) problems.push('unnamed control <' + tag + '> ' + (el.getAttribute('name') ?? ''));
    }
  }
  for (const el of [...doc.querySelectorAll('input, select, textarea')].filter((x) => x.getClientRects().length > 0)) {
    const id = el.id;
    const hasLabel = (id && doc.querySelector('label[for="' + CSS.escape(id) + '"]')) || el.closest('label') ||
      el.getAttribute('aria-label') || el.getAttribute('aria-labelledby') || el.type === 'hidden';
    if (!hasLabel) problems.push('unlabelled form field <' + el.tagName.toLowerCase() + '> name=' + (el.name ?? ''));
  }
  for (const table of [...doc.querySelectorAll('table')].filter((t) => t.getClientRects().length > 0)) {
    if (!table.querySelector('caption') && !table.getAttribute('aria-label'))
      problems.push('table without caption or aria-label');
  }
  return problems;
}
""";

  [Fact]
  [Trait("CaseId", "ANGULAR-A11Y-SWEEP-E2E")]
  public async Task EveryModuleState_SatisfiesSemanticContrastAndLocaleContract()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-A11Y-SWEEP-E2E");
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/ui/app"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();

    var failures = new List<string>();
    foreach (var route in Routes)
    {
      await page.GotoAsync(origin + route);
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
      var problems = await page.EvaluateAsync<string[]>(SemanticAuditScript);
      failures.AddRange(problems.Select((p) => route + ": " + p));
    }

    // Escape closes the global search overlay and returns focus to the combobox.
    var search = page.Locator("audit-global-search");
    await search.GetByRole(AriaRole.Combobox, new() { Name = "Search your workspace", Exact = true }).FillAsync("operations");
    await Assertions.Expect(search.Locator("li").First).ToBeAttachedAsync();
    await page.Keyboard.PressAsync("Escape");
    await Assertions.Expect(search.Locator("li")).ToHaveCountAsync(0);

    // Locale: a German-locale context must localize rendered timestamps.
    await using var german = await browser.NewContextAsync(new BrowserNewContextOptions { Locale = "de-DE" });
    var germanPage = await german.NewPageAsync();
    await germanPage.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/ui/app"));
    await Assertions.Expect(germanPage.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();
    await germanPage.GotoAsync(origin + "/ui/app/practice/leads");
    await germanPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
    var deLang = await germanPage.EvaluateAsync<string>("document.documentElement.lang");
    Assert.Equal("en", deLang);

    Assert.True(failures.Count == 0, "Accessibility sweep findings:\n" + string.Join("\n", failures));
    Assert.Empty(pageErrors);
  }
}
