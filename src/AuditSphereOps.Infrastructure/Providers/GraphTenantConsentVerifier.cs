using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Microsoft365;
using Microsoft.Extensions.Logging;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>
/// Consent application settings. The consent app is the separate directory-administration registration,
/// never the web sign-in app. Its identity callback must be registered exactly in Entra.
/// </summary>
public sealed record TenantConsentVerifierOptions(
  bool Enabled, string TenantId, string ClientId, string IdentityRedirectUri,
  string CertificatePath, string PrivateKeyPath)
{
  public bool IsComplete => Enabled && Guid.TryParse(TenantId, out _) && Guid.TryParse(ClientId, out _) &&
    Uri.TryCreate(IdentityRedirectUri, UriKind.Absolute, out var redirect) &&
    redirect.AbsolutePath == "/auth/m365-consent/identity-callback" && string.IsNullOrEmpty(redirect.Query) &&
    (redirect.Scheme == Uri.UriSchemeHttps || redirect.IsLoopback) &&
    Path.IsPathFullyQualified(CertificatePath) && Path.IsPathFullyQualified(PrivateKeyPath);
}

public sealed class GraphTenantConsentVerifier(
  HttpClient http, TenantConsentVerifierOptions options,
  IReadOnlyDictionary<string, GraphCapabilityTokenSource> capabilityTokens,
  ILogger<GraphTenantConsentVerifier>? logger = null) : IMicrosoftTenantConsentVerifier
{
  private const string MicrosoftAccountTenant = "9188040d-6c67-4c5b-b112-36a304b66dad";

  public bool IsConfigured => options.IsComplete;

  public Uri BuildIdentityChallenge(string tenantId, string state, string nonce)
  {
    if (!IsConfigured || !Guid.TryParse(tenantId, out var tenant) ||
        !string.Equals(tenant.ToString("D"), options.TenantId, StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException("consent-verifier-not-configured");
    return new Uri($"https://login.microsoftonline.com/{tenant:D}/oauth2/v2.0/authorize" +
      $"?client_id={Uri.EscapeDataString(options.ClientId)}&response_type=code&response_mode=query" +
      $"&scope={Uri.EscapeDataString("openid")}&prompt=select_account" +
      $"&redirect_uri={Uri.EscapeDataString(options.IdentityRedirectUri)}" +
      $"&state={Uri.EscapeDataString(state)}&nonce={Uri.EscapeDataString(nonce)}");
  }

  public async Task<ConsentingAdministrator> RedeemIdentityAsync(string tenantId, string code, CancellationToken ct)
  {
    if (!IsConfigured || !File.Exists(options.CertificatePath) || !File.Exists(options.PrivateKeyPath) ||
        !string.Equals(tenantId, options.TenantId, StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException("consent-verifier-not-configured");
    using var certificate = X509Certificate2.CreateFromPemFile(options.CertificatePath, options.PrivateKeyPath);
    var endpoint = $"https://login.microsoftonline.com/{options.TenantId}/oauth2/v2.0/token";
    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
    {
      Content = new FormUrlEncodedContent(new Dictionary<string, string>
      {
        ["client_id"] = options.ClientId,
        ["grant_type"] = "authorization_code",
        ["code"] = code,
        ["redirect_uri"] = options.IdentityRedirectUri,
        ["scope"] = "openid",
        ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
        ["client_assertion"] = GraphCertificateAssertion.Create(certificate, options.ClientId, endpoint)
      })
    };
    using var response = await http.SendAsync(request, ct);
    if (response.StatusCode != HttpStatusCode.OK)
    {
      // Microsoft error descriptions can include request context. Log only the
      // bounded error code, never the authorization code, token or response body.
      var error = "unknown";
      try
      {
        using var failure = await JsonDocument.ParseAsync(
          await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (failure.RootElement.TryGetProperty("error", out var value) &&
            value.ValueKind == JsonValueKind.String && value.GetString() is { } errorValue &&
            errorValue.Length is > 0 and <= 64 && errorValue.All(c => c is >= 'a' and <= 'z' or '_'))
          error = errorValue;
      }
      catch (JsonException) { }
      logger?.LogWarning("Microsoft consent identity redemption failed: HTTP {Status}, code {Code}",
        (int)response.StatusCode, error);
      throw new InvalidOperationException("identity-code-rejected");
    }
    using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    // Only the ID token is inspected. Any access/refresh token in the response is discarded unread.
    var idToken = json.RootElement.TryGetProperty("id_token", out var element) ? element.GetString() : null;
    using var claims = idToken is null ? null : GraphJwt.ReadPayload(idToken);
    if (claims is null) throw new InvalidOperationException("identity-token-missing");
    return ValidateIdToken(claims.RootElement, options.TenantId, options.ClientId, DateTimeOffset.UtcNow);
  }

  /// <summary>
  /// The ID token arrives directly from the fixed Entra token endpoint over TLS (OIDC Core 3.1.3.7),
  /// so issuer, audience, tenant, expiry and nonce are validated here without a separate signature fetch.
  /// </summary>
  public static ConsentingAdministrator ValidateIdToken(JsonElement root, string tenantId, string clientId, DateTimeOffset now)
  {
    string? Claim(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    var tid = Claim("tid");
    var aud = Claim("aud");
    var iss = Claim("iss");
    var oid = Claim("oid");
    var nonce = Claim("nonce");
    var exp = root.TryGetProperty("exp", out var e) && e.TryGetInt64(out var seconds) ? seconds : 0;
    if (!Guid.TryParse(tid, out var tenant) || !string.Equals(tenant.ToString("D"), tenantId, StringComparison.OrdinalIgnoreCase) ||
        tenant.ToString("D") == MicrosoftAccountTenant ||
        !string.Equals(aud, clientId, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(iss, $"https://login.microsoftonline.com/{tenant:D}/v2.0", StringComparison.OrdinalIgnoreCase) ||
        !Guid.TryParse(oid, out _) || string.IsNullOrEmpty(nonce) || exp <= now.ToUnixTimeSeconds())
      throw new InvalidOperationException("identity-token-invalid");
    // A guest or federated external account carries an idp claim different from the issuer.
    var idp = Claim("idp");
    var external = !string.IsNullOrEmpty(idp) && !string.Equals(idp, iss, StringComparison.OrdinalIgnoreCase);
    return new(tenant.ToString("D"), Guid.Parse(oid!).ToString("D"), nonce!, external);
  }

  public async Task<IReadOnlyList<CapabilityProbeResult>> VerifyCapabilitiesAsync(string tenantId,
    IReadOnlyList<CapabilityProbe> capabilities, CancellationToken ct)
  {
    var results = new List<CapabilityProbeResult>();
    foreach (var capability in capabilities)
    {
      if (!capabilityTokens.TryGetValue(capability.Capability, out var source) || !source.Options.IsComplete)
      {
        results.Add(new(capability.Capability, CapabilityVerificationStates.BlockedExternal, "credential-not-configured"));
        continue;
      }
      if (!string.Equals(source.Options.RequiredRole, capability.Permission, StringComparison.Ordinal))
      {
        results.Add(new(capability.Capability, CapabilityVerificationStates.Failed, "credential-permission-mismatch"));
        continue;
      }
      try
      {
        var token = await source.GetAsync(tenantId, ct);
        results.Add(await ProbeAsync(capability.Capability, token, tenantId, ct));
      }
      catch (GraphCapabilityTokenException ex)
      {
        results.Add(new(capability.Capability, ex.NotGranted ? CapabilityVerificationStates.NotGranted :
          CapabilityVerificationStates.Failed, ex.Code));
      }
      catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
      {
        results.Add(new(capability.Capability, CapabilityVerificationStates.Failed, "capability-probe-failed"));
      }
    }
    return results;
  }

  /// <summary>Bounded, read-only probe per capability. Mutation capabilities are verified by app role only.</summary>
  private async Task<CapabilityProbeResult> ProbeAsync(string capability, GraphCapabilityToken token, string tenantId,
    CancellationToken ct)
  {
    string? url = capability switch
    {
      Microsoft365Capabilities.DirectoryRead => $"{GraphCall.Graph}/users?$top=1&$select=id",
      Microsoft365Capabilities.GroupMembership => $"{GraphCall.Graph}/groups?$top=1&$select=id",
      _ => null
    };
    if (url is null)
      return new(capability, CapabilityVerificationStates.Verified, "app-role-present");
    using var response = await GraphCall.SendAsync(http, token.AccessToken, HttpMethod.Get, url, null, ct);
    return response.Success
      ? new(capability, CapabilityVerificationStates.Verified, "bounded-read-succeeded", response.CorrelationId)
      : new(capability, response.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
          ? CapabilityVerificationStates.NotGranted : CapabilityVerificationStates.Failed,
          $"bounded-read-{(int)response.Status}", response.CorrelationId);
  }
}
