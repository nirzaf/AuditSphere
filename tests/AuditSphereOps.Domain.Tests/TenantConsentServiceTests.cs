using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
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
