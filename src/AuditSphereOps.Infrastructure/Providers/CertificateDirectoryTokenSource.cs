using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Operations;

namespace AuditSphereOps.Infrastructure.Providers;

public interface IDirectoryTokenSource
{
  Task<DirectoryToken> GetAsync(string tenantId, CancellationToken ct);
}

public sealed record DirectoryToken(string AccessToken, string TenantId, IReadOnlySet<string> ApplicationRoles);

public sealed record DirectoryCertificateOptions(
  bool Enabled, string TenantId, string ClientId, string CertificatePath, string PrivateKeyPath)
{
  public void Validate()
  {
    if (!Enabled || !Guid.TryParse(TenantId, out _) || !Guid.TryParse(ClientId, out _) ||
        !Path.IsPathFullyQualified(CertificatePath) || !Path.IsPathFullyQualified(PrivateKeyPath) ||
        !File.Exists(CertificatePath) || !File.Exists(PrivateKeyPath))
      throw new OperationBlockedException("directory-reader-credential-unavailable", authorization: true);
  }
}

/// <summary>Obtains a Graph application token from the exact tenant with a private runtime certificate.</summary>
public sealed class CertificateDirectoryTokenSource(HttpClient http, DirectoryCertificateOptions options)
  : IDirectoryTokenSource
{
  public async Task<DirectoryToken> GetAsync(string tenantId, CancellationToken ct)
  {
    options.Validate();
    if (!string.Equals(tenantId, options.TenantId, StringComparison.OrdinalIgnoreCase))
      throw new OperationBlockedException("directory-reader-credential-mismatch", authorization: true);

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
        ["client_assertion"] = Assertion(certificate, options.ClientId, endpoint)
      })
    };
    using var response = await http.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
      throw new OperationBlockedException("directory-reader-token-rejected", authorization: true);
    using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    if (!json.RootElement.TryGetProperty("access_token", out var tokenElement) ||
        string.IsNullOrWhiteSpace(tokenElement.GetString()))
      throw new OperationBlockedException("directory-reader-token-missing", authorization: true);

    var token = tokenElement.GetString()!;
    // The token is received directly from the fixed Entra endpoint over TLS. These claims are
    // inspected as a defense-in-depth fence; Graph validates the token signature on use.
    var parts = token.Split('.');
    if (parts.Length != 3) throw new OperationBlockedException("directory-reader-token-invalid", authorization: true);
    try
    {
      using var claims = JsonDocument.Parse(FromBase64Url(parts[1]));
      var root = claims.RootElement;
      if (!root.TryGetProperty("tid", out var tid) ||
          !string.Equals(tid.GetString(), options.TenantId, StringComparison.OrdinalIgnoreCase) ||
          !root.TryGetProperty("aud", out var audience) ||
          audience.GetString() is not ("https://graph.microsoft.com" or "00000003-0000-0000-c000-000000000000") ||
          !root.TryGetProperty("exp", out var expiry) || !expiry.TryGetInt64(out var expirySeconds) ||
          expirySeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
          !root.TryGetProperty("roles", out var rolesElement) || rolesElement.ValueKind != JsonValueKind.Array)
        throw new OperationBlockedException("directory-reader-token-invalid", authorization: true);
      var roles = rolesElement.EnumerateArray().Select(x => x.GetString() ?? string.Empty)
        .ToHashSet(StringComparer.Ordinal);
      if (roles.Count != 1 || !roles.Contains("User.Read.All"))
        throw new OperationBlockedException("directory-reader-token-invalid", authorization: true);
      return new(token, tid.GetString()!, roles);
    }
    catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
    {
      throw new OperationBlockedException("directory-reader-token-invalid", authorization: true);
    }
  }

  private static string Assertion(X509Certificate2 certificate, string clientId, string audience)
  {
    var now = DateTimeOffset.UtcNow;
    var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
    {
      alg = "RS256", typ = "JWT", x5t = Base64Url(certificate.GetCertHash(HashAlgorithmName.SHA1))
    }));
    var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
    {
      aud = audience, iss = clientId, sub = clientId, jti = Guid.NewGuid().ToString("D"),
      nbf = now.AddMinutes(-1).ToUnixTimeSeconds(), exp = now.AddMinutes(5).ToUnixTimeSeconds()
    }));
    var unsigned = header + "." + payload;
    using var key = certificate.GetRSAPrivateKey() ??
      throw new InvalidOperationException("Directory-reader certificate has no RSA private key.");
    return unsigned + "." + Base64Url(key.SignData(
      Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
  }

  private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
    .TrimEnd('=').Replace('+', '-').Replace('/', '_');

  private static byte[] FromBase64Url(string value)
  {
    var padded = value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=');
    return Convert.FromBase64String(padded);
  }
}
