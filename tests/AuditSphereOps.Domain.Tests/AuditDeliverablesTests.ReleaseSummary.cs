using AuditSphereOps.Application.Abstractions;
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
    var (_, releaseId) = await CompletionBundleFixture.ReleasedFinancialPackageAsync(pg.Options,
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
  }
}
