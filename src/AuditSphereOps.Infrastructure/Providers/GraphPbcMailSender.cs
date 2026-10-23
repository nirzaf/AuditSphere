using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;

namespace AuditSphereOps.Infrastructure.Providers;

public sealed record GraphMailOptions(
  string TenantId,
  string ClientId,
  string SenderObjectId,
  string CertificatePath,
  string PrivateKeyPath,
  Guid FirmId)
{
  // Kept only so older offline/live harnesses still compile while they migrate. Empty sender/firm identity makes this
  // constructor fail closed in Validate and cannot authorize a Graph request.
  public GraphMailOptions(string tenantId, string clientId, string legacySenderMailbox,
    string certificatePath, string privateKeyPath)
    : this(tenantId, clientId, string.Empty, certificatePath, privateKeyPath, Guid.Empty)
  {
    _ = legacySenderMailbox;
  }

  public void Validate()
  {
    if (!Guid.TryParse(TenantId, out _) || !Guid.TryParse(ClientId, out _) ||
        !Guid.TryParse(SenderObjectId, out var senderObjectId) || senderObjectId == Guid.Empty || FirmId == Guid.Empty ||
        !Path.IsPathFullyQualified(CertificatePath) ||
        !Path.IsPathFullyQualified(PrivateKeyPath) || !File.Exists(CertificatePath) || !File.Exists(PrivateKeyPath))
      throw new InvalidOperationException("GraphMail requires tenant, client, sender object ID, firm and local certificate files.");
  }
}

