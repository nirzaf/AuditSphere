using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

internal static class PlaywrightBrowser
{
  public static Task<IBrowser> LaunchAsync(IPlaywright playwright)
  {
    var browserName = Environment.GetEnvironmentVariable("E2E_BROWSER")?.Trim().ToLowerInvariant();
    if (!string.IsNullOrEmpty(browserName) && browserName != "chrome")
      throw new InvalidOperationException($"Only Google Chrome is supported for E2E_BROWSER; received '{browserName}'.");

    return playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
      Channel = "chrome",
      Headless = true
    });
  }
}
