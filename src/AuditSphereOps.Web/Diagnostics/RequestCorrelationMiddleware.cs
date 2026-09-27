using System.Diagnostics;
using Serilog.Context;

namespace AuditSphereOps.Web.Diagnostics;

/// <summary>Assigns a server-owned diagnostic ID without reflecting caller headers.</summary>
internal sealed class RequestCorrelationMiddleware(RequestDelegate next)
{
  public async Task InvokeAsync(HttpContext context)
  {
    var correlationId = Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Correlation-Id"] = correlationId;
    Activity.Current?.SetTag("auditsphere.correlation_id", correlationId);
    using (LogContext.PushProperty("CorrelationId", correlationId))
      await next(context);
  }
}
