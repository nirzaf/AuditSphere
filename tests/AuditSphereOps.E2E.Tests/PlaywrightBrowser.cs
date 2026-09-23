using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

internal static class PlaywrightBrowser
{
  public static Task<IBrowser> LaunchAsync(IPlaywright playwright)
  {
    var browserName = Environment.GetEnvironmentVariable("E2E_BROWSER")?.Trim().ToLowerInvariant();
    var browserType = browserName switch
    {
      null or "" or "chromium" => playwright.Chromium,
      "firefox" => playwright.Firefox,
      "webkit" => playwright.Webkit,
      _ => throw new InvalidOperationException($"Unsupported E2E_BROWSER '{browserName}'.")
    };
    return browserType.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
  }
}
