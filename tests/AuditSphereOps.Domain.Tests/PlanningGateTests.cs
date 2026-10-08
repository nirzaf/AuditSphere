using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// STE-REM-04: every substantive execution route — procedure results with a linked risk, no-risk
/// procedures, and sampled-item testing — requires the approved planning basis; planning preparation
/// itself and intake stay usable before approval. The basis is established through the real
/// calculate/approve commands, never direct status edits.
/// </summary>
[Trait("Profile", "Database")]
public sealed class PlanningGateTests
{
  [Fact(DisplayName = "Substantive execution is blocked without planning readiness and works after legitimate approval")]
  public async Task SubstantiveExecution_RequiresApprovedPlanningBasis()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;

    await using var db = new AuditSphereDbContext(pg.Options);
    var approver = await NewScopedPartnerAsync(db, scope);

    var published = await AuditProgramService.PublishAsync(db, scope.Actor,
      new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
    Assert.True(published.Succeeded, published.Message);
    var adopted = await AuditProgramService.AdoptAsync(db, scope.Actor,
      new AdoptAuditProgramRequest(scope.EngagementId, published.Value!.ProgramVersionId));
    Assert.True(adopted.Succeeded, adopted.Message);
    var procedure = await db.AuditProcedures.SingleAsync(x => x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-02-01");
    Assert.True((await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityStatuses.Applicable, null))).Succeeded);

    // Program tailoring and workpaper preparation stay usable before approval (no basis yet).
    var workpaper = await AuditProgramService.GetOrCreateWorkpaperAsync(db, scope.Actor, procedure.Id);
    Assert.True(workpaper.Succeeded, workpaper.Message);

    // A no-risk (unlinked) procedure cannot execute without the approved planning basis.
    var refused = await AuditProgramService.SubmitResultAsync(db, scope.Actor,
      new SubmitProcedureResultRequest(procedure.Id, 1, "Early work attempt", "{\"result\":\"PASS\"}", ["source:early-1"], "No exception noted."));
    Assert.False(refused.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
    Assert.Contains("Planning is not approved", refused.Message, StringComparison.OrdinalIgnoreCase);
    Assert.Empty(await db.AuditProcedureResults.AsNoTracking().Where(x => x.AuditProcedureId == procedure.Id).ToListAsync());

    // Legitimate approval through the real commands unblocks the same execution path.
    await PlanningBasisSeed.EstablishAsync(db, scope.FirmId, scope.ClientId, scope.EngagementId, scope.Actor, approver);
    var submitted = await AuditProgramService.SubmitResultAsync(db, scope.Actor,
      new SubmitProcedureResultRequest(procedure.Id, 1, "Agreed the bank listing to the ledger.",
        "{\"result\":\"PASS\"}", ["source:bank-list-1"], "No exception noted."));
    Assert.True(submitted.Succeeded, submitted.Message);
    Assert.Equal(1, submitted.Value!.Revision);
  }

  [Fact(DisplayName = "Sampled-item execution and review require the approved planning basis")]
  public async Task SampledItemTesting_RequiresApprovedPlanningBasis()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;
    var preparer = scope.Actor;

    await using var db = new AuditSphereDbContext(pg.Options);
    var (approver, reviewer) = (await NewScopedPartnerAsync(db, scope), await NewScopedPartnerAsync(db, scope));

    var published = await AuditProgramService.PublishAsync(db, preparer,
      new PublishAuditProgramRequest("2026.2", AuditProgramCatalog.SourceHash));
    Assert.True(published.Succeeded, published.Message);
    var adopted = await AuditProgramService.AdoptAsync(db, preparer,
      new AdoptAuditProgramRequest(scope.EngagementId, published.Value!.ProgramVersionId));
    Assert.True(adopted.Succeeded, adopted.Message);
    var procedureId = await db.AuditProcedures.Where(x => x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-08-01")
      .Select(x => x.Id).SingleAsync();
    Assert.True((await AuditProgramService.DecideApplicabilityAsync(db, preparer,
      new DecideProcedureApplicabilityRequest(procedureId, AuditApplicabilityStatuses.Applicable, "Bank population requires item testing"))).Succeeded);

    var schedule = await AuditFieldworkService.CreateScheduleAsync(db, preparer, new CreateScheduleRequest(
      scope.EngagementId, "BANK_LISTING", "bank-001", "receipt-pg-bank", new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
      new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "debits positive; credits negative",
      Hashing.Sha256Hex("planning-gate-bank"), 100m,
      [new ScheduleRowInput("bank-row-1", 1, "1100", "Operating account", 100m, "QAR", null, new DateOnly(2026, 12, 31), null, null, "{\"source\":\"bank\"}")]));
    Assert.True(schedule.Succeeded, schedule.Message);
    Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
      new ReviewScheduleRequest(schedule.Value!.ScheduleId, "Source rows and control total reviewed.", true))).Succeeded);
    var selection = await AuditFieldworkService.CreateSelectionAsync(db, preparer, new CreateSelectionRequest(
      scope.EngagementId, procedureId, schedule.Value!.ScheduleId, null, "ALL",
      "Select the complete bank row.",
      [new SelectionItemInput("bank-row-1", 100m, "QAR", "Complete population")]));
    Assert.True(selection.Succeeded, selection.Message);
    await AuditFieldworkService.ReviewSelectionAsync(db, reviewer,
      new ReviewSelectionRequest(selection.Value!.SelectionId, AuditSelectionStatuses.Reviewed, null));
    var selectionItemId = await db.AuditSelectionItems.Where(x => x.SelectionId == selection.Value.SelectionId)
      .Select(x => x.Id).SingleAsync();

    // Sampled-item execution is refused before the planning basis is approved.
    var refusedTest = await AuditFieldworkService.RecordItemTestAsync(db, preparer,
      new RecordItemTestRequest(selectionItemId, "Agreed to bank evidence.", ["receipt:one"], AuditItemTestResults.Pass, null, null, null));
    Assert.False(refusedTest.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, refusedTest.ErrorCode);
    Assert.Contains("Planning is not approved", refusedTest.Message, StringComparison.OrdinalIgnoreCase);

    // After legitimate approval the same item can be tested and independently reviewed.
    await PlanningBasisSeed.EstablishAsync(db, scope.FirmId, scope.ClientId, scope.EngagementId, preparer, approver);
    var test = await AuditFieldworkService.RecordItemTestAsync(db, preparer,
      new RecordItemTestRequest(selectionItemId, "Agreed to bank evidence.", ["receipt:one"], AuditItemTestResults.Pass, null, null, null));
    Assert.True(test.Succeeded, test.Message);
    Assert.True((await AuditFieldworkService.ReviewItemTestAsync(db, reviewer,
      new ReviewItemTestRequest(test.Value!.AuditItemTestId, AuditItemTestReviewDecisions.Reviewed, "Evidence agrees."))).Succeeded);
  }

  /// <summary>Creates a scoped Partner identity with a persisted user and role grant.</summary>
  private static async Task<ActorContext> NewScopedPartnerAsync(AuditSphereDbContext db, PlanningScope scope)
  {
    var userId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = userId, FirmId = scope.FirmId, Subject = "planning-gate-" + userId.ToString("N"),
      TenantId = "tenant-planning", Email = $"planning-gate-{userId:N}@example.test", DisplayName = "Planning Gate Partner",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = userId, Role = "Partner",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = userId
    });
    await db.SaveChangesAsync();
    return new ActorContext(userId, scope.FirmId, 1, ["Partner"]);
  }
}
