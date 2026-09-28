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

public sealed record SelectedResourceDraftObservation(
  Guid DraftId, long DraftRevision, Guid ConnectionRevisionId, string TenantId, string SiteId, string DriveId, string RootFolderId,
  string SiteUrl);

/// <summary>
/// Read-only Graph preflight for an already active firm workspace. Target IDs are loaded from
/// the database and never accepted from a browser or caller. It returns an observation, not
/// activation or durable provider evidence.
/// </summary>
public sealed class GraphSelectedResourceProbe(
  IAuditSphereDbContextFactory factory, HttpClient http, ISelectedSiteTokenSource tokens)
{
  private const string Graph = "https://graph.microsoft.com/v1.0";

  /// <summary>
  /// Check the exact resources saved in a pending setup draft before workspace activation.
  /// This does not record verification evidence or change connection state.
  /// </summary>
  public async Task<SelectedResourceDraftObservation> ProbeDraftAsync(Guid firmId, Guid draftId, CancellationToken ct)
  {
    if (firmId == Guid.Empty || draftId == Guid.Empty)
      throw new OperationBlockedException("selected-resource-binding-unavailable", authorization: true);
    await using var db = await factory.CreateAsync(ct);
    var draft = await db.Microsoft365SetupDrafts.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == draftId && x.FirmId == firmId, ct);
    if (draft?.ConnectionRevisionId is not { } connectionId ||
        draft.State is not (Microsoft365RevisionStates.ConsentRequired or
          Microsoft365RevisionStates.Validating or Microsoft365RevisionStates.Verified))
      throw new OperationBlockedException("selected-resource-binding-unverified", authorization: true);
    var connection = await db.Microsoft365ConnectionRevisions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == connectionId && x.FirmId == firmId, ct);
    if (connection is null || connection.State is not (Microsoft365RevisionStates.ConsentRequired or
          Microsoft365RevisionStates.Validating or Microsoft365RevisionStates.Verified) ||
        !string.Equals(connection.TenantId, draft.ExpectedTenantId, StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(connection.RuntimeCredentialReference) ||
        string.IsNullOrWhiteSpace(draft.SiteId) || string.IsNullOrWhiteSpace(draft.DriveId) ||
        string.IsNullOrWhiteSpace(draft.RootFolderId) || !ValidSiteUrl(draft.SiteUrl ?? string.Empty))
      throw new OperationBlockedException("selected-resource-binding-unverified", authorization: true);

    await ProbeResourcesAsync(connection.TenantId, connection.RuntimeCredentialReference,
      draft.SiteId, draft.DriveId, draft.RootFolderId, draft.SiteUrl!, ct);
    return new(draft.Id, draft.Revision, connection.Id, connection.TenantId, draft.SiteId, draft.DriveId, draft.RootFolderId,
      draft.SiteUrl!);
  }

  /// <summary>
  /// Positive exact-resource read and negative read of a separately approved synthetic site.
  /// Both checks are read-only; this observation is not durable activation evidence.
  /// </summary>
  public async Task<SelectedResourceDraftObservation> ProbeDraftBoundaryAsync(Guid firmId, Guid draftId,
    string negativeControlSiteUrl, CancellationToken ct)
  {
    if (!ValidSiteUrl(negativeControlSiteUrl))
      throw new OperationBlockedException("selected-resource-negative-control-invalid", authorization: true);
    var observed = await ProbeDraftAsync(firmId, draftId, ct);
    if (!SameSiteHost(negativeControlSiteUrl, observed.SiteUrl) ||
        SameSiteUrl(negativeControlSiteUrl, observed.SiteUrl))
      throw new OperationBlockedException("selected-resource-negative-control-invalid", authorization: true);

    await using var db = await factory.CreateAsync(ct);
    var connection = await db.Microsoft365ConnectionRevisions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == observed.ConnectionRevisionId && x.FirmId == firmId, ct);
    if (connection is null || connection.State is not (Microsoft365RevisionStates.ConsentRequired or
          Microsoft365RevisionStates.Validating or Microsoft365RevisionStates.Verified) ||
        !string.Equals(connection.TenantId, observed.TenantId, StringComparison.Ordinal))
      throw new OperationBlockedException("selected-resource-binding-unverified", authorization: true);
    var token = await tokens.GetAsync(connection.TenantId, connection.RuntimeCredentialReference, ct);
    if (string.IsNullOrWhiteSpace(token.AccessToken) ||
        !string.Equals(token.TenantId, observed.TenantId, StringComparison.OrdinalIgnoreCase) ||
        token.ApplicationRoles.Count != 1 || !token.ApplicationRoles.Contains("Sites.Selected"))
      throw new OperationBlockedException("selected-resource-token-invalid", authorization: true);

    var control = new Uri(negativeControlSiteUrl);
    var path = string.Join('/', control.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
      .Select(Uri.EscapeDataString));
    using var request = new HttpRequestMessage(HttpMethod.Get,
      $"{Graph}/sites/{control.Host}:/{path}?$select=id");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
      throw new SafeRetryException(response.Headers.RetryAfter?.Delta);
    if (response.StatusCode != HttpStatusCode.Forbidden)
      throw new OperationBlockedException("selected-resource-negative-control-not-denied", authorization: true);
    var current = await db.Microsoft365SetupDrafts.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == draftId && x.FirmId == firmId, ct);
    if (current is null || current.Revision != observed.DraftRevision ||
        current.ConnectionRevisionId != observed.ConnectionRevisionId ||
        current.State is not (Microsoft365RevisionStates.ConsentRequired or
          Microsoft365RevisionStates.Validating or Microsoft365RevisionStates.Verified) ||
        !string.Equals(current.SiteUrl, observed.SiteUrl, StringComparison.Ordinal) ||
        !string.Equals(current.SiteId, observed.SiteId, StringComparison.Ordinal) ||
        !string.Equals(current.DriveId, observed.DriveId, StringComparison.Ordinal) ||
        !string.Equals(current.RootFolderId, observed.RootFolderId, StringComparison.Ordinal))
      throw new OperationBlockedException("selected-resource-draft-changed", authorization: true);
    return observed;
  }

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
        connection.ConsentState is not ("VERIFIED" or "OBSERVED") || !templateApproved ||
        !string.Equals(connection.TenantId, workspace.TenantId, StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(connection.RuntimeCredentialReference) ||
        string.IsNullOrWhiteSpace(workspace.SiteId) || string.IsNullOrWhiteSpace(workspace.DriveId) ||
        string.IsNullOrWhiteSpace(workspace.RootFolderId) || !ValidSiteUrl(workspace.DisplayUrl))
      throw new OperationBlockedException("selected-resource-binding-unverified", authorization: true);

    await ProbeResourcesAsync(connection.TenantId, connection.RuntimeCredentialReference,
      workspace.SiteId, workspace.DriveId, workspace.RootFolderId, workspace.DisplayUrl, ct);
    return new(workspace.Id, workspace.TenantId, workspace.SiteId, workspace.DriveId, workspace.RootFolderId);
  }

  private async Task ProbeResourcesAsync(string tenantId, string credentialReference,
    string siteId, string driveId, string rootFolderId, string siteDisplayUrl, CancellationToken ct)
  {
    var token = await tokens.GetAsync(tenantId, credentialReference, ct);
    if (string.IsNullOrWhiteSpace(token.AccessToken) ||
        !string.Equals(token.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) ||
        token.ApplicationRoles.Count != 1 || !token.ApplicationRoles.Contains("Sites.Selected"))
      throw new OperationBlockedException("selected-resource-token-invalid", authorization: true);

    using var site = await GetAsync($"{Graph}/sites/{Uri.EscapeDataString(siteId)}?$select=id,webUrl", token.AccessToken, ct);
    if (!Exact(site.RootElement, "id", siteId) ||
        !site.RootElement.TryGetProperty("webUrl", out var siteUrl) ||
        !SameSiteUrl(siteUrl.GetString(), siteDisplayUrl))
      throw new OperationBlockedException("selected-resource-site-mismatch", authorization: true);

    // Enumerate only libraries of the exact approved site. If Graph paginates the list,
    // fail closed instead of following an untrusted nextLink to another target.
    using var drives = await GetAsync($"{Graph}/sites/{Uri.EscapeDataString(siteId)}/drives?$select=id&$top=200",
      token.AccessToken, ct);
    if (!drives.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array ||
        !values.EnumerateArray().Any(x => Exact(x, "id", driveId)))
      throw new OperationBlockedException("selected-resource-drive-mismatch", authorization: true);

    using var root = await GetAsync($"{Graph}/drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(rootFolderId)}?$select=id,folder,parentReference,webUrl",
      token.AccessToken, ct);
    if (!Exact(root.RootElement, "id", rootFolderId) ||
        !root.RootElement.TryGetProperty("folder", out var folder) || folder.ValueKind != JsonValueKind.Object ||
        !root.RootElement.TryGetProperty("parentReference", out var parent) ||
        !Exact(parent, "driveId", driveId) ||
        !root.RootElement.TryGetProperty("webUrl", out var rootUrl) ||
        !UnderSite(rootUrl.GetString(), siteDisplayUrl))
      throw new OperationBlockedException("selected-resource-root-mismatch", authorization: true);
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
