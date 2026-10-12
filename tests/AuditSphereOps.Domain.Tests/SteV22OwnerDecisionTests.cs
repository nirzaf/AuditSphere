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
/// continuance-question data migration against a database that already holds the bank, the pure band rule, and the working week.
/// </summary>
public sealed class SteV22OwnerDecisionTests
{
  private const string MigrationBeforeDecisions = "20261012080058_WorkflowChangesRequiredQueue";

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
  public void CapacityDeductsLeaveOnASundayButNotOnTheWeekend()
  {
    var monday = new DateOnly(2026, 10, 12);
    var friday = monday.AddDays(4);
    var sunday = monday.AddDays(6);
    static StaffAvailability Leave(DateOnly day) => new() { StartDate = day, EndDate = day, MinutesPerDay = 480, Kind = StaffAvailabilityKinds.Leave };

    var weekendLeave = ResourceGridCalculator.Build(2400, [Leave(friday)], [], [], monday, 1).Single();
    Assert.Equal((2400, 0), (weekendLeave.CapacityMinutes, weekendLeave.UnavailableMinutes));

    var workingDayLeave = ResourceGridCalculator.Build(2400, [Leave(sunday)], [], [], monday, 1).Single();
    Assert.Equal((1920, 480), (workingDayLeave.CapacityMinutes, workingDayLeave.UnavailableMinutes));
  }
}
