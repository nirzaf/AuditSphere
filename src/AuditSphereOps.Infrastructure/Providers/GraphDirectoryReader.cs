using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>Bounded first/next-page Graph user lookup through a dedicated read-only identity.</summary>
public sealed class GraphDirectoryReader(HttpClient http, IDirectoryTokenSource tokens,
  DirectoryCertificateOptions options) : IMicrosoftDirectoryReader
{
  public async Task<DirectoryCandidatePage> SearchAsync(string tenantId, string prefix,
    string? pageToken, CancellationToken ct)
  {
    if (!options.Enabled || !Guid.TryParse(tenantId, out var tenant) ||
        !string.Equals(tenant.ToString("D"), options.TenantId, StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(prefix) || prefix.Length > 80 || prefix.Any(char.IsControl) ||
        pageToken is { Length: > 2048 })
      throw new OperationBlockedException("directory-reader-invalid-request", authorization: true);
    var token = await tokens.GetAsync(tenantId, ct);
    if (string.IsNullOrWhiteSpace(token.AccessToken) ||
        !string.Equals(token.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) ||
        token.ApplicationRoles.Count != 1 || !token.ApplicationRoles.Contains("User.Read.All"))
      throw new OperationBlockedException("directory-reader-token-invalid", authorization: true);
    var field = prefix.Contains('@') ? "userPrincipalName" : "displayName";
    var escapedPrefix = prefix.Replace("'", "''", StringComparison.Ordinal);
    var url = "https://graph.microsoft.com/v1.0/users?" +
      "$select=id,displayName,userPrincipalName,accountEnabled,userType" +
      "&$top=25&$filter=" + Uri.EscapeDataString($"startsWith({field},'{escapedPrefix}')") +
      (string.IsNullOrWhiteSpace(pageToken) ? "" : "&$skiptoken=" + Uri.EscapeDataString(pageToken));
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
      throw new SafeRetryException(response.Headers.RetryAfter?.Delta);
    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 262144)
      throw new OperationBlockedException("directory-reader-graph-rejected",
        authorization: response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    try
    {
      using var document = await JsonDocument.ParseAsync(
        await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
      var root = document.RootElement;
      if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        throw new OperationBlockedException("directory-reader-graph-invalid");
      var users = new List<DirectoryCandidate>();
      foreach (var item in value.EnumerateArray())
      {
        if (users.Count == 25)
          throw new OperationBlockedException("directory-reader-graph-invalid");
        users.Add(Candidate(item, tenant));
      }
      string? next = null;
      if (root.TryGetProperty("@odata.nextLink", out var link) &&
          link.ValueKind == JsonValueKind.String)
        next = NextToken(link.GetString());
      return new(users, next);
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
      throw new OperationBlockedException("directory-reader-graph-invalid");
    }
  }

  public async Task<DirectoryCandidate> GetByIdAsync(string tenantId, string objectId, CancellationToken ct)
  {
    if (!options.Enabled || !Guid.TryParse(tenantId, out var tenant) ||
        !string.Equals(tenant.ToString("D"), options.TenantId, StringComparison.OrdinalIgnoreCase) ||
        !Guid.TryParse(objectId, out var identity))
      throw new OperationBlockedException("directory-reader-invalid-request", authorization: true);
    var token = await tokens.GetAsync(tenantId, ct);
    if (string.IsNullOrWhiteSpace(token.AccessToken) ||
        !string.Equals(token.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) ||
        token.ApplicationRoles.Count != 1 || !token.ApplicationRoles.Contains("User.Read.All"))
      throw new OperationBlockedException("directory-reader-token-invalid", authorization: true);
    var url = $"https://graph.microsoft.com/v1.0/users/{identity:D}" +
      "?$select=id,displayName,userPrincipalName,accountEnabled,userType";
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
      throw new SafeRetryException(response.Headers.RetryAfter?.Delta);
    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 262144)
      throw new OperationBlockedException("directory-reader-graph-rejected",
        authorization: response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    try
    {
      using var document = await JsonDocument.ParseAsync(
        await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
      var candidate = Candidate(document.RootElement, tenant);
      if (candidate.ObjectId != identity.ToString("D"))
        throw new OperationBlockedException("directory-reader-identity-mismatch", authorization: true);
      return candidate;
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
      throw new OperationBlockedException("directory-reader-graph-invalid");
    }
  }

  private static DirectoryCandidate Candidate(JsonElement item, Guid tenant)
  {
    if (!item.TryGetProperty("id", out var id) ||
        !Guid.TryParse(id.GetString(), out var objectId) ||
        !item.TryGetProperty("accountEnabled", out var enabled) ||
        enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
      throw new OperationBlockedException("directory-reader-graph-invalid");
    return new(tenant.ToString("D"), objectId.ToString("D"),
      Text(item, "displayName"), Text(item, "userPrincipalName"),
      enabled.GetBoolean(), Text(item, "userType"));
  }

  private static string Text(JsonElement item, string name) =>
    item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
      ? (value.GetString() ?? string.Empty) : string.Empty;

  private static string NextToken(string? link)
  {
    if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
        uri.Host != "graph.microsoft.com" || uri.AbsolutePath != "/v1.0/users" ||
        !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
      throw new OperationBlockedException("directory-reader-next-page-invalid");
    var token = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
      .Select(x => x.Split('=', 2))
      .Where(x => Uri.UnescapeDataString(x[0]).Equals("$skiptoken", StringComparison.OrdinalIgnoreCase))
      .Select(x => x.Length == 2 ? Uri.UnescapeDataString(x[1]) : "")
      .ToArray();
    if (token.Length != 1 || token[0].Length is < 1 or > 2048)
      throw new OperationBlockedException("directory-reader-next-page-invalid");
    return token[0];
  }
}
