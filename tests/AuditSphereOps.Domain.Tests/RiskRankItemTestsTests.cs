using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// STE-REM-05: sampled-item execution and review apply the effective FSLI risk policy and reject a
/// review recorded against an obsolete planning basis; the final automatic SRM handoff requires a
/// current Manager workprogramme approval; completion readers see Partner opinion controls only as
/// an Engagement Partner.
/// </summary>
[Trait("Profile", "Database")]
public sealed class RiskRankItemTestsTests
{
  [Fact(DisplayName = "Staff cannot execute or review a Red-risk selection; a stale basis rejects the review")]
  public async Task SampledItemRanks_AndStaleBasis_EnforceEffectiveRisk()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;
    await using var db = new AuditSphereDbContext(pg.Options);

    var (senior, manager, partner) = (await PartnerActorAsync(db, scope), await PartnerActorAsync(db, scope), await PartnerActorAsync(db, scope));
    // Staff actor with an engagement staffing rank.
    var staffId = Guid.NewGuid();
    var certificationRecordedAt = DateTimeOffset.UtcNow;
    db.StaffCertifications.AddRange(
      new StaffCertification { Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = manager.UserId, Name = "ACCA", RecordedAt = certificationRecordedAt, RecordedByUserId = manager.UserId },
      new StaffCertification { Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = senior.UserId, Name = "CPA", RecordedAt = certificationRecordedAt, RecordedByUserId = manager.UserId });
    db.Users.Add(NewStaffUser(scope.FirmId, staffId));
    db.RoleGrants.Add(Grant(scope, staffId, "Staff"));
    var seniorId = senior.UserId;
    var reviewManagerId = manager.UserId;
    await db.SaveChangesAsync();

    var staffAssigned = await StaffingService.AssignAsync(db, scope.Actor, new(scope.EngagementId, staffId, StaffingLevels.StaffAssociate));
    Assert.True(staffAssigned.Succeeded, $"staff: {staffAssigned.Message}");
    var seniorAssigned = await StaffingService.AssignAsync(db, scope.Actor, new(scope.EngagementId, seniorId, StaffingLevels.SeniorAuditor));
    Assert.True(seniorAssigned.Succeeded, $"senior: {seniorAssigned.Message}");
    var managerAssigned = await StaffingService.AssignAsync(db, scope.Actor, new(scope.EngagementId, reviewManagerId, StaffingLevels.AuditManager));
    Assert.True(managerAssigned.Succeeded, $"manager: {managerAssigned.Message}");

    var published = await AuditProgramService.PublishAsync(db, scope.Actor,
      new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
    Assert.True(published.Succeeded, published.Message);
    Assert.True((await AuditProgramService.AdoptAsync(db, scope.Actor, new(scope.EngagementId, published.Value!.ProgramVersionId))).Succeeded);
    var procedureId = await db.AuditProcedures.Where(x => x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-08-01")
      .Select(x => x.Id).SingleAsync();
    Assert.True((await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(procedureId, AuditApplicabilityStatuses.Applicable, null))).Succeeded);

    // A known material FSLI: mapped revenue above PM yields an effective Red basis via a linked risk.
    var revenueRisk = await AuditPlanningService.CreateAuditRiskAsync(db, scope.Actor,
      new(scope.EngagementId, "Revenue", "Occurrence", "Critical accounting estimate for revenue recognition", "Volume",
        SignificanceDecisions.Significant, null, "Detailed testing"));
    Assert.True(revenueRisk.Succeeded, revenueRisk.Message);
    Assert.True((await AuditProgramService.LinkRiskAsync(db, scope.Actor, procedureId, revenueRisk.Value!.RiskId)).Succeeded);
    Assert.True((await RiskBandService.AssessAsync(db, scope.Actor,
      new AssessRiskBandRequest(revenueRisk.Value.RiskId, 1, 1, true, "Presumed fraud risk"))).Succeeded);

    // The approved planning basis is required for substantive execution (STE-REM-04).
    await PlanningBasisSeed.EstablishAsync(db, scope.FirmId, scope.ClientId, scope.EngagementId, scope.Actor, manager);

    var now = DateTimeOffset.UtcNow;
    var schedule = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor, new CreateScheduleRequest(
      scope.EngagementId, "SALES_LISTING", "E", "PBC-RR-1", now, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
      "QAR", "debits positive; credits negative", Hashing.Sha256Hex("rank-sales"), 600m,
      [new ScheduleRowInput("inv-001", 1, "4000", "Invoice one", 600m, "QAR", new DateOnly(2026, 1, 15), null, null, null, "{}")]));
    Assert.True(schedule.Succeeded, schedule.Message);
    var scheduleReview = await AuditFieldworkService.ReviewScheduleAsync(db, manager, new(schedule.Value!.ScheduleId, "Source rows reviewed.", true));
    Assert.True(scheduleReview.Succeeded, $"schedule review: {scheduleReview.ErrorCode} {scheduleReview.Message}");
    var selection = await AuditFieldworkService.CreateSelectionAsync(db, scope.Actor, new CreateSelectionRequest(
      scope.EngagementId, procedureId, schedule.Value.ScheduleId, null, "ALL", "Select all sales rows.",
      [new SelectionItemInput("inv-001", 600m, "QAR", "Complete population")]));
    Assert.True(selection.Succeeded, selection.Message);
    Assert.True((await AuditFieldworkService.ReviewSelectionAsync(db, manager,
      new(selection.Value!.SelectionId, AuditSelectionStatuses.Reviewed, null))).Succeeded);
    var selectionItemId = await db.AuditSelectionItems.Where(x => x.SelectionId == selection.Value.SelectionId).Select(x => x.Id).SingleAsync();

