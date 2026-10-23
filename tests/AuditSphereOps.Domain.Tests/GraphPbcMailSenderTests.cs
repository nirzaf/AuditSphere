using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Domain.Tests;

public sealed class GraphPbcMailSenderTests
{
  [Fact]
  public async Task SendAsync_AttachesExactRenderedBytesAfterTwoMatchingAuthorizationChecks()
  {
    var context = await CreateContextAsync();
    try
    {
      var receipt = Receipt(context);
      var gate = new QueueMailAuthorizationGate([receipt, receipt]);
      var token = CreateToken(context.TenantId, context.ClientId, ["Mail.Send"]);
      var handler = new GraphMailCaptureHandler(token, context.SenderId);
      using var http = new HttpClient(handler);
      var sender = new GraphPbcMailSender(http, context.Options, gate, TimeProvider.System);
      var bytes = Encoding.UTF8.GetBytes("synthetic exact rendered quotation document");
      var attachment = new PbcMailAttachment("Quotation-QA-01.docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", bytes);

      await sender.SendAsync(new PbcMailPlan("recipient@example.test", "Reviewed proposal",
        "Please review the attached offer.", Guid.NewGuid(), attachment, context.FirmId), CancellationToken.None);

      Assert.Equal(2, gate.Calls);
      Assert.Equal(1, handler.TokenCalls);
      Assert.Equal(1, handler.GraphCalls);
      Assert.NotNull(handler.GraphPayload);
      using var payload = JsonDocument.Parse(handler.GraphPayload);
      var message = payload.RootElement.GetProperty("message");
      var graphAttachment = Assert.Single(message.GetProperty("attachments").EnumerateArray());
      Assert.Equal("#microsoft.graph.fileAttachment", graphAttachment.GetProperty("@odata.type").GetString());
      Assert.Equal(attachment.FileName, graphAttachment.GetProperty("name").GetString());
      Assert.Equal(attachment.ContentType, graphAttachment.GetProperty("contentType").GetString());
      Assert.Equal(bytes, Convert.FromBase64String(graphAttachment.GetProperty("contentBytes").GetString()!));
    }
    finally
    {
      Directory.Delete(context.Directory, recursive: true);
    }
  }

  [Fact]
  public async Task PreflightAuthorizationDenial_MakesNoTokenOrGraphRequest()
  {
    var context = await CreateContextAsync();
    try
    {
      var gate = new QueueMailAuthorizationGate([], fail: true);
      var handler = new GraphMailCaptureHandler(CreateToken(context.TenantId, context.ClientId, ["Mail.Send"]), context.SenderId);
      using var http = new HttpClient(handler);
      var sender = new GraphPbcMailSender(http, context.Options, gate, TimeProvider.System);

      var exception = await Assert.ThrowsAsync<OperationBlockedException>(() => sender.PreflightAsync(context.FirmId, CancellationToken.None));

      Assert.Equal("mail-authorization-evidence-unavailable", exception.Code);
      Assert.Equal(0, handler.TokenCalls);
      Assert.Equal(0, handler.GraphCalls);
    }
    finally
    {
      Directory.Delete(context.Directory, recursive: true);
    }
  }

  [Theory]
  [InlineData("")]
  [InlineData("fedcba9876543210fedcba9876543210fedcba98")]
  public async Task MissingOrChangedBuildSource_BlocksBeforeTokenOrGraphRequest(string observedRepositoryCommit)
  {
    var context = await CreateContextAsync();
    try
    {
      var expectedRepositoryCommit = "0123456789abcdef0123456789abcdef01234567";
      var source = new MailAuthorizationSourceIdentity(expectedRepositoryCommit,
        new string('b', 64), new string('c', 64), new string('d', 64),
        new string('e', 64), new string('f', 64), new string('1', 64));
      var runtimeOptions = new MailAuthorizationRuntimeOptions(context.FirmId, context.TenantId,
        MailAuthorizationModel.LegacyApplicationAccessPolicy, context.ClientId, Guid.NewGuid(),
        [context.SenderId], Guid.NewGuid(), new string('2', 64), source, observedRepositoryCommit,
        new string('3', 64), TimeSpan.FromHours(24));
      var gate = new MailAuthorizationRuntimeGate(null!, null!, runtimeOptions, TimeProvider.System);
      var handler = new GraphMailCaptureHandler(CreateToken(context.TenantId, context.ClientId, ["Mail.Send"]), context.SenderId);
      using var http = new HttpClient(handler);
      var sender = new GraphPbcMailSender(http, context.Options, gate, TimeProvider.System);

      await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
        new PbcMailPlan("recipient@example.test", "Subject", "Body", Guid.NewGuid(), FirmId: context.FirmId),
        CancellationToken.None));

