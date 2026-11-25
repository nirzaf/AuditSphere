using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class FirmWidePartnerLookupTests
{
  [Fact]
  public async Task OnlyActiveFirmWideStaffPartnersAreReturned_AndAdminAuthorityIsCurrent()
  {
    await using var fixture = await TenantAdministrationFixture.CreateAsync();
    await using var db = fixture.Db();
    var now = DateTimeOffset.UtcNow;

    Guid Add(string name, string kind = "Staff", bool disabled = false, bool revoked = false,
      bool expired = false, Guid? client = null)
    {
      var identity = TenantAdministrationFixture.User(fixture.FirmId, fixture.TenantId,
        name + "@example.test", name, kind);
      identity.Disabled = disabled;
      db.Users.Add(identity);
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, UserId = identity.Id,
        Role = "Partner", ClientId = client, GrantedByUserId = fixture.Admin.Id,
        GrantedAt = now.AddHours(-2), ExpiresAt = expired ? now.AddHours(-1) : null,
        RevokedAt = revoked ? now.AddHours(-1) : null
      });
      return identity.Id;
    }

    var expected = new[] { Add("partner1"), Add("partner2") }.Order().ToArray();
    Add("client-scope", client: fixture.ClientId);
    Add("client-identity", kind: "Client");
    Add("disabled", disabled: true);
    Add("expired", expired: true);
    Add("revoked", revoked: true);
    await db.SaveChangesAsync();

    var first = await FirmAdministrationQuery.ListActiveFirmWidePartnersAsync(db, fixture.AdminActor, 1);
    Assert.True(first.Succeeded, first.Message);
    Assert.Equal(expected[0], Assert.Single(first.Value!.Rows).UserId);
    Assert.Equal(expected[0], first.Value.AfterUserId);
    var second = await FirmAdministrationQuery.ListActiveFirmWidePartnersAsync(db, fixture.AdminActor, 1, first.Value.AfterUserId);
    Assert.True(second.Succeeded, second.Message);
    Assert.Equal(expected[1], Assert.Single(second.Value!.Rows).UserId);
    Assert.Null(second.Value.AfterUserId);

    var invalid = await FirmAdministrationQuery.ListActiveFirmWidePartnersAsync(db, fixture.AdminActor, 51);
    Assert.Equal("request.invalid", invalid.ErrorCode);
    var admin = fixture.AdminActor;
    var grant = await db.RoleGrants.SingleAsync(x => x.UserId == admin.UserId && x.Role == "Administrator");
    grant.ClientId = fixture.ClientId;
    await db.SaveChangesAsync();
    var denied = await FirmAdministrationQuery.ListActiveFirmWidePartnersAsync(db, admin);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    Assert.Null(denied.Value);
  }
}