    // Staff (rank 1) cannot execute a Red-risk selection.
    var staffDenied = await AuditFieldworkService.RecordItemTestAsync(db, new(staffId, scope.FirmId, 1, ["Staff"]),
      new(selectionItemId, "Work performed.", ["evidence:one"], AuditItemTestResults.Pass, null, null, null));
    Assert.Equal(ErrorCodes.ScopeDenied, staffDenied.ErrorCode);
    Assert.Contains("requires execution by", staffDenied.Message, StringComparison.OrdinalIgnoreCase);

    // Manager (rank 3) may execute; Senior (rank 2) may not review a Red selection (Partner required).
    var recorded = await AuditFieldworkService.RecordItemTestAsync(db, manager,
      new(selectionItemId, "Work performed against the selected item.", ["evidence:one"], AuditItemTestResults.Pass, null, null, null));
    Assert.True(recorded.Succeeded, recorded.Message);
    var seniorDenied = await AuditFieldworkService.ReviewItemTestAsync(db, senior,
      new(recorded.Value!.AuditItemTestId, AuditItemTestReviewDecisions.Reviewed, null));
    Assert.Equal(ErrorCodes.ScopeDenied, seniorDenied.ErrorCode);

    // Engagement inputs changed after the test: the recorded basis is obsolete and the review refuses.
    await db.ClientSafetyStates.Where(x => x.Id == scope.ClientId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
    var routing = await RiskBandService.GetRoutingAsync(db, manager, scope.EngagementId);
    Assert.Equal(RiskBands.Red, routing.Value!.Single(x => x.RiskId == revenueRisk.Value!.RiskId).Band);
    var staleReview = await AuditFieldworkService.ReviewItemTestAsync(db, partner,
      new(recorded.Value.AuditItemTestId, AuditItemTestReviewDecisions.Reviewed, null));
    Assert.Equal(ErrorCodes.GenerationStale, staleReview.ErrorCode);
  }

  private static AppUser NewStaffUser(Guid firmId, Guid userId) => new()
  {
    Id = userId, FirmId = firmId, Subject = "rank-user-" + userId.ToString("N"), TenantId = "tenant-planning",
    Email = $"rank-user-{userId:N}@example.test", DisplayName = "Rank User", UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(PlanningScope scope, Guid userId, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = userId, Role = role,
    ClientId = scope.ClientId, EngagementId = scope.EngagementId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Actor.UserId
  };

  private static async Task<ActorContext> PartnerActorAsync(AuditSphereDbContext db, PlanningScope scope)
  {
    var userId = Guid.NewGuid();
    db.Users.Add(NewStaffUser(scope.FirmId, userId));
    db.RoleGrants.Add(Grant(scope, userId, "Partner"));
    await db.SaveChangesAsync();
    return new(userId, scope.FirmId, 1, ["Partner"]);
  }
}
