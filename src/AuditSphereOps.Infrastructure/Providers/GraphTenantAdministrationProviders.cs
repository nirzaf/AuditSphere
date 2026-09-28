using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Microsoft365;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>
/// Bounded Graph call helper. Every call carries a client-request-id so a timeout still has a
/// correlation identity. Raw responses and exceptions never leave this class.
/// </summary>
internal static class GraphCall
{
  public const string Graph = "https://graph.microsoft.com/v1.0";
  private const long MaxResponseBytes = 262_144;

  public sealed record Response(HttpStatusCode Status, JsonDocument? Body, string CorrelationId) : IDisposable
  {
    public bool Success => (int)Status is >= 200 and < 300;
    public void Dispose() => Body?.Dispose();
  }

  public static async Task<Response> SendAsync(HttpClient http, string accessToken, HttpMethod method, string url,
    object? body, CancellationToken ct, bool eventual = false)
  {
    var correlation = Guid.NewGuid().ToString("D");
    using var request = new HttpRequestMessage(method, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    request.Headers.TryAddWithoutValidation("client-request-id", correlation);
    if (eventual) request.Headers.TryAddWithoutValidation("ConsistencyLevel", "eventual");
    if (body is not null) request.Content = JsonContent.Create(body);
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    var requestId = response.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null;
    JsonDocument? document = null;
    if (response.Content.Headers.ContentLength is not > MaxResponseBytes &&
        response.Content.Headers.ContentType?.MediaType == "application/json")
    {
      try { document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct); }
      catch (JsonException) { document = null; }
    }
    return new(response.StatusCode, document, requestId ?? correlation);
  }

  /// <summary>Classifies a mutation. Only a definite 4xx is FAILED; transport/5xx/429 results are UNKNOWN.</summary>
  public static ProviderMutationResult Classify(Response response, string? objectId)
  {
    if (response.Success)
      return objectId is null ? ProviderMutationResult.Unknown(response.CorrelationId)
        : ProviderMutationResult.Accepted(objectId, response.CorrelationId);
    var status = (int)response.Status;
    if (status is 408 or 429 || status >= 500) return ProviderMutationResult.Unknown(response.CorrelationId);
    return ProviderMutationResult.Failed(status switch
    {
      400 => "graph-bad-request",
      401 or 403 => "graph-forbidden",
      404 => "graph-not-found",
      409 => "graph-conflict",
      _ => "graph-rejected"
    }, response.CorrelationId);
  }

  public static string? Text(JsonElement item, string name) =>
    item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

