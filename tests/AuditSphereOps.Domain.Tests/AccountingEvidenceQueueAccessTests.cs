using System.Data.Common;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed class AccountingEvidenceQueueAccessTests
{
  [Fact]
  public async Task ExpiredFirmWideGrantCannotWidenAnActiveEngagementGrant()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (f, _) = await AccountingEvidenceQueueSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var broad = PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer");
      broad.GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
      broad.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
      db.RoleGrants.Add(broad); await db.SaveChangesAsync();
    }
    await using var read = new AuditSphereDbContext(pg.Options);
    var result = await AccountingEvidenceQueueQuery.GetAsync(read, PbcSeed.Actor(f.Staff, "AccountingPreparer"));
    Assert.True(result.Succeeded, result.Message);
    var row = Assert.Single(result.Value!);
    Assert.Equal("SYNTHETIC-A", row.Reference);
    Assert.DoesNotContain(result.Value!, x => x.Reference == "SYNTHETIC-B");
  }

  [Fact]
  public async Task ExpiredOrRevokedSiblingGrantsNeverRevealRowsOrCounts()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (f, sibling) = await AccountingEvidenceQueueSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Staff, "AccountingPreparer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var grant = PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer", sibling.Client, sibling.Engagement);
    grant.GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
    grant.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
    db.RoleGrants.Add(grant); await db.SaveChangesAsync();
    Assert.Single((await AccountingEvidenceQueueQuery.GetAsync(db, actor)).Value!);
    grant.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10); await db.SaveChangesAsync();
    Assert.Equal(2, (await AccountingEvidenceQueueQuery.GetAsync(db, actor)).Value!.Count);
    grant.RevokedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
    var current = await AccountingEvidenceQueueQuery.GetAsync(db, actor);
    Assert.True(current.Succeeded, current.Message);
    Assert.Equal("SYNTHETIC-A", Assert.Single(current.Value!).Reference);
    Assert.False((await AccountingEvidenceQueueQuery.GetAsync(db, PbcSeed.Actor(f.Client, "ClientUser"))).Succeeded);
  }

  [Fact]
  public async Task EpochLossDuringProjectionReturnsNoProtectedEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (f, _) = await AccountingEvidenceQueueSeed.SeedAsync(pg);
    var interceptor = new EpochAfterRead(pg, f.Staff.Id);
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using var db = new AuditSphereDbContext(options);
    var result = await AccountingEvidenceQueueQuery.GetAsync(db, PbcSeed.Actor(f.Staff, "AccountingPreparer"));
    Assert.True(interceptor.Fired);
    Assert.False(result.Succeeded); Assert.Null(result.Value);
  }

  private sealed class EpochAfterRead(ITestPostgresDatabase pg, Guid user) : DbCommandInterceptor
  {
    public bool Fired { get; private set; }
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
      CommandExecutedEventData data, DbDataReader result, CancellationToken ct = default)
    {
      if (!Fired && command.CommandText.Contains("analytical_reviews", StringComparison.Ordinal))
      {
        Fired = true;
        await using var other = new AuditSphereDbContext(pg.Options);
        await other.Users.Where(x => x.Id == user).ExecuteUpdateAsync(x => x.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1), ct);
      }
      return result;
    }
  }

}
