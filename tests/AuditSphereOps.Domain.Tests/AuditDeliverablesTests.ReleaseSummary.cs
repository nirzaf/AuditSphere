using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  [Fact]
  public async Task EqrDecisionRechecksAuthorizationAfterAdvisoryLockWait()
  {
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var ct = deadline.Token;
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var reviewer = w.A("partner2", "Partner");
    EqrReviewBasis basisValue;
    await using (var basisDb = new AuditSphereDbContext(pg.Options))
    {
      var basis = await AuditDeliverableService.GetEqrReviewBasisAsync(basisDb, reviewer, w.EngagementId, ct);
      Assert.True(basis.Succeeded, basis.Message);
      basisValue = basis.Value!;
    }
    var request = new RecordEqrDecisionRequest("IN_PROGRESS", null, false, Guid.NewGuid().ToString("D"),
      basisValue.CaseRevision, basisValue.FactsDigest, basisValue.ReportDeliverableId, basisValue.ReportSha256);
    Guid reviewerGrantId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
      reviewerGrantId = await setup.RoleGrants.AsNoTracking().Where(x => x.FirmId == w.FirmId && x.UserId == reviewer.UserId &&
        x.Role == "Partner" && x.RevokedAt == null).Select(x => x.Id).SingleAsync(ct);

    var lockDigest = Hashing.Sha256Hex(JsonSerializer.Serialize(new
      { Namespace = "eqr-decision-v1", reviewer.FirmId, engagementId = w.EngagementId }));
    var lockKey = unchecked((long)Convert.ToUInt64(lockDigest[..16], 16));
    await using var lockHolder = new AuditSphereDbContext(pg.Options);
    await lockHolder.Database.OpenConnectionAsync(ct);
    await lockHolder.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({lockKey})", ct);
    var blockingPid = await lockHolder.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync(ct);

    var applicationName = "eqr-auth-wait-" + Guid.NewGuid().ToString("N");
    var waiterConnection = new NpgsqlConnectionStringBuilder(pg.ConnectionString) { ApplicationName = applicationName }.ConnectionString;
    var waiterOptions = new DbContextOptionsBuilder<AuditSphereDbContext>().UseNpgsql(waiterConnection).Options;
    await using var waiter = new AuditSphereDbContext(waiterOptions);
    await using var observer = new AuditSphereDbContext(pg.Options);
    await using var revoker = new AuditSphereDbContext(pg.Options);
    var administrator = await StaffingCertificationTestEvidence.CreateAdministratorAsync(revoker, w.FirmId);
    Task<CommandResult<Guid>>? pending = null;
    CommandResult? revocation = null;
    var observedWait = false;
    Exception? setupFailure = null;
    try
    {
      pending = AuditDeliverableService.RecordEqrDecisionAsync(waiter, reviewer, w.EngagementId, request, ct);
      try
      {
        await WaitUntilBlockedByAsync(observer, applicationName, blockingPid, ct);
        observedWait = true;
        revocation = await RoleAdministrationService.RevokeRoleGrantAsync(revoker, administrator, new(reviewerGrantId), ct);
      }
      catch (Exception exception)
      {
        setupFailure = exception;
      }
    }
    finally
    {
      try
      {
        var released = await lockHolder.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({lockKey}) AS \"Value\"")
          .SingleAsync(CancellationToken.None);
        Assert.True(released, "The test did not release its EQR lock.");
      }
      finally
      {
        await lockHolder.Database.CloseConnectionAsync();
      }
    }

    var decision = await pending!.WaitAsync(ct);
    if (setupFailure is not null) throw setupFailure;
    Assert.True(observedWait, "The EQR command did not wait on the held engagement lock.");
    Assert.True(revocation?.Succeeded, revocation?.Message);
    Assert.False(decision.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, decision.ErrorCode);
    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await proof.EqrDecisions.AsNoTracking().Where(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId).ToListAsync(ct));
    Assert.Empty(await proof.EqrCases.AsNoTracking().Where(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId).ToListAsync(ct));
  }

  [Fact]
  public async Task SigningAndBundleRequireEqrConcurrenceForExactReportIdentity()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var reportId = await ReportReadyForSigningAsync(pg, w);
    var partner = w.A("partner", "Partner");
    var reviewer = w.A("partner2", "Partner");
    await using var db = new AuditSphereDbContext(pg.Options);
    var basis = await AuditDeliverableService.GetEqrReviewBasisAsync(db, reviewer, w.EngagementId);
    Assert.True(basis.Succeeded, basis.Message);
    Assert.Equal(reportId, basis.Value!.ReportDeliverableId);

    var caseId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    var reportlessLegacyDecision = new EqrDecision
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, EqrCaseId = caseId,
      Revision = 1, Status = "CONCURRED", FindingsDiscussed = true, DecidedByUserId = reviewer.UserId, DecidedAt = now,
      ReviewedFactsDigest = basis.Value.FactsDigest, RequestKey = Guid.NewGuid().ToString("D")
    };
    db.EqrCases.Add(new EqrCase
    {
      Id = caseId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
      EqrPartnerUserId = reviewer.UserId, Status = "CONCURRED", ConcurrenceDate = DateOnly.FromDateTime(now.UtcDateTime),
      FindingsDiscussed = true, CompletedAt = now, Revision = 1, CurrentDecisionId = reportlessLegacyDecision.Id,
      CurrentDecisionRevision = 1, CreatedAt = now
    });
    db.EqrDecisions.Add(reportlessLegacyDecision);
    await db.SaveChangesAsync();

    var reportlessSign = await AuditDeliverableService.SignIndependentReportAsync(db, partner, reportId);
    Assert.False(reportlessSign.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, reportlessSign.ErrorCode);
    Assert.Empty(await db.SignatureApplications.AsNoTracking().Where(x => x.FirmId == w.FirmId && x.SourceDeliverableId == reportId).ToListAsync());

    basis = await AuditDeliverableService.GetEqrReviewBasisAsync(db, reviewer, w.EngagementId);
    Assert.True(basis.Succeeded, basis.Message);
    var reportlessConcurrence = await AuditDeliverableService.RecordEqrDecisionAsync(db, reviewer, w.EngagementId,
      new RecordEqrDecisionRequest("CONCURRED", "Attempt concurrence without report basis.", true, Guid.NewGuid().ToString("D"),
        basis.Value!.CaseRevision, basis.Value.FactsDigest, null, null));
    Assert.False(reportlessConcurrence.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, reportlessConcurrence.ErrorCode);
    var exactDecision = await AuditDeliverableService.RecordEqrDecisionAsync(db, reviewer, w.EngagementId,
      new RecordEqrDecisionRequest("CONCURRED", "Reviewed the exact current report.", true, Guid.NewGuid().ToString("D"),
        basis.Value!.CaseRevision, basis.Value.FactsDigest, basis.Value.ReportDeliverableId, basis.Value.ReportSha256));
    Assert.True(exactDecision.Succeeded, exactDecision.Message);
    var exactSign = await AuditDeliverableService.SignIndependentReportAsync(db, partner, reportId);
    Assert.True(exactSign.Succeeded, exactSign.Message);

    var eqrCase = await db.EqrCases.SingleAsync(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId);
    var reportlessCurrentDecision = new EqrDecision
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, EqrCaseId = eqrCase.Id,
      Revision = eqrCase.Revision + 1, Status = "CONCURRED", FindingsDiscussed = true, DecidedByUserId = reviewer.UserId,
      DecidedAt = DateTimeOffset.UtcNow, ReviewedFactsDigest = basis.Value.FactsDigest,
      RequestKey = Guid.NewGuid().ToString("D"), PreviousDecisionId = exactDecision.Value
    };
    db.EqrDecisions.Add(reportlessCurrentDecision);
    eqrCase.Status = reportlessCurrentDecision.Status;
    eqrCase.Notes = reportlessCurrentDecision.Notes;
    eqrCase.FindingsDiscussed = true;
    eqrCase.Revision = reportlessCurrentDecision.Revision;
    eqrCase.CurrentDecisionId = reportlessCurrentDecision.Id;
    eqrCase.CurrentDecisionRevision = reportlessCurrentDecision.Revision;
    eqrCase.ConcurrenceDate = DateOnly.FromDateTime(reportlessCurrentDecision.DecidedAt.UtcDateTime);
    eqrCase.CompletedAt = reportlessCurrentDecision.DecidedAt;
    await db.SaveChangesAsync();

    var bundle = await AuditDeliverableService.BundleStateAsync(db, partner, w.EngagementId);
    Assert.True(bundle.Succeeded, bundle.Message);
    Assert.Contains(bundle.Value!.Blockers, x => x.Contains("exact report and current completion basis", StringComparison.Ordinal));
  }

  [Fact]
  public async Task CompletionProjectionRequiresCurrentRetainedEqrDecisionAndExactCurrentReport()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var reportId = await ReportReadyForSigningAsync(pg, w);
    var engagementPartner = w.A("partner", "Partner");
    var reviewer = w.A("partner2", "Partner");
    await using var db = new AuditSphereDbContext(pg.Options);

    var withoutCase = await EngagementCompletionWorkspaceQuery.GetAsync(db, engagementPartner, w.EngagementId, DateTimeOffset.UtcNow);
    Assert.True(withoutCase.Succeeded, withoutCase.Message);
    Assert.Equal("NOT_ASSIGNED", withoutCase.Value!.EqrStatus);
    Assert.Equal("NOT_ASSIGNED", Assert.Single(withoutCase.Value.Gates, x => x.Name == "EQR completed").Status);

    // A legacy case can say CONCURRED without a retained current-decision pointer or reviewed-report basis.
    var completedAt = DateTimeOffset.UtcNow;
    db.EqrCases.Add(new EqrCase
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
      EqrPartnerUserId = reviewer.UserId, Status = "CONCURRED", ConcurrenceDate = DateOnly.FromDateTime(completedAt.UtcDateTime),
      FindingsDiscussed = true, CompletedAt = completedAt,
      Revision = 0, CurrentDecisionId = null, CurrentDecisionRevision = 0, CreatedAt = completedAt
    });
    await db.SaveChangesAsync();
    var persistedCompletedAt = await db.EqrCases.AsNoTracking()
      .Where(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId)
      .Select(x => x.CompletedAt)
      .SingleAsync();
    Assert.NotNull(persistedCompletedAt);

    var legacy = await EngagementCompletionWorkspaceQuery.GetAsync(db, engagementPartner, w.EngagementId, DateTimeOffset.UtcNow);
    Assert.True(legacy.Succeeded, legacy.Message);
    Assert.Equal("RECORDED_CONCURRENCE_NOT_CURRENT", legacy.Value!.EqrStatus);
    var legacyGate = Assert.Single(legacy.Value.Gates, x => x.Name == "EQR completed");
    Assert.Equal("RECORDED_CONCURRENCE_NOT_CURRENT", legacyGate.Status);
    Assert.Equal("pending", legacyGate.Tone);
    // PostgreSQL retains microseconds; compare the projection with the exact persisted timestamp.
    Assert.Equal(persistedCompletedAt, legacyGate.Date);

    var basis = await AuditDeliverableService.GetEqrReviewBasisAsync(db, reviewer, w.EngagementId);
    Assert.True(basis.Succeeded, basis.Message);
    Assert.Equal(reportId, basis.Value!.ReportDeliverableId);
    var request = new RecordEqrDecisionRequest("CONCURRED", "Reviewed the exact current report.", true,
      Guid.NewGuid().ToString("D"), basis.Value.CaseRevision, basis.Value.FactsDigest,
      basis.Value.ReportDeliverableId, basis.Value.ReportSha256);
    var decision = await AuditDeliverableService.RecordEqrDecisionAsync(db, reviewer, w.EngagementId, request);
    Assert.True(decision.Succeeded, decision.Message);

    var current = await EngagementCompletionWorkspaceQuery.GetAsync(db, engagementPartner, w.EngagementId, DateTimeOffset.UtcNow);
    Assert.True(current.Succeeded, current.Message);
    Assert.Equal("CONCURRED", current.Value!.EqrStatus);
    Assert.Equal("ok", Assert.Single(current.Value.Gates, x => x.Name == "EQR completed").Tone);
    var leader = NewUser(w.FirmId, "eqr-completion-leader");
    db.Users.Add(leader);
    db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = leader.Id,
      Role = "EngagementLeader", ClientId = w.ClientId, EngagementId = w.EngagementId, GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = reviewer.UserId });
    await db.SaveChangesAsync();
    var leaderView = await EngagementCompletionWorkspaceQuery.GetAsync(db,
      new ActorContext(leader.Id, w.FirmId, leader.SessionEpoch, ["EngagementLeader"]), w.EngagementId, DateTimeOffset.UtcNow);
    Assert.True(leaderView.Succeeded, leaderView.Message);
    Assert.Equal("CONCURRED", leaderView.Value!.EqrStatus);
    Assert.Equal("ok", Assert.Single(leaderView.Value.Gates, x => x.Name == "EQR completed").Tone);

    var nextReport = await AuditDeliverableService.GenerateReportAsync(db, w.A("manager", "Manager"), w.EngagementId,
      DeliverableKinds.IndependentAuditorsReport);
    Assert.True(nextReport.Succeeded, nextReport.Message);
    Assert.NotEqual(reportId, nextReport.Value!.DeliverableId);
    var stale = await EngagementCompletionWorkspaceQuery.GetAsync(db, engagementPartner, w.EngagementId, DateTimeOffset.UtcNow);
    Assert.True(stale.Succeeded, stale.Message);
    Assert.Equal("RECORDED_CONCURRENCE_NOT_CURRENT", stale.Value!.EqrStatus);
    Assert.Equal("pending", Assert.Single(stale.Value.Gates, x => x.Name == "EQR completed").Tone);
    var retainedCase = await db.EqrCases.AsNoTracking().SingleAsync(x => x.EngagementId == w.EngagementId);
    Assert.Equal(("CONCURRED", decision.Value), (retainedCase.Status, retainedCase.CurrentDecisionId));
    Assert.Single(await db.EqrDecisions.AsNoTracking().Where(x => x.EngagementId == w.EngagementId).ToListAsync());

    var laterBasis = await AuditDeliverableService.GetEqrReviewBasisAsync(db, reviewer, w.EngagementId);
    Assert.True(laterBasis.Succeeded, laterBasis.Message);
    var inProgress = await AuditDeliverableService.RecordEqrDecisionAsync(db, reviewer, w.EngagementId,
      new RecordEqrDecisionRequest("IN_PROGRESS", "Review remains in progress.", false, Guid.NewGuid().ToString("D"),
        laterBasis.Value!.CaseRevision, laterBasis.Value.FactsDigest, laterBasis.Value.ReportDeliverableId, laterBasis.Value.ReportSha256));
    Assert.True(inProgress.Succeeded, inProgress.Message);
    var notConcurred = await EngagementCompletionWorkspaceQuery.GetAsync(db, engagementPartner, w.EngagementId, DateTimeOffset.UtcNow);
    Assert.True(notConcurred.Succeeded, notConcurred.Message);
    Assert.Equal("IN_PROGRESS", notConcurred.Value!.EqrStatus);
    Assert.Equal("pending", Assert.Single(notConcurred.Value.Gates, x => x.Name == "EQR completed").Tone);
    retainedCase = await db.EqrCases.AsNoTracking().SingleAsync(x => x.EngagementId == w.EngagementId);
    Assert.Equal(("IN_PROGRESS", inProgress.Value), (retainedCase.Status, retainedCase.CurrentDecisionId));
    Assert.Equal(2, await db.EqrDecisions.AsNoTracking().CountAsync(x => x.EngagementId == w.EngagementId));
    Assert.Equal("CONCURRED", (await db.EqrDecisions.AsNoTracking().SingleAsync(x => x.Id == decision.Value)).Status);
  }

  private static async Task WaitUntilBlockedByAsync(AuditSphereDbContext observer, string waitingApplicationName,
    int blockingPid, CancellationToken ct)
  {
    using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
    bounded.CancelAfter(TimeSpan.FromSeconds(15));
    try
    {
      while (true)
      {
        var blocked = await observer.Database.SqlQuery<bool>($"""
          SELECT EXISTS (
            SELECT 1 FROM pg_stat_activity AS waiting
            WHERE waiting.datname = current_database() AND waiting.application_name = {waitingApplicationName}
              AND waiting.wait_event_type = 'Lock' AND {blockingPid} = ANY(pg_blocking_pids(waiting.pid))
          ) AS "Value"
          """).SingleAsync(bounded.Token);
        if (blocked) return;
        await Task.Delay(TimeSpan.FromMilliseconds(20), bounded.Token);
      }
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      throw new TimeoutException("The EQR decision did not wait on the held advisory lock.");
    }
  }

  [Fact]
  public async Task ReleaseCandidateReplayRequiresCurrentAuthorityAndPackageEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var partner = w.A("partner", "Partner");
    var (packageId, _) = await CompletionBundleFixture.ReleasedFinancialPackageAsync(pg.Options,
      new CompletionBundleScope(w.FirmId, w.ClientId, w.EngagementId, w.U));
    await using var db = new AuditSphereDbContext(pg.Options);
    // The released fixture uses its own manifest; prepare the workspace's exact calculation-hash candidate.
    var prepared = await EngagementCompletionWorkspaceQuery.PrepareReleaseCandidateAsync(db, partner, w.EngagementId, packageId);
    Assert.True(prepared.Succeeded, prepared.Message);
    var candidate = await db.ReleaseCandidates.AsNoTracking().SingleAsync(x => x.Id == prepared.Value);
    var approvals = await db.Approvals.CountAsync();
    var replay = await EngagementCompletionWorkspaceQuery.PrepareReleaseCandidateAsync(db, partner, w.EngagementId, packageId);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(prepared.Value, replay.Value);
    Assert.Equal(approvals, await db.Approvals.CountAsync());

    var forgedPartner = await EngagementCompletionWorkspaceQuery.PrepareReleaseCandidateAsync(db,
      w.A("associate", "Partner"), w.EngagementId, packageId);
    Assert.False(forgedPartner.Succeeded);
    Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.ScopeDenied, forgedPartner.ErrorCode);
    Assert.Equal(Guid.Empty, forgedPartner.Value);
    var staleSession = await EngagementCompletionWorkspaceQuery.PrepareReleaseCandidateAsync(db,
      partner with { SessionEpoch = partner.SessionEpoch + 1 }, w.EngagementId, packageId);
    Assert.False(staleSession.Succeeded);
    Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.GenerationStale, staleSession.ErrorCode);
    Assert.Equal(Guid.Empty, staleSession.Value);

    var returned = await CashFlowSourceReviewCall.RecordAsync(db, w.A("senior", "AccountingReviewer"),
      packageId, "RETURNED", "Synthetic correction requires independent source re-review.");
    Assert.True(returned.Succeeded, returned.Message);
    var staleWorkspace = await EngagementCompletionWorkspaceQuery.GetAsync(db, partner, w.EngagementId, DateTimeOffset.UtcNow);
    Assert.True(staleWorkspace.Succeeded, staleWorkspace.Message);
    Assert.False(staleWorkspace.Value!.CanPrepareRelease);
    var stale = await EngagementCompletionWorkspaceQuery.PrepareReleaseCandidateAsync(db, partner, w.EngagementId, packageId);
    Assert.False(stale.Succeeded);
    Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.GateBlocked, stale.ErrorCode);
    Assert.Equal(Guid.Empty, stale.Value);
    Assert.Equal(approvals, await db.Approvals.CountAsync());
    Assert.Single(await db.ReleaseCandidates.Where(x => x.TargetId == packageId &&
      x.TargetRevision == candidate.TargetRevision && x.ManifestDigest == candidate.ManifestDigest).ToListAsync());
  }

  [Fact]
  public async Task CompletionProjectionShowsTheExactCheckpointAndPersistedReleaseIdentity()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var partner = w.A("partner", "Partner");
    var (packageId, releaseId) = await CompletionBundleFixture.ReleasedFinancialPackageAsync(pg.Options,
      new CompletionBundleScope(w.FirmId, w.ClientId, w.EngagementId, w.U));

    await using var db = new AuditSphereDbContext(pg.Options);
    var workspace = await EngagementCompletionWorkspaceQuery.GetAsync(db, partner, w.EngagementId, DateTimeOffset.UtcNow);
    Assert.True(workspace.Succeeded, workspace.Message);
    var release = Assert.IsType<CompletionRelease>(workspace.Value!.Release);
    var storedRelease = await db.Releases.AsNoTracking().SingleAsync(x => x.Id == releaseId);
    var checkpoint = await db.ReleaseCheckpoints.AsNoTracking().SingleAsync(x => x.Id == storedRelease.CheckpointId);

    Assert.Equal((releaseId, ReleaseStates.Issued, "ISSUED"),
      (release.ReleaseId, release.CandidateStatus, release.IssueState));
    Assert.Equal(storedRelease.ManifestDigest, release.ManifestDigest);
    Assert.Equal(storedRelease.ManifestDigest, release.ReleaseManifestDigest);
    Assert.Equal(checkpoint.Id, release.CheckpointId);
    Assert.Equal(checkpoint.Id, release.ReleaseCheckpointId);
    Assert.Equal("VERIFIED", release.CheckpointState);
    Assert.True(release.CheckpointMatchesCandidate);
    Assert.True(release.CheckpointReadBackMatchesManifest);
    Assert.True(release.ReleaseMatchesCandidate);
    Assert.Equal("AWAITING_AUTHORIZATION_OR_CONFIGURATION", release.DeliveryDisposition);
    Assert.Equal(storedRelease.ReleasedAt, release.ReleasedAt);

    Assert.True(workspace.Value.PackageEvidence!.Current, workspace.Value.PackageEvidence.Message);
    Assert.True(workspace.Value.PartnerApproved);
    var decisions = await db.FinancialPackageReviewDecisions.AsNoTracking()
      .Where(x => x.FinancialPackageId == packageId).OrderBy(x => x.Sequence).ToListAsync();
    var expectedReviewEvidence = decisions.Select(x => new CompletionPackageReview(x.Id, x.FinancialPackageId,
      x.PackageRevision, x.PackageGeneration, x.PackageHash, x.FinancialPackageArtifactId, x.ArtifactVersion,
      x.ArtifactSha256Hex, x.PresentationRevisionId, x.PresentationCompositionId, x.CompositionManifestDigest,
      x.Stage, x.Decision, x.EvidenceMode, x.EvidenceReference, x.DecidedByUserId, x.DecidedAt, x.Sequence)).ToList();
    Assert.Equal(expectedReviewEvidence, workspace.Value.PackageReviews);
    var returned = await CashFlowSourceReviewCall.RecordAsync(db, w.A("senior", "AccountingReviewer"),
      packageId, "RETURNED", "Synthetic correction requires independent source re-review.");
    Assert.True(returned.Succeeded, returned.Message);

    var stale = await EngagementCompletionWorkspaceQuery.GetAsync(db, partner, w.EngagementId, DateTimeOffset.UtcNow);
    Assert.True(stale.Succeeded, stale.Message);
    Assert.False(stale.Value!.PackageEvidence!.Current);
    Assert.False(stale.Value.PartnerApproved);
    Assert.False(stale.Value.CanPrepareRelease);
    var packageGates = stale.Value.Gates.Where(x => x.Name.StartsWith("Package ", StringComparison.Ordinal)).ToList();
    Assert.NotEmpty(packageGates);
    Assert.All(packageGates, gate => { Assert.Equal("RECORDED_APPROVAL", gate.Status); Assert.Equal("neutral", gate.Tone); });
    Assert.Equal(expectedReviewEvidence, stale.Value.PackageReviews);
    // An issued record is historical fact even when its source is no longer current for a new release.
    Assert.Equal(releaseId, stale.Value.Release!.ReleaseId);
    Assert.Equal(release.ReleaseManifestDigest, stale.Value.Release.ReleaseManifestDigest);
    Assert.Equal("ISSUED", stale.Value.Release.IssueState);

    await db.DurableOperations.Where(x => x.FirmId == w.FirmId && x.OperationKind == "ReleaseDelivery.v1" &&
        x.CorrelationId == releaseId)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.ErrorCode, "legacy-delivery-disposition"));
    var unconfirmed = await EngagementCompletionWorkspaceQuery.GetAsync(db, partner, w.EngagementId, DateTimeOffset.UtcNow);
    Assert.True(unconfirmed.Succeeded, unconfirmed.Message);
    Assert.Equal("OUTCOME_UNCONFIRMED", unconfirmed.Value!.Release!.DeliveryDisposition);
  }

  [Fact]
  public async Task CompletionCapabilitiesRequireCurrentScopedPartnerGrant()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await CompletionBundleFixture.ReleasedFinancialPackageAsync(pg.Options,
      new CompletionBundleScope(w.FirmId, w.ClientId, w.EngagementId, w.U));

    await using var db = new AuditSphereDbContext(pg.Options);
    var partner = w.U["partner"];
    var now = DateTimeOffset.UtcNow;
    await db.RoleGrants.Where(x => x.FirmId == w.FirmId && x.UserId == partner.Id && x.Role == "Partner" && x.RevokedAt == null)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, now));
    partner = await db.Users.SingleAsync(x => x.Id == partner.Id);
    partner.SessionEpoch++;
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = partner.Id, Role = "Auditor", GrantedAt = now,
      GrantedByUserId = partner.Id
    });
    await db.SaveChangesAsync();

    // The session has been refreshed, but retains the stale Partner label alongside its valid Auditor read grant.
    var actor = new ActorContext(partner.Id, w.FirmId, partner.SessionEpoch, ["Partner", "Auditor"]);
    var workspace = await EngagementCompletionWorkspaceQuery.GetAsync(db, actor, w.EngagementId, now);

    Assert.True(workspace.Succeeded, workspace.Message);
    Assert.Empty(workspace.Value!.OpinionTypes);
    Assert.False(workspace.Value.CanPrepareRelease);
    Assert.False(workspace.Value.PackageEvidence!.Current);
  }
}
