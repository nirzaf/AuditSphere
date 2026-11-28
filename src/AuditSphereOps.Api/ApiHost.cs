using AuditSphereOps.Api.Contracts;
using AuditSphereOps.Api.Diagnostics;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Api.Ui;

namespace AuditSphereOps.Api;

/// <summary>Shared secure HTTP composition for the ASP.NET Core API and Angular SPA host.</summary>
public static partial class ApiHost
{
  public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? addPresentation = null,
    Action<WebApplication>? mapPresentation = null)
  {
    var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
    ConfigureObservability(builder);
    builder.Services.AddHttpContextAccessor();
    addPresentation?.Invoke(builder);
    var connection = ConfigurePersistence(builder);
    var identity = ConfigureAuthentication(builder);
    ConfigureProviders(builder, connection, identity.OidcConfigured);
    ConfigureHealth(builder, connection);
    ConfigureRateLimiting(builder);
    ConfigureRequestLimits(builder);
    ConfigureSecurityHeaders(builder);
    ConfigureForwardedHeaders(builder);
    ConfigureApiContract(builder);
    var app = builder.Build();
    app.UseForwardedHeaders();
    app.UseMiddleware<RequestCorrelationMiddleware>();
    app.UseMiddleware<SecurityHeadersMiddleware>();

    if (!app.Environment.IsDevelopment())
    {
      app.UseExceptionHandler(handler => handler.Run(async http =>
      {
        http.Response.Headers.CacheControl = "no-store";
        await Results.Problem("The request could not be completed. Use the diagnostic ID when contacting your administrator.",
          statusCode: 500, extensions: new Dictionary<string, object?> { ["diagnosticId"] = http.Response.Headers["X-Correlation-Id"].ToString() }).ExecuteAsync(http);
      }));
      app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    // After authentication so partitions can use the resolved identity; before body buffering and endpoints.
    app.UseRateLimiter();
    app.UseMiddleware<RequestBodyLimitMiddleware>();
    app.UseAntiforgery();
    app.MapHealthChecks("/health/live", new() { Predicate = r => r.Name == "self" });
    app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });

    MapAuthenticationEndpoints(app, identity);
    MapDocumentEndpoints(app);
    MapInstallationEndpoints(app);
    ApiContract.Map(app);
    app.MapUiEndpoints();
    mapPresentation?.Invoke(app);
    return app;
  }
}
