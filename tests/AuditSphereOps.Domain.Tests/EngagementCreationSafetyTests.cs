using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed class EngagementCreationSafetyTests
{
  private static CreateEngagementDraftRequest Request(PbcSeed.Fixture f) => new(f.ClientId, "AccountingOnly", "2026-01-01", "2026-12-31", "SYNTHETIC-2026");
  private static async Task GrantAsync(AuditSphereDbContext db, PbcSeed.Fixture f)
  {
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId));
    await db.SaveChangesAsync();
  }

  [Fact]
  public async Task ConcurrentCreationRetainsOneBlockedShellAndConflictingProfileIsRefused()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using (var setup = new AuditSphereDbContext(pg.Options)) await GrantAsync(setup, f);
    var actor = PbcSeed.Actor(f.Staff, "Manager"); var request = Request(f);
    async Task<Guid> Create()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var result = await EngagementLifecycleService.CreateDraftAsync(db, actor, request);
      Assert.True(result.Succeeded, result.Message); return result.Value;
    }
    var ids = await Task.WhenAll(Create(), Create()); Assert.Equal(ids[0], ids[1]);
    await using var inspect = new AuditSphereDbContext(pg.Options);
    var row = Assert.Single(await inspect.Engagements.Where(e => e.ServiceProfileId == request.ServiceProfileId).ToListAsync());
    Assert.Equal(("Draft", true), (row.Status, row.ProfessionalWorkBlocked));
    Assert.Empty(await inspect.EngagementActivations.ToListAsync());
    Assert.False((await EngagementLifecycleService.CreateDraftAsync(inspect, actor, request with { ServiceProfileId = "OTHER" })).Succeeded);
  }

  [Fact]
  public async Task RevocationAfterWriteRollsBackTheNewShell()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using (var setup = new AuditSphereDbContext(pg.Options)) await GrantAsync(setup, f);
    var hook = new RevokeAfterSave(async () =>
    {
      await using var changed = new AuditSphereDbContext(pg.Options);
      await changed.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.Role == "Manager")
        .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using (var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(hook).Options))
      Assert.False((await EngagementLifecycleService.CreateDraftAsync(db, PbcSeed.Actor(f.Staff, "Manager"), Request(f))).Succeeded);
    Assert.True(hook.Fired); await using var inspect = new AuditSphereDbContext(pg.Options);
    Assert.False(await inspect.Engagements.AnyAsync(e => e.ServiceProfileId == Request(f).ServiceProfileId));
    Assert.Empty(await inspect.EngagementActivations.ToListAsync());
  }

  [Fact]
  public async Task EngagementOnlyManagerCannotCreateClientWideShellAndStaleEpochCannotReplay()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId, f.EngagementId)); await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(f.Staff, "Manager");
    Assert.False((await EngagementLifecycleService.CreateDraftAsync(db, actor, Request(f))).Succeeded);
    await GrantAsync(db, f); var created = await EngagementLifecycleService.CreateDraftAsync(db, actor, Request(f)); Assert.True(created.Succeeded);
    Assert.False((await EngagementLifecycleService.CreateDraftAsync(db, actor, Request(foreign))).Succeeded);
    await db.Users.Where(u => u.Id == actor.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    Assert.False((await EngagementLifecycleService.CreateDraftAsync(db, actor, Request(f))).Succeeded);
    Assert.Single(await db.Engagements.Where(e => e.ServiceProfileId == Request(f).ServiceProfileId).ToListAsync());
  }

  private sealed class RevokeAfterSave(Func<Task> revoke) : SaveChangesInterceptor
  {
    public bool Fired { get; private set; }
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData d, int result, CancellationToken ct = default)
    {
      if (!Fired) { Fired = true; await revoke(); } return result;
    }
  }
}
