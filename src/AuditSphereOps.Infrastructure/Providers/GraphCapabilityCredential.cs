using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Operations;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>
/// One Microsoft capability = one app identity with one certificate and exactly one Graph application
/// role, unless the deployment approved a shared identity for a fixed role set (the tenant administration
/// app: User.Create, User.Invite.All, GroupMember.ReadWrite.All). A token holding any other role is refused.
/// Certificate/private-key files come from an approved secret store mount; nothing here is persisted.
/// </summary>
public sealed record GraphCapabilityCredentialOptions(
  bool Enabled, string TenantId, string ClientId, string CertificatePath, string PrivateKeyPath, string RequiredRole,
  IReadOnlySet<string>? ApprovedRoleSet = null)
{
  /// <summary>Roles the identity may hold: the approved shared set, otherwise only the required role.</summary>
  public IReadOnlySet<string> AllowedRoles => ApprovedRoleSet ?? new HashSet<string>(StringComparer.Ordinal) { RequiredRole };

  public bool IsComplete => Enabled && Guid.TryParse(TenantId, out _) && Guid.TryParse(ClientId, out _) &&
    Path.IsPathFullyQualified(CertificatePath) && Path.IsPathFullyQualified(PrivateKeyPath) &&
    !string.IsNullOrWhiteSpace(RequiredRole);

  public void Validate()
  {
    if (!IsComplete || !File.Exists(CertificatePath) || !File.Exists(PrivateKeyPath))
      throw new OperationBlockedException("graph-capability-credential-unavailable", authorization: true);
  }
}

public sealed record GraphCapabilityToken(string AccessToken, string TenantId, IReadOnlySet<string> ApplicationRoles);

/// <summary>Typed token failure. NotGranted means Entra issued a token without the required app role.</summary>
public sealed class GraphCapabilityTokenException(string code, bool notGranted) : Exception(code)
{
  public string Code { get; } = code;
  public bool NotGranted { get; } = notGranted;
}

public sealed class GraphCapabilityTokenSource(HttpClient http, GraphCapabilityCredentialOptions options)
{
  public GraphCapabilityCredentialOptions Options => options;

  /// <summary>Returns a token only when it is for the exact tenant and carries exactly the required role.</summary>
  public async Task<GraphCapabilityToken> GetAsync(string tenantId, CancellationToken ct)
  {
    options.Validate();
    if (!string.Equals(tenantId, options.TenantId, StringComparison.OrdinalIgnoreCase))
      throw new GraphCapabilityTokenException("credential-tenant-mismatch", notGranted: false);
    using var certificate = X509Certificate2.CreateFromPemFile(options.CertificatePath, options.PrivateKeyPath);
    var endpoint = $"https://login.microsoftonline.com/{options.TenantId}/oauth2/v2.0/token";
    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
    {
      Content = new FormUrlEncodedContent(new Dictionary<string, string>
      {
        ["client_id"] = options.ClientId,
        ["scope"] = "https://graph.microsoft.com/.default",
        ["grant_type"] = "client_credentials",
        ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
        ["client_assertion"] = GraphCertificateAssertion.Create(certificate, options.ClientId, endpoint)
      })
    };
    using var response = await http.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
      throw new GraphCapabilityTokenException("token-rejected", notGranted: false);
    using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    if (!json.RootElement.TryGetProperty("access_token", out var tokenElement) || string.IsNullOrWhiteSpace(tokenElement.GetString()))
      throw new GraphCapabilityTokenException("token-missing", notGranted: false);
    var token = tokenElement.GetString()!;
    var claims = GraphJwt.ReadPayload(token) ?? throw new GraphCapabilityTokenException("token-invalid", notGranted: false);
    using (claims)
    {
      var root = claims.RootElement;
      if (!root.TryGetProperty("tid", out var tid) || !string.Equals(tid.GetString(), options.TenantId, StringComparison.OrdinalIgnoreCase) ||
          !root.TryGetProperty("aud", out var aud) || aud.GetString() is not ("https://graph.microsoft.com" or "00000003-0000-0000-c000-000000000000") ||
          !root.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expSeconds) ||
          expSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        throw new GraphCapabilityTokenException("token-invalid", notGranted: false);
      var roles = root.TryGetProperty("roles", out var rolesElement) && rolesElement.ValueKind == JsonValueKind.Array
        ? rolesElement.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToHashSet(StringComparer.Ordinal)
        : new HashSet<string>(StringComparer.Ordinal);
      if (!roles.Contains(options.RequiredRole))
        throw new GraphCapabilityTokenException("app-role-not-granted", notGranted: true);
      // Least privilege: the identity must not hold any role outside its approved set.
      if (!roles.All(options.AllowedRoles.Contains))
        throw new GraphCapabilityTokenException("app-role-set-too-broad", notGranted: false);
      return new(token, tid.GetString()!, roles);
    }
  }
}

internal static class GraphCertificateAssertion
{
  public static string Create(X509Certificate2 certificate, string clientId, string audience)
  {
    var now = DateTimeOffset.UtcNow;
    var header = GraphJwt.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
    {
      alg = "RS256", typ = "JWT", x5t = GraphJwt.Base64Url(certificate.GetCertHash(HashAlgorithmName.SHA1))
    }));
    var payload = GraphJwt.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
    {
      aud = audience, iss = clientId, sub = clientId, jti = Guid.NewGuid().ToString("D"),
      nbf = now.AddMinutes(-1).ToUnixTimeSeconds(), exp = now.AddMinutes(5).ToUnixTimeSeconds()
    }));
    var unsigned = header + "." + payload;
    using var key = certificate.GetRSAPrivateKey() ??
      throw new InvalidOperationException("The capability certificate has no RSA private key.");
    return unsigned + "." + GraphJwt.Base64Url(key.SignData(
      Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
  }
}

internal static class GraphJwt
{
  public static JsonDocument? ReadPayload(string token)
  {
    var parts = token.Split('.');
    if (parts.Length != 3) return null;
    try { return JsonDocument.Parse(FromBase64Url(parts[1])); }
    catch (Exception ex) when (ex is JsonException or FormatException) { return null; }
  }

  public static string Base64Url(byte[] value) => Convert.ToBase64String(value)
    .TrimEnd('=').Replace('+', '-').Replace('/', '_');

  public static byte[] FromBase64Url(string value)
  {
    var padded = value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=');
    return Convert.FromBase64String(padded);
  }
}
