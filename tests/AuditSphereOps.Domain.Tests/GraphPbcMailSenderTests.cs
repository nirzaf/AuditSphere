using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Domain.Tests;

public sealed class GraphPbcMailSenderTests
{
  [Fact]
  public async Task SendAsync_AttachesTheExactRenderedOfferBytesToGraphMail()
  {
    var directory = Path.Combine(Path.GetTempPath(), "auditsphere-graph-mail-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var certificatePath = Path.Combine(directory, "certificate.pem");
    var keyPath = Path.Combine(directory, "private-key.pem");
    try
    {
      using var key = RSA.Create(2048);
      var request = new CertificateRequest("CN=AuditSphere Graph mail test", key, HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
      using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
      await File.WriteAllTextAsync(certificatePath, certificate.ExportCertificatePem());
      await File.WriteAllTextAsync(keyPath, key.ExportPkcs8PrivateKeyPem());

      var handler = new GraphMailCaptureHandler();
      using var http = new HttpClient(handler);
      var sender = new GraphPbcMailSender(http, new GraphMailOptions(
        Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), "sender@example.test", certificatePath, keyPath));
      var bytes = System.Text.Encoding.UTF8.GetBytes("synthetic exact rendered quotation document");
      var attachment = new PbcMailAttachment("Quotation-QA-01.docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", bytes);

      await sender.SendAsync(new PbcMailPlan("recipient@example.test", "Reviewed proposal", "Please review the attached offer.",
        Guid.NewGuid(), attachment), CancellationToken.None);

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
      Directory.Delete(directory, recursive: true);
    }
  }

  private sealed class GraphMailCaptureHandler : HttpMessageHandler
  {
    public string? GraphPayload { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      if (request.RequestUri?.Host == "login.microsoftonline.com")
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "test-token" }) };

      Assert.Equal("graph.microsoft.com", request.RequestUri?.Host);
      Assert.Equal(HttpMethod.Post, request.Method);
      Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
      GraphPayload = await request.Content!.ReadAsStringAsync(cancellationToken);
      return new HttpResponseMessage(HttpStatusCode.Accepted);
    }
  }
}
