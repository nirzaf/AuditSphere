using AuditSphereOps.Api.Diagnostics;
using AuditSphereOps.Api.Ui;

namespace AuditSphereOps.Api;

/// <summary>Shared secure HTTP composition for the ASP.NET Core API and Angular SPA host.</summary>
public static partial class ApiHost
{
  public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? addPresentation = null,
    Action<WebApplication>? mapPresentation = null, bool legacyPresentation = false)
  {
    var builder = WebApplication.CreateBuilder(args);
    EnsureLegacyPresentationEnvironment(legacyPresentation, builder.Environment.EnvironmentName);
    builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");
    ConfigureObservability(builder, legacyPresentation);
    builder.Services.AddHttpContextAccessor();
    addPresentation?.Invoke(builder);
    var connection = ConfigurePersistence(builder, legacyPresentation);
    var identity = ConfigureAuthentication(builder, legacyPresentation);
    ConfigureProviders(builder, connection, identity.OidcConfigured);
    ConfigureHealth(builder, connection);
    var app = builder.Build();
    app.UseMiddleware<RequestCorrelationMiddleware>();

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
    app.UseAntiforgery();
    app.MapHealthChecks("/health/live", new() { Predicate = r => r.Name == "self" });
    app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });

    MapAuthenticationEndpoints(app, identity, legacyPresentation);
    MapDocumentEndpoints(app);
    MapInstallationEndpoints(app);
    app.MapUiEndpoints(legacyPresentation);
    mapPresentation?.Invoke(app);
    return app;
  }

  internal static void EnsureLegacyPresentationEnvironment(bool legacyPresentation, string environmentName)
  {
    if (legacyPresentation)
      throw new InvalidOperationException("The Blazor presentation host has been retired. Run the Angular UI through AuditSphereOps.Api.");
  }
}
