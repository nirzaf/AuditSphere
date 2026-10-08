using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// STE 3.2 manager-controlled practical rounding persisted through the real migrations: each rounding writes a new
/// effective draft with an append-only decision, a superseded draft cannot be approved, and Partner approval binds to the
/// exact rounded head. The original calculation stays unchanged.
/// </summary>
public sealed partial class PlanningResourcesAndMaterialityTests
{
  [Fact]
  public async Task PracticalRounding_SupersedesDraftsAndPartnerApprovesOnlyTheCurrentRoundedHead()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await SeedApprovedMappingAsync(pg, w);
    var manager = w.Actor("manager", "Manager");
    var partner = w.Actor("partner", "Partner");

    Guid originalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var calc = await MaterialityEngineService.CalculateAsync(db, manager,
        new(w.EngagementId, MaterialityBenchmarks.Revenue, null, 1m, 75m, 5m, "Revenue-driven trading entity"));
      Assert.True(calc.Succeeded, calc.Message);
      originalId = calc.Value!.AssessmentId;
    }

    Guid firstRoundingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Only Audit Managers may round; a Partner is refused even though the Partner later approves.
      Assert.Equal(ErrorCodes.ScopeDenied, (await MaterialityEngineService.ApplyPracticalRoundingAsync(db, partner,
        new(originalId, 20_500m, 15_750m, 975m, "Partner attempt"))).ErrorCode);
      // +5.001% on planning materiality is outside the bound.
      Assert.Equal(ErrorCodes.AuditPlanning.Invalid, (await MaterialityEngineService.ApplyPracticalRoundingAsync(db, manager,
        new(originalId, 21_001m, 15_750m, 975m, "Out of bounds"))).ErrorCode);
      Assert.Equal(ErrorCodes.AuditPlanning.Invalid, (await MaterialityEngineService.ApplyPracticalRoundingAsync(db, manager,
        new(originalId, 20_500m, 15_750m, 975m, ""))).ErrorCode);

      // Exactly +2.5%, +5% (boundary) and -2.5%.
      var first = await MaterialityEngineService.ApplyPracticalRoundingAsync(db, manager,
        new(originalId, 20_500m, 15_750m, 975m, "Rounded to the nearest 500 for planning"));
      Assert.True(first.Succeeded, first.Message);
      firstRoundingId = first.Value!.EffectiveAssessmentId;
      Assert.NotEqual(originalId, firstRoundingId);
      Assert.Equal((2.5m, 5m, -2.5m), (first.Value.Decision.PlanningDeltaPercent, first.Value.Decision.TolerableDeltaPercent, first.Value.Decision.SadDeltaPercent));
      Assert.Equal((20_000m, 15_000m, 1_000m), (first.Value.Decision.ComputedPlanningMateriality, first.Value.Decision.ComputedTolerableError, first.Value.Decision.ComputedSadThreshold));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // The original draft is superseded the moment a rounding exists.
      Assert.Equal(ErrorCodes.ProtectedState, (await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, partner, originalId)).ErrorCode);
      Assert.False(await MaterialityEngineService.IsAssessmentCurrentAsync(db, w.FirmId, originalId));
      Assert.True(await MaterialityEngineService.IsAssessmentCurrentAsync(db, w.FirmId, firstRoundingId));
    }

    Guid secondRoundingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.GenerationStale, (await MaterialityEngineService.ApplyPracticalRoundingAsync(db, manager,
        new(originalId, 20_200m, 15_000m, 1_000m, "Targeting a superseded draft"))).ErrorCode);
      var second = await MaterialityEngineService.ApplyPracticalRoundingAsync(db, manager,
        new(firstRoundingId, 20_200m, 15_000m, 1_000m, "Re-rounded after review"));
      Assert.True(second.Succeeded, second.Message);
      secondRoundingId = second.Value!.EffectiveAssessmentId;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.ProtectedState, (await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, partner, firstRoundingId)).ErrorCode);
      var approved = await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, partner, secondRoundingId);
      Assert.True(approved.Succeeded, approved.Message);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var latest = await MaterialityEngineService.GetLatestAsync(db, w.FirmId, w.EngagementId);
      Assert.Equal((secondRoundingId, MaterialityCalculationStates.Approved), (latest!.AssessmentId, latest.State));
      Assert.Equal(20_200m, latest.Rounding!.AdjustedPlanningMateriality);
      Assert.True(await MaterialityEngineService.IsPartnerApprovedCurrentAsync(db,
        await db.MaterialityAssessments.AsNoTracking().SingleAsync(x => x.Id == secondRoundingId)));
      // Rounding cannot be changed once Partner approval exists.
      Assert.Equal(ErrorCodes.ProtectedState, (await MaterialityEngineService.ApplyPracticalRoundingAsync(db, manager,
        new(secondRoundingId, 20_100m, 15_000m, 1_000m, "After approval"))).ErrorCode);
      // The original calculation is never rewritten by rounding.
      var calc = await db.MaterialityCalculations.AsNoTracking().SingleAsync(x => x.MaterialityAssessmentId == originalId);
      Assert.Equal(20_000m, calc.PlanningMateriality);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var decision = await db.MaterialityRoundingDecisions.AsNoTracking().FirstAsync(x => x.EffectiveAssessmentId == firstRoundingId);
      var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE materiality_rounding_decisions SET adjusted_planning_materiality = 1 WHERE id = {decision.Id}"));
      Assert.Contains("immutable evidence", ex.MessageText, StringComparison.Ordinal);
    }
  }
}
