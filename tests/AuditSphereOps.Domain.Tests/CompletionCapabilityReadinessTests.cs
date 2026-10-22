using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// WU-008: a Graph capability token is accepted only for the exact tenant, application, audience, lifetime and
/// single approved role. Stubbed HTTP only; no Microsoft access.
/// </summary>
[Trait("Profile", "Unit")]
[Trait("CompletionUnit", "WU-008")]
public sealed class CompletionCapabilityReadinessTests : IDisposable
{
  private static readonly string Tenant = Guid.NewGuid().ToString("D");
  private static readonly string Client = Guid.NewGuid().ToString("D");
  private readonly string directory = Path.Combine(Path.GetTempPath(), "as-wu008-" + Guid.NewGuid().ToString("N"));
  private readonly GraphCapabilityCredentialOptions credential;

  public CompletionCapabilityReadinessTests()
  {
    Directory.CreateDirectory(directory);
    using var rsa = RSA.Create(2048);
    using var certificate = new CertificateRequest("CN=AuditSphere WU-008", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
      .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    File.WriteAllText(Path.Combine(directory, "cert.pem"), certificate.ExportCertificatePem());
    File.WriteAllText(Path.Combine(directory, "key.pem"), rsa.ExportPkcs8PrivateKeyPem());
    credential = new(true, Tenant, Client, Path.Combine(directory, "cert.pem"), Path.Combine(directory, "key.pem"), "User.Invite.All");
  }

  public void Dispose() { try { Directory.Delete(directory, recursive: true); } catch (IOException) { } }

  public static TheoryData<string, string> RejectedTokens => new()
  {
    { "rotated-or-substituted-application", "token-identity-mismatch" },
    { "missing-application-identity", "token-identity-mismatch" },
    { "token-for-another-tenant", "token-invalid" },
    { "expired", "token-invalid" },
    { "wrong-audience", "token-invalid" },
    { "extra-role", "app-role-set-too-broad" },
  };

  [Theory]
  [MemberData(nameof(RejectedTokens))]
  public async Task CapabilityToken_IsRefusedForAnyIdentityOrScopeDeviation(string variant, string code)
  {
    var claims = new Dictionary<string, object?>
    {
      ["tid"] = Tenant, ["aud"] = "https://graph.microsoft.com", ["appid"] = Client,
      ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), ["roles"] = new[] { "User.Invite.All" }
    };
    switch (variant)
    {
      case "rotated-or-substituted-application": claims["appid"] = Guid.NewGuid().ToString("D"); break;
      case "missing-application-identity": claims.Remove("appid"); break;
      case "token-for-another-tenant": claims["tid"] = Guid.NewGuid().ToString("D"); break;
      case "expired": claims["exp"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(); break;
      case "wrong-audience": claims["aud"] = "https://outlook.office365.com"; break;
      case "extra-role": claims["roles"] = new[] { "User.Invite.All", "Directory.ReadWrite.All" }; break;
    }
    var source = new GraphCapabilityTokenSource(new HttpClient(new TokenEndpoint(claims)), credential);
    var refused = await Assert.ThrowsAsync<GraphCapabilityTokenException>(() => source.GetAsync(Tenant, default));
    Assert.Equal(code, refused.Code);
    // Diagnostics name the reason only; no token material leaks into the exception.
    Assert.DoesNotContain("eyJ", refused.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task CapabilityToken_IsAcceptedForTheExactApplicationViaV1OrV2Claim()
  {
    foreach (var identityClaim in new[] { "appid", "azp" })
    {
      var claims = new Dictionary<string, object?>
      {
        ["tid"] = Tenant, ["aud"] = "https://graph.microsoft.com", [identityClaim] = Client.ToUpperInvariant(),
        ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), ["roles"] = new[] { "User.Invite.All" }
      };
      var token = await new GraphCapabilityTokenSource(new HttpClient(new TokenEndpoint(claims)), credential).GetAsync(Tenant, default);
      Assert.Equal(["User.Invite.All"], token.ApplicationRoles);
    }
  }

  private sealed class TokenEndpoint(Dictionary<string, object?> claims) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
      var body = JsonSerializer.Serialize(new { access_token = "eyJhbGciOiJub25lIn0." + payload + ".sig" });
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
  }
}
