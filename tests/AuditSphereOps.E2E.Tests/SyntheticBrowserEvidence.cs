using System.Text.Json;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>Opt-in evidence for synthetic regression fixtures only. Never captures headers, cookies or tokens.</summary>
internal sealed class SyntheticBrowserEvidence : IAsyncDisposable
{
  private readonly IPage page;
  private readonly string path;
  private readonly Guid expectedUserId;
  private readonly Guid expectedFirmId;
  private readonly object gate = new();
  private readonly List<Task> pending = [];

  public SyntheticBrowserEvidence(IPage page, OwnedHost host, string name, Guid expectedUserId)
  {
    this.page = page;
    this.expectedUserId = expectedUserId;
    expectedFirmId = host.FirmId;
    path = Path.Combine(host.EvidenceRoot, name + ".browser.log");
    Write($"synthetic-fixture actor={expectedUserId:D}; firm={expectedFirmId:D}");
    page.Request += OnRequest;
    page.RequestFailed += OnRequestFailed;
    page.Response += OnResponse;
  }

  private static string Location(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
    ? uri.AbsolutePath : "<non-absolute>";

  private void Write(string value)
  {
    lock (gate) File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {value}\n");
  }

  private void OnRequest(object? sender, IRequest request) => Write($"request {request.Method} {Location(request.Url)}");
  private void OnRequestFailed(object? sender, IRequest request) =>
    Write($"request-failed {request.Method} {Location(request.Url)} transportFailure={request.Failure?.StartsWith("net::", StringComparison.Ordinal) == true}");
  private void OnResponse(object? sender, IResponse response)
  {
    Write($"response {response.Status} {Location(response.Url)}");
    if (!Uri.TryCreate(response.Url, UriKind.Absolute, out var uri)) return;
    // Read only the session contract, in memory. Never retain arbitrary response bodies or page text.
    if (uri.AbsolutePath != "/api/ui/session") return;
    lock (gate) pending.Add(CaptureSessionAsync(response));
  }

  private async Task CaptureSessionAsync(IResponse response)
  {
    try
    {
      using var document = JsonDocument.Parse(await response.TextAsync().WaitAsync(TimeSpan.FromSeconds(5)));
      var root = document.RootElement;
      var userMatches = root.TryGetProperty("userId", out var user) && user.TryGetGuid(out var userId) && userId == expectedUserId;
      var firmMatches = root.TryGetProperty("firmId", out var firm) && firm.TryGetGuid(out var firmId) && firmId == expectedFirmId;
      var staff = root.TryGetProperty("staff", out var value) && value.ValueKind == JsonValueKind.True;
      Write($"session expectedActor={userMatches}; expectedFirm={firmMatches}; staff={staff}");
    }
    catch (Exception exception) { Write("session-unavailable " + exception.GetType().Name); }
  }

  public async ValueTask DisposeAsync()
  {
    page.Request -= OnRequest;
    page.RequestFailed -= OnRequestFailed;
    page.Response -= OnResponse;
    Task[] reads;
    lock (gate) reads = pending.ToArray();
    await Task.WhenAll(reads);
  }
}
