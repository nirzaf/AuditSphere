using System.Net;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Infrastructure.Providers;
using OpenTelemetry;

namespace AuditSphereOps.Domain.Tests;

public sealed class GraphPreauthenticatedTransportTests
{
  [Theory]
  [InlineData("http://tenant.sharepoint.com/upload")]
  [InlineData("https://tenant.sharepoint.com.evil.test/upload")]
  [InlineData("https://user@tenant.sharepoint.com/upload")]
  [InlineData("https://tenant.sharepoint.com:8443/upload")]
  [InlineData("https://tenant.sharepoint.com/upload#fragment")]
  public async Task NonSelectedCapabilityUrl_IsRejectedBeforeHttp(string value)
  {
    var handler = new RecordingHandler(HttpStatusCode.OK);
    using var transport = new GraphPreauthenticatedTransport(handler);
    await Assert.ThrowsAsync<OperationBlockedException>(() =>
      transport.SendAsync(new Uri(value), HttpMethod.Get, null, CancellationToken.None));
    Assert.Equal(0, handler.Calls);
  }

  [Fact]
  public async Task AcceptedUploadUrl_HasNoGraphAuthorizationAndSuppressesInstrumentation()
  {
    var handler = new RecordingHandler(HttpStatusCode.Created);
    using var transport = new GraphPreauthenticatedTransport(handler);
    using var content = new ByteArrayContent([1, 2, 3]);
    using var response = await transport.SendAsync(
      new Uri("https://tenant.sharepoint.com/upload?opaque=private"),
      HttpMethod.Put, content, CancellationToken.None);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Null(response.RequestMessage);
    Assert.Equal(1, handler.Calls);
    Assert.False(handler.HadAuthorization);
    Assert.True(handler.WasInstrumentationSuppressed);
  }

  [Fact]
  public async Task RedirectIsNotAcceptedAndSensitiveUrlDoesNotEscapeOnTransportError()
  {
    var redirect = new RecordingHandler(HttpStatusCode.TemporaryRedirect);
    using (var transport = new GraphPreauthenticatedTransport(redirect))
      await Assert.ThrowsAsync<OperationBlockedException>(() =>
        transport.SendAsync(new Uri("https://tenant.sharepoint.com/upload?opaque=private"),
          HttpMethod.Get, null, CancellationToken.None));
    using var failing = new GraphPreauthenticatedTransport(new ThrowingHandler());
    var error = await Assert.ThrowsAsync<OperationBlockedException>(() =>
      failing.SendAsync(new Uri("https://tenant.sharepoint.com/upload?opaque=private"),
        HttpMethod.Get, null, CancellationToken.None));
    Assert.DoesNotContain("opaque", error.ToString(), StringComparison.Ordinal);
  }

  private sealed class RecordingHandler(HttpStatusCode code) : HttpMessageHandler
  {
    public int Calls { get; private set; }
    public bool HadAuthorization { get; private set; }
    public bool WasInstrumentationSuppressed { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      Calls++;
      HadAuthorization = request.Headers.Authorization is not null;
      WasInstrumentationSuppressed = Sdk.SuppressInstrumentation;
      return Task.FromResult(new HttpResponseMessage(code));
    }
  }

  private sealed class ThrowingHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
      throw new HttpRequestException("https://tenant.sharepoint.com/upload?opaque=private");
  }
}
