using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>STE v2.1 §4.1.5 / mandatory control C-02: every activation requires the paid 50% advance.</summary>
public sealed class EngagementActivationAdvanceGateTests
{
  [Fact]
  public async Task ActivationWithoutLinkedFeeAgreementIsBlocked()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await EngagementActivationReviewSeed.PopulateAsync(db, f, advanceState: null);
    var partner = PbcSeed.Actor(f.Staff, "Partner");

    var state = (await EngagementActivationWorkspace.StateAsync(db, partner, f.EngagementId)).Value!;
    Assert.False(state.Eligible);
    Assert.Contains(state.Blockers, x => x.Code == "advance.fee-agreement-missing");
    Assert.Equal(ErrorCodes.GateBlocked, (await EngagementLifecycleService.ActivateAsync(db, partner, f.EngagementId)).ErrorCode);
    var request = new EngagementActivationRequest(Guid.NewGuid(), state.ReviewBasis);
    Assert.False((await EngagementActivationWorkspace.PreviewAsync(db, partner, f.EngagementId, request)).Succeeded);
    Assert.Empty(await db.EngagementActivations.ToListAsync());
  }

  [Fact]
  public async Task LinkedAgreementMustHavePaidAdvanceBeforeActivation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await EngagementActivationReviewSeed.PopulateAsync(db, f, FeeMilestoneStates.Planned);
    var partner = PbcSeed.Actor(f.Staff, "Partner");

    var state = (await EngagementActivationWorkspace.StateAsync(db, partner, f.EngagementId)).Value!;
    Assert.False(state.Eligible);
    Assert.Contains(state.Blockers, x => x.Code == "advance.unpaid");
    Assert.Equal(ErrorCodes.GateBlocked, (await EngagementLifecycleService.ActivateAsync(db, partner, f.EngagementId)).ErrorCode);
    Assert.Empty(await db.EngagementActivations.ToListAsync());
  }

  [Fact]
  public async Task PaidAdvanceAllowsActivationAfterTheRemainingReviewGatesPass()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await EngagementActivationReviewSeed.PopulateAsync(db, f, FeeMilestoneStates.Paid);
    var partner = PbcSeed.Actor(f.Staff, "Partner");

    var state = (await EngagementActivationWorkspace.StateAsync(db, partner, f.EngagementId)).Value!;
    Assert.True(state.Eligible);
    Assert.True((await EngagementLifecycleService.ActivateAsync(db, partner, f.EngagementId)).Succeeded);
    Assert.Single(await db.EngagementActivations.ToListAsync());
  }
}
