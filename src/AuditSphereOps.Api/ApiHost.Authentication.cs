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
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;

namespace AuditSphereOps.Api;

public static partial class ApiHost
{
  private static AuthenticationConfiguration ConfigureAuthentication(WebApplicationBuilder builder, bool legacyPresentation)
  {
    var identity = builder.Configuration.GetSection("Identity");
    var tenantId = identity["TenantId"];
    var clientId = identity["ClientId"];
    var clientSecret = identity["ClientSecret"];
    var oidcConfigured = !string.IsNullOrWhiteSpace(tenantId) &&
                         !string.IsNullOrWhiteSpace(clientId) &&
                         !string.IsNullOrWhiteSpace(clientSecret);
    var developmentIdentityEnabled = builder.Configuration.GetValue<bool>("DevelopmentIdentity:Enabled");
    if (developmentIdentityEnabled && !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test"))
      throw new InvalidOperationException("Development identity is allowed only in Development or Test.");
    if (developmentIdentityEnabled && oidcConfigured)
      throw new InvalidOperationException("Development identity cannot be enabled with OIDC.");
    if (identity.Exists() && !oidcConfigured)
      throw new InvalidOperationException("Identity configuration requires TenantId, ClientId and ClientSecret together.");
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Test") && !oidcConfigured)
      throw new InvalidOperationException("Production identity requires configured OIDC.");
    ProductionDataProtection.Configure(builder, tenantId);
    var authentication = builder.Services.AddAuthentication(options =>
    {
      options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
      options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
      options.DefaultChallengeScheme = oidcConfigured ? "Entra" : CookieAuthenticationDefaults.AuthenticationScheme;
    }).AddCookie(options =>
    {
      options.LoginPath = "/auth/sign-in";
      options.Cookie.HttpOnly = true;
      options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test")
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
      options.Events.OnCheckSlidingExpiration = context =>
      {
        // Passive browser verification must not keep an otherwise idle login alive.
        if (context.Request.Path.Equals("/api/ui/session", StringComparison.OrdinalIgnoreCase))
          context.ShouldRenew = false;
        return Task.CompletedTask;
      };
      options.Events.OnRedirectToLogin = context =>
      {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
          context.Response.StatusCode = StatusCodes.Status401Unauthorized;
          return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
      };
      options.Events.OnRedirectToAccessDenied = context =>
      {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
          context.Response.StatusCode = StatusCodes.Status403Forbidden;
          return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
      };
    });
    if (oidcConfigured)
    {
      authentication.AddOpenIdConnect("Entra", options =>
      {
        options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        options.ClientId = clientId!;
        options.ClientSecret = clientSecret!;
        options.ResponseType = "code";
        options.CallbackPath = identity["CallbackPath"] ?? "/signin-oidc";
        // TrustedActorResolver intentionally consumes the immutable raw Entra oid/tid claims;
        // do not rewrite them into WS-* claim URIs before the tenant/object lookup.
        options.MapInboundClaims = false;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Events = new OpenIdConnectEvents
        {
          OnTokenValidated = async context =>
          {
            var tokenTenant = context.Principal?.FindFirst("tid")?.Value;
            var objectId = context.Principal?.FindFirst("oid")?.Value;
            if (!string.Equals(tokenTenant, tenantId, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParse(objectId, out _))
            {
              context.Fail("The identity is not from the configured workforce tenant or has no immutable object ID.");
              return;
            }

            var dbFactory = context.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<AuditSphereDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync(context.HttpContext.RequestAborted);
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
              x.TenantId == tokenTenant && x.Subject == objectId, context.HttpContext.RequestAborted);
            if (user is null && InitialAdministratorSignIn.AllowsUnmappedIdentity(
                  builder.Configuration, tokenTenant, objectId))
            {
              // Only proof-backed setup can use this Microsoft identity. Without an epoch claim,
              // TrustedActorResolver denies every protected application actor and role.
              if (!legacyPresentation && context.Properties is { } properties)
                properties.RedirectUri = "/ui/setup/microsoft365";
              return;
            }
            if (user is null || user.Disabled)
            {
              context.Fail("The identity is not assigned or is disabled.");
              return;
            }
            await db.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(x =>
              x.SetProperty(u => u.LastSignInAt, DateTimeOffset.UtcNow), context.HttpContext.RequestAborted);
            var claimsIdentity = (ClaimsIdentity)context.Principal!.Identity!;
            foreach (var existing in claimsIdentity.FindAll(TrustedActorResolver.SessionEpochClaimType).ToList())
              claimsIdentity.RemoveClaim(existing);
            claimsIdentity.AddClaim(new Claim(
              TrustedActorResolver.SessionEpochClaimType,
              user.SessionEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture)));
          },
          OnRemoteFailure = async context =>
          {
            context.HandleResponse();
            if (context.Failure?.GetBaseException().Message == "The identity is not assigned or is disabled.")
            {
              context.Response.Redirect("/auth/access-not-assigned");
              return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Microsoft sign-in could not be completed. Please try again.");
          }
        };
      });
    }
    builder.Services.AddAuthorization();
    builder.Services.AddScoped<TrustedActorResolver>();


    return new(oidcConfigured, developmentIdentityEnabled);
  }

  private sealed record AuthenticationConfiguration(bool OidcConfigured, bool DevelopmentIdentityEnabled);
}
