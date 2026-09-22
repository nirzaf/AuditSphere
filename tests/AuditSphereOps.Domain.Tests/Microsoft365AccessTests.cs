using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class Microsoft365AccessTests
{
  [Fact]
  public async Task InitialBootstrap_BindsOnlyTheConfiguredMicrosoftIdentity_AndIsIdempotent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    await using var db = new AuditSphereDbContext(pg.Options);
    var claim = await Microsoft365OnboardingService.ClaimAsync(db, firmId, "install-bootstrap", "proof", Hash("proof"), now);
    Assert.True(claim.Succeeded);

    var wrong = await Microsoft365OnboardingService.CompleteInitialAdministratorBootstrapAsync(db,
      new(claim.Value!.SessionId, claim.Value.Capability, "tenant", "wrong-oid", "admin@example.test", "Admin"),
      "tenant", "approved-oid", now);
    Assert.False(wrong.Succeeded);

    var first = await Microsoft365OnboardingService.CompleteInitialAdministratorBootstrapAsync(db,
      new(claim.Value.SessionId, claim.Value.Capability, "tenant", "approved-oid", "admin@example.test", "Admin"),
      "tenant", "approved-oid", now);
    Assert.True(first.Succeeded);
    var replay = await Microsoft365OnboardingService.CompleteInitialAdministratorBootstrapAsync(db,
      new(claim.Value.SessionId, claim.Value.Capability, "tenant", "approved-oid", "admin@example.test", "Admin"),
      "tenant", "approved-oid", now.AddSeconds(1));
    Assert.True(replay.Succeeded);
    Assert.Equal(first.Value, replay.Value);
    Assert.Equal(1, await db.RoleGrants.CountAsync(x => x.FirmId == firmId && x.Role == "Administrator"));
    Assert.Equal(1, await db.RoleGrantChangeEvidences.CountAsync(x => x.Source == "BOOTSTRAP"));
    Assert.NotNull(await db.Microsoft365SetupSessions.SingleAsync(x => x.Id == claim.Value.SessionId && x.ConsumedAt != null));

    var unauthorisedResume = await Microsoft365OnboardingService.ClaimAsync(db, firmId, "install-bootstrap", "proof", Hash("proof"), now.AddSeconds(2));
    Assert.False(unauthorisedResume.Succeeded);
    var authorisedResume = await Microsoft365OnboardingService.ClaimAsync(db, firmId, "install-bootstrap", "proof", Hash("proof"), now.AddSeconds(2),
      authenticatedTenantId: "tenant", authenticatedObjectId: "approved-oid");
    Assert.True(authorisedResume.Succeeded);
  }

  [Fact]
  public async Task ClientAssignment_CreatesScopedCopyInvitation_AndRevokeStopsCopy()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, engagementId) = await pg.SeedScopeAsync();
    var admin = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "admin-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant", Email = "admin@example.test", DisplayName = "Admin", CreatedAt = DateTimeOffset.UtcNow
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(admin);
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = firmId, UserId = admin.Id, Role = "Administrator",
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = admin.Id
      });
      await db.SaveChangesAsync();
      var actor = new ActorContext(admin.Id, firmId, admin.SessionEpoch, ["Administrator"]);
      var user = await RoleAdministrationService.EnsureUserAsync(db, actor,
        new("tenant", "client-oid", "client@example.test", "Client Contact", "Client", "APPROVED_ROSTER"));
      Assert.True(user.Succeeded);

      var assigned = await RoleAdministrationService.ApplyRoleGrantAndInvitationAsync(db, actor,
        new(user.Value, "clientuser", "CLIENT", clientId));
      Assert.True(assigned.Succeeded);
      var repeated = await RoleAdministrationService.ApplyRoleGrantAndInvitationAsync(db, actor,
        new(user.Value, "ClientUser", "CLIENT", clientId));
      Assert.True(repeated.Succeeded);
      Assert.Equal(assigned.Value!.RoleGrantId, repeated.Value!.RoleGrantId);
      Assert.Equal(assigned.Value.InvitationId, repeated.Value.InvitationId);
      Assert.Equal("/auth/landing", assigned.Value.DestinationPath);
      Assert.Equal(UserAccessInvitationStates.NotSent, assigned.Value.DeliveryState);
      Assert.Equal(1, await db.UserAccessInvitations.CountAsync(x => x.UserId == user.Value));

      var copied = await RoleAdministrationService.MarkInvitationCopiedAsync(db, actor, assigned.Value.InvitationId);
      Assert.True(copied.Succeeded);
      Assert.Equal(UserAccessInvitationStates.Copied,
        await db.UserAccessInvitations.Where(x => x.Id == assigned.Value.InvitationId).Select(x => x.DeliveryState).SingleAsync());
      Assert.Contains(await db.RoleGrantChangeEvidences.Where(x => x.RoleGrantId == assigned.Value.RoleGrantId).ToListAsync(),
        x => x.Action == "INVITATION_COPIED" && x.ActorUserId == admin.Id);

      var grantId = assigned.Value.RoleGrantId;
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, actor, new(grantId));
      Assert.True(revoked.Succeeded);
      var copiedAfterRevoke = await RoleAdministrationService.MarkInvitationCopiedAsync(db, actor, assigned.Value.InvitationId);
      Assert.False(copiedAfterRevoke.Succeeded);

      var staleClient = new ActorContext(user.Value, firmId, 1, ["ClientUser"]);
      var auth = await AuthorizationDecision.AuthorizeAsync(db, staleClient,
        new AuthorizationRequest(firmId, ClientId: clientId, RequiredRoles: ["ClientUser"]));
      Assert.False(auth.Succeeded);
    }
  }

  [Fact]
  public async Task ClientIdentity_CannotReceiveStaffRoleOrFirmWideAccess()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, _) = await pg.SeedScopeAsync();
    var admin = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "admin-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant", Email = "admin@example.test", DisplayName = "Admin", CreatedAt = DateTimeOffset.UtcNow
    };
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(admin);
    db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = firmId, UserId = admin.Id, Role = "Administrator", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = admin.Id });
    await db.SaveChangesAsync();
    var actor = new ActorContext(admin.Id, firmId, admin.SessionEpoch, ["Administrator"]);
    var user = await RoleAdministrationService.EnsureUserAsync(db, actor,
      new("tenant", "client-oid", "client@example.test", "Client", "Client", "APPROVED_ROSTER"));
    Assert.True(user.Succeeded);
    var staffRole = await RoleAdministrationService.ApplyRoleGrantAndInvitationAsync(db, actor,
      new(user.Value, "Staff", "CLIENT", clientId));
    Assert.False(staffRole.Succeeded);
    var firmWide = await RoleAdministrationService.ApplyRoleGrantAndInvitationAsync(db, actor,
      new(user.Value, "ClientUser", "FIRM_WIDE"));
    Assert.False(firmWide.Succeeded);

    var self = await RoleAdministrationService.ApplyRoleGrantAndInvitationAsync(db, actor,
      new(admin.Id, "Staff", "FIRM_WIDE"));
    Assert.False(self.Succeeded);
    Assert.Equal("roles.self-elevation", self.ErrorCode);
  }

  private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
