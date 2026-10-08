using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// STE 4.4.3 Partner early compliance lock: fails closed without a final release, a Partner, a confirmation, a current
/// countdown revision and the reviewed archive-readiness digest; then freezes the file with append-only evidence.
/// </summary>
public sealed partial class AuditDeliverablesTests
{
  [Fact]
  public async Task PartnerEarlyLock_FailsClosedThenFreezesOnlyTheReviewedReadiness()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await SignedReportAsync(pg, w);
    var partner = w.A("partner", "Partner");
    var manager = w.A("manager", "Manager");
    var now = DateTimeOffset.UtcNow;

    Guid freezeId;
    long revision;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var freeze = await db.EngagementFileFreezes.AsNoTracking().SingleAsync(x => x.EngagementId == w.EngagementId);
      (freezeId, revision) = (freeze.Id, freeze.Revision);
      var request = new FileFreezeService.EarlyComplianceLockRequest(w.EngagementId, revision, true, "Assembled file ready for archive", new string('0', 64));
      Assert.Equal(ErrorCodes.ScopeDenied, (await FileFreezeService.RequestEarlyComplianceLockAsync(db, manager, request, now)).ErrorCode);
      Assert.Equal(ErrorCodes.GateBlocked, (await FileFreezeService.RequestEarlyComplianceLockAsync(db, partner, request with { PartnerConfirmed = false }, now)).ErrorCode);
      Assert.Equal(ErrorCodes.AuditPlanning.Invalid, (await FileFreezeService.RequestEarlyComplianceLockAsync(db, partner, request with { Rationale = " " }, now)).ErrorCode);
      // No final release yet: the file cannot be locked early and the readiness shows the blocker.
      Assert.Equal(ErrorCodes.GateBlocked, (await FileFreezeService.RequestEarlyComplianceLockAsync(db, partner, request, now)).ErrorCode);
      Assert.Contains("final-release-missing", (await FileFreezeService.GetArchiveReadinessAsync(db, partner, w.EngagementId, now)).Value!.Blockers);
    }

    await CompletionBundleFixture.ReleasedFinancialPackageAsync(pg.Options, new CompletionBundleScope(w.FirmId, w.ClientId, w.EngagementId, w.U));

    string digest;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var readiness = (await FileFreezeService.GetArchiveReadinessAsync(db, partner, w.EngagementId, now)).Value!;
      Assert.Empty(readiness.Blockers);
      digest = readiness.ArchiveReadinessDigest!;
      Assert.Matches("^[0-9a-f]{64}$", digest);
      var request = new FileFreezeService.EarlyComplianceLockRequest(w.EngagementId, revision, true, "Assembled file ready for archive", digest);
      // A stale countdown revision or a digest the Partner did not review is refused.
      Assert.Equal(ErrorCodes.GenerationStale, (await FileFreezeService.RequestEarlyComplianceLockAsync(db, partner, request with { ExpectedRevision = revision + 1 }, now)).ErrorCode);
      Assert.Equal(ErrorCodes.GenerationStale, (await FileFreezeService.RequestEarlyComplianceLockAsync(db, partner, request with { ArchiveReadinessDigest = new string('a', 64) }, now)).ErrorCode);
    }

    Guid lockedFreezeId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var request = new FileFreezeService.EarlyComplianceLockRequest(w.EngagementId, revision, true, "Assembled file ready for archive", digest);
      var locked = await FileFreezeService.RequestEarlyComplianceLockAsync(db, partner, request, now);
      Assert.True(locked.Succeeded, locked.Message);
      Assert.Equal((FileFreezeStates.Frozen, false), (locked.Value!.State, locked.Value.AlreadyFrozen));
      lockedFreezeId = locked.Value.FreezeId;
      Assert.Equal(freezeId, lockedFreezeId);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var freeze = await db.EngagementFileFreezes.AsNoTracking().SingleAsync(x => x.Id == freezeId);
      Assert.Equal((FileFreezeStates.Frozen, ExternalReadOnlyStates.BlockedExternal), (freeze.State, freeze.ExternalReadOnly));
      Assert.True((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == w.EngagementId)).ProfessionalWorkBlocked);
      var evidence = await db.FileFreezeEarlyLocks.AsNoTracking().SingleAsync(x => x.FreezeId == freezeId);
      Assert.Equal((w.U["partner"].Id, digest, revision), (evidence.LockedByUserId, evidence.ArchiveReadinessDigest, evidence.FreezeRevision));

      // Repeating an early lock on an already frozen file reports it frozen without a second lock.
      var repeat = await FileFreezeService.RequestEarlyComplianceLockAsync(db, partner,
        new(w.EngagementId, revision, true, "Repeat", digest), now);
      Assert.Equal((true, FileFreezeStates.Frozen), (repeat.Value!.AlreadyFrozen, repeat.Value.State));
      Assert.Equal(1, await db.FileFreezeEarlyLocks.CountAsync(x => x.FreezeId == freezeId));

      // The lock evidence is append-only.
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE file_freeze_early_locks SET rationale = 'rewritten' WHERE id = {evidence.Id}"));
    }
  }
}