public sealed class GraphPbcMailSender(HttpClient http, GraphMailOptions options,
  IMailAuthorizationRuntimeGate authorization, TimeProvider clock) : IPbcMailSender
{
  public GraphPbcMailSender(HttpClient http, GraphMailOptions options)
    : this(http, options, new UnconfiguredMailAuthorizationRuntimeGate(), TimeProvider.System)
  {
  }

  public async Task PreflightAsync(Guid firmId, CancellationToken ct)
  {
    var (tenantId, clientId, senderObjectId) = ValidateWorkerBinding(firmId);
    var receipt = await authorization.RequireCurrentAsync(firmId, tenantId, clientId, senderObjectId, ct);
    if (!ReceiptMatchesWorkerBinding(receipt, tenantId, clientId, senderObjectId))
      throw new OperationBlockedException("mail-authorization-evidence-unavailable", authorization: true);
  }

  public async Task SendAsync(PbcMailPlan plan, CancellationToken ct)
  {
    var (tenantId, clientId, senderObjectId) = ValidateWorkerBinding(plan.FirmId);
    if (plan.FirmId != options.FirmId)
      throw new OperationBlockedException("mail-authorization-worker-binding-mismatch", authorization: true);

    var beforeToken = await authorization.RequireCurrentAsync(plan.FirmId, tenantId, clientId, senderObjectId, ct);
    if (!ReceiptMatchesWorkerBinding(beforeToken, tenantId, clientId, senderObjectId))
      throw new OperationBlockedException("mail-authorization-evidence-unavailable", authorization: true);
    X509Certificate2 certificate;
    try
    {
      options.Validate();
      certificate = X509Certificate2.CreateFromPemFile(options.CertificatePath, options.PrivateKeyPath);
    }
    catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or
        InvalidOperationException or ArgumentException)
    {
      throw new OperationBlockedException("mail-authorization-credential-unavailable", authorization: true);
    }
    using var certificateLifetime = certificate;
    var tokenEndpoint = $"https://login.microsoftonline.com/{options.TenantId}/oauth2/v2.0/token";
    string clientAssertion;
    try { clientAssertion = Assertion(certificate, options.ClientId, tokenEndpoint); }
    catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or ArgumentException)
    {
      throw new OperationBlockedException("mail-authorization-credential-unavailable", authorization: true);
    }
    using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
    {
      Content = new FormUrlEncodedContent(new Dictionary<string, string>
      {
        ["client_id"] = options.ClientId,
        ["scope"] = "https://graph.microsoft.com/.default",
        ["grant_type"] = "client_credentials",
        ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
        ["client_assertion"] = clientAssertion
      })
    };
    HttpResponseMessage tokenResponse;
    try { tokenResponse = await http.SendAsync(tokenRequest, ct); }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SafeRetryException(); }
    catch (HttpRequestException) { throw new SafeRetryException(); }
    using var tokenResponseLifetime = tokenResponse;
    if (tokenResponse.StatusCode == HttpStatusCode.TooManyRequests)
      throw new SafeRetryException(tokenResponse.Headers.RetryAfter?.Delta);
    if ((int)tokenResponse.StatusCode >= 500)
      throw new SafeRetryException();
    if (!tokenResponse.IsSuccessStatusCode)
      throw new OperationBlockedException("mail-authorization-token-rejected", authorization: true);
    byte[] tokenResponseBytes;
    try { tokenResponseBytes = await ReadBoundedTokenResponseAsync(tokenResponse.Content, ct); }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SafeRetryException(); }
    catch (HttpRequestException) { throw new SafeRetryException(); }
    catch (InvalidDataException) { throw new OperationBlockedException("mail-authorization-token-invalid", authorization: true); }
    catch (IOException) { throw new SafeRetryException(); }
    JsonDocument tokenJsonDocument;
    try
    {
      tokenJsonDocument = JsonDocument.Parse(tokenResponseBytes, new JsonDocumentOptions
      {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 8
      });
    }
    catch (JsonException)
    {
      throw new OperationBlockedException("mail-authorization-token-invalid", authorization: true);
    }
    using var tokenJson = tokenJsonDocument;
    if (tokenJson.RootElement.ValueKind != JsonValueKind.Object ||
        tokenJson.RootElement.EnumerateObject().GroupBy(x => x.Name, StringComparer.Ordinal).Any(x => x.Count() > 1) ||
        !tokenJson.RootElement.TryGetProperty("access_token", out var token) ||
        token.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(token.GetString()))
      throw new OperationBlockedException("mail-authorization-token-invalid", authorization: true);
    var accessToken = token.GetString()!;
    try
    {
      GraphMailAccessTokenValidator.Validate(accessToken, beforeToken.PermissionModel, tenantId, clientId,
        clock.GetUtcNow());
    }
    catch (GraphMailAccessTokenValidationException)
    {
      throw new OperationBlockedException("mail-authorization-token-invalid", authorization: true);
    }

    var beforeDispatch = await authorization.RequireCurrentAsync(plan.FirmId, tenantId, clientId, senderObjectId, ct);
    if (!SameReceipt(beforeToken, beforeDispatch))
      throw new OperationBlockedException("mail-authorization-evidence-changed", authorization: true);

    var endpoint = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(senderObjectId.ToString("D"))}/sendMail";
    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    request.Headers.TryAddWithoutValidation("client-request-id", plan.CorrelationId.ToString("D"));
    request.Headers.TryAddWithoutValidation("return-client-request-id", "true");
    var message = new Dictionary<string, object?>
    {
      ["subject"] = plan.Subject,
      ["body"] = new { contentType = "Text", content = plan.Body },
      ["toRecipients"] = new[] { new { emailAddress = new { address = plan.Recipient } } }
    };
    if (plan.Attachment is { } attachment)
    {
      if (attachment.Bytes.Length == 0 || attachment.Bytes.Length > 3 * 1024 * 1024 ||
          string.IsNullOrWhiteSpace(attachment.FileName) || string.IsNullOrWhiteSpace(attachment.ContentType))
        throw new OperationBlockedException("graph-mail-attachment-invalid");
      message["attachments"] = new object[]
      {
        new Dictionary<string, object>
        {
          ["@odata.type"] = "#microsoft.graph.fileAttachment",
          ["name"] = attachment.FileName,
          ["contentType"] = attachment.ContentType,
          ["contentBytes"] = Convert.ToBase64String(attachment.Bytes)
        }
      };
    }
    request.Content = JsonContent.Create(new Dictionary<string, object?>
    {
      ["message"] = message,
      ["saveToSentItems"] = true
    });
    using var response = await http.SendAsync(request, ct);
    if (response.StatusCode == HttpStatusCode.Accepted) return;
    if (response.StatusCode == HttpStatusCode.TooManyRequests)
      throw new SafeRetryException(response.Headers.RetryAfter?.Delta);
    if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
      throw new OperationBlockedException("graph-mail-rejected", authorization: response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    throw new HttpRequestException("Graph mail outcome was not accepted.");
  }

  private (Guid TenantId, Guid ClientId, Guid SenderObjectId) ValidateWorkerBinding(Guid firmId)
  {
    if (firmId == Guid.Empty || firmId != options.FirmId ||
        !Guid.TryParse(options.TenantId, out var tenantId) || tenantId == Guid.Empty ||
        !Guid.TryParse(options.ClientId, out var clientId) || clientId == Guid.Empty ||
        !Guid.TryParse(options.SenderObjectId, out var senderObjectId) || senderObjectId == Guid.Empty)
      throw new OperationBlockedException("mail-authorization-worker-binding-mismatch", authorization: true);
    return (tenantId, clientId, senderObjectId);
  }

  private static bool SameReceipt(MailAuthorizationRuntimeReceipt left, MailAuthorizationRuntimeReceipt right) =>
    left.PermissionModel == right.PermissionModel && left.TenantId == right.TenantId &&
    left.RuntimeClientId == right.RuntimeClientId && left.ConnectionRevisionId == right.ConnectionRevisionId &&
    left.ConsentAttemptId == right.ConsentAttemptId && left.SenderMailboxObjectId == right.SenderMailboxObjectId &&
    string.Equals(left.ProviderSnapshotSha256, right.ProviderSnapshotSha256, StringComparison.Ordinal) &&
    string.Equals(left.AttestationDigest, right.AttestationDigest, StringComparison.Ordinal);

  private static bool ReceiptMatchesWorkerBinding(MailAuthorizationRuntimeReceipt receipt, Guid tenantId,
    Guid clientId, Guid senderObjectId) =>
    (receipt.PermissionModel is MailAuthorizationModel.LegacyApplicationAccessPolicy or
      MailAuthorizationModel.ExchangeRbacResourceScope) &&
    receipt.TenantId == tenantId && receipt.RuntimeClientId == clientId &&
    receipt.SenderMailboxObjectId == senderObjectId && receipt.ConnectionRevisionId != Guid.Empty &&
    receipt.ConsentAttemptId != Guid.Empty && IsSha256(receipt.ProviderSnapshotSha256) &&
    IsSha256(receipt.AttestationDigest);

  private static bool IsSha256(string? value) => value is { Length: 64 } &&
    value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

  private static async Task<byte[]> ReadBoundedTokenResponseAsync(HttpContent content, CancellationToken ct)
  {
    const int maximumBytes = 65_536;
    if (content.Headers.ContentLength is > maximumBytes)
      throw new InvalidDataException();
    await using var stream = await content.ReadAsStreamAsync(ct);
    using var buffer = new MemoryStream();
    var chunk = new byte[8192];
    while (true)
    {
      var read = await stream.ReadAsync(chunk, ct);
      if (read == 0) break;
      if (buffer.Length + read > maximumBytes) throw new InvalidDataException();
      await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
    }
    return buffer.ToArray();
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

  private sealed class UnconfiguredMailAuthorizationRuntimeGate : IMailAuthorizationRuntimeGate
  {
    public Task<MailAuthorizationRuntimeReceipt> RequireCurrentAsync(Guid firmId, Guid tenantId, Guid runtimeClientId,
      Guid senderMailboxObjectId, CancellationToken ct) =>
      Task.FromException<MailAuthorizationRuntimeReceipt>(
        new OperationBlockedException("mail-authorization-evidence-unavailable", authorization: true));
  }
}
