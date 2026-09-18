using AuditSphereOps.Application.Abstractions;
using Microsoft.AspNetCore.Components.Authorization;

namespace AuditSphereOps.Web.Authentication;

public sealed class CurrentActorResolver(
  AuthenticationStateProvider authentication,
  TrustedActorResolver resolver)
{
  public async Task<ActorContext?> ResolveAsync(CancellationToken ct = default)
  {
    return await resolver.ResolveAsync((await authentication.GetAuthenticationStateAsync()).User, ct);
  }
}
