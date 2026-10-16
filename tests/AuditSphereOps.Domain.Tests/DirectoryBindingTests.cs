using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class DirectoryBindingTests
{
  private sealed class Reader(Func<Task<DirectoryCandidate>> read) : IMicrosoftDirectoryReader
  {
    public int Calls;
    public async Task<DirectoryCandidate> GetByIdAsync(string tenantId, string objectId, CancellationToken ct)
    {
      Interlocked.Increment(ref Calls);
      return await read();
    }
    public Task<DirectoryCandidatePage> SearchAsync(string tenantId, string prefix, string? pageToken, CancellationToken ct, string? domain = null) => throw new NotSupportedException();
    public Task<DirectoryCandidatePage> BrowseActiveAsync(string tenantId, string? domain, string? pageToken, CancellationToken ct) => throw new NotSupportedException();
  }

  private static DirectoryCandidate Candidate(TenantAdministrationFixture f, Guid id) =>
    new(f.TenantId, id.ToString("D"), "Synthetic member", "directory-binding@example.test", true, "Member");

  [Fact]
  public async Task ReconciliationReturnsExactCurrentBindingWithoutProviderReadMutationOrGrant()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var objectId = Guid.NewGuid();
    var candidate = Candidate(f, objectId);
    var reader = new Reader(() => Task.FromResult(candidate));
    var absent = await DirectoryUserBindingService.ReconcileAsync(db, f.AdminActor, f.TenantId, Guid.Parse(f.TenantId), objectId);
    Assert.True(absent.Succeeded, absent.Message);
    Assert.False(absent.Value!.Bound);
    Assert.Null(absent.Value.UserId);
    var bound = await DirectoryUserBindingService.BindMemberAsync(db, f.AdminActor, reader, f.TenantId, objectId);
    Assert.True(bound.Succeeded, bound.Message);
    var count = await db.DirectoryUserObservations.CountAsync();
    var result = await DirectoryUserBindingService.ReconcileAsync(db, f.AdminActor, f.TenantId, Guid.Parse(f.TenantId), objectId);
    Assert.True(result.Succeeded, result.Message);
    Assert.True(result.Value!.Bound);
    Assert.Equal(bound.Value, result.Value.UserId);
    Assert.Equal(f.TenantId, result.Value.TenantId);
    Assert.Equal(objectId.ToString("D"), result.Value.ObjectId);
    Assert.Equal("Staff", result.Value.UserKind);
    Assert.Equal("ENABLED", result.Value.DirectoryEnabledState);
    Assert.Equal(count, await db.DirectoryUserObservations.CountAsync());
    Assert.Equal(1, reader.Calls);
    Assert.False(await db.RoleGrants.AnyAsync(x => x.UserId == bound.Value));
    Assert.False((await DirectoryUserBindingService.ReconcileAsync(db, f.AdminActor, f.TenantId, Guid.NewGuid(), objectId)).Succeeded);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task ExactNegativeEvidenceInvalidatesPositiveIncludingMalformedOptionalFields(bool typeChanged, bool malformed)
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var objectId = Guid.NewGuid();
    var candidate = Candidate(f, objectId);
    var original = await DirectoryUserBindingService.BindMemberAsync(db, f.AdminActor, new Reader(() => Task.FromResult(candidate)), f.TenantId, objectId);
    Assert.True(original.Succeeded, original.Message);
    var negative = candidate with { AccountEnabled = typeChanged, UserType = typeChanged ? "Guest" : "Member" };
    if (malformed) negative = negative with { DisplayName = new string('x', 301), UserPrincipalName = "unsafe\0upn" };
    var refused = await DirectoryUserBindingService.BindMemberAsync(db, f.AdminActor, new Reader(() => Task.FromResult(negative)), f.TenantId, objectId);
    Assert.False(refused.Succeeded);
    var latest = await db.DirectoryUserObservations.AsNoTracking().Where(x => x.ObjectId == candidate.ObjectId)
      .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id).FirstAsync();
    Assert.Equal(typeChanged ? "ENABLED" : "DISABLED", latest.EnabledState);
    Assert.Equal(negative.UserType, latest.UserType);
    Assert.NotNull(latest.UserPrincipalName);
    Assert.DoesNotContain('\0', latest.UserPrincipalName);
    Assert.True(latest.DisplayName.Length <= 300);
    var grant = await RoleAssignmentService.AssignAsync(db, f.AdminActor,
      new(original.Value, "Staff", "CLIENT", f.ClientId, Reason: "Negative directory evidence", ConfirmScopeExpansion: true), DateTimeOffset.UtcNow);
    Assert.False(grant.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, grant.ErrorCode);
    Assert.False(await db.RoleGrants.AnyAsync(x => x.UserId == original.Value));
    Assert.Equal(candidate.DisplayName, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == original.Value)).DisplayName);
    Assert.Equal(2, await db.DirectoryUserObservations.CountAsync(x => x.ObjectId == candidate.ObjectId));
  }

  [Fact]
  public async Task OlderDelayedPositiveCannotOvertakeNewerNegativeAndDoesNotRefreshItsTimestamp()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var actor = f.AdminActor;
    var objectId = Guid.NewGuid();
    var candidate = Candidate(f, objectId);
    var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await using var older = f.Db();
    var before = DateTimeOffset.UtcNow;
    var delayed = DirectoryUserBindingService.BindMemberAsync(older, actor, new Reader(async () =>
    {
      reached.SetResult();
      await release.Task;
      return candidate;
    }), f.TenantId, objectId);
    await reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    await using var newer = f.Db();
    var negative = await DirectoryUserBindingService.BindMemberAsync(newer, actor,
      new Reader(() => Task.FromResult(candidate with { AccountEnabled = false })), f.TenantId, objectId);
    Assert.False(negative.Succeeded);
    release.SetResult();
    var stale = await delayed.WaitAsync(TimeSpan.FromSeconds(15));
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    Assert.False(await newer.Users.AnyAsync(x => x.Subject == candidate.ObjectId));
    var observation = await newer.DirectoryUserObservations.SingleAsync(x => x.ObjectId == candidate.ObjectId);
    Assert.Equal("DISABLED", observation.EnabledState);
    Assert.True(observation.ObservedAt >= before.AddTicks(-10));
  }

  [Fact]
  public async Task TimestampIsRequestStartBeforeProviderCompletionNotPublicationTime()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var objectId = Guid.NewGuid();
    DateTimeOffset providerCompletedAt = default;
    var reader = new Reader(async () =>
    {
      await Task.Delay(20);
      providerCompletedAt = DateTimeOffset.UtcNow;
      return Candidate(f, objectId);
    });
    var before = DateTimeOffset.UtcNow;
    Assert.True((await DirectoryUserBindingService.BindMemberAsync(db, f.AdminActor, reader, f.TenantId, objectId)).Succeeded);
    var observation = await db.DirectoryUserObservations.SingleAsync(x => x.ObjectId == objectId.ToString("D"));
    Assert.True(observation.ObservedAt >= before.AddTicks(-10));
    Assert.True(observation.ObservedAt < providerCompletedAt);
  }

  [Theory, InlineData(false), InlineData(true)]
  public async Task WrongTenantOrObjectCannotInvalidateExactIdentityEvidence(bool wrongTenant)
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var objectId = Guid.NewGuid();
    var candidate = Candidate(f, objectId);
    var wrong = wrongTenant ? candidate with { TenantId = Guid.NewGuid().ToString("D") } : candidate with { ObjectId = Guid.NewGuid().ToString("D") };
    Assert.False((await DirectoryUserBindingService.BindMemberAsync(db, f.AdminActor, new Reader(() => Task.FromResult(wrong with { AccountEnabled = false })), f.TenantId, objectId)).Succeeded);
    Assert.Empty(await db.DirectoryUserObservations.ToListAsync());
    Assert.False(await db.Users.AnyAsync(x => x.Subject == candidate.ObjectId));
  }

  [Theory, InlineData(false), InlineData(true)]
  public async Task RevocationOrEpochChangeDuringProviderReadRefusesEvidenceAndBinding(bool epoch)
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var actor = f.AdminActor;
    var objectId = Guid.NewGuid();
    await using var db = f.Db();
    var reader = new Reader(async () =>
    {
      await using var revoke = f.Db();
      if (epoch) await revoke.Users.Where(x => x.Id == actor.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
      else await revoke.RoleGrants.Where(x => x.UserId == actor.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      return Candidate(f, objectId);
    });
    Assert.False((await DirectoryUserBindingService.BindMemberAsync(db, actor, reader, f.TenantId, objectId)).Succeeded);
    Assert.Empty(await db.DirectoryUserObservations.ToListAsync());
    Assert.False((await DirectoryUserBindingService.ReconcileAsync(db, actor, f.TenantId, Guid.Parse(f.TenantId), objectId)).Succeeded);
  }

  private sealed class PausePublication(bool refuse) : SaveChangesInterceptor
  {
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    {
      if (data.Context!.ChangeTracker.Entries<DirectoryUserObservation>().Any(x => x.State == EntityState.Added))
      {
        Reached.TrySetResult();
        await Release.Task.WaitAsync(ct);
        if (refuse) throw new InvalidOperationException("SYNTHETIC directory publication failure");
      }
      return result;
    }
  }

  [Theory, InlineData(false), InlineData(true)]
  public async Task ReconciliationWaitsForBindingCommitOrRollback(bool rollback)
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    var actor = f.AdminActor;
    var objectId = Guid.NewGuid();
    var pause = new PausePublication(rollback);
    await using var writer = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(f.Pg.Options).AddInterceptors(pause).Options);
    await using var reader = f.Db();
    var binding = DirectoryUserBindingService.BindMemberAsync(writer, actor, new Reader(() => Task.FromResult(Candidate(f, objectId))), f.TenantId, objectId);
    await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    var reconciliation = DirectoryUserBindingService.ReconcileAsync(reader, actor, f.TenantId, Guid.Parse(f.TenantId), objectId);
    try
    {
      await Task.Delay(150);
      Assert.False(reconciliation.IsCompleted);
    }
    finally { pause.Release.TrySetResult(); }
    if (rollback) await Assert.ThrowsAsync<InvalidOperationException>(async () => { await binding; });
    else Assert.True((await binding).Succeeded);
    var result = await reconciliation.WaitAsync(TimeSpan.FromSeconds(15));
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(!rollback, result.Value!.Bound);
    Assert.Equal(rollback ? 0 : 1, await reader.DirectoryUserObservations.CountAsync());
  }
}
