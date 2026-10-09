using AuditSphereOps.Api.Contracts;
using AuditSphereOps.Api.Diagnostics;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Api.Ui;

namespace AuditSphereOps.Api;

/// <summary>Shared secure HTTP composition for the ASP.NET Core API and Angular SPA host.</summary>
public static partial class ApiHost
{
  public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? addPresentation = null,
    Action<WebApplication>? mapPresentation = null, bool legacyPresentation = false)
  {
    var builder = WebApplication.CreateBuilder(args);
    EnsureLegacyPresentationEnvironment(legacyPresentation, builder.Environment.EnvironmentName,
      builder.Configuration.GetValue<bool>("LegacyPresentation:Enabled"));
    builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
    if (legacyPresentation && (builder.Environment.IsEnvironment("Test") || builder.Environment.IsDevelopment()))
      builder.WebHost.UseStaticWebAssets();
    ConfigureObservability(builder, legacyPresentation);
    builder.Services.AddHttpContextAccessor();
    addPresentation?.Invoke(builder);
    var connection = ConfigurePersistence(builder, legacyPresentation);
    var identity = ConfigureAuthentication(builder, legacyPresentation);
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
    app.UseMiddleware<RequestBodyLimitMiddleware>();

    if (!app.Environment.IsDevelopment())
    {
      if (legacyPresentation)
      {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
      }
      else
      {
        app.UseExceptionHandler(handler => handler.Run(async http =>
        {
          http.Response.Headers.CacheControl = "no-store";
          await Results.Problem("The request could not be completed. Use the diagnostic ID when contacting your administrator.",
            statusCode: 500, extensions: new Dictionary<string, object?> { ["diagnosticId"] = http.Response.Headers["X-Correlation-Id"].ToString() }).ExecuteAsync(http);
        }));
      }
      app.UseHsts();
    }

    app.UseHttpsRedirection();
    if (legacyPresentation) app.UseStaticFiles();
    app.UseAuthentication();
    app.UseAuthorization();
    // After authentication so partitions can use the resolved identity; before endpoints.
    app.UseRateLimiter();
    app.UseAntiforgery();
    app.MapHealthChecks("/health/live", new() { Predicate = r => r.Name == "self" });
    app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });

    MapAuthenticationEndpoints(app, identity, legacyPresentation);
    MapDocumentEndpoints(app);
    MapInstallationEndpoints(app);
    ApiContract.Map(app);
    app.MapUiEndpoints(legacyPresentation);
    mapPresentation?.Invoke(app);
    return app;
  }

  internal static void EnsureLegacyPresentationEnvironment(bool legacyPresentation, string environmentName,
    bool enabled = false)
  {
    if (legacyPresentation && !string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase) && !enabled)
      throw new InvalidOperationException("The Blazor rollback host is disabled. Set LegacyPresentation:Enabled=true only for an explicitly approved rollback deployment.");
  }
}
