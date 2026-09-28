using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// In-memory Microsoft Graph drive + SharePoint upload/download host for provider tests. It enforces
/// the Graph rules the provider depends on: exact-parent path lookups, conflictBehavior=fail (409),
/// 320 KiB-aligned upload fragments, redirected content downloads and newest-first versions.
/// </summary>
internal sealed class FakeGraphDrive : HttpMessageHandler
{
  public const string DriveId = "b!fake-drive";
  public const string RootId = "ROOT";
  private sealed record Node(string Id, string Name, string ParentId, bool Folder)
  {
    public List<byte[]> Versions { get; } = [];
  }

  private readonly ConcurrentDictionary<string, Node> nodes = new() { [RootId] = new(RootId, "root", "", true) };
  private readonly ConcurrentDictionary<string, (string ParentId, string Name, long Total, MemoryStream Buffer)> sessions = new();
  public List<string> FragmentRanges { get; } = [];
  public int BearerSentToSharePoint { get; private set; }
  public int FolderCreates { get; private set; }
  public bool FailProbeReadback { get; set; }

  public IEnumerable<(string Id, string Name, string ParentId, bool Folder)> Items =>
    nodes.Values.Select(x => (x.Id, x.Name, x.ParentId, x.Folder));

  public void Tamper(string itemId, byte[] content) => nodes[itemId].Versions.Add(content);

  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
  {
    var uri = request.RequestUri!;
    if (uri.Host.EndsWith(".sharepoint.com", StringComparison.Ordinal))
    {
      if (request.Headers.Authorization is not null) BearerSentToSharePoint++;
      return await SharePointAsync(request, uri, ct);
    }
    if (request.Headers.Authorization?.Parameter != "sites-selected-token") return new(HttpStatusCode.Unauthorized);
    var path = Uri.UnescapeDataString(uri.AbsolutePath);
    var prefix = $"/v1.0/drives/{DriveId}/items/";
    if (!path.StartsWith(prefix, StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
    var rest = path[prefix.Length..];

    // Path-relative addressing: {parent}:/{name}: or {parent}:/{name}:/content | /createUploadSession
    var colon = rest.IndexOf(":/", StringComparison.Ordinal);
    if (colon > 0)
    {
      var parent = rest[..colon];
      var tail = rest[(colon + 2)..];
      var end = tail.IndexOf(':');
      var name = end >= 0 ? tail[..end] : tail;
      var action = end >= 0 ? tail[(end + 1)..] : "";
      var child = nodes.Values.FirstOrDefault(x => x.ParentId == parent && x.Name == name);
      if (request.Method == HttpMethod.Get && action == "")
        return child is null ? new(HttpStatusCode.NotFound) : Json(Item(child));
      if (request.Method == HttpMethod.Put && action == "/content")
      {
        var bytes = await request.Content!.ReadAsByteArrayAsync(ct);
        var file = child ?? new Node(Guid.NewGuid().ToString("N"), name, parent, false);
        file.Versions.Add(bytes);
        nodes[file.Id] = file;
        return Json(Item(file), HttpStatusCode.Created);
      }
      if (request.Method == HttpMethod.Post && action == "/createUploadSession")
      {
        if (child is not null) return new(HttpStatusCode.Conflict);
        var session = Guid.NewGuid().ToString("N");
        sessions[session] = (parent, name, 0, new MemoryStream());
        return Json(new { uploadUrl = $"https://fake.sharepoint.com/upload/{session}?tempauth=secret" });
      }
      return new(HttpStatusCode.BadRequest);
    }

    var segments = rest.Split('/');
    var id = segments[0];
    if (!nodes.TryGetValue(id, out var node)) return new(HttpStatusCode.NotFound);
    if (segments.Length == 1 && request.Method == HttpMethod.Get) return Json(Item(node));
    if (segments.Length == 1 && request.Method == HttpMethod.Delete) { nodes.TryRemove(id, out _); return new(HttpStatusCode.NoContent); }
    if (segments is [_, "children"] && request.Method == HttpMethod.Post)
    {
      using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
      var name = body.RootElement.GetProperty("name").GetString()!;
      if (nodes.Values.Any(x => x.ParentId == id && x.Name == name)) return new(HttpStatusCode.Conflict);
      var folder = new Node(Guid.NewGuid().ToString("N"), name, id, true);
      nodes[folder.Id] = folder;
      FolderCreates++;
      return Json(Item(folder), HttpStatusCode.Created);
    }
    if (segments is [_, "versions"])
      return Json(new { value = Enumerable.Range(1, node.Versions.Count).Reverse().Select(v => new { id = $"{v}.0" }) });
    if (segments is [_, "content"])
      return Redirect($"https://fake.sharepoint.com/download/{id}/{node.Versions.Count}.0?tempauth=secret");
    if (segments is [_, "versions", var version, "content"])
      return Redirect($"https://fake.sharepoint.com/download/{id}/{version}?tempauth=secret");
    return new(HttpStatusCode.BadRequest);
  }

  private async Task<HttpResponseMessage> SharePointAsync(HttpRequestMessage request, Uri uri, CancellationToken ct)
  {
    var segments = uri.AbsolutePath.Trim('/').Split('/');
    if (segments is ["upload", var sessionId] && request.Method == HttpMethod.Put && sessions.TryGetValue(sessionId, out var s))
    {
      var range = request.Content!.Headers.ContentRange!;
      FragmentRanges.Add(range.ToString());
      var bytes = await request.Content.ReadAsByteArrayAsync(ct);
      var last = range.To + 1 == range.Length;
      if (range.From != s.Buffer.Length || (!last && bytes.Length % (320 * 1024) != 0))
        return new(HttpStatusCode.RequestedRangeNotSatisfiable);
      s.Buffer.Write(bytes);
      if (!last) return new(HttpStatusCode.Accepted) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
      var file = new Node(Guid.NewGuid().ToString("N"), s.Name, s.ParentId, false);
      file.Versions.Add(s.Buffer.ToArray());
      nodes[file.Id] = file;
      sessions.TryRemove(sessionId, out _);
      return Json(Item(file), HttpStatusCode.Created);
    }
    if (segments is ["download", var itemId, var version] && request.Method == HttpMethod.Get && nodes.TryGetValue(itemId, out var n))
    {
      var index = (int)double.Parse(version, System.Globalization.CultureInfo.InvariantCulture) - 1;
      var content = n.Versions[index];
      if (FailProbeReadback && n.Name.StartsWith(".auditsphere-probe", StringComparison.Ordinal))
      {
        content = [.. content];
        content[0] ^= 0xFF; // same size, different bytes
      }
      return new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
    }
    return new(HttpStatusCode.NotFound);
  }

  private static object Item(Node node) => node.Folder
    ? new { id = node.Id, name = node.Name, size = 0, folder = new { childCount = 0 }, parentReference = new { id = node.ParentId } }
    : new { id = node.Id, name = node.Name, size = node.Versions[^1].Length, file = new { mimeType = "application/octet-stream" }, parentReference = new { id = node.ParentId } };

  private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
    new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

  private static HttpResponseMessage Redirect(string url)
  {
    var response = new HttpResponseMessage(HttpStatusCode.Found);
    response.Headers.Location = new Uri(url);
    return response;
  }
}

internal sealed class FakeSelectedSiteTokens : ISelectedSiteTokenSource
{
  public Task<SelectedSiteToken> GetAsync(string tenantId, string credentialReference, CancellationToken ct) =>
    Task.FromResult(new SelectedSiteToken("sites-selected-token", tenantId, new HashSet<string> { "Sites.Selected" }));
}
