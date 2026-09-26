using System.Security.Claims;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Testing;
using AuditSphereOps.Web.Authentication;

namespace AuditSphereOps.Api.Tests;

public sealed class TrustedActorResolverTests
{
  [Fact]
  [Trait("CaseId", "P1-API-01")]
  public async Task IdentityLookup_RequiresImmutableOidEvenWhenOtherSubjectsMatch()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("P1-API-01");
    var fixture = await PbcSeed.SeedAsync(pg);
    var resolver = new TrustedActorResolver(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var user = fixture.Staff;

    ClaimsPrincipal Principal(params Claim[] claims) =>
      new(new ClaimsIdentity(claims, "fixture"));

    var aliasesOnly = Principal(
      new Claim("tid", user.TenantId),
      new Claim("sub", user.Subject),
      new Claim(ClaimTypes.NameIdentifier, user.Subject),
      new Claim(TrustedActorResolver.SessionEpochClaimType, user.SessionEpoch.ToString()));
    Assert.Null(await resolver.ResolveAsync(aliasesOnly));

    var immutableIdentity = Principal(
      new Claim("tid", user.TenantId),
      new Claim("oid", user.Subject),
      new Claim("sub", "different-subject"),
      new Claim(ClaimTypes.Email, "changed@example.test"),
      new Claim(TrustedActorResolver.SessionEpochClaimType, user.SessionEpoch.ToString()));
    var actor = await resolver.ResolveAsync(immutableIdentity);
    Assert.NotNull(actor);
    Assert.Equal(user.Id, actor.UserId);

    var wrongTenant = Principal(
      new Claim("tid", "another-tenant"),
      new Claim("oid", user.Subject),
      new Claim(TrustedActorResolver.SessionEpochClaimType, user.SessionEpoch.ToString()));
    Assert.Null(await resolver.ResolveAsync(wrongTenant));
  }
}