  public static DirectoryUserRecord? User(JsonElement item, string tenantId)
  {
    if (!Guid.TryParse(Text(item, "id"), out var id) || !item.TryGetProperty("accountEnabled", out var enabled) ||
        enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
      return null;
    DateTimeOffset? created = DateTimeOffset.TryParse(Text(item, "createdDateTime"), out var c) ? c : null;
    return new(tenantId, id.ToString("D"), Text(item, "displayName") ?? string.Empty,
      Text(item, "userPrincipalName") ?? string.Empty, Text(item, "mail"), enabled.GetBoolean(),
      Text(item, "userType") ?? string.Empty, created);
  }

  public static string ODataLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

/// <summary>POST /users with User.Create; reconciliation reads use the separate User.Read.All reader.</summary>
public sealed class GraphDirectoryUserProvisioner(HttpClient http, GraphCapabilityTokenSource create,
  GraphCapabilityTokenSource reader) : IMicrosoftDirectoryUserProvisioner
{
  public bool IsConfigured => create.Options.IsComplete && reader.Options.IsComplete;

  public async Task<ProviderMutationResult> CreateUserAsync(string tenantId, NewDirectoryUser user, CancellationToken ct)
  {
    var token = await create.GetAsync(tenantId, ct);
    try
    {
      using var response = await GraphCall.SendAsync(http, token.AccessToken, HttpMethod.Post, $"{GraphCall.Graph}/users", new
      {
        accountEnabled = user.AccountEnabled,
        displayName = user.DisplayName,
        mailNickname = user.MailNickname,
        userPrincipalName = user.UserPrincipalName,
        passwordProfile = new { forceChangePasswordNextSignIn = user.ForceChangePasswordNextSignIn, password = user.TemporaryPassword }
      }, ct);
      var id = response.Body is { } body && Guid.TryParse(GraphCall.Text(body.RootElement, "id"), out var created) ? created.ToString("D") : null;
      return GraphCall.Classify(response, id);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
    {
      return ProviderMutationResult.Unknown();
    }
  }

  public async Task<DirectoryUserRecord?> FindByUserPrincipalNameAsync(string tenantId, string userPrincipalName, CancellationToken ct)
  {
    var token = await reader.GetAsync(tenantId, ct);
    var url = $"{GraphCall.Graph}/users?$select=id,displayName,userPrincipalName,mail,accountEnabled,userType,createdDateTime&$top=2&$filter=" +
      Uri.EscapeDataString($"userPrincipalName eq '{GraphCall.ODataLiteral(userPrincipalName)}'");
    using var response = await GraphCall.SendAsync(http, token.AccessToken, HttpMethod.Get, url, null, ct);
    if (!response.Success || response.Body is null ||
        !response.Body.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
      throw new InvalidOperationException("directory-lookup-failed");
    var users = value.EnumerateArray().Select(x => GraphCall.User(x, tenantId)).ToList();
    if (users.Count > 1 || users.Any(x => x is null)) throw new InvalidOperationException("directory-lookup-ambiguous");
    return users.SingleOrDefault();
  }
}

/// <summary>POST /invitations with User.Invite.All. The invitation email is sent by Microsoft.</summary>
public sealed class GraphGuestInvitationProvider(HttpClient http, GraphCapabilityTokenSource invite,
  GraphCapabilityTokenSource reader) : IMicrosoftGuestInvitationProvider
{
  public bool IsConfigured => invite.Options.IsComplete && reader.Options.IsComplete;

  public async Task<ProviderMutationResult> InviteAsync(string tenantId, string email, string redirectUrl, CancellationToken ct)
  {
    var token = await invite.GetAsync(tenantId, ct);
    try
    {
      using var response = await GraphCall.SendAsync(http, token.AccessToken, HttpMethod.Post, $"{GraphCall.Graph}/invitations", new
      {
        invitedUserEmailAddress = email,
        inviteRedirectUrl = redirectUrl,
        sendInvitationMessage = true,
        invitedUserType = "Guest"
      }, ct);
      string? id = null;
      if (response.Body is { } body && body.RootElement.TryGetProperty("invitedUser", out var invited) &&
          Guid.TryParse(GraphCall.Text(invited, "id"), out var parsed))
        id = parsed.ToString("D");
      return GraphCall.Classify(response, id);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
    {
      return ProviderMutationResult.Unknown();
    }
  }

  public async Task<IReadOnlyList<DirectoryUserRecord>> FindGuestsByEmailAsync(string tenantId, string email, CancellationToken ct)
  {
    var token = await reader.GetAsync(tenantId, ct);
    var literal = GraphCall.ODataLiteral(email);
    var url = $"{GraphCall.Graph}/users?$select=id,displayName,userPrincipalName,mail,accountEnabled,userType,createdDateTime&$top=5&$count=true&$filter=" +
      Uri.EscapeDataString($"userType eq 'Guest' and (mail eq '{literal}' or otherMails/any(m:m eq '{literal}'))");
    using var response = await GraphCall.SendAsync(http, token.AccessToken, HttpMethod.Get, url, null, ct, eventual: true);
    if (!response.Success || response.Body is null ||
        !response.Body.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
      throw new InvalidOperationException("guest-lookup-failed");
    var users = value.EnumerateArray().Select(x => GraphCall.User(x, tenantId)).ToList();
    if (users.Any(x => x is null)) throw new InvalidOperationException("guest-lookup-invalid");
    return users!;
  }
}

/// <summary>Group reads and member add/remove with GroupMember.ReadWrite.All only.</summary>
public sealed class GraphGroupMembershipProvider(HttpClient http, GraphCapabilityTokenSource groups)
  : IMicrosoftGroupMembershipProvider
{
  public bool IsConfigured => groups.Options.IsComplete;

  public async Task<DirectoryGroupRecord> GetGroupAsync(string tenantId, string groupObjectId, CancellationToken ct)
  {
    var token = await groups.GetAsync(tenantId, ct);
    var id = Guid.Parse(groupObjectId).ToString("D");
    using var response = await GraphCall.SendAsync(http, token.AccessToken, HttpMethod.Get,
      $"{GraphCall.Graph}/groups/{id}?$select=id,displayName,securityEnabled,isAssignableToRole,groupTypes", null, ct);
    if (!response.Success || response.Body is null) throw new InvalidOperationException("group-read-failed");
    var root = response.Body.RootElement;
    var dynamic = root.TryGetProperty("groupTypes", out var types) && types.ValueKind == JsonValueKind.Array &&
      types.EnumerateArray().Any(x => x.GetString() == "DynamicMembership");
    // Unknown isAssignableToRole is treated as privileged (fail closed).
    var assignable = !root.TryGetProperty("isAssignableToRole", out var a) || a.ValueKind != JsonValueKind.False;
    var security = root.TryGetProperty("securityEnabled", out var s) && s.ValueKind == JsonValueKind.True;
    return new(tenantId, GraphCall.Text(root, "id") ?? string.Empty, GraphCall.Text(root, "displayName") ?? string.Empty,
      security, assignable, dynamic);
  }

  public async Task<DirectoryGroupMemberPage> ListMembersAsync(string tenantId, string groupObjectId, string? pageToken, CancellationToken ct)
  {
    var token = await groups.GetAsync(tenantId, ct);
    var id = Guid.Parse(groupObjectId).ToString("D");
    var url = $"{GraphCall.Graph}/groups/{id}/members/microsoft.graph.user?$select=id,displayName,userPrincipalName&$top=25" +
      (string.IsNullOrWhiteSpace(pageToken) ? "" : "&$skiptoken=" + Uri.EscapeDataString(pageToken));
    using var response = await GraphCall.SendAsync(http, token.AccessToken, HttpMethod.Get, url, null, ct);
    if (!response.Success || response.Body is null ||
        !response.Body.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
      throw new InvalidOperationException("group-members-failed");
    var members = value.EnumerateArray().Take(25).Select(x => new DirectoryGroupMember(
      GraphCall.Text(x, "id") ?? string.Empty, GraphCall.Text(x, "displayName") ?? string.Empty,
      GraphCall.Text(x, "userPrincipalName") ?? string.Empty)).ToList();
    string? next = null;
    if (GraphCall.Text(response.Body.RootElement, "@odata.nextLink") is { } link && Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
        uri.Host == "graph.microsoft.com")
      next = uri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2))
        .Where(x => x.Length == 2 && Uri.UnescapeDataString(x[0]).Equals("$skiptoken", StringComparison.OrdinalIgnoreCase))
        .Select(x => Uri.UnescapeDataString(x[1])).FirstOrDefault();
    return new(members, next);
  }

  public async Task<bool> IsMemberAsync(string tenantId, string groupObjectId, string memberObjectId, CancellationToken ct)
  {
    var token = await groups.GetAsync(tenantId, ct);
    var group = Guid.Parse(groupObjectId).ToString("D");
    var member = Guid.Parse(memberObjectId).ToString("D");
    using var response = await GraphCall.SendAsync(http, token.AccessToken, HttpMethod.Get,
      $"{GraphCall.Graph}/groups/{group}/members?$select=id&$filter=" + Uri.EscapeDataString($"id eq '{member}'"), null, ct, eventual: true);
    if (!response.Success || response.Body is null ||
        !response.Body.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
      throw new InvalidOperationException("group-membership-read-failed");
    return value.EnumerateArray().Any(x => string.Equals(GraphCall.Text(x, "id"), member, StringComparison.OrdinalIgnoreCase));
  }

  public async Task<ProviderMutationResult> AddMemberAsync(string tenantId, string groupObjectId, string memberObjectId, CancellationToken ct)
  {
    var token = await groups.GetAsync(tenantId, ct);
    var group = Guid.Parse(groupObjectId).ToString("D");
    var member = Guid.Parse(memberObjectId).ToString("D");
    try
    {
      using var response = await GraphCall.SendAsync(http, token.AccessToken, HttpMethod.Post,
        $"{GraphCall.Graph}/groups/{group}/members/$ref",
        new Dictionary<string, string> { ["@odata.id"] = $"{GraphCall.Graph}/directoryObjects/{member}" }, ct);
      return GraphCall.Classify(response, member);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
    {
      return ProviderMutationResult.Unknown();
    }
  }

  public async Task<ProviderMutationResult> RemoveMemberAsync(string tenantId, string groupObjectId, string memberObjectId, CancellationToken ct)
  {
    var token = await groups.GetAsync(tenantId, ct);
    var group = Guid.Parse(groupObjectId).ToString("D");
    var member = Guid.Parse(memberObjectId).ToString("D");
    try
    {
      using var response = await GraphCall.SendAsync(http, token.AccessToken, HttpMethod.Delete,
        $"{GraphCall.Graph}/groups/{group}/members/{member}/$ref", null, ct);
      return GraphCall.Classify(response, member);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
    {
      return ProviderMutationResult.Unknown();
    }
  }
}
