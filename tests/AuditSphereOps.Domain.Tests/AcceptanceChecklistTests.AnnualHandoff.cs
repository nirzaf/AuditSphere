using System.Text.Json;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AcceptanceChecklistTests
{
  [Fact]
  public async Task AnnualChecklistHandoffRequiresFreshReviewedEvaluationAndPinsTheNewDecision()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await SeedAsync(pg);
    var foreign = await SeedAsync(pg, seedBanks: false);
    await using var db = new AuditSphereDbContext(pg.Options);
    var source = await EngagementLifecycleService.CreateDraftAsync(db, w.Partner,
      new(w.ClientId, "AccountingOnly", "2026-01-01", "2026-12-31", "SYNTHETIC"));
    Assert.True(source.Succeeded, source.Message);
    await AnswerAllAsync(pg, w);
    var prior = await AcceptanceDecisionService.RecordAsync(db, w.Partner,
      new(w.ClientId, source.Value, "AccountingOnly", "Accepted", "Synthetic prior-period decision", null, 1));
    Assert.True(prior.Succeeded, prior.Message);
    var priorJson = JsonSerializer.Serialize(await db.AcceptanceDecisions.AsNoTracking().SingleAsync(x => x.Id == prior.Value));
    var target = await EngagementLifecycleService.CreateDraftAsync(db, w.Partner,
      new(w.ClientId, "AccountingOnly", "2027-01-01", "2027-12-31", "SYNTHETIC"));
    Assert.True(target.Succeeded, target.Message);
    var closed = await AssessmentWorkspaceQuery.GetAsync(db, w.Manager, w.ClientId, engagementId: target.Value);
    Assert.True(closed.Succeeded, closed.Message);
    Assert.Equal("FRESH_EVALUATION_REQUIRED", closed.Value!.TargetEngagement!.State);
    Assert.True(closed.Value.Checklist.CanStartContinuance); Assert.False(closed.Value.Checklist.CanEdit);
    Assert.False((await AssessmentWorkspaceQuery.GetAsync(db, w.Manager, foreign.ClientId, engagementId: target.Value)).Succeeded);
    Assert.False((await AssessmentWorkspaceQuery.GetAsync(db, w.Manager, w.ClientId, engagementId: Guid.NewGuid())).Succeeded);
    Assert.False((await AssessmentWorkspaceQuery.GetAsync(db, w.Partner, w.ClientId, prior.Value, engagementId: target.Value)).Succeeded);
    var request = await ReviewAsync(db, w.Manager, w.ClientId, new("CONTINUANCE", "1"));
    var opened = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Manager, w.ClientId, request);
    Assert.True(opened.Succeeded, opened.Message);
    Assert.Equal(opened.Value!.Id, (await AssessmentCommandWorkspace.ExecuteAsync(db, w.Manager, w.ClientId, request)).Value!.Id);
    var fresh = await AssessmentWorkspaceQuery.GetAsync(db, w.Partner, w.ClientId, engagementId: target.Value);
    Assert.True(fresh.Succeeded, fresh.Message);
    Assert.Equal(("EVALUATION_OPEN", "2", "CONTINUANCE"),
      (fresh.Value!.TargetEngagement!.State, fresh.Value.Checklist.Generation, fresh.Value.Checklist.Path));
    Assert.All(fresh.Value.Checklist.Questions, x => Assert.Null(x.Answer));
    Assert.False(fresh.Value.Checklist.Ready);
    await AnswerAllAsync(pg, w);
    var decisionRequest = await ReviewAsync(db, w.Partner, w.ClientId, new("DECISION", "2",
      ServiceRoute: "AccountingOnly", Decision: "Accepted", Rationale: "Synthetic fresh annual decision", EngagementId: target.Value));
    var recorded = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Partner, w.ClientId, decisionRequest);
    Assert.True(recorded.Succeeded, recorded.Message);
    var current = await AssessmentWorkspaceQuery.GetAsync(db, w.Partner, w.ClientId, engagementId: target.Value);
    Assert.Equal("DECISION_RECORDED", current.Value!.TargetEngagement!.State);
    Assert.Equal(target.Value, current.Value.SelectedDecision!.EngagementId);
    Assert.False(current.Value.Checklist.CanDecide);
    Assert.Equal(priorJson, JsonSerializer.Serialize(await db.AcceptanceDecisions.AsNoTracking().SingleAsync(x => x.Id == prior.Value)));
    Assert.True(await db.EvaluationResponses.AnyAsync(x => x.PracticeClientId == w.ClientId && x.Generation == 1));
    Assert.Empty(await db.EngagementActivations.ToListAsync());
    Assert.True((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == target.Value)).ProfessionalWorkBlocked);
    await db.RoleGrants.Where(x => x.UserId == w.Manager.UserId).ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    Assert.False((await AssessmentWorkspaceQuery.GetAsync(db, w.Manager, w.ClientId, engagementId: target.Value)).Succeeded);
  }
}
