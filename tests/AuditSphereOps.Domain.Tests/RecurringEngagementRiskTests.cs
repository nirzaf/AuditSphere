using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class RecurringEngagementRiskTests
{
  [Fact]
  public async Task PriorClientAcceptanceCannotActivateRecurringPeriodAndExactDecisionRestoresEligibility()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var priorDecisionId = await EngagementActivationReviewSeed.PopulateAsync(db, f);
    var target = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == f.EngagementId);
    var prior = new Engagement { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
      ServiceRoute = target.ServiceRoute, ServiceProfileId = "Historical reference only",
      PeriodStart = "2025-01-01", PeriodEnd = "2025-12-31", CreatedAt = DateTimeOffset.UtcNow.AddYears(-1) };
    db.Engagements.Add(prior); await db.SaveChangesAsync(); var actor = PbcSeed.Actor(f.Staff, "Partner");
    Assert.Null(await EngagementRiskEvidence.LatestAsync(db, f.FirmId, target, default));
    Assert.Null(await EngagementRiskEvidence.ForPeriodAsync(db, f.FirmId, f.ClientId, target.ServiceRoute,
      target.PeriodStart, target.PeriodEnd, default));
    var state = await EngagementActivationWorkspace.StateAsync(db, actor, target.Id);
    Assert.True(state.Succeeded, state.Message); Assert.False(state.Value!.Eligible);
    Assert.Contains(state.Value.Blockers, x => x.Code == "acceptance.missing");
    Assert.False((await EngagementLifecycleService.ActivateAsync(db, actor, target.Id)).Succeeded);
    Assert.Empty(await db.EngagementActivations.ToListAsync());

    var fresh = Decision(f, target.Id, target.ServiceRoute, target.Generation);
    db.AcceptanceDecisions.Add(fresh); await db.SaveChangesAsync();
    Assert.Equal(fresh.Id, (await EngagementRiskEvidence.LatestAsync(db, f.FirmId, target, default))!.Id);
    Assert.True((await EngagementActivationWorkspace.StateAsync(db, actor, target.Id)).Value!.Eligible);
    var activation = await EngagementLifecycleService.ActivateAsync(db, actor, target.Id);
    Assert.True(activation.Succeeded, activation.Message);
    Assert.Equal(fresh.Id, (await db.EngagementActivations.AsNoTracking().SingleAsync()).AcceptanceDecisionId);
    Assert.True(await db.AcceptanceDecisions.AnyAsync(x => x.Id == priorDecisionId));
    Assert.Equal("2025-12-31", (await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == prior.Id)).PeriodEnd);
  }

  [Fact]
  public async Task SiblingDecisionIsExcludedAndLaterClientDeclineStillBlocks()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); await EngagementActivationReviewSeed.PopulateAsync(db, f);
    var target = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == f.EngagementId);
    var sibling = new Engagement { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
      ServiceRoute = target.ServiceRoute, PeriodStart = "2025-01-01", PeriodEnd = "2025-12-31", CreatedAt = DateTimeOffset.UtcNow };
    db.Engagements.Add(sibling); db.AcceptanceDecisions.Add(Decision(f, sibling.Id, target.ServiceRoute, target.Generation));
    await db.SaveChangesAsync(); Assert.Null(await EngagementRiskEvidence.LatestAsync(db, f.FirmId, target, default));
    var exact = Decision(f, target.Id, target.ServiceRoute, target.Generation);
    db.AcceptanceDecisions.Add(exact); await db.SaveChangesAsync();
    var declined = Decision(f, null, target.ServiceRoute, target.Generation); declined.Decision = "Declined";
    declined.DecidedAt = DateTimeOffset.UtcNow.AddSeconds(1); db.AcceptanceDecisions.Add(declined); await db.SaveChangesAsync();
    Assert.Equal("Declined", (await EngagementRiskEvidence.LatestAsync(db, f.FirmId, target, default))!.Decision);
    Assert.False((await EngagementLifecycleService.ActivateAsync(db, PbcSeed.Actor(f.Staff, "Partner"), target.Id)).Succeeded);
    Assert.Null(await EngagementRiskEvidence.LatestAsync(db, Guid.NewGuid(), target, default));
  }

  [Fact]
  public async Task FirstPeriodCompatibilityRemainsButRecurringLetterWithoutExactShellHasNoClearance()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); var id = await EngagementActivationReviewSeed.PopulateAsync(db, f);
    var target = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == f.EngagementId);
    Assert.Equal(id, (await EngagementRiskEvidence.LatestAsync(db, f.FirmId, target, default))!.Id);
    Assert.Null(await EngagementRiskEvidence.ForPeriodAsync(db, f.FirmId, f.ClientId, target.ServiceRoute,
      "2027-01-01", "2027-12-31", default));
    var wrongService = await AcceptanceDecisionService.RecordAsync(db, PbcSeed.Actor(f.Staff, "Partner"),
      new(f.ClientId, target.Id, "InternalAudit", "Declined", "Synthetic mismatch", null, target.Generation));
    Assert.Equal("scope.denied", wrongService.ErrorCode);
  }

  private static AcceptanceDecision Decision(PbcSeed.Fixture f, Guid? engagementId, string route, long generation) =>
    new() { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, EngagementId = engagementId,
      ServiceRoute = route, Generation = generation, Decision = "Accepted", Rationale = "Synthetic fresh scoped decision",
      Path = AcceptancePaths.Continuance, EvaluationTemplateVersion = "synthetic.v1", EvaluationSnapshotDigest = new string('b', 64),
      DecidedByUserId = f.Staff.Id, DecidedAt = DateTimeOffset.UtcNow };
}
