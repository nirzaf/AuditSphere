using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class TenantConsentServiceTests
{
  private static readonly string TenantId = Guid.NewGuid().ToString("D");
  private static readonly string ClientId = Guid.NewGuid().ToString("D");

  [Fact]
  public async Task ConsentReturnIsOneUseAndDoesNotVerifyConnection()
  {
    await using var fixture = await Fixture.CreateAsync();
    var now = DateTimeOffset.UtcNow;
    await using var db = new AuditSphereDbContext(fixture.Options);
    var started = await TenantConsentService.BeginAsync(db, fixture.Actor,
      fixture.DraftId, TenantId, ClientId, now);
    Assert.True(started.Succeeded);
    var state = started.Value!.State;
    Assert.NotEmpty(state);
    db.ChangeTracker.Clear();
    var stored = await db.TenantConsentAttempts.SingleAsync();
    Assert.NotEqual(state, stored.StateHash);
    Assert.Equal(64, stored.StateHash.Length);
    Assert.Equal(fixture.ObjectId, stored.InitiatorObjectId);
    Assert.Equal(TenantConsentAttemptStates.Pending, stored.State);

    var wrongActor = fixture.Actor with { UserId = Guid.NewGuid() };
    Assert.False((await TenantConsentService.CompleteCallbackAsync(db, wrongActor,
      state, TenantId, true, false, now.AddMinutes(1))).Succeeded);
    Assert.False((await TenantConsentService.CompleteCallbackAsync(db, fixture.Actor,
      "forged", TenantId, true, false, now.AddMinutes(1))).Succeeded);
    var returned = await TenantConsentService.CompleteCallbackAsync(db, fixture.Actor,
      state, TenantId, true, false, now.AddMinutes(1));
    Assert.True(returned.Succeeded);
    Assert.Equal(TenantConsentAttemptStates.ReturnedUnverified, returned.Value!.State);
    Assert.False((await TenantConsentService.CompleteCallbackAsync(db, fixture.Actor,
      state, TenantId, true, false, now.AddMinutes(2))).Succeeded);
    db.ChangeTracker.Clear();
    Assert.Equal(TenantConsentAttemptStates.ReturnedUnverified,
      (await db.TenantConsentAttempts.SingleAsync()).State);
    Assert.Equal(Microsoft365RevisionStates.ConsentRequired,
      (await db.Microsoft365ConnectionRevisions.SingleAsync()).State);
    Assert.Equal("REQUIRED", (await db.Microsoft365ConnectionRevisions.SingleAsync()).ConsentState);
    Assert.Empty(db.IntegrationVerificationEvidences);
  }

  [Fact]
  public async Task WrongTenantAndExpiredReturnConsumeAttemptWithoutActivation()
  {
    await using var fixture = await Fixture.CreateAsync();
    var now = DateTimeOffset.UtcNow;
    await using var db = new AuditSphereDbContext(fixture.Options);
    var first = (await TenantConsentService.BeginAsync(db, fixture.Actor,
      fixture.DraftId, TenantId, ClientId, now)).Value!;
    Assert.False((await TenantConsentService.CompleteCallbackAsync(db, fixture.Actor,
      first.State, Guid.NewGuid().ToString("D"), true, false, now.AddMinutes(1))).Succeeded);
    Assert.False((await TenantConsentService.CompleteCallbackAsync(db, fixture.Actor,
      first.State, TenantId, true, false, now.AddMinutes(2))).Succeeded);
    var second = (await TenantConsentService.BeginAsync(db, fixture.Actor,
      fixture.DraftId, TenantId, ClientId, now)).Value!;
    Assert.False((await TenantConsentService.CompleteCallbackAsync(db, fixture.Actor,
      second.State, TenantId, true, false, now.AddMinutes(11))).Succeeded);
    db.ChangeTracker.Clear();
    var states = await db.TenantConsentAttempts.Select(x => x.State).ToListAsync();
    Assert.Contains(TenantConsentAttemptStates.Denied, states);
    Assert.Contains(TenantConsentAttemptStates.Expired, states);
  }

  [Fact]
  public async Task RevokedOrMismatchedIdentityCannotBeginOrCompleteConsent()
  {
    await using var fixture = await Fixture.CreateAsync();
    var now = DateTimeOffset.UtcNow;
    await using var db = new AuditSphereDbContext(fixture.Options);
    Assert.False((await TenantConsentService.BeginAsync(db, fixture.Actor,
      fixture.DraftId, Guid.NewGuid().ToString("D"), ClientId, now)).Succeeded);
    Assert.False((await TenantConsentService.BeginAsync(db, fixture.Actor with { FirmId = Guid.NewGuid() },
      fixture.DraftId, TenantId, ClientId, now)).Succeeded);
    var started = (await TenantConsentService.BeginAsync(db, fixture.Actor,
      fixture.DraftId, TenantId, ClientId, now)).Value!;
    var grant = await db.RoleGrants.SingleAsync();
    grant.RevokedAt = now.AddSeconds(1);
    var user = await db.Users.SingleAsync();
    user.SessionEpoch++;
    await db.SaveChangesAsync();
    Assert.False((await TenantConsentService.CompleteCallbackAsync(db, fixture.Actor,
      started.State, TenantId, true, false, now.AddMinutes(1))).Succeeded);
    db.ChangeTracker.Clear();
    Assert.Equal(TenantConsentAttemptStates.Pending,
      (await db.TenantConsentAttempts.SingleAsync()).State);
  }

  [Fact]
  public async Task DirectorySearchRequiresCurrentFirmWideAdministratorAndExactTenant()
  {
    await using var fixture = await Fixture.CreateAsync();
    await using var db = new AuditSphereDbContext(fixture.Options);
    var reader = new FakeDirectoryReader(new DirectoryCandidatePage(
      [new(TenantId, Guid.NewGuid().ToString("D"), "Ada", "ada@example.test", true, "Member")], null));
    var found = await DirectoryDiscoveryService.SearchAsync(db, fixture.Actor, reader,
      TenantId, "Ad", null);
    Assert.True(found.Succeeded);
    Assert.Single(found.Value!.Users);
    Assert.Equal(1, reader.Calls);

    var wrongTenantReader = new FakeDirectoryReader(new DirectoryCandidatePage(
      [new(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), "Other", "other@example.test", true, "Member")], null));
    Assert.False((await DirectoryDiscoveryService.SearchAsync(db, fixture.Actor,
      wrongTenantReader, TenantId, "Ot", null)).Succeeded);
    var grant = await db.RoleGrants.SingleAsync();
    grant.RevokedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    Assert.False((await DirectoryDiscoveryService.SearchAsync(db, fixture.Actor, reader,
      TenantId, "Ad", null)).Succeeded);
    Assert.Equal(1, reader.Calls);
  }

  [Fact]
  public async Task ActiveDirectoryBrowseRequiresAdminAndExactDomain()
  {
    await using var fixture = await Fixture.CreateAsync();
    await using var db = new AuditSphereDbContext(fixture.Options);
    var person = new DirectoryCandidate(TenantId, Guid.NewGuid().ToString("D"),
      "Ada", "ada@example.test", true, "Member");
    var reader = new FakeDirectoryReader(new DirectoryCandidatePage([person], null));
    Assert.Single((await DirectoryDiscoveryService.BrowseActiveAsync(db, fixture.Actor,
      reader, TenantId, "@Example.Test", null)).Value!.Users);
    Assert.False((await DirectoryDiscoveryService.BrowseActiveAsync(db, fixture.Actor,
      reader, TenantId, "other.test", null)).Succeeded);
    Assert.False((await DirectoryDiscoveryService.BrowseActiveAsync(db, fixture.Actor,
      reader, TenantId, "example.test' OR 1=1", null)).Succeeded);
    var grant = await db.RoleGrants.SingleAsync();
    grant.RevokedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    Assert.False((await DirectoryDiscoveryService.BrowseActiveAsync(db, fixture.Actor,
      reader, TenantId, "example.test", null)).Succeeded);
  }

  [Fact]
  public async Task ExactEnabledDirectoryMemberBindsOnceWithoutGrant()
  {
    await using var fixture = await Fixture.CreateAsync();
    await using var db = new AuditSphereDbContext(fixture.Options);
    var objectId = Guid.NewGuid();
    var candidate = new DirectoryCandidate(TenantId, objectId.ToString("D"),
      "Verified member", "verified@example.test", true, "Member");
    var reader = new FakeDirectoryReader(new DirectoryCandidatePage([candidate], null), candidate);
    var first = await DirectoryUserBindingService.BindMemberAsync(db, fixture.Actor,
      reader, TenantId, objectId);
    Assert.True(first.Succeeded);
    Assert.NotEqual(Guid.Empty, first.Value);
    var second = await DirectoryUserBindingService.BindMemberAsync(db, fixture.Actor,
      reader, TenantId, objectId);
    Assert.True(second.Succeeded);
    Assert.Equal(first.Value, second.Value);
    Assert.Equal(2, reader.ExactCalls);
    Assert.Equal(0, await db.RoleGrants.CountAsync(x => x.UserId == first.Value));
    Assert.Equal("GRAPH", (await db.DirectoryUserObservations.OrderByDescending(x => x.ObservedAt)
      .FirstAsync(x => x.ObjectId == objectId.ToString("D"))).Source);
    Assert.Equal("ENABLED", (await db.DirectoryUserObservations.OrderByDescending(x => x.ObservedAt)
      .FirstAsync(x => x.ObjectId == objectId.ToString("D"))).EnabledState);
    Assert.False((await RoleAdministrationService.EnsureUserAsync(db, fixture.Actor,
      new(TenantId, objectId.ToString("D"), "verified@example.test", "Verified member", "Staff", "GRAPH"))).Succeeded);
  }

  [Fact]
  public async Task DisabledOrWrongDirectoryObjectCannotBind()
  {
    await using var fixture = await Fixture.CreateAsync();
    await using var db = new AuditSphereDbContext(fixture.Options);
    var objectId = Guid.NewGuid();
    var disabled = new DirectoryCandidate(TenantId, objectId.ToString("D"),
      "Disabled member", "disabled@example.test", false, "Member");
    Assert.False((await DirectoryUserBindingService.BindMemberAsync(db, fixture.Actor,
      new FakeDirectoryReader(new DirectoryCandidatePage([], null), disabled), TenantId, objectId)).Succeeded);
    var wrong = disabled with { AccountEnabled = true, ObjectId = Guid.NewGuid().ToString("D") };
    Assert.False((await DirectoryUserBindingService.BindMemberAsync(db, fixture.Actor,
      new FakeDirectoryReader(new DirectoryCandidatePage([], null), wrong), TenantId, objectId)).Succeeded);
    Assert.False(await db.Users.AnyAsync(x => x.Subject == objectId.ToString("D")));
  }

  [Fact]
  public async Task StaleGraphObservationBlocksLocalGrantUntilExactReverification()
  {
    await using var fixture = await Fixture.CreateAsync();
    await using var db = new AuditSphereDbContext(fixture.Options);
    var objectId = Guid.NewGuid();
    var candidate = new DirectoryCandidate(TenantId, objectId.ToString("D"),
      "Verified member", "fresh@example.test", true, "Member");
    var reader = new FakeDirectoryReader(new DirectoryCandidatePage([candidate], null), candidate);
    var bound = await DirectoryUserBindingService.BindMemberAsync(db, fixture.Actor,
      reader, TenantId, objectId);
    Assert.True(bound.Succeeded);
    var observation = await db.DirectoryUserObservations.SingleAsync(x => x.ObjectId == objectId.ToString("D"));
    observation.ObservedAt = DateTimeOffset.UtcNow.AddHours(-1);
    await db.SaveChangesAsync();
    var proposed = new ApplyRoleGrantAndInvitationRequest(bound.Value, "Staff", "FIRM_WIDE");
    Assert.False((await RoleAdministrationService.ApplyRoleGrantAndInvitationAsync(db, fixture.Actor,
      proposed)).Succeeded);
    Assert.Equal(0, await db.RoleGrants.CountAsync(x => x.UserId == bound.Value));
    Assert.True((await DirectoryUserBindingService.BindMemberAsync(db, fixture.Actor,
      reader, TenantId, objectId)).Succeeded);
    Assert.True((await RoleAdministrationService.ApplyRoleGrantAndInvitationAsync(db, fixture.Actor,
      proposed)).Succeeded);
    Assert.Equal(1, await db.RoleGrants.CountAsync(x => x.UserId == bound.Value));
  }

  [Fact]
  public async Task DirectoryCapabilityCheckPersistsExactProviderResultWithoutActivatingConsent()
  {
    await using var fixture = await Fixture.CreateAsync();
    await using var db = new AuditSphereDbContext(fixture.Options);
    var now = DateTimeOffset.UtcNow;
    var candidate = new DirectoryCandidate(TenantId, fixture.ObjectId,
      "Synthetic administrator", "consent-admin@example.test", true, "Member");
    var reader = new FakeDirectoryReader(new DirectoryCandidatePage([], null), candidate);
    var checkedResult = await DirectoryCapabilityVerificationService.VerifyAsync(db, fixture.Actor,
      reader, TenantId, ClientId, now);
    Assert.True(checkedResult.Succeeded);
    Assert.Equal(1, reader.ExactCalls);
    db.ChangeTracker.Clear();
    var evidence = await db.IntegrationVerificationEvidences.SingleAsync();
    Assert.Equal("DIRECTORY", evidence.ResourceKind);
    Assert.Equal("EXACT_USER_READ", evidence.Operation);
    Assert.Equal("PASS", evidence.Result);
    Assert.Equal(ClientId, evidence.IdentityReference);
    var snapshot = await TenantConnectionQuery.GetAsync(db, fixture.Actor);
    Assert.Equal("VERIFIED_RECENT", snapshot.Value!.DirectoryCapabilityState);
    Assert.Equal(Microsoft365RevisionStates.ConsentRequired, snapshot.Value.ConnectionState);
    Assert.Equal("REQUIRED", snapshot.Value.ConsentState);
    Assert.Null((await db.Microsoft365ConnectionRevisions.SingleAsync()).VerifiedAt);
  }

  [Fact]
  public async Task FailedOrRevokedDirectoryCheckNeverReportsVerified()
  {
    await using var fixture = await Fixture.CreateAsync();
    await using var db = new AuditSphereDbContext(fixture.Options);
    var candidate = new DirectoryCandidate(TenantId, fixture.ObjectId,
      "Synthetic administrator", "consent-admin@example.test", false, "Member");
    var reader = new FakeDirectoryReader(new DirectoryCandidatePage([], null), candidate);
    Assert.False((await DirectoryCapabilityVerificationService.VerifyAsync(db, fixture.Actor,
      reader, TenantId, ClientId, DateTimeOffset.UtcNow)).Succeeded);
    db.ChangeTracker.Clear();
    Assert.Equal("BLOCKED", (await TenantConnectionQuery.GetAsync(db, fixture.Actor))
      .Value!.DirectoryCapabilityState);
    var grant = await db.RoleGrants.SingleAsync();
    grant.RevokedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    Assert.False((await DirectoryCapabilityVerificationService.VerifyAsync(db, fixture.Actor,
      reader, TenantId, ClientId, DateTimeOffset.UtcNow)).Succeeded);
    Assert.Single(await db.IntegrationVerificationEvidences.ToListAsync());
  }

  [Fact]
  public async Task AdministrationOverviewCountsOnlyCurrentFirmAndActiveGrantInvitations()
  {
    await using var fixture = await Fixture.CreateAsync();
    await using var db = new AuditSphereDbContext(fixture.Options);
    var now = DateTimeOffset.UtcNow;
    var memberId = Guid.CreateVersion7();
    var grantId = Guid.CreateVersion7();
    db.Users.Add(new AppUser
    {
      Id = memberId, FirmId = fixture.Actor.FirmId, TenantId = TenantId,
      Subject = Guid.NewGuid().ToString("D"), Email = "member@example.test",
      DisplayName = "Member", UserKind = "Staff", CreatedAt = now
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = grantId, FirmId = fixture.Actor.FirmId, UserId = memberId,
      Role = "Staff", GrantedAt = now, GrantedByUserId = fixture.Actor.UserId
    });
    db.UserAccessInvitations.Add(new UserAccessInvitation
    {
      Id = Guid.CreateVersion7(), FirmId = fixture.Actor.FirmId, UserId = memberId,
      RoleGrantId = grantId, RecipientEmail = "member@example.test",
      CreatedAt = now, UpdatedAt = now
    });
    await db.SaveChangesAsync();
    var overview = (await FirmAdministrationQuery.GetAsync(db, fixture.Actor)).Value!;
    Assert.Equal(2, overview.LocalUserCount);
    Assert.Equal(1, overview.PendingInvitationCount);

    (await db.RoleGrants.SingleAsync(x => x.Id == grantId)).RevokedAt = now;
    await db.SaveChangesAsync();
    Assert.Equal(0, (await FirmAdministrationQuery.GetAsync(db, fixture.Actor))
      .Value!.PendingInvitationCount);
    Assert.False((await FirmAdministrationQuery.GetAsync(db,
      fixture.Actor with { FirmId = Guid.NewGuid() })).Succeeded);
  }

  private sealed class FakeDirectoryReader(DirectoryCandidatePage page, DirectoryCandidate? exact = null) : IMicrosoftDirectoryReader
  {
    public int Calls { get; private set; }
    public int ExactCalls { get; private set; }
    public Task<DirectoryCandidatePage> SearchAsync(string tenantId, string prefix,
      string? pageToken, CancellationToken ct)
    {
      Calls++;
      return Task.FromResult(page);
    }
    public Task<DirectoryCandidate> GetByIdAsync(string tenantId, string objectId, CancellationToken ct)
    {
      ExactCalls++;
      return Task.FromResult(exact ?? throw new InvalidOperationException("No exact user configured."));
    }
    public Task<DirectoryCandidatePage> BrowseActiveAsync(string tenantId, string? domain,
      string? pageToken, CancellationToken ct) => Task.FromResult(page);
  }

  private sealed class Fixture(PgTestSchema pg) : IAsyncDisposable
  {
    public DbContextOptions<AuditSphereDbContext> Options => pg.Options;
    public Guid DraftId { get; } = Guid.CreateVersion7();
    public string ObjectId { get; } = Guid.NewGuid().ToString("D");
    public ActorContext Actor { get; } = new(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, ["Administrator"]);

    public static async Task<Fixture> CreateAsync()
    {
      var fixture = new Fixture(await PgTestSchema.CreateAsync());
      var now = DateTimeOffset.UtcNow;
      var sessionId = Guid.CreateVersion7();
      var connectionId = Guid.CreateVersion7();
      await using var db = new AuditSphereDbContext(fixture.Options);
      db.Users.Add(new AppUser
      {
        Id = fixture.Actor.UserId, FirmId = fixture.Actor.FirmId, TenantId = TenantId,
        Subject = fixture.ObjectId, Email = "consent-admin@example.test",
        DisplayName = "Synthetic administrator", UserKind = "Staff", SessionEpoch = 1, CreatedAt = now
      });
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.Actor.FirmId, UserId = fixture.Actor.UserId,
        Role = "Administrator", GrantedAt = now, GrantedByUserId = fixture.Actor.UserId
      });
      db.Microsoft365SetupSessions.Add(new Microsoft365SetupSession
      {
        Id = sessionId, FirmId = fixture.Actor.FirmId, InstallationId = "consent-test",
        BootstrapProofHash = new string('a', 64), CapabilityHash = new string('b', 64),
        ClaimedByUserId = fixture.Actor.UserId, ClaimedAt = now, ExpiresAt = now.AddHours(1)
      });
      db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision
      {
        Id = connectionId, FirmId = fixture.Actor.FirmId, TenantId = TenantId,
        LoginClientIdReference = "slot:login", RuntimeCredentialReference = "slot:reader",
        State = Microsoft365RevisionStates.ConsentRequired, ConsentState = "REQUIRED", CreatedAt = now
      });
      db.Microsoft365SetupDrafts.Add(new Microsoft365SetupDraft
      {
        Id = fixture.DraftId, FirmId = fixture.Actor.FirmId, SetupSessionId = sessionId,
        ConnectionRevisionId = connectionId, State = Microsoft365RevisionStates.ConsentRequired,
        ExpectedTenantId = TenantId, CreatedAt = now, UpdatedAt = now
      });
      await db.SaveChangesAsync();
      return fixture;
    }

    public ValueTask DisposeAsync() => pg.DisposeAsync();
  }
}
