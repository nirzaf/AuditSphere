using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
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

    var legacy = await EngagementCompletionWorkspaceQuery.GetAsync(db, engagementPartner, w.EngagementId, DateTimeOffset.UtcNow);
    Assert.True(legacy.Succeeded, legacy.Message);
    Assert.Equal("RECORDED_CONCURRENCE_NOT_CURRENT", legacy.Value!.EqrStatus);
    var legacyGate = Assert.Single(legacy.Value.Gates, x => x.Name == "EQR completed");
    Assert.Equal("RECORDED_CONCURRENCE_NOT_CURRENT", legacyGate.Status);
    Assert.Equal("pending", legacyGate.Tone);
    Assert.Equal(completedAt, legacyGate.Date);

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
