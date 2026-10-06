using Microsoft.Extensions.Options;

namespace AuditSphereOps.Api.HttpBoundary;

/// <summary>
/// Applies the centralized browser-security header policy and consistent cache behavior.
/// Security headers are set on every response; CSP applies only to browser documents. Sensitive
/// authenticated API and auth responses are forced to no-store unless the handler set an
/// explicit (never weaker) cache policy; fingerprinted static assets are handled by the static
/// file options, not here.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecurityHeaderOptions> options)
{
  public async Task InvokeAsync(HttpContext context)
  {
    var response = context.Response;
    response.Headers["X-Content-Type-Options"] = "nosniff";
    response.Headers["Referrer-Policy"] = "no-referrer";
    response.Headers["Permissions-Policy"] = options.Value.PermissionsPolicy;
    response.Headers["X-Frame-Options"] = "DENY";
    response.OnStarting(() =>
    {
      if (response.ContentType is { } contentType && contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        response.Headers.ContentSecurityPolicy = options.Value.EffectiveContentSecurityPolicy;
      if ((context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/auth")) &&
          !response.Headers.ContainsKey("Cache-Control"))
        response.Headers.CacheControl = "no-store";
      return Task.CompletedTask;
    });
    await next(context);
  }
}
