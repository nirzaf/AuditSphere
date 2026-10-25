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