      Assert.Equal(0, handler.TokenCalls);
      Assert.Equal(0, handler.GraphCalls);
    }
    finally
    {
      Directory.Delete(context.Directory, recursive: true);
    }
  }

  [Fact]
  public async Task ReceiptChangesAfterTokenValidation_MakesNoGraphRequest()
  {
    var context = await CreateContextAsync();
    try
    {
      var first = Receipt(context);
      var changed = first with { AttestationDigest = new string('b', 64) };
      var gate = new QueueMailAuthorizationGate([first, changed]);
      var handler = new GraphMailCaptureHandler(CreateToken(context.TenantId, context.ClientId, ["Mail.Send"]), context.SenderId);
      using var http = new HttpClient(handler);
      var sender = new GraphPbcMailSender(http, context.Options, gate, TimeProvider.System);

      var exception = await Assert.ThrowsAsync<OperationBlockedException>(() => sender.SendAsync(
        new PbcMailPlan("recipient@example.test", "Subject", "Body", Guid.NewGuid(), FirmId: context.FirmId),
        CancellationToken.None));

      Assert.Equal("mail-authorization-evidence-changed", exception.Code);
      Assert.Equal(2, gate.Calls);
      Assert.Equal(1, handler.TokenCalls);
      Assert.Equal(0, handler.GraphCalls);
    }
    finally
    {
      Directory.Delete(context.Directory, recursive: true);
    }
  }

  [Fact]
  public async Task InvalidLegacyTokenClaims_MakeNoGraphRequest()
  {
    var context = await CreateContextAsync();
    try
    {
      var gate = new QueueMailAuthorizationGate([Receipt(context)]);
      var handler = new GraphMailCaptureHandler(CreateToken(context.TenantId, Guid.NewGuid(), ["Mail.Send"]), context.SenderId);
      using var http = new HttpClient(handler);
      var sender = new GraphPbcMailSender(http, context.Options, gate, TimeProvider.System);

      var exception = await Assert.ThrowsAsync<OperationBlockedException>(() => sender.SendAsync(
        new PbcMailPlan("recipient@example.test", "Subject", "Body", Guid.NewGuid(), FirmId: context.FirmId),
        CancellationToken.None));

      Assert.Equal("mail-authorization-token-invalid", exception.Code);
      Assert.Equal(1, handler.TokenCalls);
      Assert.Equal(0, handler.GraphCalls);
    }
    finally
    {
      Directory.Delete(context.Directory, recursive: true);
    }
  }

  private static async Task<TestMailContext> CreateContextAsync()
  {
    var directory = Path.Combine(Path.GetTempPath(), "auditsphere-graph-mail-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var certificatePath = Path.Combine(directory, "certificate.pem");
    var keyPath = Path.Combine(directory, "private-key.pem");
    using var key = RSA.Create(2048);
    var request = new CertificateRequest("CN=AuditSphere Graph mail test", key, HashAlgorithmName.SHA256,
      RSASignaturePadding.Pkcs1);
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
    await File.WriteAllTextAsync(certificatePath, certificate.ExportCertificatePem());
    await File.WriteAllTextAsync(keyPath, key.ExportPkcs8PrivateKeyPem());
    var tenantId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var senderId = Guid.NewGuid();
    var firmId = Guid.NewGuid();
    return new TestMailContext(directory,
      new GraphMailOptions(tenantId.ToString("D"), clientId.ToString("D"), senderId.ToString("D"),
        certificatePath, keyPath, firmId), tenantId, clientId, senderId, firmId);
  }

  private static MailAuthorizationRuntimeReceipt Receipt(TestMailContext context) => new(
    MailAuthorizationModel.LegacyApplicationAccessPolicy, context.TenantId, context.ClientId,
    Guid.NewGuid(), Guid.NewGuid(), context.SenderId, new string('a', 64), new string('c', 64));

  private static string CreateToken(Guid tenantId, Guid clientId, string[]? roles)
  {
    var payload = new Dictionary<string, object>
    {
      ["tid"] = tenantId.ToString("D"),
      ["aud"] = GraphMailAccessTokenValidator.GraphAudience,
      ["appid"] = clientId.ToString("D"),
      ["exp"] = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds()
    };
    if (roles is not null) payload["roles"] = roles;
    var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
    var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
    return $"{header}.{body}.{Base64Url([1, 2, 3])}";
  }

  private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
    .TrimEnd('=').Replace('+', '-').Replace('/', '_');

  private sealed record TestMailContext(string Directory, GraphMailOptions Options, Guid TenantId,
    Guid ClientId, Guid SenderId, Guid FirmId);

  private sealed class QueueMailAuthorizationGate(IReadOnlyList<MailAuthorizationRuntimeReceipt> receipts,
    bool fail = false) : IMailAuthorizationRuntimeGate
  {
    public int Calls { get; private set; }

    public Task<MailAuthorizationRuntimeReceipt> RequireCurrentAsync(Guid firmId, Guid tenantId,
      Guid runtimeClientId, Guid senderMailboxObjectId, CancellationToken ct)
    {
      Calls++;
      if (fail) return Task.FromException<MailAuthorizationRuntimeReceipt>(
        new OperationBlockedException("mail-authorization-evidence-unavailable", authorization: true));
      return Task.FromResult(receipts[Math.Min(Calls - 1, receipts.Count - 1)]);
    }
  }

  private sealed class GraphMailCaptureHandler(string accessToken, Guid senderObjectId) : HttpMessageHandler
  {
    public string? GraphPayload { get; private set; }
    public int TokenCalls { get; private set; }
    public int GraphCalls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      if (request.RequestUri?.Host == "login.microsoftonline.com")
      {
        TokenCalls++;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = accessToken }) };
      }

      GraphCalls++;
      Assert.Equal("graph.microsoft.com", request.RequestUri?.Host);
      Assert.Equal($"/v1.0/users/{senderObjectId:D}/sendMail", request.RequestUri?.AbsolutePath);
      Assert.Equal(HttpMethod.Post, request.Method);
      Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
      GraphPayload = await request.Content!.ReadAsStringAsync(cancellationToken);
      return new HttpResponseMessage(HttpStatusCode.Accepted);
    }
  }
}
