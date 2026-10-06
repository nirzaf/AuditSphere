using System.Text;
using System.Threading.RateLimiting;
using AuditSphereOps.Api.Contracts;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Api.Ui;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AuditSphereOps.Api;

public static partial class ApiHost
{
  /// <summary>
  /// Native ASP.NET Core rate limiting for the HTTP boundary. The global chained limiter
  /// classifies every /api and /auth request itself (endpoint markers, method, path) and no-ops
  /// everything else; static assets, health probes and the SPA shell are never limited.
  /// </summary>
  private static void ConfigureRateLimiting(WebApplicationBuilder builder)
  {
    var rateOptions = builder.Configuration.GetSection(ApiRateLimitOptions.SectionName).Get<ApiRateLimitOptions>() ?? new();
    builder.Services.AddRateLimiter(options =>
    {
      options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
      options.OnRejected = (context, cancellationToken) =>
      {
        var http = context.HttpContext;
        http.Response.Headers.RetryAfter = rateOptions.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("AuditSphereOps.Api.HttpBoundary")
          .LogWarning("Rate limit rejected a {RateClass} request for {Endpoint}; diagnostic {DiagnosticId}",
            ApiRateLimiter.ResolveClass(http), (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? http.Request.Path.Value,
            http.TraceIdentifier);
        return new ValueTask(http.Response.WriteAsJsonAsync(new ApiError("request.throttled",
          "Too many requests. Wait briefly and try again.", http.TraceIdentifier), cancellationToken));
      };
      options.GlobalLimiter = ApiRateLimiter.CreateGlobalLimiter(rateOptions);
    });
  }

  /// <summary>
  /// Explicit request-size policy: a host-level ceiling for every body plus a JSON command
  /// ceiling enforced before business processing. Host limits are declared even though the
  /// in-memory test server does not exercise Kestrel's enforcement.
  /// </summary>
  private static void ConfigureRequestLimits(WebApplicationBuilder builder)
  {
    var limits = builder.Configuration.GetSection(RequestBodyLimitOptions.SectionName).Get<RequestBodyLimitOptions>() ?? new();
    builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = limits.MaxRequestBodyBytes);
    builder.Services.AddSingleton(limits);
  }

  /// <summary>Canonical OpenAPI 3.1 contract generation from the actual endpoints; exposed outside production.</summary>
  private static void ConfigureApiContract(WebApplicationBuilder builder) => ApiContract.Configure(builder);

  /// <summary>
  /// Builds the centralized browser-security header policy. When the approved Angular build is
  /// available, inline script hashes are computed from its index.html so script-src stays strict
  /// without 'unsafe-inline' or 'unsafe-eval'.
  /// </summary>
  private static void ConfigureSecurityHeaders(WebApplicationBuilder builder)
  {
    var options = new SecurityHeaderOptions
    {
      UpgradeInsecureRequests = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test")
    };
    if (AngularRouteOwnership.ResolveBuildRoot(builder.Configuration, builder.Environment) is { } root &&
        File.Exists(Path.Combine(root, "index.html")))
      options.InlineScriptHashes = SecurityHeaderOptions.ComputeInlineScriptHashes(
        File.ReadAllText(Path.Combine(root, "index.html"), Encoding.UTF8));
    builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
  }
}
