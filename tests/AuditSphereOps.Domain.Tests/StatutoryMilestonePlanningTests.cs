using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class StatutoryMilestonePlanningTests
{
  [Fact]
  public void Calculator_DefaultMilestones_CalculatesStandardDatesAndArchiveDeadline()
  {
    var periodEnd = new DateOnly(2026, 12, 31);
    var statutoryCutoff = new DateOnly(2027, 4, 30);

    var result = StatutoryMilestoneCalculator.Evaluate(periodEnd, statutoryCutoff);

    Assert.Equal(statutoryCutoff, result.StatutoryFilingCutoff);
    Assert.Equal(new DateOnly(2027, 1, 7), result.FieldworkStartDate);
    Assert.Equal(new DateOnly(2027, 3, 13), result.DraftReportDate);
    Assert.Equal(new DateOnly(2027, 4, 15), result.FinalReportDate);
    // 60-day archive freeze under ISA 230 §4.4.3
    Assert.Equal(new DateOnly(2027, 6, 14), result.ArchiveDeadlineDate);
    Assert.False(result.HasAdjustments);
    Assert.Empty(result.Warnings);
    Assert.False(result.WarningOverrideRequired);
  }

  [Fact]
  public void Calculator_CustomMilestones_HonorsAdjustments()
  {
    var periodEnd = new DateOnly(2026, 12, 31);
    var statutoryCutoff = new DateOnly(2027, 4, 30);
    var fieldwork = new DateOnly(2027, 2, 1);
    var draft = new DateOnly(2027, 3, 15);
    var finalReport = new DateOnly(2027, 4, 10);

    var result = StatutoryMilestoneCalculator.Evaluate(
      periodEnd,
      statutoryCutoff,
      requestedFieldworkStart: fieldwork,
      requestedDraftReport: draft,
      requestedFinalReport: finalReport,
      adjustmentReason: "Client audit committee schedule");

    Assert.Equal(fieldwork, result.FieldworkStartDate);
    Assert.Equal(draft, result.DraftReportDate);
    Assert.Equal(finalReport, result.FinalReportDate);
    Assert.Equal(finalReport.AddDays(60), result.ArchiveDeadlineDate);
    Assert.True(result.HasAdjustments);
    Assert.Empty(result.Warnings);
  }

  [Fact]
  public void Calculator_ChronologyValidation_FailsWhenFinalReportExceedsStatutoryCutoff()
  {
    var periodEnd = new DateOnly(2026, 12, 31);
    var statutoryCutoff = new DateOnly(2027, 4, 30);
    var fieldwork = new DateOnly(2027, 1, 15);
    var draft = new DateOnly(2027, 4, 15);
    var finalReport = new DateOnly(2027, 5, 2); // Exceeds cutoff

    var error = StatutoryMilestoneCalculator.ValidateChronology(
      periodEnd, statutoryCutoff, fieldwork, draft, finalReport, finalReport.AddDays(60));

    Assert.NotNull(error);
    Assert.Contains("statutory filing cutoff", error);
  }

  [Fact]
  public void Calculator_ChronologyValidation_FailsWhenFieldworkStartsBeforePeriodEnd()
  {
    var periodEnd = new DateOnly(2026, 12, 31);
    var statutoryCutoff = new DateOnly(2027, 4, 30);
    var fieldwork = new DateOnly(2026, 12, 1); // Before period end
    var draft = new DateOnly(2027, 3, 1);
    var finalReport = new DateOnly(2027, 4, 15);

    var error = StatutoryMilestoneCalculator.ValidateChronology(
      periodEnd, statutoryCutoff, fieldwork, draft, finalReport, finalReport.AddDays(60));

    Assert.NotNull(error);
    Assert.Contains("period end", error);
  }

  [Fact]
  public void Calculator_Warnings_DetectsCompressedWindowsAndRequiresOverrideReason()
  {
    var periodEnd = new DateOnly(2026, 12, 31);
    var statutoryCutoff = new DateOnly(2027, 4, 30);
    var fieldwork = new DateOnly(2027, 4, 1);
    var draft = new DateOnly(2027, 4, 8); // Fieldwork window = 7 days (< 14)
    var finalReport = new DateOnly(2027, 4, 28); // Buffer to statutory cutoff = 2 days (< 5)

    // Without override reason -> WarningOverrideRequired is true
    var withoutReason = StatutoryMilestoneCalculator.Evaluate(
      periodEnd, statutoryCutoff, fieldwork, draft, finalReport,
      adjustmentReason: "Accelerated reporting requested by client");

    Assert.NotEmpty(withoutReason.Warnings);
    Assert.True(withoutReason.WarningOverrideRequired);

    // With override reason -> WarningOverrideRequired is false
    var withReason = StatutoryMilestoneCalculator.Evaluate(
      periodEnd, statutoryCutoff, fieldwork, draft, finalReport,
      adjustmentReason: "Accelerated reporting requested by client",
      warningOverrideReason: "Partner approved accelerated fieldwork with dedicated senior team");

    Assert.NotEmpty(withReason.Warnings);
    Assert.False(withReason.WarningOverrideRequired);
  }

  private sealed record World(Guid FirmId, Guid ClientId, Guid EngagementId, Dictionary<string, AppUser> Users)
  {
    public ActorContext Actor(string name, params string[] roles) =>
      new(Users[name].Id, FirmId, Users[name].SessionEpoch, roles);
  }

  private static AppUser NewUser(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(),
    FirmId = firmId,
    Subject = name + "-" + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-milestones",
    Email = $"{name}-{Guid.NewGuid():N}@example.test",
    DisplayName = name,
    UserKind = "Staff",
    CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role, Guid? clientId = null, Guid? engagementId = null) => new()
  {
    Id = Guid.NewGuid(),
    FirmId = firmId,
    UserId = user.Id,
    Role = role,
    ClientId = clientId,
    EngagementId = engagementId,
    GrantedAt = DateTimeOffset.UtcNow,
    GrantedByUserId = user.Id
  };

  private static async Task<World> SeedAsync(PgTestSchema pg)
  {
    var (firmId, clientId, engagementId) = await pg.SeedScopeAsync();
    var users = new[] { "partner", "manager", "senior", "outsider" }.ToDictionary(x => x, x => NewUser(firmId, x));

    await using var db = new AuditSphereDbContext(pg.Options);
    await db.Engagements.Where(x => x.Id == engagementId).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.Status, "Active")
      .SetProperty(x => x.ProfessionalWorkBlocked, false)
      .SetProperty(x => x.ServiceRoute, "FinancialStatementAudit")
      .SetProperty(x => x.PeriodEnd, "2026-12-31"));

    db.Users.AddRange(users.Values);
    db.RoleGrants.AddRange(
      Grant(firmId, users["partner"], "Partner", clientId, engagementId),
      Grant(firmId, users["manager"], "Manager", clientId, engagementId),
      Grant(firmId, users["senior"], "Senior", clientId, engagementId));

    await db.SaveChangesAsync();
    return new(firmId, clientId, engagementId, users);
  }

  [Fact]
  public async Task Service_SaveMilestones_SavesExplicitStatutoryCutoffAndCalculatesArchiveDeadline()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var manager = w.Actor("manager", "Manager");
    var statutoryCutoff = new DateOnly(2027, 4, 30);

    var saveResult = await StatutoryMilestoneService.SaveMilestonesAsync(db, manager,
      new SaveMilestonesRequest(w.EngagementId, statutoryCutoff));

    Assert.True(saveResult.Succeeded, saveResult.Message);
    var saved = saveResult.Value!;
    Assert.Equal(statutoryCutoff, saved.StatutoryFilingCutoff);
    Assert.Equal(new DateOnly(2027, 1, 7), saved.FieldworkStartDate);
    Assert.Equal(new DateOnly(2027, 3, 13), saved.DraftReportDate);
    Assert.Equal(new DateOnly(2027, 4, 15), saved.FinalReportDate);
    Assert.Equal(new DateOnly(2027, 6, 14), saved.ArchiveDeadlineDate);
    Assert.Equal(1, saved.Revision);
    Assert.True(saved.CanConfigure);

    // Verify retrieval via GetMilestonesAsync
    var getResult = await StatutoryMilestoneService.GetMilestonesAsync(db, manager, w.EngagementId);
    Assert.True(getResult.Succeeded);
    Assert.NotNull(getResult.Value);
    Assert.Equal(saved.Id, getResult.Value!.Id);

    // Verify workspace projection returns the milestone plan
    var workspaceResult = await AuditPlanWorkspaceQuery.GetAsync(db, manager, w.EngagementId);
    Assert.True(workspaceResult.Succeeded);
    Assert.NotNull(workspaceResult.Value!.MilestonePlan);
    Assert.Equal(statutoryCutoff, workspaceResult.Value!.MilestonePlan!.StatutoryFilingCutoff);
  }

  [Fact]
  public async Task Service_SaveMilestones_RequiresOverrideReasonWhenWarningsPresent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var manager = w.Actor("manager", "Manager");
    var statutoryCutoff = new DateOnly(2027, 4, 30);
    // Compressed fieldwork window (7 days) triggers warning
    var fieldwork = new DateOnly(2027, 4, 1);
    var draft = new DateOnly(2027, 4, 8);
    var finalReport = new DateOnly(2027, 4, 20);

    // Without override reason -> must fail closed
    var failed = await StatutoryMilestoneService.SaveMilestonesAsync(db, manager,
      new SaveMilestonesRequest(w.EngagementId, statutoryCutoff, fieldwork, draft, finalReport,
        AdjustmentReason: "Client requested condensed timeline"));

    Assert.False(failed.Succeeded);
    Assert.Equal("milestone.override-required", failed.ErrorCode);

    // With override reason -> succeeds and increments revision
    var succeeded = await StatutoryMilestoneService.SaveMilestonesAsync(db, manager,
      new SaveMilestonesRequest(w.EngagementId, statutoryCutoff, fieldwork, draft, finalReport,
        AdjustmentReason: "Client requested condensed timeline",
        WarningOverrideReason: "Partner confirmed double staffing on site to ensure quality"));

    Assert.True(succeeded.Succeeded, succeeded.Message);
    Assert.Equal("Partner confirmed double staffing on site to ensure quality", succeeded.Value!.WarningOverrideReason);
  }

  [Fact]
  public async Task Service_Authorization_EnforcesPlanningAndConfigureRoles()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var senior = w.Actor("senior", "Senior");
    var outsider = w.Actor("outsider", "Staff");
    var statutoryCutoff = new DateOnly(2027, 4, 30);

    // Senior can read preview
    var preview = await StatutoryMilestoneService.CalculatePreviewAsync(db, senior,
      new PreviewMilestonesRequest(w.EngagementId, statutoryCutoff));
    Assert.True(preview.Succeeded);

    // Senior cannot save milestones (requires Partner, Manager, SeniorManager, or Administrator)
    var deniedSave = await StatutoryMilestoneService.SaveMilestonesAsync(db, senior,
      new SaveMilestonesRequest(w.EngagementId, statutoryCutoff));
    Assert.False(deniedSave.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, deniedSave.ErrorCode);

    // Outsider cannot even read milestones
    var outsiderRead = await StatutoryMilestoneService.GetMilestonesAsync(db, outsider, w.EngagementId);
    Assert.False(outsiderRead.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, outsiderRead.ErrorCode);
  }

  [Fact]
  public async Task Service_ChronologyViolation_FailsClosed()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var manager = w.Actor("manager", "Manager");
    var statutoryCutoff = new DateOnly(2027, 4, 30);
    // Final report past statutory cutoff
    var fieldwork = new DateOnly(2027, 1, 15);
    var draft = new DateOnly(2027, 4, 15);
    var finalReport = new DateOnly(2027, 5, 5);

    var result = await StatutoryMilestoneService.SaveMilestonesAsync(db, manager,
      new SaveMilestonesRequest(w.EngagementId, statutoryCutoff, fieldwork, draft, finalReport));

    Assert.False(result.Succeeded);
    Assert.Equal("milestone.chronology-error", result.ErrorCode);
  }
}
