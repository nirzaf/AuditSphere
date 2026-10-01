using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;

namespace AuditSphereOps.Infrastructure.Providers;

public sealed record ClientSiteProviderOptions(string TenantId, string ProvisionerClientId, string SiteHost,
  string CertificatePath, string PrivateKeyPath, string DocumentWorkerClientId, string CustodianObjectId)
{
  public void Validate()
  {
    if (!Guid.TryParse(TenantId, out _) || !Guid.TryParse(ProvisionerClientId, out _) || !Guid.TryParse(DocumentWorkerClientId, out _) ||
        !Guid.TryParse(CustodianObjectId, out _) || Guid.Parse(ProvisionerClientId) == Guid.Parse(DocumentWorkerClientId) ||
        !Uri.TryCreate("https://" + SiteHost, UriKind.Absolute, out var host) || host.Host != SiteHost ||
        !SiteHost.EndsWith(".sharepoint.com", StringComparison.Ordinal) || host.AbsolutePath != "/" ||
        !Path.IsPathFullyQualified(CertificatePath) || !Path.IsPathFullyQualified(PrivateKeyPath) ||
        !File.Exists(CertificatePath) || !File.Exists(PrivateKeyPath))
      throw new OperationBlockedException("client-site-credential-unavailable", authorization: true);
  }
}

/// <summary>Separate SharePoint resource token, never a Graph token passed to a SharePoint endpoint.</summary>
internal sealed class SharePointProvisioningTokenSource(HttpClient http, ClientSiteProviderOptions options)
{
  public async Task<string> GetAsync(CancellationToken ct)
  {
    options.Validate();
    using var certificate = X509Certificate2.CreateFromPemFile(options.CertificatePath, options.PrivateKeyPath);
    var endpoint = $"https://login.microsoftonline.com/{options.TenantId}/oauth2/v2.0/token";
    using var response = await http.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string>
    {
      ["client_id"] = options.ProvisionerClientId, ["scope"] = $"https://{options.SiteHost}/.default", ["grant_type"] = "client_credentials",
      ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
      ["client_assertion"] = GraphCertificateAssertion.Create(certificate, options.ProvisionerClientId, endpoint)
    }), ct);
    if (!response.IsSuccessStatusCode) throw new OperationBlockedException("client-site-token-rejected", authorization: true);
    using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    var token = json.RootElement.GetProperty("access_token").GetString() ?? throw new OperationBlockedException("client-site-token-missing");
    using var claims = GraphJwt.ReadPayload(token) ?? throw new OperationBlockedException("client-site-token-invalid");
    var root = claims.RootElement;
    if (!root.TryGetProperty("tid", out var tid) || tid.GetString() != options.TenantId ||
        !root.TryGetProperty("aud", out var aud) || (aud.GetString() != "00000003-0000-0ff1-ce00-000000000000" && aud.GetString() != $"https://{options.SiteHost}") ||
        !root.TryGetProperty("exp", out var exp) || exp.GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
        !root.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array ||
        roles.GetArrayLength() != 1 || roles[0].GetString() != "Sites.FullControl.All")
      throw new OperationBlockedException("client-site-token-invalid", authorization: true);
    return token;
  }
}

/// <summary>
/// Supported SPSiteManager provider; no beta APIs, no user passwords and no credentials in receipts.
/// All URLs are derived from the configured SharePoint host and stored firm/client marker.
/// </summary>
public sealed class SharePointClientSiteProvider : IClientSharePointSiteProvider
{
  private readonly AsyncLocal<Guid?> clientCorrelation = new();
  private readonly HttpClient http;
  private readonly ClientSiteProviderOptions options;
  private readonly SharePointProvisioningTokenSource sharePointTokens;
  private readonly GraphCapabilityTokenSource graphTokens, directoryTokens;
  public SharePointClientSiteProvider(HttpClient http, HttpClient tokenHttp, ClientSiteProviderOptions options,
    GraphCapabilityTokenSource directoryTokens)
  {
    options.Validate();
    if (string.Equals(directoryTokens.Options.ClientId, options.ProvisionerClientId, StringComparison.OrdinalIgnoreCase) || directoryTokens.Options.RequiredRole != "User.Read.All" ||
        directoryTokens.Options.TenantId != options.TenantId ||
        string.Equals(directoryTokens.Options.ClientId, options.DocumentWorkerClientId, StringComparison.OrdinalIgnoreCase) ||
        directoryTokens.Options.AllowedRoles.Count != 1 || !directoryTokens.Options.AllowedRoles.Contains("User.Read.All")) throw new OperationBlockedException("client-site-reader-boundary-invalid", authorization: true);
    this.http = http; this.options = options; this.directoryTokens = directoryTokens;
    sharePointTokens = new(tokenHttp, options);
    graphTokens = new(tokenHttp, new(true, options.TenantId, options.ProvisionerClientId, options.CertificatePath, options.PrivateKeyPath, "Sites.FullControl.All"));
  }

