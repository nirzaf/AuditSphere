using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// PostgreSQL-backed tenant-administration fixture: one firm with a client, sibling client, engagements,
/// a prepared tenant connection, two firm administrators and an in-memory simulated Microsoft tenant.
/// </summary>
internal sealed class TenantAdministrationFixture : IAsyncDisposable
{
  public PgTestSchema Pg { get; }
  public string TenantId { get; } = Guid.NewGuid().ToString("D");
  public Guid FirmId { get; private set; }
  public Guid ClientId { get; private set; }
  public Guid EngagementId { get; private set; }
  public Guid SiblingClientId { get; } = Guid.NewGuid();
  public Guid SiblingEngagementId { get; } = Guid.NewGuid();
  public Guid DraftId { get; } = Guid.CreateVersion7();
  public Guid ConnectionId { get; } = Guid.CreateVersion7();
  public AppUser Admin { get; private set; } = default!;
  public AppUser SecondAdmin { get; private set; } = default!;
  public SimulatedMicrosoftTenant Microsoft { get; private set; } = default!;
  public string MemberObjectId { get; } = Guid.NewGuid().ToString("D");
  public string DisabledMemberObjectId { get; } = Guid.NewGuid().ToString("D");
  public string GuestObjectId { get; } = Guid.NewGuid().ToString("D");
  public string GroupObjectId { get; } = Guid.NewGuid().ToString("D");
  public string PrivilegedGroupObjectId { get; } = Guid.NewGuid().ToString("D");
  public TenantAdministrationOptions Options => new(TenantId, ProvisioningEnabled: true, GuestInvitationEnabled: true,
    GroupMembershipEnabled: true, DirectoryReadEnabled: true, OutboundMailEnabled: false,
    GuestRedirectUrl: "https://auditsphere.example.test/auth/landing");

  private TenantAdministrationFixture(PgTestSchema pg) => Pg = pg;

  public ActorContext AdminActor => new(Admin.Id, FirmId, CurrentEpoch(Admin.Id), ["Administrator"]);
  public ActorContext SecondAdminActor => new(SecondAdmin.Id, FirmId, CurrentEpoch(SecondAdmin.Id), ["Administrator"]);

  public AuditSphereDbContext Db() => new(Pg.Options);

  public long CurrentEpoch(Guid userId)
  {
    using var db = Db();
    return db.Users.AsNoTracking().Single(x => x.Id == userId).SessionEpoch;
  }

