using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;

namespace AuditSphereOps.Infrastructure.Providers;

public sealed record GraphMailOptions(
  string TenantId,
  string ClientId,
  string SenderMailbox,
  string CertificatePath,
  string PrivateKeyPath)
{
  public void Validate()
  {
    if (!Guid.TryParse(TenantId, out _) || !Guid.TryParse(ClientId, out _) ||
        string.IsNullOrWhiteSpace(SenderMailbox) || !Path.IsPathFullyQualified(CertificatePath) ||
        !Path.IsPathFullyQualified(PrivateKeyPath) || !File.Exists(CertificatePath) || !File.Exists(PrivateKeyPath))
      throw new InvalidOperationException("GraphMail requires tenant, client, sender mailbox and local certificate files.");
  }
}

public sealed class GraphPbcMailSender(HttpClient http, GraphMailOptions options) : IPbcMailSender
{
  public async Task SendAsync(PbcMailPlan plan, CancellationToken ct)
  {
    options.Validate();
    using var certificate = X509Certificate2.CreateFromPemFile(options.CertificatePath, options.PrivateKeyPath);
    var tokenEndpoint = $"https://login.microsoftonline.com/{options.TenantId}/oauth2/v2.0/token";
    using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
    {
      Content = new FormUrlEncodedContent(new Dictionary<string, string>
      {
        ["client_id"] = options.ClientId,
        ["scope"] = "https://graph.microsoft.com/.default",
        ["grant_type"] = "client_credentials",
        ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
        ["client_assertion"] = Assertion(certificate, options.ClientId, tokenEndpoint)
      })
    };
    using var tokenResponse = await http.SendAsync(tokenRequest, ct);
    if (!tokenResponse.IsSuccessStatusCode)
      throw new OperationBlockedException("graph-token-rejected", authorization: true);
    using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStreamAsync(ct));
    if (!tokenJson.RootElement.TryGetProperty("access_token", out var token) ||
        string.IsNullOrWhiteSpace(token.GetString()))
      throw new OperationBlockedException("graph-token-missing", authorization: true);

    var endpoint = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(options.SenderMailbox)}/sendMail";
    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.GetString());
    request.Headers.TryAddWithoutValidation("client-request-id", plan.CorrelationId.ToString("D"));
    request.Headers.TryAddWithoutValidation("return-client-request-id", "true");
    request.Content = JsonContent.Create(new
    {
      message = new
      {
        subject = plan.Subject,
        body = new { contentType = "Text", content = plan.Body },
        toRecipients = new[] { new { emailAddress = new { address = plan.Recipient } } }
      },
      saveToSentItems = true
    });
    using var response = await http.SendAsync(request, ct);
    if (response.StatusCode == HttpStatusCode.Accepted) return;
    if (response.StatusCode == HttpStatusCode.TooManyRequests)
      throw new SafeRetryException(response.Headers.RetryAfter?.Delta);
    if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
      throw new OperationBlockedException("graph-mail-rejected", authorization: response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    throw new HttpRequestException("Graph mail outcome was not accepted.");
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
      throw new InvalidOperationException("GraphMail certificate has no RSA private key.");
    return unsigned + "." + Base64Url(key.SignData(
      Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
  }

  private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
