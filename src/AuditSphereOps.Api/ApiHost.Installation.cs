using System.Security.Claims;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Api.Ui;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api;

public static partial class ApiHost
{
  public sealed record InstallationBootstrapInput(string Proof, bool Reviewed);

  internal static Microsoft365InstallationOptions InstallationOptions(IConfiguration config) => new(
    Guid.TryParse(config["Setup:FirmId"] ?? config["Application:FirmId"], out var firmId) ? firmId : Guid.Empty,
    config["Setup:InstallationId"] ?? config["Application:InstallationId"] ?? string.Empty,
    config["Setup:BootstrapProofHash"] ?? string.Empty,
    config["Setup:InitialAdministratorTenantId"] ?? string.Empty, config["Setup:InitialAdministratorObjectId"] ?? string.Empty);

  private static void MapInstallationEndpoints(WebApplication app)
  {
    // JSON endpoints challenge the established cookie; interactive OIDC starts only at /auth/sign-in.
    var group = app.MapGroup("/api/setup")
      .RequireAuthorization(HttpBoundary.HttpPolicies.AuthenticatedSession());
    group.AddEndpointFilter(async (context, next) =>
    {
      context.HttpContext.Response.Headers.CacheControl = "no-store";
      context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
      if (context.HttpContext.User.FindAll("tid").Count() != 1 || context.HttpContext.User.FindAll("oid").Count() != 1)
        return UiEndpoints.Failure("scope.denied", "Initial installation setup is not available to this identity.", 403);
      try { return await next(context); }
      catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
      catch (Exception error)
      {
        app.Logger.LogError("Installation request failed: {ErrorType}; diagnostic {DiagnosticId}", error.GetType().Name, context.HttpContext.TraceIdentifier);
        return Results.Json(new { code = "setup.unavailable", message = "Setup could not be confirmed. Sign in again to check the recorded state.",
          correlationId = context.HttpContext.TraceIdentifier }, statusCode: 503);
      }
    });
    group.MapGet("/session", async (HttpContext http, IConfiguration config, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await Microsoft365InstallationService.StatusAsync(db, InstallationOptions(config),
        http.User.FindFirstValue("tid") ?? string.Empty, http.User.FindFirstValue("oid") ?? string.Empty, http.RequestAborted);
      if (!result.Succeeded) return UiEndpoints.Failure(result.ErrorCode, result.Message);
      var tokens = csrf.GetAndStoreTokens(http);
      http.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions { HttpOnly = false, SameSite = SameSiteMode.Strict,
        Path = "/", Secure = http.Request.IsHttps || !(app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test")) });
      return UiEndpoints.UiResult(result.Value);
    });
    group.MapPost("/bootstrap", async (InstallationBootstrapInput input, HttpContext http, IConfiguration config,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return UiEndpoints.Failure("csrf.invalid", "Refresh setup and review again.", 403); }
      var identity = new InstallationMicrosoftIdentity(http.User.FindFirstValue("tid") ?? string.Empty, http.User.FindFirstValue("oid") ?? string.Empty,
        http.User.FindFirstValue("email") ?? http.User.FindFirstValue("preferred_username") ?? http.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
        http.User.FindFirstValue("name") ?? http.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await Microsoft365InstallationService.BootstrapAsync(db, InstallationOptions(config), identity,
        input.Proof ?? string.Empty, input.Reviewed, DateTimeOffset.UtcNow, http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { completed = true, requiresFreshSignIn = true }) : UiEndpoints.Failure(result.ErrorCode, result.Message);
    });
  }
}
