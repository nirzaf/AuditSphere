using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>A credential source must authenticate to the named tenant with the separate runtime slot.</summary>
public interface ISelectedSiteTokenSource
{
  Task<SelectedSiteToken> GetAsync(string tenantId, string credentialReference, CancellationToken ct);
}

public sealed record SelectedSiteToken(string AccessToken, string TenantId, IReadOnlySet<string> ApplicationRoles);

public sealed record SelectedResourceObservation(
  Guid WorkspaceId, string TenantId, string SiteId, string DriveId, string RootFolderId);

/// <summary>
/// Read-only Graph preflight for an already active firm workspace. Target IDs are loaded from
/// the database and never accepted from a browser or caller. It returns an observation, not
/// activation or durable provider evidence.
/// </summary>
public sealed class GraphSelectedResourceProbe(
  IAuditSphereDbContextFactory factory, HttpClient http, ISelectedSiteTokenSource tokens)
{
  private const string Graph = "https://graph.microsoft.com/v1.0";

  public async Task<SelectedResourceObservation> ProbeAsync(Guid firmId, Guid workspaceId, CancellationToken ct)
  {
    if (firmId == Guid.Empty || workspaceId == Guid.Empty)
      throw new OperationBlockedException("selected-resource-binding-unavailable", authorization: true);
    await using var db = await factory.CreateAsync(ct);
    var workspace = await db.FirmWorkspaceConfigurations.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == workspaceId && x.FirmId == firmId, ct);
    if (workspace is null)
      throw new OperationBlockedException("selected-resource-binding-unavailable", authorization: true);
    var connection = await db.Microsoft365ConnectionRevisions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == workspace.ConnectionRevisionId && x.FirmId == firmId, ct);
    var templateApproved = await db.FolderTemplateVersions.AsNoTracking().AnyAsync(x =>
      x.Id == workspace.FolderTemplateVersionId && x.FirmId == firmId &&
      x.Purpose == FolderTemplatePurposes.ClientWorkspace && x.ApprovedAt != null, ct);
    if (connection is null || connection.State != Microsoft365RevisionStates.Active ||
        connection.ConsentState != "OBSERVED" || !templateApproved ||
        !string.Equals(connection.TenantId, workspace.TenantId, StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(connection.RuntimeCredentialReference) ||
        string.IsNullOrWhiteSpace(workspace.SiteId) || string.IsNullOrWhiteSpace(workspace.DriveId) ||
        string.IsNullOrWhiteSpace(workspace.RootFolderId) || !ValidSiteUrl(workspace.DisplayUrl))
      throw new OperationBlockedException("selected-resource-binding-unverified", authorization: true);

    var token = await tokens.GetAsync(connection.TenantId, connection.RuntimeCredentialReference, ct);
    if (string.IsNullOrWhiteSpace(token.AccessToken) ||
        !string.Equals(token.TenantId, connection.TenantId, StringComparison.OrdinalIgnoreCase) ||
        token.ApplicationRoles.Count != 1 || !token.ApplicationRoles.Contains("Sites.Selected"))
      throw new OperationBlockedException("selected-resource-token-invalid", authorization: true);

    using var site = await GetAsync($"{Graph}/sites/{Uri.EscapeDataString(workspace.SiteId)}?$select=id,webUrl", token.AccessToken, ct);
    if (!Exact(site.RootElement, "id", workspace.SiteId) ||
        !site.RootElement.TryGetProperty("webUrl", out var siteUrl) ||
        !SameSiteUrl(siteUrl.GetString(), workspace.DisplayUrl))
      throw new OperationBlockedException("selected-resource-site-mismatch", authorization: true);

    // Enumerate only libraries of the exact approved site. If Graph paginates the list,
    // fail closed instead of following an untrusted nextLink to another target.
    using var drives = await GetAsync($"{Graph}/sites/{Uri.EscapeDataString(workspace.SiteId)}/drives?$select=id&$top=200",
      token.AccessToken, ct);
    if (!drives.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array ||
        !values.EnumerateArray().Any(x => Exact(x, "id", workspace.DriveId)))
      throw new OperationBlockedException("selected-resource-drive-mismatch", authorization: true);

    using var root = await GetAsync($"{Graph}/drives/{Uri.EscapeDataString(workspace.DriveId)}/items/{Uri.EscapeDataString(workspace.RootFolderId)}?$select=id,folder,parentReference,webUrl",
      token.AccessToken, ct);
    if (!Exact(root.RootElement, "id", workspace.RootFolderId) ||
        !root.RootElement.TryGetProperty("folder", out var folder) || folder.ValueKind != JsonValueKind.Object ||
        !root.RootElement.TryGetProperty("parentReference", out var parent) ||
        !Exact(parent, "driveId", workspace.DriveId) ||
        !root.RootElement.TryGetProperty("webUrl", out var rootUrl) ||
        !UnderSite(rootUrl.GetString(), workspace.DisplayUrl))
      throw new OperationBlockedException("selected-resource-root-mismatch", authorization: true);

    return new(workspace.Id, workspace.TenantId, workspace.SiteId, workspace.DriveId, workspace.RootFolderId);
  }

  private async Task<JsonDocument> GetAsync(string url, string token, CancellationToken ct)
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
      throw new SafeRetryException(response.Headers.RetryAfter?.Delta);
    if (!response.IsSuccessStatusCode)
      throw new OperationBlockedException("selected-resource-graph-rejected",
        authorization: response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    try { return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct); }
    catch (JsonException) { throw new OperationBlockedException("selected-resource-graph-invalid"); }
  }

  private static bool Exact(JsonElement element, string key, string expected) =>
    element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) &&
    value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), expected, StringComparison.Ordinal);

  private static bool ValidSiteUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
    uri.Scheme == Uri.UriSchemeHttps && uri.Host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase) &&
    string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

  private static bool SameSiteUrl(string? observed, string expected) =>
    ValidSiteUrl(observed ?? string.Empty) &&
    string.Equals(new Uri(observed!).GetLeftPart(UriPartial.Path).TrimEnd('/'),
      new Uri(expected).GetLeftPart(UriPartial.Path).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

  private static bool UnderSite(string? child, string site)
  {
    if (!ValidSiteUrl(child ?? string.Empty) || !SameSiteHost(child!, site)) return false;
    var sitePath = new Uri(site).AbsolutePath.TrimEnd('/');
    var childPath = new Uri(child!).AbsolutePath;
    return childPath.StartsWith(sitePath + "/", StringComparison.OrdinalIgnoreCase);
  }

  private static bool SameSiteHost(string child, string site) =>
    string.Equals(new Uri(child).Host, new Uri(site).Host, StringComparison.OrdinalIgnoreCase);
}
