using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Practice;
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
    Assert.Contains(state.Blockers, x => x.Code == "commercial.not-current");
    Assert.Null(state.Commercial);
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
    Assert.NotNull(state.Commercial);
    Assert.DoesNotContain(state.Blockers, x => x.Code == "commercial.not-current");
    Assert.Equal(ErrorCodes.GateBlocked, (await EngagementLifecycleService.ActivateAsync(db, partner, f.EngagementId)).ErrorCode);
    Assert.Empty(await db.EngagementActivations.ToListAsync());
  }

  [Fact]
  public async Task TwentyThousandFeeRequiresFourThousandPlusSixThousandAllocated_NotAPaidMarker()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    // Deliberately inconsistent legacy marker: only a 4,000 receipt is applied.
    // The 20,000 agreement requires a fully allocated 10,000 advance.
    await EngagementActivationReviewSeed.PopulateAsync(db, f, FeeMilestoneStates.Paid, allocatedAdvance: 4000m);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager", f.ClientId));
    await db.SaveChangesAsync();
    var partner = PbcSeed.Actor(f.Staff, "Partner");
    var state = await EngagementActivationWorkspace.StateAsync(db, partner, f.EngagementId);
    Assert.True(state.Succeeded, state.Message);
    Assert.False(state.Value!.Eligible);
    Assert.Contains(state.Value.Blockers, x => x.Code == "advance.unpaid");
    Assert.Equal(ErrorCodes.GateBlocked, (await EngagementLifecycleService.ActivateAsync(db, partner, f.EngagementId)).ErrorCode);
    Assert.Empty(await db.EngagementActivations.ToListAsync());

    var milestone = await db.FeeMilestones.AsNoTracking().SingleAsync(x => x.FirmId == f.FirmId);
    var finance = PbcSeed.Actor(f.Admin, "FinanceManager");
    var originalReceipt = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == milestone.ReceiptId!.Value);
    var secondReceipt = await BillingService.RecordReceiptAsync(db, finance,
      new(originalReceipt.BillingAccountId, 5999.99m, "SYNTHETIC-SECOND-ADVANCE", RequestId: Guid.CreateVersion7()));
    Assert.True(secondReceipt.Succeeded, secondReceipt.Message);
    var allocation = await BillingService.AllocateReceiptCommandAsync(db, finance,
      new(secondReceipt.Value, milestone.InvoiceId!.Value, 5999.99m, Guid.CreateVersion7()));
    Assert.True(allocation.Succeeded, allocation.Message);
    Assert.Equal(9999.99m, await BillingService.GetNetAppliedToInvoiceAsync(db, f.FirmId, milestone.InvoiceId.Value));
    var oneCentShort = await EngagementActivationWorkspace.StateAsync(db, partner, f.EngagementId);
    Assert.True(oneCentShort.Succeeded, oneCentShort.Message);
    Assert.False(oneCentShort.Value!.Eligible);
    Assert.Contains(oneCentShort.Value.Blockers, x => x.Code == "advance.unpaid");
    Assert.Equal(ErrorCodes.GateBlocked,
      (await EngagementLifecycleService.ActivateAsync(db, partner, f.EngagementId)).ErrorCode);
    Assert.Empty(await db.EngagementActivations.ToListAsync());
    var finalCentReceipt = await BillingService.RecordReceiptAsync(db, finance,
      new(originalReceipt.BillingAccountId, 0.01m, "SYNTHETIC-FINAL-ADVANCE-CENT", RequestId: Guid.CreateVersion7()));
    Assert.True(finalCentReceipt.Succeeded, finalCentReceipt.Message);
    var finalCent = await BillingService.AllocateReceiptCommandAsync(db, finance,
      new(finalCentReceipt.Value, milestone.InvoiceId.Value, 0.01m, Guid.CreateVersion7()));
    Assert.True(finalCent.Succeeded, finalCent.Message);
    Assert.Equal(10000m, await BillingService.GetNetAppliedToInvoiceAsync(db, f.FirmId, milestone.InvoiceId.Value));
    var ready = await EngagementActivationWorkspace.StateAsync(db, partner, f.EngagementId);
    Assert.True(ready.Succeeded, ready.Message);
    Assert.True(ready.Value!.Eligible);
    Assert.True((await EngagementLifecycleService.ActivateAsync(db, partner, f.EngagementId)).Succeeded);
    Assert.Single(await db.EngagementActivations.ToListAsync());
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
    Assert.NotNull(state.Commercial);
    Assert.True((await EngagementLifecycleService.ActivateAsync(db, partner, f.EngagementId)).Succeeded);
    Assert.Single(await db.EngagementActivations.ToListAsync());
  }
}
