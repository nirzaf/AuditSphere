using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// ADR-0017: the STE v2.2 decisions the owner delegated. These cover the parts that do not sit in an existing fixture: the
/// continuance-question data migration against a database that already holds the bank, the pure band rule, the working week, and the
/// move of stored allocation weeks from a Monday to a Sunday start.
/// </summary>
public sealed class SteV22OwnerDecisionTests
{
  private const string MigrationBeforeDecisions = "20261012080058_WorkflowChangesRequiredQueue";
  private const string MigrationBeforeSundayWeek = "20261012095835_SteV22OwnerDecisions";

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ContinuanceMigration_AddsTheTrackBQuestionsToADatabaseThatAlreadyHoldsTheBank()
  {
    await using var pg = await PgTestSchema.CreateAsync(MigrationBeforeDecisions);
    await using var db = new AuditSphereDbContext(pg.Options);
    var templateId = QuestionnaireSeed.RvTemplateId;
    // A database from before the decisions: the continuance template at 1.0 with its concluding question in position 30.
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO questionnaire_templates (id, bank, version, name, is_active, created_at)
      VALUES ({templateId}, 'RV', '1.0', 'Continuance bank', true, statement_timestamp())
      """);
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO question_definitions
        (id, template_id, question_code, section, prompt_text, category, answer_type, requires_evidence, sort_order, adverse_answer, escalates_on_change)
      VALUES ({Guid.NewGuid()}, {templateId}, 'RV-030', 'B.1', 'Should the relationship continue?', 'Continuance', 'BOOLEAN', true, 30, 'NO', false)
      """);

    await db.Database.MigrateAsync();

    Assert.Equal("1.1", (await db.QuestionnaireTemplates.AsNoTracking().SingleAsync(x => x.Id == templateId)).Version);
    var questions = await db.QuestionDefinitions.AsNoTracking().Where(x => x.TemplateId == templateId)
      .OrderBy(x => x.SortOrder).ToListAsync();
    Assert.Equal(["RV-031", "RV-032", "RV-030"], questions.Select(x => x.QuestionCode));
    var legalNotice = questions.Single(x => x.QuestionCode == "RV-031");
    Assert.Equal((true, "YES", true), (legalNotice.RequiresEvidence, legalNotice.AdverseAnswer, legalNotice.EscalatesOnChange));
    var feesSettled = questions.Single(x => x.QuestionCode == "RV-032");
    Assert.Equal((true, "NO", false), (feesSettled.RequiresEvidence, feesSettled.AdverseAnswer, feesSettled.EscalatesOnChange));
    // The migrated rows carry the same wording as the seed that new databases load.
    Assert.Equal(QuestionnaireSeed.RvQuestions.Single(x => x.QuestionCode == "RV-031").PromptText, legalNotice.PromptText);
    Assert.Equal(QuestionnaireSeed.RvQuestions.Single(x => x.QuestionCode == "RV-032").PromptText, feesSettled.PromptText);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ContinuanceMigration_InsertsNothingWhenTheBankWasNeverLoaded()
  {
    await using var pg = await PgTestSchema.CreateAsync(MigrationBeforeDecisions);
    await using var db = new AuditSphereDbContext(pg.Options);

    await db.Database.MigrateAsync();

    // The migration never invents a template: questions appear only where the continuance bank already exists.
    Assert.Empty(await db.QuestionnaireTemplates.AsNoTracking().ToListAsync());
    Assert.Empty(await db.QuestionDefinitions.AsNoTracking().ToListAsync());
  }

  [Fact]
  public void RecordedCriticalEstimateBandsRedAndLeavesEveryOtherAssessmentUnchanged()
  {
    Assert.Equal(RiskBands.Green, RiskBandRules.Band(1, 1, significant: false, fraudRisk: false));
    Assert.Equal(RiskBands.Amber, RiskBandRules.Band(3, 1, significant: false, fraudRisk: false));
    Assert.Equal(RiskBands.Red, RiskBandRules.Band(1, 1, significant: false, fraudRisk: false, criticalEstimate: true));
    // The rule version is deliberately unchanged, so results captured under it are not marked stale.
    Assert.Equal("STE-RISK-BAND-2026.1", RiskBandRules.RuleVersion);
  }

  [Fact]
  public void FirmWorkingWeekIsSundayToThursday()
  {
    // 2026-10-11 is a Sunday.
    var sunday = new DateOnly(2026, 10, 11);
    Assert.Equal(DayOfWeek.Sunday, sunday.DayOfWeek);
    Assert.True(ResourceGridCalculator.IsWorkingDay(sunday));
    Assert.True(ResourceGridCalculator.IsWorkingDay(sunday.AddDays(4)));   // Thursday
    Assert.False(ResourceGridCalculator.IsWorkingDay(sunday.AddDays(5)));  // Friday
    Assert.False(ResourceGridCalculator.IsWorkingDay(sunday.AddDays(6)));  // Saturday
  }

  [Fact]
  public void FirmWeekStartsOnSunday()
  {
    var sunday = new DateOnly(2026, 10, 11);
    // Every day from Sunday to Saturday belongs to the week that opened on that Sunday; the next Sunday opens the next week.
    for (var offset = 0; offset < 7; offset++)
      Assert.Equal(sunday, ResourceGridCalculator.WeekStart(sunday.AddDays(offset)));
    Assert.Equal(sunday.AddDays(7), ResourceGridCalculator.WeekStart(sunday.AddDays(7)));
    Assert.Equal(sunday.AddDays(-7), ResourceGridCalculator.WeekStart(sunday.AddDays(-1)));
  }

