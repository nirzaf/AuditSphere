using System.Net;
using AuditSphereOps.Application.Operations;
using OpenTelemetry;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>
/// Restricted transport for Graph-issued upload/download URLs. They are bearer-equivalent
/// capabilities: never attach a Graph token, follow a redirect, or emit the URL to telemetry.
/// </summary>
public sealed class GraphPreauthenticatedTransport : IDisposable
{
  private readonly HttpClient _http;

  public GraphPreauthenticatedTransport() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }

  internal GraphPreauthenticatedTransport(HttpMessageHandler handler) =>
    _http = new HttpClient(handler, disposeHandler: true);

  public async Task<HttpResponseMessage> SendAsync(
    Uri url, HttpMethod method, HttpContent? content, CancellationToken ct)
  {
    if (!Allowed(url) || method != HttpMethod.Get && method != HttpMethod.Put && method != HttpMethod.Delete)
      throw new OperationBlockedException("provider-capability-url-rejected", authorization: true);
    using var request = new HttpRequestMessage(method, url) { Content = content };
    try
    {
      using var suppressed = SuppressInstrumentationScope.Begin();
      var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
      if ((int)response.StatusCode is >= 300 and < 400)
      {
        response.Dispose();
        throw new OperationBlockedException("provider-capability-redirect-rejected", authorization: true);
      }
      if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
      {
        var delay = response.Headers.RetryAfter?.Delta;
        response.Dispose();
        throw new SafeRetryException(delay);
      }
      response.RequestMessage = null;
      return response;
    }
    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException ||
      ex is TaskCanceledException && !ct.IsCancellationRequested)
    {
      // Transport exceptions can include the capability URL. Never propagate their text.
      throw new OperationBlockedException("provider-capability-transport-failed");
    }
  }

  private static bool Allowed(Uri url)
  {
    if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps ||
        !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Fragment) ||
        url.Port != 443) return false;
    var host = url.IdnHost;
    return host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase) ||
      host.EndsWith(".1drv.com", StringComparison.OrdinalIgnoreCase);
  }

  public void Dispose() => _http.Dispose();
}
