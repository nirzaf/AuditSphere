using System.Text.Json;

namespace AuditSphereOps.E2E.Tests;

/// <summary>Optional bounded manual inspection of a live owned fixture; no persistent reset or credential transfer.</summary>
internal static class OwnedBrowserFixtureHandoff
{
  internal static async Task WaitAsync(string origin, string path, string? target = null)
  {
    var selectedTarget = Environment.GetEnvironmentVariable("AUDITSPHERE_BROWSER_FIXTURE_TARGET");
    if (!string.IsNullOrEmpty(selectedTarget) && selectedTarget != target) return;
    // Optional bounded handoff reuses this owned synthetic API/database for built-in-browser inspection.
    // Basenames only; never overwrite an existing artifact or touch a persistent application database.
    var readyName = Environment.GetEnvironmentVariable("AUDITSPHERE_BROWSER_FIXTURE_READY_FILE");
    var releaseName = Environment.GetEnvironmentVariable("AUDITSPHERE_BROWSER_FIXTURE_RELEASE_FILE");
    if (!string.IsNullOrEmpty(readyName) && !string.IsNullOrEmpty(releaseName))
    {
      if (Path.GetFileName(readyName) != readyName || Path.GetFileName(releaseName) != releaseName)
        throw new InvalidOperationException("Fixture handoff names must be temporary-file basenames.");
      var ready = Path.Combine(Path.GetTempPath(), readyName);
      var release = Path.Combine(Path.GetTempPath(), releaseName);
      if (File.Exists(ready) || File.Exists(release)) throw new InvalidOperationException("Fixture handoff already exists.");
      var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
      var ownsReadyFile = false;
      try
      {
        await using (var stream = new FileStream(ready, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
          ownsReadyFile = true;
          await JsonSerializer.SerializeAsync(stream, new { origin, path, environment = "Owned synthetic test fixture", expiresAtUtc = deadline });
        }
        while (!File.Exists(release) && DateTimeOffset.UtcNow < deadline) await Task.Delay(250);
      }
      finally { if (ownsReadyFile) File.Delete(ready); }
    }
  }
}