  public static async Task<TenantAdministrationFixture> CreateAsync()
  {
    var fixture = new TenantAdministrationFixture(await PgTestSchema.CreateAsync());
    var (firmId, clientId, engagementId) = await fixture.Pg.SeedScopeAsync();
    fixture.FirmId = firmId;
    fixture.ClientId = clientId;
    fixture.EngagementId = engagementId;
    var now = DateTimeOffset.UtcNow;
    await using var db = fixture.Db();
    db.PracticeClients.Add(new Domain.Practice.PracticeClient
    {
      Id = fixture.SiblingClientId, FirmId = firmId, LegalName = "SIBLING CLIENT", CreatedAt = now
    });
    db.Engagements.Add(new Domain.Engagements.Engagement
    {
      Id = fixture.SiblingEngagementId, FirmId = firmId, PracticeClientId = fixture.SiblingClientId, CreatedAt = now
    });
    db.ClientSafetyStates.Add(new Domain.Completion.ClientSafetyState { Id = fixture.SiblingClientId, FirmId = firmId });
    fixture.Admin = User(firmId, fixture.TenantId, "admin@example.test", "Primary Administrator");
    fixture.SecondAdmin = User(firmId, fixture.TenantId, "admin2@example.test", "Second Administrator");
    db.Users.AddRange(fixture.Admin, fixture.SecondAdmin);
    foreach (var admin in new[] { fixture.Admin, fixture.SecondAdmin })
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.CreateVersion7(), FirmId = firmId, UserId = admin.Id, Role = "Administrator",
        GrantedAt = now, GrantedByUserId = admin.Id
      });
    var sessionId = Guid.CreateVersion7();
    db.Microsoft365SetupSessions.Add(new Microsoft365SetupSession
    {
      Id = sessionId, FirmId = firmId, InstallationId = "tenant-admin-test",
      BootstrapProofHash = new string('a', 64), CapabilityHash = new string('b', 64),
      ClaimedByUserId = fixture.Admin.Id, ClaimedAt = now, ExpiresAt = now.AddHours(1)
    });
    db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision
    {
      Id = fixture.ConnectionId, FirmId = firmId, TenantId = fixture.TenantId,
      LoginClientIdReference = "slot:login", RuntimeCredentialReference = "slot:reader",
      State = Microsoft365RevisionStates.ConsentRequired, ConsentState = "REQUIRED", CreatedAt = now
    });
    db.Microsoft365SetupDrafts.Add(new Microsoft365SetupDraft
    {
      Id = fixture.DraftId, FirmId = firmId, SetupSessionId = sessionId, ConnectionRevisionId = fixture.ConnectionId,
      State = Microsoft365RevisionStates.ConsentRequired, ExpectedTenantId = fixture.TenantId, CreatedAt = now, UpdatedAt = now
    });
    await db.SaveChangesAsync();
    fixture.Microsoft = new SimulatedMicrosoftTenant(fixture.TenantId,
      [
        new(fixture.Admin.Subject, "Primary Administrator", "admin@example.test"),
        new(fixture.MemberObjectId, "Directory Member", "member@example.test"),
        new(fixture.DisabledMemberObjectId, "Disabled Member", "disabled@example.test", AccountEnabled: false),
        new(fixture.GuestObjectId, "Existing Guest", "guest_client.test#EXT#@example.onmicrosoft.com", "Guest", Mail: "guest@client.test"),
      ],
      [
        new(fixture.GroupObjectId, "AuditSphere Audit Team"),
        new(fixture.PrivilegedGroupObjectId, "Privileged Role Group", IsAssignableToRole: true),
      ]);
    return fixture;
  }

  /// <summary>Runs the full consent flow (state → identity leg) and capability verification.</summary>
  public async Task ConnectAndVerifyAsync()
  {
    var now = DateTimeOffset.UtcNow;
    await using var db = Db();
    var begin = await TenantConsentService.BeginAsync(db, AdminActor, DraftId, TenantId, Guid.NewGuid().ToString("D"), now);
    Assert.True(begin.Succeeded, begin.Message);
    var returned = await TenantConsentService.CompleteCallbackAsync(db, AdminActor, begin.Value!.State, TenantId, true, false, now);
    Assert.True(returned.Succeeded, returned.Message);
    var challenge = await TenantConsentService.BeginIdentityVerificationAsync(db, AdminActor, returned.Value!.AttemptId, now);
    Assert.True(challenge.Succeeded, challenge.Message);
    var code = Microsoft.IssueCode(TenantId, Admin.Subject, challenge.Value!.Nonce);
    var verified = await TenantConsentService.CompleteIdentityVerificationAsync(db, AdminActor, Microsoft,
      challenge.Value.State, code, false, now);
    Assert.True(verified.Succeeded, verified.Message);
    var capabilities = await TenantCapabilityService.VerifyAsync(db, AdminActor, Microsoft, Options, now);
    Assert.True(capabilities.Succeeded, capabilities.Message);
  }

  public static AppUser User(Guid firmId, string tenantId, string email, string name, string kind = "Staff") => new()
  {
    Id = Guid.CreateVersion7(), FirmId = firmId, TenantId = tenantId, Subject = Guid.NewGuid().ToString("D"),
    Email = email, DisplayName = name, UserKind = kind, CreatedAt = DateTimeOffset.UtcNow
  };

  public static string Key() => "test-" + Guid.NewGuid().ToString("N");

  public ValueTask DisposeAsync() => Pg.DisposeAsync();
}
