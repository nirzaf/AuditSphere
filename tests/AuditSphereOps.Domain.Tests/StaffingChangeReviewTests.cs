using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class StaffingChangeReviewTests
{
  [Fact]
  public async Task ConcurrentReviewedAssignmentAndRevocationRetainActorOwnedImmutableReceipts()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var r = new StaffingChangeRequest(Guid.NewGuid(), new("ASSIGN", f.Reviewer.Id, StaffingLevels.StaffAssociate));
    var p = (await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, r)).Value!;
    Assert.Contains("Full Control", p.SharePointEffect);
    r = r with { Reviewed = true, RequestHash = p.RequestHash, ReviewBasis = p.ReviewBasis };
    async Task<string> Save()
    {
      await using var c = new AuditSphereDbContext(pg.Options);
      var result = await StaffingChangeWorkspace.ExecuteAsync(c, actor, f.EngagementId, r);
      Assert.True(result.Succeeded, result.Message); return JsonSerializer.Serialize(result.Value);
    }
    var results = await Task.WhenAll(Save(), Save()); Assert.Equal(results[0], results[1]);
    var receipt = await db.StaffingChanges.AsNoTracking().SingleAsync();
    Assert.Single(await db.EngagementStaffAssignments.ToListAsync());
    Assert.True((await StaffingChangeWorkspace.LookupAsync(db, actor, f.EngagementId, r.RequestId, r.RequestHash)).Value!.Found);
    Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE staffing_changes SET preview_json='{{}}' WHERE id={receipt.Id}"))).SqlState);
    Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM staffing_changes WHERE id={receipt.Id}"))).SqlState);
    var revoke = new StaffingChangeRequest(Guid.NewGuid(), new("REVOKE", f.Reviewer.Id, StaffingLevels.StaffAssociate, receipt.AssignmentId));
    var rp = (await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, revoke)).Value!;
    Assert.True(rp.RemovesLocalGrant);
    revoke = revoke with { Reviewed = true, RequestHash = rp.RequestHash, ReviewBasis = rp.ReviewBasis };
    var revoked = await StaffingChangeWorkspace.ExecuteAsync(db, actor, f.EngagementId, revoke);
    Assert.True(revoked.Succeeded, revoked.Message);
    Assert.True((await StaffingChangeWorkspace.ExecuteAsync(db, actor, f.EngagementId, revoke)).Succeeded);
    Assert.Equal(f.Reviewer.SessionEpoch + 1, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == f.Reviewer.Id)).SessionEpoch);
    Assert.True((await StaffingChangeWorkspace.LookupAsync(db, actor, f.EngagementId, r.RequestId, r.RequestHash)).Value!.Found);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId)); await db.SaveChangesAsync();
    Assert.False((await StaffingChangeWorkspace.LookupAsync(db, PbcSeed.Actor(f.Staff, "Manager"), f.EngagementId, r.RequestId, r.RequestHash)).Value!.Found);
    await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Disabled, true));
    Assert.False((await StaffingChangeWorkspace.LookupAsync(db, actor, f.EngagementId, r.RequestId, r.RequestHash)).Succeeded);
  }

  [Fact]
  public async Task InvalidUnreviewedSelfWrongScopeAndStaleTargetChangesNeverPublish()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg); var other = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var r = new StaffingChangeRequest(Guid.NewGuid(), new("ASSIGN", f.Reviewer.Id, StaffingLevels.StaffAssociate));
    Assert.False((await StaffingChangeWorkspace.ExecuteAsync(db, actor, f.EngagementId, r)).Succeeded);
    Assert.False((await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, r with { Fields = r.Fields with { UserId = actor.UserId } })).Succeeded);
    Assert.Equal((await StaffingChangeWorkspace.PreviewAsync(db, actor, other.EngagementId, r)).ErrorCode,
      (await StaffingChangeWorkspace.PreviewAsync(db, actor, Guid.NewGuid(), r)).ErrorCode);
    var p = (await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, r)).Value!;
    r = r with { Reviewed = true, RequestHash = p.RequestHash, ReviewBasis = p.ReviewBasis };
    await db.Users.Where(x => x.Id == f.Reviewer.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    Assert.Equal("revision.stale", (await StaffingChangeWorkspace.ExecuteAsync(db, actor, f.EngagementId, r)).ErrorCode);
    Assert.Empty(await db.StaffingChanges.ToListAsync()); Assert.Empty(await db.EngagementStaffAssignments.ToListAsync());
    Assert.False((await StaffingChangeWorkspace.ExecuteAsync(db, actor, f.EngagementId, r with { RequestHash = new string('f',64) })).Succeeded);
    await db.Users.Where(x => x.Id == f.Reviewer.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Disabled, true));
    Assert.False((await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, r)).Succeeded);
  }

  [Fact]
  public async Task HigherStaffingEligibilityUsesLatestExactReviewAcrossCandidatesCommandsAndPreview()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var revokedTarget = PbcSeed.User(f.FirmId, "Staff");
    var staleTarget = PbcSeed.User(f.FirmId, "Staff");
    db.Users.AddRange(revokedTarget, staleTarget);
    var recordedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
    var firstSource = new StaffCertification { Id = Guid.NewGuid(), FirmId = f.FirmId, UserId = f.Reviewer.Id,
      Name = "Synthetic CPA", RecordedAt = recordedAt, RecordedByUserId = f.Staff.Id };
    var revokedSource = new StaffCertification { Id = Guid.NewGuid(), FirmId = f.FirmId, UserId = revokedTarget.Id,
      Name = "Synthetic ACCA", RecordedAt = recordedAt, RecordedByUserId = f.Staff.Id };
    var staleSource = new StaffCertification { Id = Guid.NewGuid(), FirmId = f.FirmId, UserId = staleTarget.Id,
      Name = "Synthetic CA", RecordedAt = recordedAt, RecordedByUserId = f.Staff.Id };
    db.StaffCertifications.AddRange(firstSource, revokedSource, staleSource);
    await db.SaveChangesAsync();

    Assert.False((await StaffingService.CandidatesAsync(db, actor)).Single(x => x.UserId == f.Reviewer.Id).Certified);
    var firstRequest = new StaffingChangeRequest(Guid.NewGuid(), new("ASSIGN", f.Reviewer.Id, StaffingLevels.AuditManager));
    Assert.Equal("staffing.certification", (await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, firstRequest)).ErrorCode);
    Assert.Equal("staffing.certification", (await StaffingService.AssignAsync(db, actor,
      new(f.EngagementId, f.Reviewer.Id, StaffingLevels.AuditManager))).ErrorCode);

    await StaffingCertificationTestEvidence.ReviewAsync(db, actor, firstSource.Id);
    await StaffingCertificationTestEvidence.ReviewAsync(db, actor, revokedSource.Id);
    await StaffingCertificationTestEvidence.ReviewAsync(db, actor, staleSource.Id);
    Assert.True((await StaffingService.CandidatesAsync(db, actor)).Single(x => x.UserId == f.Reviewer.Id).Certified);

    var firstPreview = (await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, firstRequest)).Value!;
    Assert.True(firstPreview.Certified);
    var secondRequest = new StaffingChangeRequest(Guid.NewGuid(), new("ASSIGN", f.Reviewer.Id, StaffingLevels.AuditManager));
    await StaffingCertificationTestEvidence.ReviewAsync(db, actor, firstSource.Id, expectedRevision: 1);
    var secondPreview = (await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId, secondRequest)).Value!;
    Assert.True(secondPreview.Certified);
    Assert.NotEqual(firstPreview.ReviewBasis, secondPreview.ReviewBasis);

    var staleHead = firstRequest with { Reviewed = true, RequestHash = firstPreview.RequestHash, ReviewBasis = firstPreview.ReviewBasis };
    Assert.Equal(ErrorCodes.StaleRevision, (await StaffingChangeWorkspace.ExecuteAsync(db, actor, f.EngagementId, staleHead)).ErrorCode);
    var approved = secondRequest with { Reviewed = true, RequestHash = secondPreview.RequestHash, ReviewBasis = secondPreview.ReviewBasis };
    var assigned = await StaffingChangeWorkspace.ExecuteAsync(db, actor, f.EngagementId, approved);
    Assert.True(assigned.Succeeded, assigned.Message);
    var assignmentReceipt = assigned.Value!;
    Assert.True((await StaffingService.ListAsync(db, actor, f.EngagementId)).Value!
      .Single(x => x.UserId == f.Reviewer.Id).Certified);
    var historicAssignment = await db.EngagementStaffAssignments.AsNoTracking().SingleAsync(x => x.Id == assignmentReceipt.AssignmentId);
    var historicReceipt = await db.StaffingChanges.AsNoTracking().SingleAsync(x => x.Id == assignmentReceipt.Id);
    var historicGrant = await db.RoleGrants.AsNoTracking().SingleAsync(x => x.Id == historicAssignment.RoleGrantId);

    await StaffingCertificationTestEvidence.ReviewAsync(db, actor, firstSource.Id, expectedRevision: 2, decision: "REVOKED");
    await StaffingCertificationTestEvidence.ReviewAsync(db, actor, revokedSource.Id, expectedRevision: 1, decision: "REVOKED");
    Assert.False((await StaffingService.CandidatesAsync(db, actor)).Single(x => x.UserId == f.Reviewer.Id).Certified);
    Assert.False((await StaffingService.CandidatesAsync(db, actor)).Single(x => x.UserId == revokedTarget.Id).Certified);
    var listed = (await StaffingService.ListAsync(db, actor, f.EngagementId)).Value!.Single(x => x.UserId == f.Reviewer.Id);
    Assert.False(listed.Certified);
    Assert.Equal(historicAssignment.Id, listed.AssignmentId);
    Assert.Equal("staffing.certification", (await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId,
      new(Guid.NewGuid(), new("ASSIGN", revokedTarget.Id, StaffingLevels.AuditManager)))).ErrorCode);
    Assert.Equal("staffing.certification", (await StaffingService.AssignAsync(db, actor,
      new(f.EngagementId, revokedTarget.Id, StaffingLevels.AuditManager))).ErrorCode);

    await db.StaffCertifications.Where(x => x.Id == staleSource.Id)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Changed synthetic source"));
    Assert.False((await StaffingService.CandidatesAsync(db, actor)).Single(x => x.UserId == staleTarget.Id).Certified);
    Assert.Equal("staffing.certification", (await StaffingChangeWorkspace.PreviewAsync(db, actor, f.EngagementId,
      new(Guid.NewGuid(), new("ASSIGN", staleTarget.Id, StaffingLevels.AuditManager)))).ErrorCode);
    Assert.Equal("staffing.certification", (await StaffingService.AssignAsync(db, actor,
      new(f.EngagementId, staleTarget.Id, StaffingLevels.AuditManager))).ErrorCode);

    var currentAssignment = await db.EngagementStaffAssignments.AsNoTracking().SingleAsync(x => x.Id == historicAssignment.Id);
    var currentGrant = await db.RoleGrants.AsNoTracking().SingleAsync(x => x.Id == historicGrant.Id);
    var currentReceipt = await db.StaffingChanges.AsNoTracking().SingleAsync(x => x.Id == historicReceipt.Id);
    Assert.Equal((historicAssignment.AssignedAt, historicAssignment.AssignedByUserId, historicAssignment.RevokedAt),
      (currentAssignment.AssignedAt, currentAssignment.AssignedByUserId, currentAssignment.RevokedAt));
    Assert.Equal((historicGrant.Role, historicGrant.RevokedAt), (currentGrant.Role, currentGrant.RevokedAt));
    Assert.Equal((historicReceipt.ReviewBasis, historicReceipt.PreviewJson), (currentReceipt.ReviewBasis, currentReceipt.PreviewJson));
    Assert.Equal(new long[] { 1, 2, 3 }, await db.StaffCertificationReviews.AsNoTracking()
      .Where(x => x.CertificationId == firstSource.Id).OrderBy(x => x.Revision).Select(x => x.Revision).ToListAsync());
  }

  [Fact]
  public async Task CertificationRevocationCommittedBeforeReviewedStaffingPublicationPreventsAssignment()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var source = new StaffCertification
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, UserId = f.Reviewer.Id, Name = "Synthetic CPA",
      RecordedAt = DateTimeOffset.UtcNow.AddSeconds(-1), RecordedByUserId = f.Staff.Id
    };
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.StaffCertifications.Add(source);
      await setup.SaveChangesAsync();
      await StaffingCertificationTestEvidence.ReviewAsync(setup, actor, source.Id);
    }

    var request = new StaffingChangeRequest(Guid.NewGuid(), new("ASSIGN", f.Reviewer.Id, StaffingLevels.AuditManager));
    await using (var previewDb = new AuditSphereDbContext(pg.Options))
    {
      var preview = (await StaffingChangeWorkspace.PreviewAsync(previewDb, actor, f.EngagementId, request)).Value!;
      request = request with { Reviewed = true, RequestHash = preview.RequestHash, ReviewBasis = preview.ReviewBasis };
    }

    StaffCertification persistedSource;
    await using (var sourceDb = new AuditSphereDbContext(pg.Options))
      persistedSource = await sourceDb.StaffCertifications.AsNoTracking().SingleAsync(x => x.Id == source.Id);
    var revoke = new ReviewStaffCertificationRequest(persistedSource.Id, Guid.NewGuid(),
      StaffCertificationEvidence.SourceDigest(persistedSource), ExpectedRevision: 1, Decision: "REVOKED",
      EvidenceReference: "test://independent-review", Reason: "Synthetic qualification withdrawal before assignment.");
    var pause = new PauseCertificationReview();
    await using var reviewDb = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    // The full Domain workflow runs PostgreSQL-heavy cohorts concurrently; allow the reviewed command
    // to reach the explicit save barrier while still bounding a genuinely stalled interleaving.
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var revokeTask = ResourcePlanningService.ReviewCertificationAsync(reviewDb, actor, revoke, timeout.Token);
    var pauseWait = pause.Reached.Task.WaitAsync(timeout.Token);
    if (await Task.WhenAny(pauseWait, revokeTask) == revokeTask)
    {
      var earlyResult = await revokeTask;
      Assert.True(earlyResult.Succeeded,
        $"Certification review finished before the save barrier: {earlyResult.ErrorCode} {earlyResult.Message}");
      Assert.Fail("Certification review completed without reaching the save barrier.");
    }
    await pauseWait;

    await using var staffingDb = new AuditSphereDbContext(pg.Options);
    await using var observer = new AuditSphereDbContext(pg.Options);
    var staffingTask = StaffingChangeWorkspace.ExecuteAsync(staffingDb, actor, f.EngagementId, request, timeout.Token);
    try
    {
      await WaitForFirmSafetyLockWaitAsync(observer, timeout.Token);
      Assert.False(staffingTask.IsCompleted);
    }
    finally
    {
      pause.Release.TrySetResult();
    }

    var revoked = await revokeTask;
    Assert.True(revoked.Succeeded, revoked.Message);
    var assignment = await staffingTask;
    Assert.Equal("staffing.certification", assignment.ErrorCode);

    await using var check = new AuditSphereDbContext(pg.Options);
    var latest = await check.StaffCertificationReviews.AsNoTracking()
      .Where(x => x.CertificationId == source.Id).OrderByDescending(x => x.Revision).FirstAsync();
    Assert.Equal((2L, "REVOKED"), (latest.Revision, latest.Decision));
    Assert.Empty(await check.EngagementStaffAssignments.Where(x => x.EngagementId == f.EngagementId && x.UserId == f.Reviewer.Id).ToListAsync());
    Assert.Empty(await check.StaffingChanges.ToListAsync());
    Assert.False(await check.RoleGrants.AnyAsync(x => x.UserId == f.Reviewer.Id &&
      x.Reason != null && x.Reason.StartsWith("Engagement staffing: ")));
  }

  private static async Task WaitForFirmSafetyLockWaitAsync(AuditSphereDbContext observer, CancellationToken ct)
  {
    var end = DateTimeOffset.UtcNow.AddSeconds(10);
    while (DateTimeOffset.UtcNow < end)
    {
      await observer.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()", ct);
      if (await observer.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE '%firm_safety_states%'").SingleAsync(ct) > 0)
        return;
      await Task.Delay(20, ct);
    }
    Assert.Fail("Expected staffing publication to wait on the certification review firm lock.");
  }

  private sealed class PauseCertificationReview : SaveChangesInterceptor
  {
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
      InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      if (eventData.Context!.ChangeTracker.Entries<StaffCertificationReview>().Any(x => x.State == EntityState.Added))
      {
        Reached.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }
}

internal static class StaffingCertificationTestEvidence
{
  internal static async Task<ActorContext> CreateAdministratorAsync(AuditSphereDbContext db, Guid firmId)
  {
    var admin = PbcSeed.User(firmId, "Staff");
    db.Users.Add(admin);
    db.RoleGrants.Add(PbcSeed.AdminGrant(firmId, admin));
    await db.SaveChangesAsync();
    return PbcSeed.Actor(admin, "Administrator");
  }

  internal static async Task<Guid> ReviewAsync(AuditSphereDbContext db, ActorContext admin, Guid certificationId,
    long expectedRevision = 0, string decision = "VERIFIED")
  {
    var source = await db.StaffCertifications.AsNoTracking()
      .SingleAsync(x => x.FirmId == admin.FirmId && x.Id == certificationId);
    var result = await ResourcePlanningService.ReviewCertificationAsync(db, admin,
      new(certificationId, Guid.NewGuid(), StaffCertificationEvidence.SourceDigest(source), expectedRevision,
        decision, "test://independent-review", "Synthetic independent administrator review."));
    Assert.True(result.Succeeded, result.Message);
    return result.Value;
  }
}
