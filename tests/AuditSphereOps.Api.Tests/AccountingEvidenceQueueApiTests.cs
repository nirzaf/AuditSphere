using System.Net;
using System.Text.Json;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class AccountingEvidenceQueueApiTests
{
  [Fact]
  public async Task HttpQueueDoesNotExposeSiblingEvidenceThroughAnExpiredBroadGrant()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-EVIDENCE-QUEUE-ACCESS");
    var (f, _) = await AccountingEvidenceQueueSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var expired = PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer");
      expired.GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
      expired.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
      db.RoleGrants.Add(expired); await db.SaveChangesAsync();
    }
    using var factory = Factory(pg, f.Staff);
    using var client = factory.CreateClient();
    await client.GetAsync("/auth/sign-in");
    // Expiration reconciliation invalidates the original cookie before any protected rows.
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/accounting/evidence")).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True(await db.RoleGrants.AnyAsync(g => g.UserId == f.Staff.Id && g.ClientId == null && g.RevokedAt != null));
    await client.GetAsync("/auth/sign-in");
    using var response = await client.GetAsync("/api/ui/accounting/evidence");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var row = Assert.Single(body.RootElement.EnumerateArray());
    Assert.Equal("SYNTHETIC-A", row.GetProperty("reference").GetString());
    Assert.DoesNotContain("SYNTHETIC-B", body.RootElement.GetRawText());
    Assert.True(response.Headers.CacheControl?.NoStore);
    using var portalFactory = Factory(pg, f.Client);
    using var portal = portalFactory.CreateClient();
    await portal.GetAsync("/auth/sign-in");
    Assert.Equal(HttpStatusCode.Forbidden, (await portal.GetAsync("/api/ui/accounting/evidence")).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Users.Where(u => u.Id == f.Staff.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/accounting/evidence")).StatusCode);
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AuditSphereOps.Domain.Security.AppUser user) =>
    new(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:TenantId"] = user.TenantId, ["DevelopmentIdentity:Subject"] = user.Subject,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
}
