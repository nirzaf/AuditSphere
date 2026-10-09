using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace AuditSphereOps.Api;

public static partial class ApiHost
{
  private static void ConfigureForwardedHeaders(WebApplicationBuilder builder)
  {
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
      options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                                 ForwardedHeaders.XForwardedHost |
                                 ForwardedHeaders.XForwardedProto;
      options.ForwardLimit = 1;

      foreach (var value in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
      {
        if (!IPAddress.TryParse(value, out var address))
          throw new InvalidOperationException("ForwardedHeaders:KnownProxies must contain valid IP addresses.");
        options.KnownProxies.Add(address);
      }
    });
  }
}