  public async Task<ClientSiteReceipt> ReconcileAsync(ClientSiteRequest request, bool mayCreate, CancellationToken ct)
  {
    clientCorrelation.Value = request.CorrelationId ?? Guid.NewGuid();
    Uri uri;
    string sp, graph, directory, owner;
    Reply status;
    var manager = $"https://{options.SiteHost}/_api/SPSiteManager";
    var mutations = new List<ClientSiteMutation>();
    try
    {
      uri = ValidateRequest(request);
      sp = await sharePointTokens.GetAsync(ct);
      graph = (await graphTokens.GetAsync(request.TenantId, ct)).AccessToken;
      directory = (await directoryTokens.GetAsync(request.TenantId, ct)).AccessToken;
      owner = await DirectoryIdentityAsync(options.CustodianObjectId, directory, ct)
        ?? throw new OperationBlockedException("client-site-custodian-disabled", authorization: true);
      status = await CallAsync(HttpMethod.Get, manager + "/status?url='" + Uri.EscapeDataString(request.Url) + "'", sp, null, ct);
    }
    catch (Exception ex) when (ex is SafeRetryException or HttpRequestException or OperationBlockedException or GraphCapabilityTokenException)
    {
      throw new ClientSitePreflightException(ex is SafeRetryException or HttpRequestException,
        ex is GraphCapabilityTokenException || ex is OperationBlockedException { Authorization: true });
    }
    var siteStatus = status.Json.GetProperty("SiteStatus").GetInt32();
    if (siteStatus == 0)
    {
      if (!mayCreate || request.KnownSiteId != null) throw new OperationBlockedException("client-site-unknown-create-needs-review");
      // From this point a timeout is UNKNOWN, not SafeRetry. Reconciliation only reads the requested URL.
      var created = await CallAsync(HttpMethod.Post, manager + "/create", sp, new { request = new
        { Title = request.Title, Url = request.Url, Lcid = 1033, ShareByEmailEnabled = false, Description = request.OwnershipMarker,
          WebTemplate = "STS#3", Owner = owner } }, ct);
      siteStatus = created.Json.GetProperty("SiteStatus").GetInt32();
      mutations.Add(new("CLIENT_SITE_CREATED", request.ClientId.ToString("D"), "NONE", "CREATED", created.Correlation));
    }
    if (siteStatus == 1) throw new InvalidOperationException("client-site-provisioning-pending");
    if (siteStatus is not (2 or 4)) throw new OperationBlockedException("client-site-provisioning-failed");
    var web = await CallAsync(HttpMethod.Get, request.Url + "/_api/web?$select=Id,Url,Description", sp, null, ct);
    if (web.Json.GetProperty("Description").GetString() != request.OwnershipMarker ||
        !string.Equals(web.Json.GetProperty("Url").GetString()?.TrimEnd('/'), request.Url, StringComparison.OrdinalIgnoreCase))
      throw new OperationBlockedException("client-site-url-owned-by-another-client", authorization: true);
    var site = await CallAsync(HttpMethod.Get, "https://graph.microsoft.com/v1.0/sites/" + options.SiteHost + ":" + uri.AbsolutePath + "?$select=id,webUrl", graph, null, ct);
    var siteId = site.Json.GetProperty("id").GetString()!;
    var parts = siteId.Split(',');
    if (parts.Length != 3 || parts[0] != options.SiteHost || !Guid.TryParse(parts[1], out _) || !Guid.TryParse(parts[2], out var webId) ||
        !Guid.TryParse(web.Json.GetProperty("Id").GetString(), out var actualWebId) || webId != actualWebId ||
        !string.Equals(site.Json.GetProperty("webUrl").GetString()?.TrimEnd('/'), request.Url, StringComparison.OrdinalIgnoreCase))
      throw new OperationBlockedException("client-site-graph-identity-mismatch", authorization: true);
    if (request.KnownSiteId != null && request.KnownSiteId != siteId) throw new OperationBlockedException("client-site-identity-changed", authorization: true);
    var permissionsUrl = "https://graph.microsoft.com/v1.0/sites/" + Uri.EscapeDataString(siteId) + "/permissions";
    var permissions = await CollectionAsync(permissionsUrl, graph, ct);
    var existing = permissions.Where(p => PermissionApplicationIds(p).Contains(options.DocumentWorkerClientId, StringComparer.OrdinalIgnoreCase)).ToList();
    if (existing.Count > 1) throw new OperationBlockedException("client-site-worker-grant-ambiguous");
    if (existing.Count == 0)
    {
      var grantResult = await CallAsync(HttpMethod.Post, permissionsUrl, graph, new { roles = new[] { "write" }, grantedToIdentities = new[]
        { new { application = new { id = options.DocumentWorkerClientId, displayName = "AuditSphere selected-site document worker" } } } }, ct);
      mutations.Add(new("CLIENT_SITE_WORKER_GRANT", options.DocumentWorkerClientId, "NONE", "WRITE", grantResult.Correlation));
    }
    var verified = (await CollectionAsync(permissionsUrl, graph, ct)).Where(p => PermissionApplicationIds(p)
      .Contains(options.DocumentWorkerClientId, StringComparer.OrdinalIgnoreCase)).ToList();
    if (verified.Count != 1 || !verified[0].GetProperty("roles").EnumerateArray().Select(x => x.GetString()).SequenceEqual(["write"]))
      throw new OperationBlockedException("client-site-worker-grant-not-exact-write");
    var drive = await CallAsync(HttpMethod.Get, "https://graph.microsoft.com/v1.0/sites/" + Uri.EscapeDataString(siteId) + "/drive?$select=id", graph, null, ct);
    var driveId = drive.Json.GetProperty("id").GetString()!;
    var root = await CallAsync(HttpMethod.Get, "https://graph.microsoft.com/v1.0/drives/" + Uri.EscapeDataString(driveId) + "/root?$select=id", graph, null, ct);

    const string groupTitle = "AuditSphere Assigned Staff";
    var groupUrl = request.Url + "/_api/web/sitegroups/getbyname('" + Uri.EscapeDataString(groupTitle) + "')";
    var group = await CallAsync(HttpMethod.Get, groupUrl, sp, null, ct, allowMissing: true);
    if (group.Missing)
    {
      await CallAsync(HttpMethod.Post, request.Url + "/_api/web/sitegroups", sp,
        new { Title = groupTitle, Description = request.OwnershipMarker }, ct);
      group = await CallAsync(HttpMethod.Get, groupUrl, sp, null, ct);
    }
    if (group.Json.GetProperty("Description").GetString() != request.OwnershipMarker)
      throw new OperationBlockedException("client-site-staff-group-not-managed", authorization: true);
    var groupId = group.Json.GetProperty("Id").GetInt32();
    var fullControl = await CallAsync(HttpMethod.Get, request.Url + "/_api/web/roledefinitions/getbytype(5)?$select=Id,RoleTypeKind", sp, null, ct);
    if (fullControl.Json.GetProperty("RoleTypeKind").GetInt32() != 5) throw new OperationBlockedException("client-site-full-control-unavailable");
    var roleId = fullControl.Json.GetProperty("Id").GetInt32();
    var roleUrl = request.Url + $"/_api/web/roleassignments/getbyprincipalid({groupId})/roledefinitionbindings";
    var roles = await CollectionAsync(roleUrl, sp, ct, allowMissing: true);
    if (!roles.Any(x => x.GetProperty("Id").GetInt32() == roleId))
    {
      var roleResult = await CallAsync(HttpMethod.Post, request.Url + $"/_api/web/roleassignments/addroleassignment(principalid={groupId},roledefid={roleId})", sp, null, ct);
      mutations.Add(new("CLIENT_SITE_GROUP_FULL_CONTROL", groupId.ToString(), "NONE", "FULL_CONTROL", roleResult.Correlation));
    }
    if (!(await CollectionAsync(roleUrl, sp, ct)).Any(x => x.GetProperty("Id").GetInt32() == roleId))
      throw new OperationBlockedException("client-site-full-control-not-verified");

    var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var oid in request.StaffObjectIds.Distinct(StringComparer.OrdinalIgnoreCase))
    {
      var upn = await DirectoryIdentityAsync(oid, directory, ct);
      if (upn != null) desired.Add("i:0#.f|membership|" + upn.ToLowerInvariant(), oid);
    }
    var usersUrl = request.Url + $"/_api/web/sitegroups({groupId})/users";
    var members = await CollectionAsync(usersUrl, sp, ct);
    // Remove obsolete managed membership before additions; do not touch site custodians or unrelated groups.
    foreach (var member in members)
      if (!desired.ContainsKey(member.GetProperty("LoginName").GetString()!))
      {
        var removal = await CallAsync(HttpMethod.Post, usersUrl + $"/removebyid({member.GetProperty("Id").GetInt32()})", sp, null, ct);
        mutations.Add(new("CLIENT_SITE_MEMBER_REMOVED", "sharepoint-user:" + member.GetProperty("Id").GetInt32(), "FULL_CONTROL", "NONE", removal.Correlation));
      }
    foreach (var login in desired.Keys)
    {
      if (!members.Any(m => string.Equals(m.GetProperty("LoginName").GetString(), login, StringComparison.OrdinalIgnoreCase)))
      {
        var user = await CallAsync(HttpMethod.Post, request.Url + "/_api/web/ensureuser", sp, new { logonName = login }, ct);
        if (!string.Equals(user.Json.GetProperty("LoginName").GetString(), login, StringComparison.OrdinalIgnoreCase))
          throw new OperationBlockedException("client-site-staff-identity-mismatch", authorization: true);
        var principal = await CallAsync(HttpMethod.Get, request.Url + $"/_api/web/siteusers/getbyid({user.Json.GetProperty("Id").GetInt32()})?$select=Id,LoginName,AadObjectId", sp, null, ct);
        if (!HasObjectIdentity(principal.Json, desired[login]))
          throw new OperationBlockedException("client-site-staff-object-id-mismatch", authorization: true);
        var addition = await CallAsync(HttpMethod.Post, usersUrl, sp, new { LoginName = login }, ct);
        mutations.Add(new("CLIENT_SITE_MEMBER_ADDED", desired[login], "NONE", "FULL_CONTROL", addition.Correlation));
      }
      else
      {
        var member = members.Single(m => string.Equals(m.GetProperty("LoginName").GetString(), login, StringComparison.OrdinalIgnoreCase));
        var principal = await CallAsync(HttpMethod.Get, request.Url + $"/_api/web/siteusers/getbyid({member.GetProperty("Id").GetInt32()})?$select=Id,LoginName,AadObjectId", sp, null, ct);
        if (!HasObjectIdentity(principal.Json, desired[login]))
        {
          await CallAsync(HttpMethod.Post, usersUrl + $"/removebyid({member.GetProperty("Id").GetInt32()})", sp, null, ct);
          throw new OperationBlockedException("client-site-staff-object-id-mismatch", authorization: true);
        }
      }
    }
    var final = await CollectionAsync(usersUrl, sp, ct);
    if (final.Any(m => !desired.ContainsKey(m.GetProperty("LoginName").GetString()!)))
      throw new OperationBlockedException("client-site-staff-removal-not-verified");
    var observed = final.Select(m => desired[m.GetProperty("LoginName").GetString()!]).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
    return new(request.TenantId, request.Url, request.OwnershipMarker, siteId, driveId,
      root.Json.GetProperty("id").GetString()!, groupId.ToString(System.Globalization.CultureInfo.InvariantCulture), observed, web.Correlation, mutations);
  }

  private static bool HasObjectIdentity(JsonElement principal, string oid)
  {
    // SharePoint's UserIdInfo names the Entra object. Unsupported/missing identity is never treated as verified.
    if (!principal.TryGetProperty("AadObjectId", out var identity)) return false;
    var value = identity.ValueKind == JsonValueKind.String ? identity.GetString() :
      identity.ValueKind == JsonValueKind.Object && identity.TryGetProperty("NameId", out var nameId) ? nameId.GetString() : null;
    return Guid.TryParse(value, out var actual) && Guid.TryParse(oid, out var expected) && actual == expected;
  }

  private Uri ValidateRequest(ClientSiteRequest request)
  {
    if (request.TenantId != options.TenantId || request.FirmId == Guid.Empty || request.ClientId == Guid.Empty ||
        request.OwnershipMarker != $"AuditSphere:{request.FirmId:D}:{request.ClientId:D}" ||
        !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != options.SiteHost ||
        !uri.IsDefaultPort || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 ||
        !uri.AbsolutePath.StartsWith("/sites/", StringComparison.Ordinal) ||
        !uri.AbsolutePath.EndsWith("-" + request.ClientId.ToString("N"), StringComparison.Ordinal) ||
        uri.AbsolutePath[7..].Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || request.StaffObjectIds.Count > 200)
      throw new OperationBlockedException("client-site-request-scope-invalid", authorization: true);
    return uri;
  }

  private async Task<string?> DirectoryIdentityAsync(string oid, string token, CancellationToken ct)
  {
    if (!Guid.TryParse(oid, out var id)) throw new OperationBlockedException("client-site-object-id-invalid", authorization: true);
    var result = await CallAsync(HttpMethod.Get, "https://graph.microsoft.com/v1.0/users/" + id.ToString("D") + "?$select=id,userPrincipalName,accountEnabled,userType", token, null, ct, allowMissing: true);
    if (result.Missing) return null;
    var user = result.Json;
    if (!Guid.TryParse(user.GetProperty("id").GetString(), out var returned) || returned != id)
      throw new OperationBlockedException("client-site-object-id-mismatch", authorization: true);
    if (!user.TryGetProperty("accountEnabled", out var enabled) || enabled.ValueKind != JsonValueKind.True ||
        !user.TryGetProperty("userType", out var type) || type.GetString() != "Member") return null;
    var upn = user.GetProperty("userPrincipalName").GetString();
    if (string.IsNullOrWhiteSpace(upn) || upn.Length > 320 || upn.Any(char.IsControl) || !upn.Contains('@'))
      throw new OperationBlockedException("client-site-upn-invalid");
    return upn;
  }

  private static IEnumerable<string?> PermissionApplicationIds(JsonElement permission)
  {
    foreach (var property in new[] { "grantedToIdentitiesV2", "grantedToIdentities" })
      if (permission.TryGetProperty(property, out var list))
        foreach (var item in list.EnumerateArray())
          if (item.TryGetProperty("application", out var app) && app.TryGetProperty("id", out var id)) yield return id.GetString();
  }

  private sealed record Reply(JsonElement Json, string? Correlation, bool Missing = false);
  private async Task<Reply> CallAsync(HttpMethod method, string url, string token, object? body, CancellationToken ct, bool allowMissing = false)
  {
    using var request = new HttpRequestMessage(method, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    request.Headers.Accept.ParseAdd(new Uri(url).Host == "graph.microsoft.com" ? "application/json" : "application/json;odata=nometadata");
    request.Headers.Add("client-request-id", (clientCorrelation.Value ?? Guid.NewGuid()).ToString("D"));
    if (body != null)
    {
      request.Content = JsonContent.Create(body, options: new JsonSerializerOptions { PropertyNamingPolicy = null });
      if (new Uri(url).Host != "graph.microsoft.com") request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json;odata=nometadata");
    }
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    if (allowMissing && response.StatusCode == HttpStatusCode.NotFound) return new(default, null, true);
    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
      throw new OperationBlockedException("client-site-consent-or-authority-denied", authorization: true);
    if (!response.IsSuccessStatusCode)
    {
      // No automatic mutation retry: 429/503 after a POST is reconciled, never assumed unapplied.
      if (method == HttpMethod.Get && response.StatusCode == HttpStatusCode.TooManyRequests)
        throw new SafeRetryException(TimeSpan.FromSeconds(30));
      throw new HttpRequestException("client-site-provider-outcome-unconfirmed");
    }
    await response.Content.LoadIntoBufferAsync(1024 * 1024, ct);
    var bytes = await response.Content.ReadAsByteArrayAsync(ct);
    var correlation = response.Headers.TryGetValues("SPRequestGuid", out var ids) || response.Headers.TryGetValues("request-id", out ids) ? ids.FirstOrDefault() : null;
    if (bytes.Length == 0) return new(default, correlation);
    using var json = JsonDocument.Parse(bytes);
    return new(json.RootElement.Clone(), Guid.TryParse(correlation, out var guid) ? guid.ToString("D") : null);
  }

  private async Task<List<JsonElement>> CollectionAsync(string url, string token, CancellationToken ct, bool allowMissing = false)
  {
    var results = new List<JsonElement>();
    var first = new Uri(url);
    for (var page = 0; page < 20; page++)
    {
      var reply = await CallAsync(HttpMethod.Get, url, token, null, ct, allowMissing);
      if (reply.Missing) return results;
      results.AddRange(reply.Json.GetProperty("value").EnumerateArray().Select(x => x.Clone()));
      if (results.Count > 1000) throw new OperationBlockedException("client-site-membership-bound-exceeded");
      var next = reply.Json.TryGetProperty("@odata.nextLink", out var graph) ? graph.GetString() :
        reply.Json.TryGetProperty("odata.nextLink", out var sp) ? sp.GetString() : null;
      if (next == null) return results;
      if (!Uri.TryCreate(first, next, out var uri) || uri.Scheme != "https" || uri.Host != first.Host || !uri.IsDefaultPort ||
          !uri.AbsolutePath.StartsWith(first.AbsolutePath, StringComparison.Ordinal) || uri.UserInfo.Length != 0)
        throw new OperationBlockedException("client-site-provider-page-invalid", authorization: true);
      url = uri.AbsoluteUri;
    }
    throw new OperationBlockedException("client-site-page-bound-exceeded");
  }
}
