using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    Guid workpaperId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await AuditPlanningService.CreateWorkpaperAsync(db, w.A("associate", "Staff"),
        new(w.EngagementId, "ARCHIVE-WP-01", "Synthetic archive selection workpaper",
          "Verify the archive readiness selects the issued financial package.", "test-v1", w.ProcedureId, "Inspect the issued release identity."));
      Assert.True(created.Succeeded, created.Message);
      workpaperId = created.Value!.WorkpaperId;
    }
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

    var (_, financialReleaseId) = await CompletionBundleFixture.ReleasedSupportedFinancialPackageAsync(
      pg.Options, new CompletionBundleScope(w.FirmId, w.ClientId, w.EngagementId, w.U));
    var laterWorkpaperRelease = await IssueLaterWorkpaperReleaseAsync(pg, w, workpaperId);
    Assert.True(laterWorkpaperRelease.ReleasedAt > (await GetReleaseAsync(pg, financialReleaseId)).ReleasedAt);

    string digest;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var readiness = (await FileFreezeService.GetArchiveReadinessAsync(db, partner, w.EngagementId, now)).Value!;
      Assert.Empty(readiness.Blockers);
      digest = readiness.ArchiveReadinessDigest!;
      Assert.Matches("^[0-9a-f]{64}$", digest);
      var freeze = await db.EngagementFileFreezes.AsNoTracking().SingleAsync(x => x.EngagementId == w.EngagementId);
      var financialRelease = await db.Releases.AsNoTracking().SingleAsync(x => x.Id == financialReleaseId);
      Assert.Equal(FileFreezeService.ArchiveReadinessDigest(freeze, financialRelease), digest);
      var request = new FileFreezeService.EarlyComplianceLockRequest(w.EngagementId, revision, true, "Assembled file ready for archive", digest);
      // A stale countdown revision or a digest the Partner did not review is refused.
      Assert.Equal(ErrorCodes.GenerationStale, (await FileFreezeService.RequestEarlyComplianceLockAsync(db, partner, request with { ExpectedRevision = revision + 1 }, now)).ErrorCode);
      Assert.Equal(ErrorCodes.GenerationStale, (await FileFreezeService.RequestEarlyComplianceLockAsync(db, partner, request with { ArchiveReadinessDigest = new string('a', 64) }, now)).ErrorCode);
    }

    SubsequentEventReviewValue subsequentEvent;
    OpeningBalanceVerificationValue openingBalance;
    var preparer = w.A("associate", "Staff");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var prepared = await AuditCompletionService.RecordSubsequentEventAsync(db, preparer,
        new(w.EngagementId, null, new(2026, 12, 31), new(2027, 1, 20), "Synthetic post-period event",
          SubsequentEventClassifications.NonAdjusting, false, true, "Note 27",
          "Synthetic conditions arose after the reporting date.", "QAR", 125.123456m, []));
      Assert.True(prepared.Succeeded, prepared.Message);
      subsequentEvent = prepared.Value!;
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var prepared = await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, preparer,
        new(w.EngagementId, null, null, "Synthetic prior statement", new(2026, 1, 1), "QAR",
          1000.123456m, 1000.123456m, true, "Synthetic comparison", ["synthetic-prior-source"], 0));
      Assert.True(prepared.Succeeded, prepared.Message);
      openingBalance = prepared.Value!;
    }

    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var ct = deadline.Token;
    var barrier = new EarlyLockSaveBarrier();
    var lockOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(barrier).Options;
    await using (var locker = new AuditSphereDbContext(lockOptions))
    await using (var eventReviewer = new AuditSphereDbContext(pg.Options))
    await using (var openingReviewer = new AuditSphereDbContext(pg.Options))
    await using (var observer = new AuditSphereDbContext(pg.Options))
    {
      await locker.Database.OpenConnectionAsync(ct);
      await eventReviewer.Database.OpenConnectionAsync(ct);
      await openingReviewer.Database.OpenConnectionAsync(ct);
      var lockerPid = ((NpgsqlConnection)locker.Database.GetDbConnection()).ProcessID;
      var eventPid = ((NpgsqlConnection)eventReviewer.Database.GetDbConnection()).ProcessID;
      var openingPid = ((NpgsqlConnection)openingReviewer.Database.GetDbConnection()).ProcessID;
      var request = new FileFreezeService.EarlyComplianceLockRequest(w.EngagementId, revision, true,
        "Assembled file ready for archive", digest);
      var locking = FileFreezeService.RequestEarlyComplianceLockAsync(locker, partner, request, now, ct);
      Task<CommandResult>? eventReview = null;
      Task<CommandResult>? openingReview = null;
      try
      {
        Assert.Same(barrier.Reached.Task, await Task.WhenAny(barrier.Reached.Task, locking).WaitAsync(ct));
        eventReview = AuditCompletionService.ReviewCompletionRecordAsync(eventReviewer, manager,
          "SUBSEQUENT_EVENT", subsequentEvent.ReviewId, subsequentEvent.Revision, ct);
        await AssertWaitingAsync(eventReview, eventPid, lockerPid, "%FROM engagements%");

        openingReview = AuditPlanningCompletionService.ReviewOpeningBalanceVerificationAsync(openingReviewer,
          manager, new(w.EngagementId, openingBalance.VerificationId, openingBalance.Revision, null), ct);
        await AssertWaitingAsync(openingReview, openingPid, eventPid, "%FROM client_safety_states%");
      }
      finally
      {
        barrier.Release.TrySetResult();
        await Task.WhenAll(new Task?[] { locking, eventReview, openingReview }.OfType<Task>());
      }
      var locked = await locking;
      Assert.True(locked.Succeeded, locked.Message);
      Assert.Equal((freezeId, FileFreezeStates.Frozen, false),
        (locked.Value!.FreezeId, locked.Value.State, locked.Value.AlreadyFrozen));
      Assert.Equal(ErrorCodes.ProtectedState, (await eventReview!).ErrorCode);
      Assert.Equal(ErrorCodes.ProtectedState, (await openingReview!).ErrorCode);

      async Task AssertWaitingAsync(Task review, int waiter, int blocker, string queryPattern)
      {
        while (!review.IsCompleted)
        {
          if (await observer.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
              SELECT 1 FROM pg_stat_activity WHERE pid = {waiter} AND wait_event_type = 'Lock'
                AND query LIKE {queryPattern} AND {blocker} = ANY(pg_blocking_pids(pid))
            ) AS "Value"
            """).SingleAsync(ct)) return;
          await Task.Delay(20, ct);
        }
        Assert.Fail($"Expected PostgreSQL session {waiter} to wait for {blocker} on {queryPattern}.");
      }
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var freeze = await db.EngagementFileFreezes.AsNoTracking().SingleAsync(x => x.Id == freezeId);
      Assert.Equal((FileFreezeStates.Frozen, ExternalReadOnlyStates.BlockedExternal), (freeze.State, freeze.ExternalReadOnly));
      Assert.True((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == w.EngagementId)).ProfessionalWorkBlocked);
      var evidence = await db.FileFreezeEarlyLocks.AsNoTracking().SingleAsync(x => x.FreezeId == freezeId);
      Assert.Equal((w.U["partner"].Id, digest, revision), (evidence.LockedByUserId, evidence.ArchiveReadinessDigest, evidence.FreezeRevision));
      Assert.Equal(financialReleaseId, evidence.ReleaseId);
      Assert.NotEqual(laterWorkpaperRelease.ReleaseId, evidence.ReleaseId);
      var retainedEvent = await db.SubsequentEventReviews.AsNoTracking().SingleAsync(x => x.Id == subsequentEvent.ReviewId);
      Assert.Equal((subsequentEvent.Revision, (Guid?)null, (DateTimeOffset?)null, (long?)null),
        (retainedEvent.Revision, retainedEvent.ReviewedByUserId, retainedEvent.ReviewedAt, retainedEvent.ReviewedRevision));
      var retainedOpening = await db.OpeningBalanceVerifications.AsNoTracking()
        .SingleAsync(x => x.Id == openingBalance.VerificationId);
      Assert.Equal((openingBalance.Revision, (Guid?)null, (DateTimeOffset?)null, (long?)null),
        (retainedOpening.Revision, retainedOpening.ReviewedByUserId, retainedOpening.ReviewedAt, retainedOpening.ReviewedBasisRevision));
      Assert.Equal(2, await db.FrozenAccessAttempts.CountAsync(x => x.EngagementId == w.EngagementId &&
        x.ActorUserId == manager.UserId && x.Action == "professional work on a frozen engagement file"));

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

  private sealed class EarlyLockSaveBarrier : SaveChangesInterceptor
  {
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
      CancellationToken cancellationToken = default)
    {
      Reached.TrySetResult();
      await Release.Task.WaitAsync(cancellationToken);
      return result;
    }
  }

  private static async Task<(Guid ReleaseId, DateTimeOffset ReleasedAt)> IssueLaterWorkpaperReleaseAsync(
    PgTestSchema pg, World w, Guid workpaperId)
  {
    var partner = w.A("partner", "Partner");
    var manager = w.A("manager", "Manager");
    var manifest = System.Text.Encoding.UTF8.GetBytes("Synthetic later workpaper release for archive selection regression.");
    var digest = Hashing.Sha256Hex(manifest);
    var releaseKey = "archive-selection-workpaper-" + Guid.NewGuid().ToString("N");
    var checkpointRoot = Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N"));

    try
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var inputGeneration = await db.ClientSafetyStates.AsNoTracking()
        .Where(x => x.FirmId == w.FirmId && x.Id == w.ClientId).Select(x => x.InputGeneration).SingleAsync();
      var policyGeneration = await db.FirmSafetyStates.AsNoTracking()
        .Where(x => x.Id == w.FirmId).Select(x => x.PolicyGeneration).SingleAsync();
      var approval = await ApprovalService.CreateAsync(db, manager,
        new(ReleaseTargetKinds.Workpaper, workpaperId, 1, inputGeneration, policyGeneration, digest));
      Assert.True(approval.Succeeded, approval.Message);
      var candidate = await ReleaseService.CreateCandidateAsync(db, partner,
        new(approval.Value, ReleaseTargetKinds.Workpaper, workpaperId, 1, inputGeneration, policyGeneration, digest));
      Assert.True(candidate.Succeeded, candidate.Message);

      var checkpoint = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db,
        new LocalAppendOnlyCheckpointStore(checkpointRoot), partner,
        new(candidate.Value, 1, releaseKey, digest, manifest));
      Assert.True(checkpoint.Succeeded, checkpoint.Message);
      var issued = await ReleaseService.IssueAsync(db, partner,
        new(candidate.Value, 1, digest, releaseKey), new ReleaseSafetyOptions());
      Assert.True(issued.Succeeded, issued.Message);
      var release = await db.Releases.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId && x.Id == issued.Value);
      return (release.Id, release.ReleasedAt);
    }
    finally
    {
      if (Directory.Exists(checkpointRoot)) Directory.Delete(checkpointRoot, recursive: true);
    }
  }

  private static async Task<Release> GetReleaseAsync(PgTestSchema pg, Guid releaseId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    return await db.Releases.AsNoTracking().SingleAsync(x => x.Id == releaseId);
  }
}
