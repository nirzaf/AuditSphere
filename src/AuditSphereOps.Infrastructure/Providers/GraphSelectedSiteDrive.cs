using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>A drive item observed in the selected site, with its current version.</summary>
public sealed record SelectedSiteItem(string ItemId, string Name, string ParentId, long Size, bool IsFolder);

/// <summary>
/// Graph drive operations confined to one selected site. Every call uses the Sites.Selected token for
/// the exact tenant and credential slot; the Graph-issued upload/download URLs go only through the
/// restricted <see cref="GraphPreauthenticatedTransport"/> and never receive the bearer token.
/// Provider response text is never surfaced; failures map to allowlisted codes.
/// </summary>
public sealed class GraphSelectedSiteDrive(HttpClient graph, ISelectedSiteTokenSource tokens,
  GraphPreauthenticatedTransport transport, bool configured) : ISelectedSiteWorkspaceProvisioner
{
  private const string Graph = "https://graph.microsoft.com/v1.0";
  // Graph requires upload fragments in multiples of 320 KiB; 10 MiB = 32 × 320 KiB.
  internal const int FragmentBytes = 10 * 1024 * 1024;

  public bool IsConfigured => configured;

  public async Task<RemoteFolder> EnsureFolderAsync(SelectedSiteLocation location, string parentItemId, string name, CancellationToken ct)
  {
    var token = await TokenAsync(location, ct);
    var existing = await GetChildAsync(token, location.DriveId, parentItemId, name, ct);
    if (existing is not null)
      return existing.IsFolder ? new(existing.ItemId, existing.Name) : throw Blocked("selected-site-name-is-a-file");
    using (var create = Request(HttpMethod.Post, $"{Graph}/drives/{Id(location.DriveId)}/items/{Id(parentItemId)}/children", token))
    {
      create.Content = JsonContent.Create(new Dictionary<string, object>
      {
        ["name"] = name, ["folder"] = new { }, ["@microsoft.graph.conflictBehavior"] = "fail"
      });
      using var response = await graph.SendAsync(create, ct);
      if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
      {
        var item = await ItemAsync(response, ct);
        if (!item.IsFolder || item.ParentId != parentItemId) throw Blocked("selected-site-folder-mismatch");
        return new(item.ItemId, item.Name);
      }
      if (response.StatusCode != HttpStatusCode.Conflict) throw Classify(response);
    }
    // A concurrent or earlier attempt created it: reconcile by the exact name under the exact parent.
    var reconciled = await GetChildAsync(token, location.DriveId, parentItemId, name, ct);
    return reconciled is { IsFolder: true } ? new(reconciled.ItemId, reconciled.Name) : throw Blocked("selected-site-folder-conflict");
  }

  public async Task<RemoteCapabilityTest> TestReadWriteAsync(SelectedSiteLocation location, string folderItemId, CancellationToken ct)
  {
    var token = await TokenAsync(location, ct);
    var content = Encoding.UTF8.GetBytes($"AuditSphere selected-site capability probe {Guid.NewGuid():N} {DateTimeOffset.UtcNow:O}");
    var expected = Hex(SHA256.HashData(content));
    var name = $".auditsphere-probe-{Guid.NewGuid():N}.txt";
    string? itemId = null;
    string? correlation = null;
    try
    {
      using var put = Request(HttpMethod.Put, $"{Graph}/drives/{Id(location.DriveId)}/items/{Id(folderItemId)}:/{Uri.EscapeDataString(name)}:/content", token);
      put.Content = new ByteArrayContent(content);
      put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
      using var created = await graph.SendAsync(put, ct);
      correlation = RequestId(created);
      if (created.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
        return new(false, false, true, $"probe-upload-{(int)created.StatusCode}", correlation);
      var item = await ItemAsync(created, ct);
      itemId = item.ItemId;
      if (item.ParentId != folderItemId) return new(true, false, await DeleteAsync(token, location.DriveId, itemId, ct), "probe-parent-mismatch", correlation);
      var (digest, size) = await ContentDigestAsync(token, location.DriveId, itemId, null, content.Length, ct);
      var matched = digest == expected && size == content.Length;
      var deleted = await DeleteAsync(token, location.DriveId, itemId, ct);
      itemId = null;
      return new(true, matched, deleted, matched ? (deleted ? "upload-readback-delete-succeeded" : "probe-cleanup-failed") : "probe-readback-mismatch", correlation);
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
      var cleaned = itemId is null || await DeleteAsync(token, location.DriveId, itemId, ct);
      return new(itemId is not null, false, cleaned, ex is OperationBlockedException b ? b.Code : "probe-request-failed", correlation);
    }
  }

  /// <summary>Streams the staged chunks into one new file via a Graph upload session (conflict = fail).</summary>
  internal async Task<SelectedSiteItem> UploadAsync(SelectedSiteLocation location, string folderItemId, string name,
    PbcTransferPlan plan, CancellationToken ct)
  {
    var token = await TokenAsync(location, ct);
    string uploadUrl;
    using (var session = Request(HttpMethod.Post,
      $"{Graph}/drives/{Id(location.DriveId)}/items/{Id(folderItemId)}:/{Uri.EscapeDataString(name)}:/createUploadSession", token))
    {
      session.Content = JsonContent.Create(new { item = new Dictionary<string, string> { ["@microsoft.graph.conflictBehavior"] = "fail" } });
      using var response = await graph.SendAsync(session, ct);
      // 409: a previous attempt already created the file. Surface an unknown outcome so the durable
      // operation reconciles by exact name and digest instead of overwriting.
      if (response.StatusCode == HttpStatusCode.Conflict) throw new IOException("selected-site-file-exists");
      if (!response.IsSuccessStatusCode) throw Classify(response);
      using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
      uploadUrl = json.RootElement.TryGetProperty("uploadUrl", out var url) && url.GetString() is { Length: > 0 } u
        ? u : throw Blocked("selected-site-upload-session-invalid");
    }

    var buffer = new byte[FragmentBytes];
    var filled = 0;
    long offset = 0;
    SelectedSiteItem? final = null;
    foreach (var chunk in plan.Chunks)
    {
      await using var input = new FileStream(chunk.StagedPath, FileMode.Open, FileAccess.Read, FileShare.Read,
        64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
      int read;
      while ((read = await input.ReadAsync(buffer.AsMemory(filled, buffer.Length - filled), ct)) > 0)
      {
        filled += read;
        if (filled == buffer.Length)
        {
          final = await PutFragmentAsync(uploadUrl, buffer, filled, offset, plan.DeclaredByteCount, ct);
          offset += filled;
          filled = 0;
        }
      }
    }
    if (filled > 0)
    {
      final = await PutFragmentAsync(uploadUrl, buffer, filled, offset, plan.DeclaredByteCount, ct);
      offset += filled;
    }
    if (offset != plan.DeclaredByteCount || final is null) throw Blocked("selected-site-upload-incomplete");
    return final;
  }

  private async Task<SelectedSiteItem?> PutFragmentAsync(string uploadUrl, byte[] buffer, int count, long offset, long total, CancellationToken ct)
  {
    using var content = new ByteArrayContent(buffer, 0, count);
    content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + count - 1, total);
    using var response = await transport.SendAsync(new Uri(uploadUrl), HttpMethod.Put, content, ct);
    if (response.StatusCode == HttpStatusCode.Accepted) return null;
    if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK) return await ItemAsync(response, ct);
    throw response.StatusCode is HttpStatusCode.RequestedRangeNotSatisfiable or HttpStatusCode.Conflict
      ? Blocked("selected-site-upload-range-rejected")
      : new IOException("selected-site-upload-fragment-failed");
  }

  internal async Task<SelectedSiteItem?> GetChildAsync(SelectedSiteLocation location, string parentItemId, string name, CancellationToken ct) =>
    await GetChildAsync(await TokenAsync(location, ct), location.DriveId, parentItemId, name, ct);

  internal async Task<SelectedSiteItem?> GetItemAsync(SelectedSiteLocation location, string itemId, CancellationToken ct)
  {
    var token = await TokenAsync(location, ct);
    using var request = Request(HttpMethod.Get, $"{Graph}/drives/{Id(location.DriveId)}/items/{Id(itemId)}?$select=id,name,size,folder,file,parentReference", token);
    using var response = await graph.SendAsync(request, ct);
    if (response.StatusCode == HttpStatusCode.NotFound) return null;
    if (!response.IsSuccessStatusCode) throw Classify(response);
    return await ItemAsync(response, ct);
  }

  /// <summary>Current version ID of a file (Graph lists versions newest first).</summary>
  internal async Task<string> CurrentVersionAsync(SelectedSiteLocation location, string itemId, CancellationToken ct)
  {
    var token = await TokenAsync(location, ct);
    using var request = Request(HttpMethod.Get, $"{Graph}/drives/{Id(location.DriveId)}/items/{Id(itemId)}/versions?$select=id&$top=1", token);
    using var response = await graph.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode) throw Classify(response);
    using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    return json.RootElement.TryGetProperty("value", out var value) && value.GetArrayLength() > 0 &&
      value[0].TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } v
        ? v : throw Blocked("selected-site-version-unavailable");
  }

  /// <summary>SHA-256 of an exact version's bytes (or the current content when versionId is null).</summary>
  internal async Task<(string Sha256, long Size)> ContentDigestAsync(SelectedSiteLocation location, string itemId,
    string? versionId, long maxBytes, CancellationToken ct) =>
    await ContentDigestAsync(await TokenAsync(location, ct), location.DriveId, itemId, versionId, maxBytes, ct);

  private async Task<(string Sha256, long Size)> ContentDigestAsync(string token, string driveId, string itemId,
    string? versionId, long maxBytes, CancellationToken ct)
  {
    var path = versionId is null
      ? $"{Graph}/drives/{Id(driveId)}/items/{Id(itemId)}/content"
      : $"{Graph}/drives/{Id(driveId)}/items/{Id(itemId)}/versions/{Uri.EscapeDataString(versionId)}/content";
    using var request = Request(HttpMethod.Get, path, token);
    using var response = await graph.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect)
    {
      // Graph redirects content reads to a pre-authenticated download URL; fetch it without the token.
      var location = response.Headers.Location ?? throw Blocked("selected-site-download-location-missing");
      using var download = await transport.SendAsync(location, HttpMethod.Get, null, ct);
      if (!download.IsSuccessStatusCode) throw Blocked("selected-site-download-rejected");
      return await HashAsync(download, maxBytes, ct);
    }
    if (!response.IsSuccessStatusCode) throw Classify(response);
    return await HashAsync(response, maxBytes, ct);
  }

  private static async Task<(string, long)> HashAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
  {
    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    await using var stream = await response.Content.ReadAsStreamAsync(ct);
    var buffer = new byte[64 * 1024];
    long total = 0;
    int read;
    while ((read = await stream.ReadAsync(buffer, ct)) > 0)
    {
      total += read;
      if (total > maxBytes) throw Blocked("selected-site-content-too-large");
      digest.AppendData(buffer, 0, read);
    }
    return (Hex(digest.GetHashAndReset()), total);
  }

  /// <summary>Deletes one item (a folder deletes its children). Used for disposable test cleanup only.</summary>
  internal async Task<bool> DeleteItemAsync(SelectedSiteLocation location, string itemId, CancellationToken ct) =>
    await DeleteAsync(await TokenAsync(location, ct), location.DriveId, itemId, ct);

  private async Task<bool> DeleteAsync(string token, string driveId, string itemId, CancellationToken ct)
  {
    try
    {
      using var request = Request(HttpMethod.Delete, $"{Graph}/drives/{Id(driveId)}/items/{Id(itemId)}", token);
      using var response = await graph.SendAsync(request, ct);
      return response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound;
    }
    catch (HttpRequestException) { return false; }
  }

  private async Task<SelectedSiteItem?> GetChildAsync(string token, string driveId, string parentItemId, string name, CancellationToken ct)
  {
    using var request = Request(HttpMethod.Get,
      $"{Graph}/drives/{Id(driveId)}/items/{Id(parentItemId)}:/{Uri.EscapeDataString(name)}:?$select=id,name,size,folder,file,parentReference", token);
    using var response = await graph.SendAsync(request, ct);
    if (response.StatusCode == HttpStatusCode.NotFound) return null;
    if (!response.IsSuccessStatusCode) throw Classify(response);
    var item = await ItemAsync(response, ct);
    return item.ParentId == parentItemId && item.Name == name ? item : throw Blocked("selected-site-item-mismatch");
  }

  private async Task<string> TokenAsync(SelectedSiteLocation location, CancellationToken ct)
  {
    if (!configured) throw Blocked("selected-site-credential-unavailable");
    var token = await tokens.GetAsync(location.TenantId, location.CredentialReference, ct);
    // Wrong-tenant fence: the token must be for the binding's exact tenant and hold only Sites.Selected.
    return string.Equals(token.TenantId, location.TenantId, StringComparison.OrdinalIgnoreCase) &&
      token.ApplicationRoles.Count == 1 && token.ApplicationRoles.Contains("Sites.Selected")
        ? token.AccessToken : throw new OperationBlockedException("selected-site-token-invalid", authorization: true);
  }

  private static HttpRequestMessage Request(HttpMethod method, string url, string token)
  {
    var request = new HttpRequestMessage(method, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    request.Headers.TryAddWithoutValidation("client-request-id", Guid.NewGuid().ToString("D"));
    return request;
  }

  private static async Task<SelectedSiteItem> ItemAsync(HttpResponseMessage response, CancellationToken ct)
  {
    using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    var root = json.RootElement;
    string? Text(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    var parent = root.TryGetProperty("parentReference", out var p) ? Text(p, "id") : null;
    var size = root.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
    return new(Text(root, "id") ?? throw Blocked("selected-site-item-invalid"), Text(root, "name") ?? string.Empty,
      parent ?? string.Empty, size, root.TryGetProperty("folder", out _));
  }

  private static Exception Classify(HttpResponseMessage response) => response.StatusCode switch
  {
    HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable => new SafeRetryException(response.Headers.RetryAfter?.Delta),
    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new OperationBlockedException("selected-site-access-denied", authorization: true),
    HttpStatusCode.NotFound => new OperationBlockedException("selected-site-item-not-found"),
    _ when (int)response.StatusCode >= 500 => new IOException("selected-site-provider-unavailable"),
    _ => new OperationBlockedException("selected-site-request-rejected")
  };

  private static string? RequestId(HttpResponseMessage response) =>
    response.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null;

  private static string Id(string value) => Uri.EscapeDataString(value);
  private static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();
  private static OperationBlockedException Blocked(string code) => new(code);
}