  [Fact]
  public void CapacityDeductsLeaveOnASundayButNotOnTheWeekend()
  {
    var sunday = new DateOnly(2026, 10, 11);
    var friday = sunday.AddDays(5);
    static StaffAvailability Leave(DateOnly day) => new() { StartDate = day, EndDate = day, MinutesPerDay = 480, Kind = StaffAvailabilityKinds.Leave };

    var weekendLeave = ResourceGridCalculator.Build(2400, [Leave(friday)], [], [], sunday, 1).Single();
    Assert.Equal((sunday, 2400, 0), (weekendLeave.WeekStart, weekendLeave.CapacityMinutes, weekendLeave.UnavailableMinutes));

    var workingDayLeave = ResourceGridCalculator.Build(2400, [Leave(sunday)], [], [], sunday, 1).Single();
    Assert.Equal((1920, 480), (workingDayLeave.CapacityMinutes, workingDayLeave.UnavailableMinutes));

    // The five working days share one bucket: leave across Sunday to Thursday removes the whole week, not parts of two weeks.
    var wholeWeek = new StaffAvailability { StartDate = sunday, EndDate = sunday.AddDays(4), MinutesPerDay = 480, Kind = StaffAvailabilityKinds.Leave };
    var cells = ResourceGridCalculator.Build(2400, [wholeWeek], [], [], sunday, 2);
    Assert.Equal((0, 2400), (cells[0].CapacityMinutes, cells[0].UnavailableMinutes));
    Assert.Equal((2400, 0), (cells[1].CapacityMinutes, cells[1].UnavailableMinutes));
    // 2,401 minutes must not lose its remainder or retain phantom capacity after a full week of leave.
    Assert.Equal(481, ResourceGridCalculator.DailyCapacityMinutes(2401, DayOfWeek.Sunday));
    Assert.Equal(480, ResourceGridCalculator.DailyCapacityMinutes(2401, DayOfWeek.Thursday));
    Assert.Equal(2401, ResourceGridCalculator.FirmWorkingWeek.Sum(day => ResourceGridCalculator.DailyCapacityMinutes(2401, day)));
    var oddLeave = new StaffAvailability { StartDate = sunday, EndDate = sunday.AddDays(4), MinutesPerDay = 481, Kind = StaffAvailabilityKinds.Leave };
    var oddWeek = ResourceGridCalculator.Build(2401, [oddLeave], [], [], sunday, 1).Single();
    Assert.Equal((0, 2401), (oddWeek.CapacityMinutes, oddWeek.UnavailableMinutes));

  }

  [Fact]
  public void ResourceUtilizationKeepsPlannedBillableAndAllApprovedTimeSeparate()
  {
    var sunday = new DateOnly(2026, 10, 11);
    // UX-AC-25 is a calculator fixture, not authority to save a profile/allocation above
    // the existing 80-hour command limit. All approved time includes 20 non-billable hours.
    var cell = ResourceGridCalculator.Build(6000, [],
      [new StaffAllocation { WeekStart = sunday, PlannedMinutes = 6600 }],
      [(sunday, 4800), (sunday.AddDays(1), 1200)], sunday, 1,
      approvedBillableTime: [(sunday, 4800)]).Single();
    Assert.Equal((6000, 6600, 6000, 4800),
      (cell.CapacityMinutes, cell.PlannedMinutes, cell.ApprovedActualMinutes, cell.ApprovedBillableMinutes));
    Assert.Equal(110m, cell.PlannedUtilizationPercent);
    Assert.Equal(80m, cell.ActualUtilizationPercent);
    Assert.True(cell.OverAllocated);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task SundayWeekMigration_MovesStoredMondayAllocationsIntoTheWeekThatContainsThem()
  {
    await using var pg = await PgTestSchema.CreateAsync(MigrationBeforeSundayWeek);
    var scope = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    // Keep real owner-scoped parents and foreign keys intact, so upgrading through every
    // later migration also validates the transformed legacy allocations.
    var firmId = scope.FirmId; var engagementId = scope.EngagementId; var userId = scope.Staff.Id;
    var first = Guid.NewGuid(); var second = Guid.NewGuid();
    foreach (var (id, monday, minutes) in new[] { (first, new DateOnly(2026, 10, 5), 1200), (second, new DateOnly(2026, 10, 12), 600) })
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO staff_allocations (id, firm_id, client_id, engagement_id, user_id, week_start, planned_minutes, updated_at, updated_by_user_id)
        VALUES ({id}, {firmId}, {scope.ClientId}, {engagementId}, {userId}, {monday}, {minutes}, statement_timestamp(), {userId})
        """);

    await db.Database.MigrateAsync();

    var stored = await db.StaffAllocations.AsNoTracking().OrderBy(x => x.WeekStart).ToListAsync();
    // Each Monday-start bucket becomes the Sunday-start week that contains that Monday, with its identity and minutes intact.
    Assert.Equal([(first, new DateOnly(2026, 10, 4), 1200), (second, new DateOnly(2026, 10, 11), 600)],
      stored.Select(x => (x.Id, x.WeekStart, x.PlannedMinutes)));
    Assert.All(stored, x => Assert.Equal(x.WeekStart, ResourceGridCalculator.WeekStart(x.WeekStart)));
    // After the change the database refuses a week that does not start on Sunday.
    var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO staff_allocations (id, firm_id, client_id, engagement_id, user_id, week_start, planned_minutes, updated_at, updated_by_user_id)
      VALUES ({Guid.NewGuid()}, {firmId}, {scope.ClientId}, {engagementId}, {userId}, {new DateOnly(2026, 10, 19)}, 60, statement_timestamp(), {userId})
      """));
    Assert.Equal("ck_staff_allocation_values", refused.ConstraintName);
  }
}
