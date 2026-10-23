using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class ClientRelationshipRouteBindingApiTests
{
  [Fact]
  public async Task RelationshipRevocationIsBoundToTheStoredClientAtTheHttpBoundary()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("CLIENT-RELATIONSHIP-ROUTE-BINDING-API");
    var fixture = await PbcSeed.SeedAsync(pg);
    var relatedClientId = Guid.CreateVersion7();
    var unrelatedClientId = Guid.CreateVersion7();
    var relationshipId = Guid.CreateVersion7();

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, fixture.Admin, "Partner"));
      db.PracticeClients.AddRange(
        new PracticeClient
        {
          Id = relatedClientId,
          FirmId = fixture.FirmId,
          LegalName = "Synthetic Related Client",
          Status = "Active",
          CreatedAt = DateTimeOffset.UtcNow
        },
        new PracticeClient
        {
          Id = unrelatedClientId,
          FirmId = fixture.FirmId,
          LegalName = "Synthetic Unrelated Client",
          Status = "Active",
          CreatedAt = DateTimeOffset.UtcNow
        });
      db.ClientRelationships.Add(new ClientRelationship
      {
        Id = relationshipId,
        FirmId = fixture.FirmId,
        PrimaryClientId = fixture.ClientId,
        RelatedClientId = relatedClientId,
        RelationshipKind = ClientRelationshipKinds.Affiliate,
        OwnershipPercentage = 51.25m,
        CreatedByUserId = fixture.Admin.Id,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = fixture.Admin.Subject,
      ["DevelopmentIdentity:TenantId"] = fixture.Admin.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    var csrf = Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie")
      .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))
      .Split(';')[0]["XSRF-TOKEN=".Length..]);

    using var mismatchedRoute = await Delete(client, csrf,
      $"/api/ui/clients/{unrelatedClientId}/relationships/{relationshipId}",
      new { reason = "Wrong client route must not revoke this relationship." });
    Assert.Equal(HttpStatusCode.Forbidden, mismatchedRoute.StatusCode);
    using (var error = JsonDocument.Parse(await mismatchedRoute.Content.ReadAsStringAsync()))
      Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.ScopeDenied,
        error.RootElement.GetProperty("code").GetString());

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var retained = await db.ClientRelationships.AsNoTracking().SingleAsync(x => x.Id == relationshipId);
      Assert.Null(retained.RevokedAt);
      Assert.Null(retained.RevokedByUserId);
      Assert.Null(retained.RevocationReason);
    }

    using var correctRoute = await Delete(client, csrf,
      $"/api/ui/clients/{fixture.ClientId}/relationships/{relationshipId}",
      new { reason = "Ownership structure changed." });
    Assert.Equal(HttpStatusCode.OK, correctRoute.StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var revoked = await db.ClientRelationships.AsNoTracking().SingleAsync(x => x.Id == relationshipId);
      Assert.NotNull(revoked.RevokedAt);
      Assert.Equal(fixture.Admin.Id, revoked.RevokedByUserId);
      Assert.Equal("Ownership structure changed.", revoked.RevocationReason);
    }
  }

  [Fact]
  public async Task RelationshipCreationRejectsCrossFirmRelatedClientWithoutEffects()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("CLIENT-RELATIONSHIP-CROSS-FIRM-CREATE-API");
    var fixture = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    Assert.NotEqual(fixture.FirmId, foreign.FirmId);

    int existingRelationshipCount;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, fixture.Admin, "Partner"));
      await db.SaveChangesAsync();
      existingRelationshipCount = await db.ClientRelationships.CountAsync();
    }

    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = fixture.Admin.Subject,
      ["DevelopmentIdentity:TenantId"] = fixture.Admin.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    var csrf = Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie")
      .Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))
      .Split(';')[0]["XSRF-TOKEN=".Length..]);

    using var denied = await Post(client, csrf, $"/api/ui/clients/{fixture.ClientId}/relationships", new
    {
      relatedClientId = foreign.ClientId,
      relationshipKind = ClientRelationshipKinds.Affiliate,
      ownershipPercentage = (decimal?)null,
      effectiveFrom = (string?)null,
      effectiveTo = (string?)null,
      notes = (string?)null
    });
    Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    using (var error = JsonDocument.Parse(await denied.Content.ReadAsStringAsync()))
      Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.ScopeDenied,
        error.RootElement.GetProperty("code").GetString());

    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Equal(existingRelationshipCount, await proof.ClientRelationships.CountAsync());
  }

  private static Task<HttpResponseMessage> Post(HttpClient client, string csrf, string url, object body)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
    request.Headers.Add("X-XSRF-TOKEN", csrf);
    return client.SendAsync(request);
  }

  private static Task<HttpResponseMessage> Delete(HttpClient client, string csrf, string url, object body)
  {
    var request = new HttpRequestMessage(HttpMethod.Delete, url) { Content = JsonContent.Create(body) };
    request.Headers.Add("X-XSRF-TOKEN", csrf);
    return client.SendAsync(request);
  }
}
