using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

internal static class PlaywrightBrowser
{
  public static Task<IBrowser> LaunchAsync(IPlaywright playwright)
  {
    var browserName = Environment.GetEnvironmentVariable("E2E_BROWSER")?.Trim().ToLowerInvariant();
    var browserType = browserName switch
    {
      null or "" or "chromium" or "chrome" => playwright.Chromium,
      "firefox" => playwright.Firefox,
      "webkit" => playwright.Webkit,
      _ => throw new InvalidOperationException($"Unsupported E2E_BROWSER '{browserName}'.")
    };
    var launch = new BrowserTypeLaunchOptions { Headless = true };
    if (browserName == "chrome") launch.Channel = "chrome";
    return browserType.LaunchAsync(launch);
  }
}
