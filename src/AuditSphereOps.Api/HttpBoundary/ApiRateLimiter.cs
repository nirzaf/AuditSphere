using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuditSphereOps.Api.HttpBoundary;

/// <summary>Configurable per-class budgets; defaults protect capacity without slowing ordinary navigation.</summary>
public sealed class ApiRateLimitOptions
{
  public const string SectionName = "HttpBoundary:RateLimit";

  public ClassLimit NormalRead { get; set; } = new(240, 60);
  public ClassLimit Command { get; set; } = new(60, 60);
  public ClassLimit Search { get; set; } = new(30, 60);
  public ClassLimit Export { get; set; } = new(20, 60);
  public ClassLimit FileUpload { get; set; } = new(120, 60);
  // Anonymous traffic is IP-partitioned and therefore shared behind office NAT or a reverse
  // proxy; 60/minute stays far below read budgets while tolerating legitimate shared origins.
  public ClassLimit Authentication { get; set; } = new(60, 60);
  // Administrative M365 verification and consent workflows legitimately burst during setup
  // wizards; 30/minute keeps them the most conservative authenticated class.
  public ClassLimit M365Administration { get; set; } = new(30, 60);

  /// <summary>Per-identity concurrent export generations; excess exports queue briefly instead of stacking.</summary>
  public int ExportConcurrency { get; set; } = 4;

  /// <summary>Per-identity concurrent upload streams.</summary>
  public int UploadConcurrency { get; set; } = 4;

  /// <summary>How long a rejected client should wait before retrying, reported as Retry-After seconds.</summary>
  public int RetryAfterSeconds { get; set; } = 15;

  public sealed record ClassLimit(int PermitLimit, int WindowSeconds);
}

/// <summary>
/// The single HTTP-boundary rate limiter, composed as a chained global limiter: a per-identity
/// sliding-window stage followed by a per-identity concurrency stage for resource-intensive
/// classes. .NET 10 removed partition factories returning chained limiters, so the chain lives
/// at the global level and every stage returns a no-op partition for traffic outside its class.
/// Only /api and /auth paths are limited; static assets, health probes and the SPA shell are not.
/// The cost class is resolved per request from explicit endpoint metadata (exports, uploads) or
/// from the HTTP method, and the partition key is the immutable authenticated identity (tid/oid)
/// with a bounded remote-IP fallback for anonymous authentication traffic. Browser-supplied firm,
/// client or engagement identifiers are never used as rate-limit identity.
/// </summary>
public static class ApiRateLimiter
{
  public static PartitionedRateLimiter<HttpContext> CreateGlobalLimiter(ApiRateLimitOptions options) =>
    PartitionedRateLimiter.CreateChained(WindowStage(options), ConcurrencyStage(options));

  internal static PartitionedRateLimiter<HttpContext> WindowStage(ApiRateLimitOptions options) =>
    PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
      if (!IsLimited(http)) return RateLimitPartition.GetNoLimiter("unassigned");
      var rateClass = ResolveClass(http);
      return RateLimitPartition.GetSlidingWindowLimiter($"{rateClass}:{ResolveIdentity(http)}",
        _ => WindowOptions(limitFor(rateClass, options)));
    });

  internal static PartitionedRateLimiter<HttpContext> ConcurrencyStage(ApiRateLimitOptions options) =>
    PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
      if (!IsLimited(http)) return RateLimitPartition.GetNoLimiter("unassigned");
      var rateClass = ResolveClass(http);
      if (rateClass is not (ApiRateClass.Export or ApiRateClass.FileUpload))
        return RateLimitPartition.GetNoLimiter("no-concurrency");
      var permits = rateClass == ApiRateClass.Export ? options.ExportConcurrency : options.UploadConcurrency;
      return RateLimitPartition.GetConcurrencyLimiter($"{rateClass}:{ResolveIdentity(http)}", _ => new ConcurrencyLimiterOptions
      {
        PermitLimit = permits,
        QueueLimit = 4,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
      });
    });

  /// <summary>Only the API and authentication boundary is throttled; shared infrastructure paths are not.</summary>
  internal static bool IsLimited(HttpContext http) =>
    http.Request.Path.StartsWithSegments("/api") || http.Request.Path.StartsWithSegments("/auth");

  internal static ApiRateClass ResolveClass(HttpContext httpContext)
  {
    if (httpContext.GetEndpoint()?.Metadata.GetMetadata<ApiRateClassAttribute>() is { } marker)
      return marker.RateClass;
    var path = httpContext.Request.Path;
    if (path.StartsWithSegments("/auth/m365-consent") ||
        path.StartsWithSegments("/api/ui/administration/microsoft365") ||
        path.StartsWithSegments("/api/ui/administration/directory"))
      return ApiRateClass.M365Administration;
    if (path.StartsWithSegments("/auth/sign-in"))
      return ApiRateClass.Authentication;
    var method = httpContext.Request.Method;
    return HttpMethods.IsGet(method) || HttpMethods.IsHead(method) ? ApiRateClass.NormalRead : ApiRateClass.Command;
  }

  internal static string ResolveIdentity(HttpContext httpContext)
  {
    var user = httpContext.User;
    if (user.Identity?.IsAuthenticated == true)
    {
      var objectId = user.FindFirstValue("oid");
      var tenantId = user.FindFirstValue("tid");
      return !string.IsNullOrWhiteSpace(objectId) && !string.IsNullOrWhiteSpace(tenantId)
        ? $"user:{tenantId}:{objectId}"
        : $"user:{user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unresolved"}";
    }
    return $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unspecified"}";
  }

  private static ApiRateLimitOptions.ClassLimit limitFor(ApiRateClass rateClass, ApiRateLimitOptions options) => rateClass switch
  {
    ApiRateClass.Command => options.Command,
    ApiRateClass.Search => options.Search,
    ApiRateClass.Export => options.Export,
    ApiRateClass.FileUpload => options.FileUpload,
    ApiRateClass.Authentication => options.Authentication,
    ApiRateClass.M365Administration => options.M365Administration,
    _ => options.NormalRead
  };

  private static SlidingWindowRateLimiterOptions WindowOptions(ApiRateLimitOptions.ClassLimit limit) => new()
  {
    PermitLimit = limit.PermitLimit,
    Window = TimeSpan.FromSeconds(limit.WindowSeconds),
    SegmentsPerWindow = 6,
    QueueLimit = 0,
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
  };
}
