using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// STE v2.1 §4.1.5 / mandatory control C-02: the recorded 50% advance gates the transition into
/// PORTAL_ACTIVE_PLANNING. The requirement is enforced unconditionally by default, and the single recorded
/// deviation narrows it to engagements that already have a linked fee agreement (ADR-0012).
/// </summary>
public sealed class EngagementActivationAdvanceGateTests
{
  [Theory]
  [InlineData(AdvanceGateModes.Always, true)]
  [InlineData(AdvanceGateModes.WhenFeeAgreementLinked, true)]
  [InlineData("always", false)]
  [InlineData("WHEN_LINKED", false)]
  [InlineData("OFF", false)]
  [InlineData("", false)]
  public void AnUnrecognisedGateModeRefusesToLoadInsteadOfWeakeningTheGate(string mode, bool accepted)
  {
    var options = new EngagementActivationOptions { AdvanceGateMode = mode };
    if (accepted) options.Validate();
    else Assert.Throws<InvalidOperationException>(() => options.Validate());
  }

  [Fact]
  public void TheDefaultPolicyIsTheRequirementMode()
  {
    Assert.Equal(AdvanceGateModes.Always, new EngagementActivationOptions().AdvanceGateMode);
  }

  [Fact]
  public async Task DefaultModeRefusesActivationWithoutALinkedPaidAdvanceAndTheRecordedDeviationAllowsIt()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await EngagementActivationReviewSeed.PopulateAsync(db, f, advanceState: null);
    var partner = PbcSeed.Actor(f.Staff, "Partner");

    // Control C-02: an engagement with no linked fee agreement cannot activate on the default policy.
    var state = (await EngagementActivationWorkspace.StateAsync(db, partner, f.EngagementId)).Value!;
    Assert.False(state.Eligible);
    Assert.Contains(state.Blockers, x => x.Code == "advance.fee-agreement-missing");
    Assert.Equal(ErrorCodes.GateBlocked, (await EngagementLifecycleService.ActivateAsync(db, partner, f.EngagementId)).ErrorCode);
    // The reviewed workspace must refuse the same intent the command refuses.
    var request = new EngagementActivationRequest(Guid.NewGuid(), state.ReviewBasis);
    Assert.False((await EngagementActivationWorkspace.PreviewAsync(db, partner, f.EngagementId, request)).Succeeded);
    Assert.Empty(await db.EngagementActivations.ToListAsync());

    // The recorded deviation (ADR-0012) narrows the rule to linked agreements, so this engagement may activate.
    var deviation = new EngagementActivationOptions { AdvanceGateMode = AdvanceGateModes.WhenFeeAgreementLinked };
    Assert.True((await EngagementLifecycleService.ActivateAsync(db, partner, f.EngagementId, options: deviation)).Succeeded);
    Assert.Single(await db.EngagementActivations.ToListAsync());
  }

  [Fact]
  public async Task ALinkedAgreementAlwaysGatesEvenUnderTheRecordedDeviation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await EngagementActivationReviewSeed.PopulateAsync(db, f, FeeMilestoneStates.Planned);
    var partner = PbcSeed.Actor(f.Staff, "Partner");

    var state = (await EngagementActivationWorkspace.StateAsync(db, partner, f.EngagementId)).Value!;
    Assert.False(state.Eligible);
    Assert.Contains(state.Blockers, x => x.Code == "advance.unpaid");

    var deviation = new EngagementActivationOptions { AdvanceGateMode = AdvanceGateModes.WhenFeeAgreementLinked };
    foreach (var options in new EngagementActivationOptions?[] { null, deviation })
      Assert.Equal(ErrorCodes.GateBlocked,
        (await EngagementLifecycleService.ActivateAsync(db, partner, f.EngagementId, options: options)).ErrorCode);
    Assert.Empty(await db.EngagementActivations.ToListAsync());
  }
}
