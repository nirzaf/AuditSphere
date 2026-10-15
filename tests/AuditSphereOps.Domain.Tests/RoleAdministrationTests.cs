using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class RoleAdministrationTests
{
  [Fact]
  public async Task RosterIdentityAndRoleGrant_AreExactScopedIdempotentAndRevokeSafely()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, clientId, engagementId) = await pg.SeedScopeAsync();
    var admin = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "admin-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant", Email = "admin@example.test", DisplayName = "Administrator", CreatedAt = DateTimeOffset.UtcNow
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
    }

    var actor = new ActorContext(admin.Id, firmId, admin.SessionEpoch, ["Administrator"]);
    Guid targetId;
    Guid clientGrantId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var onboard = await RoleAdministrationService.EnsureStaffUserAsync(db, actor,
        new("tenant", "target-oid", "target@example.test", "Target Staff", "APPROVED_ROSTER"));
      Assert.True(onboard.Succeeded);
      targetId = onboard.Value;

      var conflict = await RoleAdministrationService.EnsureStaffUserAsync(db, actor,
        new("tenant", "different-oid", "target@example.test", "Different Identity", "APPROVED_ROSTER"));
      Assert.False(conflict.Succeeded);
      Assert.Equal("roles.identity-conflict", conflict.ErrorCode);

      var clientGrant = await RoleAdministrationService.ApplyRoleGrantAsync(db, actor,
        new(targetId, "Manager", "client", ClientId: clientId));
      Assert.True(clientGrant.Succeeded);
      clientGrantId = clientGrant.Value;
      var repeat = await RoleAdministrationService.ApplyRoleGrantAsync(db, actor,
        new(targetId, "Manager", "CLIENT", ClientId: clientId));
      Assert.True(repeat.Succeeded);
      Assert.Equal(clientGrantId, repeat.Value);
      Assert.Equal(2, (await db.Users.SingleAsync(x => x.Id == targetId)).SessionEpoch);

      var invalidScope = await RoleAdministrationService.ApplyRoleGrantAsync(db, actor,
        new(targetId, "Staff", "ENGAGEMENT", ClientId: null, EngagementId: engagementId));
      Assert.False(invalidScope.Succeeded);
      Assert.Equal("roles.invalid", invalidScope.ErrorCode);

      var evidence = await db.RoleGrantChangeEvidences.Where(x => x.TargetUserId == targetId).ToListAsync();
      Assert.Contains(evidence, x => x.Source == "APPROVED_ROSTER" && x.NewRole == "NONE");
      Assert.Contains(evidence, x => x.Action == "GRANTED" && x.NewRole == "Manager");
    }

    Guid replacementId;
    Guid adminGrantId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      adminGrantId = await db.RoleGrants.Where(x => x.UserId == admin.Id && x.Role == "Administrator" && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync();
      var replacement = await RoleAdministrationService.EnsureStaffUserAsync(db, actor,
        new("tenant", "replacement-oid", "replacement@example.test", "Replacement Admin", "VERIFIED_SIGN_IN"));
      replacementId = replacement.Value;
      var grant = await RoleAdministrationService.ApplyRoleGrantAsync(db, actor,
        new(replacementId, "Administrator", "FIRM_WIDE"));
      Assert.True(grant.Succeeded);

      var revoke = await RoleAdministrationService.RevokeRoleGrantAsync(db, actor, new(adminGrantId, replacementId));
      Assert.True(revoke.Succeeded);
      Assert.True((await db.RoleGrants.SingleAsync(x => x.Id == adminGrantId)).RevokedAt.HasValue);
      Assert.Equal(2, (await db.Users.SingleAsync(x => x.Id == admin.Id)).SessionEpoch);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(2, await db.RoleGrants.CountAsync(x => x.FirmId == firmId && x.Role == "Administrator"));
      Assert.Equal(1, await db.RoleGrantChangeEvidences.CountAsync(x => x.Action == "REVOKED" && x.RoleGrantId == adminGrantId));
    }
  }

  [Fact]
  public async Task ConcurrentAdministratorRevocations_CannotRemoveTheLastUsableAdministrator()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, _, _) = await pg.SeedScopeAsync();
    var first = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "admin-1-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant", Email = "admin1@example.test", DisplayName = "Admin One", CreatedAt = DateTimeOffset.UtcNow
    };
    var second = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "admin-2-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant", Email = "admin2@example.test", DisplayName = "Admin Two", CreatedAt = DateTimeOffset.UtcNow
    };
    Guid firstGrantId, secondGrantId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.AddRange(first, second);
      firstGrantId = Guid.NewGuid();
      secondGrantId = Guid.NewGuid();
      db.RoleGrants.AddRange(
        new RoleGrant { Id = firstGrantId, FirmId = firmId, UserId = first.Id, Role = "Administrator", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = first.Id },
        new RoleGrant { Id = secondGrantId, FirmId = firmId, UserId = second.Id, Role = "Administrator", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = first.Id });
      await db.SaveChangesAsync();
    }

    var firstTask = Task.Run(async () =>
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await RoleAdministrationService.RevokeRoleGrantAsync(
        db, new ActorContext(first.Id, firmId, first.SessionEpoch, ["Administrator"]), new(firstGrantId, second.Id));
    });
    var secondTask = Task.Run(async () =>
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await RoleAdministrationService.RevokeRoleGrantAsync(
        db, new ActorContext(second.Id, firmId, second.SessionEpoch, ["Administrator"]), new(secondGrantId, first.Id));
    });

    var results = await Task.WhenAll(firstTask, secondTask);
    Assert.Single(results, x => x.Succeeded);
    var rejected = Assert.Single(results, x => !x.Succeeded);
    Assert.Equal("roles.last-admin", rejected.ErrorCode);

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(1, await verify.RoleGrants.CountAsync(x => x.FirmId == firmId && x.Role == "Administrator" && x.RevokedAt == null));
  }

  [Fact]
  public async Task RoleRevocationRefusesAdministratorRevokedWhileWaitingForFirmLock()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, _, _) = await pg.SeedScopeAsync();
    var actor = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "admin-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant", Email = "admin@example.test", DisplayName = "Administrator", CreatedAt = DateTimeOffset.UtcNow
    };
    var target = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "target-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant", Email = "target@example.test", DisplayName = "Target", CreatedAt = DateTimeOffset.UtcNow
    };
    var actorGrantId = Guid.NewGuid();
    var targetGrantId = Guid.NewGuid();
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Users.AddRange(actor, target);
      setup.RoleGrants.AddRange(
        new RoleGrant
        {
          Id = actorGrantId, FirmId = firmId, UserId = actor.Id, Role = "Administrator",
          GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = actor.Id
        },
        new RoleGrant
        {
          Id = targetGrantId, FirmId = firmId, UserId = target.Id, Role = "Staff",
          GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = actor.Id
        });
      await setup.SaveChangesAsync();
    }

    var actorContext = new ActorContext(actor.Id, firmId, actor.SessionEpoch, ["Administrator"]);
    await using var blocker = new AuditSphereDbContext(pg.Options);
    await using var blockerTransaction = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleAsync();

    var signal = new ObserveFirmAdministrationLock();
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(signal).Options;
    await using var writer = new AuditSphereDbContext(options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var attempt = RoleAdministrationService.RevokeRoleGrantAsync(
      writer, actorContext, new RevokeRoleGrantRequest(targetGrantId), timeout.Token);
    await signal.Reached.Task.WaitAsync(timeout.Token);

    await blocker.RoleGrants.Where(x => x.Id == actorGrantId)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), timeout.Token);
    await blocker.Users.Where(x => x.Id == actor.Id)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), timeout.Token);
    await blockerTransaction.CommitAsync(timeout.Token);

    var result = await attempt.WaitAsync(timeout.Token);
    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, result.ErrorCode);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Null((await verify.RoleGrants.SingleAsync(x => x.Id == targetGrantId)).RevokedAt);
    Assert.False(await verify.RoleGrantChangeEvidences.AnyAsync(x =>
      x.RoleGrantId == targetGrantId && x.Action == "REVOKED"));
  }

  private sealed class ObserveFirmAdministrationLock : DbCommandInterceptor
  {
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
      DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("firm_safety_states", StringComparison.Ordinal) &&
          command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
        Reached.TrySetResult();
      return ValueTask.FromResult(result);
    }
  }
}
