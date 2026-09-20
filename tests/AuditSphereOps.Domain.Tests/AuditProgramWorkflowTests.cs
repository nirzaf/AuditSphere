using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditProgramWorkflowTests
{
  [Fact(DisplayName = "Audit program publishes, adopts, executes and independently reviews the controlled catalog")]
  public async Task ControlledCatalog_UsesScopedAppendOnlyWorkflow()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;

    await using var db = new AuditSphereDbContext(pg.Options);
    var published = await AuditProgramService.PublishAsync(db, scope.Actor,
      new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));

    Assert.True(published.Succeeded);
    Assert.Equal(165, published.Value!.ProcedureCount);
    Assert.True(AuditProgramCatalog.IsComplete);
    Assert.Equal(165, await db.AuditProgramProcedures.CountAsync());

    var idempotent = await AuditProgramService.PublishAsync(db, scope.Actor,
      new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
    Assert.True(idempotent.Succeeded);
    Assert.Equal(published.Value.ProgramVersionId, idempotent.Value!.ProgramVersionId);
    Assert.Equal(165, await db.AuditProgramProcedures.CountAsync());

    var adopted = await AuditProgramService.AdoptAsync(db, scope.Actor,
      new AdoptAuditProgramRequest(scope.EngagementId, published.Value.ProgramVersionId));
    Assert.True(adopted.Succeeded);
    Assert.Equal(165, adopted.Value!.ProcedureCount);
    Assert.Equal(165, await db.AuditProcedures.CountAsync(x => x.EngagementId == scope.EngagementId));
    Assert.Equal(0, await db.AuditProcedures.CountAsync(x => x.EngagementId == scope.EngagementId && x.RiskId != null));
    Assert.False((await AuditProgramService.AdoptAsync(db, scope.Actor,
      new AdoptAuditProgramRequest(fixture.Other.EngagementId, published.Value.ProgramVersionId))).Succeeded);

    var procedure = await db.AuditProcedures.SingleAsync(x => x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-08-01");
    var decided = await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityStatuses.Applicable, null));
    Assert.True(decided.Succeeded);

    var submitted = await AuditProgramService.SubmitResultAsync(db, scope.Actor,
      new SubmitProcedureResultRequest(procedure.Id, 1, "Agreed the bank listing to the ledger.",
        "{\"result\":\"PASS\"}", ["source:bank-list-1"], "No exception noted."));
    Assert.True(submitted.Succeeded);
    Assert.Equal(1, submitted.Value!.Revision);

    var reviewerId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = reviewerId,
      FirmId = scope.FirmId,
      Subject = "reviewer-" + reviewerId.ToString("N"),
      TenantId = "tenant-planning",
      Email = "reviewer@example.test",
      DisplayName = "Independent Reviewer",
      UserKind = "Staff",
      SessionEpoch = 1,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = reviewerId, Role = "Reviewer",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Actor.UserId
    });
    await db.SaveChangesAsync();

    var reviewer = new ActorContext(reviewerId, scope.FirmId, 1, ["Reviewer"]);
    var reviewed = await AuditProgramService.ReviewResultAsync(db, reviewer,
      new ReviewProcedureResultRequest(submitted.Value.AuditProcedureResultId,
        AuditProcedureReviewDecisions.Reviewed, "Evidence and conclusion agree."));
    Assert.True(reviewed.Succeeded);
    Assert.Equal(AuditProcedureStatuses.Reviewed,
      (await db.AuditProcedures.AsNoTracking().SingleAsync(x => x.Id == procedure.Id)).Status);
    Assert.Equal(1, await db.AuditProcedureReviews.CountAsync(x => x.AuditProcedureId == procedure.Id));

    var second = await db.AuditProcedures.SingleAsync(x => x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-08-02");
    var secondDecision = await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(second.Id, AuditApplicabilityStatuses.Applicable, null));
    Assert.True(secondDecision.Succeeded);
    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE client_safety_states SET input_generation = 2 WHERE id = {scope.ClientId}");
    var stale = await AuditProgramService.SubmitResultAsync(db, scope.Actor,
      new SubmitProcedureResultRequest(second.Id, 1, "Stale input check.", "{\"result\":\"PASS\"}",
        ["source:bank-list-2"], "No exception noted."));
    Assert.False(stale.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, stale.ErrorCode);
  }
}
