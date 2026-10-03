using System.Buffers;
using System.Security.Claims;
using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Diagnostics;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.Diagnostics;
using AuditSphereOps.Api.Ui;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Api;

public static partial class ApiHost
{
  private static void MapAuthenticationEndpoints(WebApplication app, AuthenticationConfiguration identity, bool legacyPresentation)
  {
    var oidcConfigured = identity.OidcConfigured;
    var developmentIdentityEnabled = identity.DevelopmentIdentityEnabled;
    if (oidcConfigured)
    {
      app.MapGet("/auth/sign-in", (HttpContext http, string? returnUrl,
        bool selectAccount = false, bool reauthenticate = false) =>
      {
        return Results.Challenge(new OpenIdConnectChallengeProperties
        {
          RedirectUri = LocalDestination(returnUrl),
          Prompt = reauthenticate ? "login" : selectAccount ? "select_account" : null
        }, ["Entra"]);
      });
    }
    else if (developmentIdentityEnabled)
    {
      var subject = app.Configuration["DevelopmentIdentity:Subject"];
      var identityTenantId = app.Configuration["DevelopmentIdentity:TenantId"];
      if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(identityTenantId))
        throw new InvalidOperationException("Development identity requires Subject and TenantId.");

      app.MapGet("/auth/sign-in", async (HttpContext http, IDbContextFactory<AuditSphereDbContext> dbFactory, string? returnUrl) =>
      {
        await using var db = await dbFactory.CreateDbContextAsync(http.RequestAborted);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Subject == subject && x.TenantId == identityTenantId, http.RequestAborted);
        if (user is null || user.Disabled)
          return Results.Problem("The configured development identity is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        await db.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(x =>
          x.SetProperty(u => u.LastSignInAt, DateTimeOffset.UtcNow), http.RequestAborted);

        var claims = new[]
        {
          new Claim("oid", user.Subject),
          new Claim("tid", user.TenantId),
          new Claim(TrustedActorResolver.SessionEpochClaimType,
            user.SessionEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture)),
          new Claim(ClaimTypes.Name, user.DisplayName),
          new Claim(ClaimTypes.Email, user.Email)
        };
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
          new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        return Results.Redirect(LocalDestination(returnUrl));
      });
    }
    if (oidcConfigured || developmentIdentityEnabled)
    {
      app.MapGet("/auth/m365-consent/callback", async (HttpContext http,
        TrustedActorResolver actorResolver, IDbContextFactory<AuditSphereDbContext> dbFactory,
        IConfiguration configuration, IMicrosoftDirectoryReader directoryReader, CancellationToken ct) =>
      {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!http.RequestServices.GetRequiredService<TenantAdministrationSettings>().ConsentEnabled)
          return Results.NotFound();
        var consentPage = !legacyPresentation || configuration.GetValue<bool>("AngularUi:Enabled")
          ? AngularRouteOwnership.Destination(configuration, "/app/administration/microsoft365/tenant-connection") : "/app/administration/microsoft365/tenant-connection";
        var query = http.Request.Query;
        if (query["state"].Count != 1 || query["tenant"].Count > 1 ||
            query["admin_consent"].Count > 1 || query["error"].Count > 1)
          return Results.Redirect(consentPage + "?result=blocked");
        var actor = await actorResolver.ResolveAsync(http.User, ct);
        if (actor is null) return Results.Forbid();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var result = await TenantConsentService.CompleteCallbackAsync(db, actor,
          query["state"].ToString(), query["tenant"].ToString(),
          string.Equals(query["admin_consent"].ToString(), "True", StringComparison.OrdinalIgnoreCase),
          !string.IsNullOrWhiteSpace(query["error"].ToString()), DateTimeOffset.UtcNow, ct);
        if (!result.Succeeded)
          return Results.Redirect(consentPage + "?result=blocked");
        if (configuration.GetValue<bool>("DirectoryReader:Enabled") &&
            Guid.TryParse(configuration["TenantConsent:ClientId"], out var consentClientId) &&
            Guid.TryParse(configuration["DirectoryReader:ClientId"], out var readerClientId) &&
            consentClientId == readerClientId)
        {
          // Capability check only. Microsoft does not identify the consent grantor in this
          // callback; the nonce-bound identity leg below does.
          await DirectoryCapabilityVerificationService.VerifyAsync(db, actor, directoryReader,
            configuration["Identity:TenantId"] ?? string.Empty,
            readerClientId.ToString("D"), DateTimeOffset.UtcNow, ct,
            expectedDraftId: result.Value!.SetupDraftId);
        }
        // The consent callback cannot identify who granted consent; a nonce-bound sign-in must follow.
        var verifier = http.RequestServices.GetRequiredService<IMicrosoftTenantConsentVerifier>();
        if (!verifier.IsConfigured)
          return Results.Redirect(consentPage + "?result=returned");
        var challenge = await TenantConsentService.BeginIdentityVerificationAsync(db, actor,
          result.Value!.AttemptId, DateTimeOffset.UtcNow, ct);
        if (!challenge.Succeeded)
          return Results.Redirect(consentPage + "?result=blocked");
        return Results.Redirect(verifier.BuildIdentityChallenge(challenge.Value!.TenantId,
          challenge.Value.State, challenge.Value.Nonce).ToString());
      });
      TenantAdministrationComposition.MapEndpoints(app, nativeUi: !legacyPresentation);
      app.MapGet("/auth/landing", async (HttpContext http, TrustedActorResolver actorResolver,
        IDbContextFactory<AuditSphereDbContext> dbFactory, CancellationToken ct) =>
      {
        if (http.User.Identity?.IsAuthenticated != true)
          return Results.Redirect(legacyPresentation ? "/app" : AngularRouteOwnership.Destination(app.Configuration, "/app"));
        var subject = http.User.FindFirstValue("oid");
        var tenant = http.User.FindFirstValue("tid");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenant))
          return Results.Redirect("/auth/access-not-assigned");
        var actor = await actorResolver.ResolveAsync(http.User, ct);
        if (actor is null || actor.Roles.Count == 0)
          return Results.Redirect("/auth/access-not-assigned");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == actor.UserId && x.FirmId == actor.FirmId && x.TenantId == tenant && x.Subject == subject, ct);
        if (user is null)
          return Results.Redirect("/auth/access-not-assigned");
        return user.UserKind.Equals("Client", StringComparison.OrdinalIgnoreCase) ||
               actor.Roles.Contains("ClientUser", StringComparer.OrdinalIgnoreCase)
          ? Results.Redirect(legacyPresentation ? "/portal" : AngularRouteOwnership.Destination(app.Configuration, "/portal"))
          : Results.Redirect(legacyPresentation ? "/app" : AngularRouteOwnership.Destination(app.Configuration, "/app"));
      });
      app.MapGet("/auth/sign-out", async (HttpContext http) =>
      {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/");
      });
    }

    if (!legacyPresentation)
    {
      app.MapGet("/", () => Results.Redirect(AngularRouteOwnership.Destination(app.Configuration, "/app")));
      app.MapGet("/auth/access-not-assigned", (HttpContext http) =>
      {
        http.Response.Headers.CacheControl = "no-store";
        const string page = """
<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><title>AuditSphere access not assigned</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>body{font-family:system-ui,sans-serif;margin:0;display:grid;place-items:center;min-height:100vh;background:#f6f7f9;color:#1c1f23}
main{max-width:34rem;padding:2rem;text-align:center}a.button{display:inline-block;margin-top:1rem;padding:.6rem 1.2rem;border:1px solid #5b6470;border-radius:.4rem;text-decoration:none}</style></head>
<body><main><h1>Access not assigned</h1>
<p>Your Microsoft identity has no current AuditSphere access. Contact an authorized AuditSphere administrator to be granted a role and scope.</p>
<p><a class="button" href="/auth/sign-out">Sign out</a></p></main></body>
</html>
""";
        return Results.Text(page, "text/html; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);
      });
      if (!oidcConfigured && !developmentIdentityEnabled)
        app.MapGet("/auth/sign-in", () => Results.Problem("Microsoft sign-in is not configured for this deployment.", statusCode: 503));
    }
    string LocalDestination(string? returnUrl) =>
      !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/') &&
      !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.Contains('\\') && !returnUrl.Any(char.IsControl)
        ? returnUrl : legacyPresentation ? "/auth/landing" : AngularRouteOwnership.Destination(app.Configuration, "/app");
  }
}
