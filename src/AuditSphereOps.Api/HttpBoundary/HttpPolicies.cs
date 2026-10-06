using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace AuditSphereOps.Api.HttpBoundary;

/// <summary>
/// Shared HTTP-boundary authorization policies. HTTP authorization only establishes that the
/// request belongs to an authenticated AuditSphere browser session; TrustedActorResolver,
/// AuthorizationDecision, RoleGrant scope checks and every business gate remain the authority
/// of the Application layer and are never replaced or weakened here.
/// </summary>
public static class HttpPolicies
{
  /// <summary>Requires an established cookie session and nothing more: no role, scope or firm decision is made at this boundary.</summary>
  public static AuthorizationPolicy AuthenticatedSession() =>
    new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme)
      .RequireAuthenticatedUser()
      .Build();
}
