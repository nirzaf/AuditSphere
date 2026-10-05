using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.E2E.Tests;

/// <summary>Deterministic synthetic tenant and capability fixtures shared by Angular browser journeys.</summary>
internal static class TenantAdministrationSeed
{
  internal sealed record Seeded(OwnedHost Host, AppUser Admin, string TenantId, string MemberObjectId, string GroupObjectId);

  internal static async Task<Seeded> SeedAsync(OwnedHost host, bool verified)
  {
    var tenantId = Guid.NewGuid().ToString("D");
    var now = DateTimeOffset.UtcNow;
    var admin = new AppUser
    {
      Id = Guid.CreateVersion7(), FirmId = host.Fixture.FirmId, TenantId = tenantId, Subject = Guid.NewGuid().ToString("D"),
      Email = "tenant.admin@example.test", DisplayName = "Tenant Journey Admin", UserKind = "Staff", CreatedAt = now
    };
    var second = new AppUser
    {
      Id = Guid.CreateVersion7(), FirmId = host.Fixture.FirmId, TenantId = tenantId, Subject = Guid.NewGuid().ToString("D"),
      Email = "second.admin@example.test", DisplayName = "Second Journey Admin", UserKind = "Staff", CreatedAt = now
    };
    await using var db = host.CreateDbContext();
    db.Users.AddRange(admin, second);
    foreach (var user in new[] { admin, second })
      db.RoleGrants.Add(new RoleGrant { Id = Guid.CreateVersion7(), FirmId = host.Fixture.FirmId, UserId = user.Id,
        Role = "Administrator", GrantedAt = now, GrantedByUserId = user.Id });
    var sessionId = Guid.CreateVersion7();
    var connectionId = Guid.CreateVersion7();
    var draftId = Guid.CreateVersion7();
    db.Microsoft365SetupSessions.Add(new Microsoft365SetupSession
    {
      Id = sessionId, FirmId = host.Fixture.FirmId, InstallationId = "journey", BootstrapProofHash = new string('a', 64),
      CapabilityHash = new string('b', 64), ClaimedByUserId = admin.Id, ClaimedAt = now, ExpiresAt = now.AddHours(1)
    });
    db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision
    {
      Id = connectionId, FirmId = host.Fixture.FirmId, TenantId = tenantId, LoginClientIdReference = "slot:login",
      RuntimeCredentialReference = "slot:reader", State = Microsoft365RevisionStates.ConsentRequired, ConsentState = "REQUIRED", CreatedAt = now
    });
    db.Microsoft365SetupDrafts.Add(new Microsoft365SetupDraft
    {
      Id = draftId, FirmId = host.Fixture.FirmId, SetupSessionId = sessionId, ConnectionRevisionId = connectionId,
      State = Microsoft365RevisionStates.ConsentRequired, ExpectedTenantId = tenantId, CreatedAt = now, UpdatedAt = now
    });
    if (verified)
    {
      var attemptId = Guid.CreateVersion7();
      db.TenantConsentAttempts.Add(new TenantConsentAttempt
      {
        Id = attemptId, FirmId = host.Fixture.FirmId, SetupDraftId = draftId, InitiatedByUserId = admin.Id,
        InitiatingSessionEpoch = admin.SessionEpoch, InitiatorObjectId = admin.Subject, ExpectedTenantId = tenantId,
        ApplicationClientId = Guid.NewGuid().ToString("D"), StateHash = new string('c', 64),
        State = TenantConsentAttemptStates.ConsentVerified, CreatedAt = now, ExpiresAt = now.AddMinutes(10),
        ConsentingTenantId = tenantId, ConsentingObjectId = admin.Subject, ConsentVerifiedAt = now
      });
      foreach (var (capability, permission) in new[] { (Microsoft365Capabilities.DirectoryRead, "User.Read.All"),
                 (Microsoft365Capabilities.TenantUserProvisioning, "User.Create"), (Microsoft365Capabilities.GuestInvitation, "User.Invite.All"),
                 (Microsoft365Capabilities.GroupMembership, "GroupMember.ReadWrite.All") })
        db.TenantCapabilityVerifications.Add(new TenantCapabilityVerification
        {
          Id = Guid.CreateVersion7(), FirmId = host.Fixture.FirmId, ConnectionRevisionId = connectionId, ConsentAttemptId = attemptId,
          TenantId = tenantId, Capability = capability, Permission = permission, State = CapabilityVerificationStates.Verified,
          DiagnosticCode = "simulated-verified", ObservedByUserId = admin.Id, ObservedAt = now
        });
    }
    await db.SaveChangesAsync();
    return new(host, admin, tenantId, Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
  }

  internal static Dictionary<string, string> Simulation(Seeded seeded, bool provisioning = true) => new()
  {
    ["TenantAdministration__Simulation__Enabled"] = "true",
    ["TenantAdministration__Provisioning__Enabled"] = provisioning ? "true" : "false",
    ["TenantAdministration__GuestInvitation__Enabled"] = "true",
    ["TenantAdministration__GroupMembership__Enabled"] = "true",
    ["TenantAdministration__GuestRedirectUrl"] = "http://127.0.0.1/auth/landing",
    ["TenantAdministration__Simulation__Users__0__ObjectId"] = seeded.Admin.Subject,
    ["TenantAdministration__Simulation__Users__0__DisplayName"] = seeded.Admin.DisplayName,
    ["TenantAdministration__Simulation__Users__0__UserPrincipalName"] = seeded.Admin.Email,
    ["TenantAdministration__Simulation__Users__1__ObjectId"] = seeded.MemberObjectId,
    ["TenantAdministration__Simulation__Users__1__DisplayName"] = "Directory Journey Member",
    ["TenantAdministration__Simulation__Users__1__UserPrincipalName"] = "journey.member@example.test",
    ["TenantAdministration__Simulation__Groups__0__ObjectId"] = seeded.GroupObjectId,
    ["TenantAdministration__Simulation__Groups__0__DisplayName"] = "AuditSphere Journey Team",
  };
}
