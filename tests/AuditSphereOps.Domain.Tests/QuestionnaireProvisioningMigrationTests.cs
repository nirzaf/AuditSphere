using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed class QuestionnaireProvisioningMigrationTests
{
  private const string PreviousMigration = "20261105173658_ArchiveProfileIdentity";
  private const string ProvisioningMigration = "20261110083105_ProvisionAcceptanceQuestionnaireBanks";

  [Fact(DisplayName = "PMS R01: migration provisions exact banks, preserves complete identities, fills partial and refuses conflicts")]
  public async Task QuestionnaireProvisioning_IsAtomicRepeatableAndConcurrent()
  {
    await using var pg = await PgTestSchema.CreateAsync(PreviousMigration);
    Dictionary<string, Guid> originalIds;
    Guid ceTemplateId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.Database.MigrateAsync();
      var templates = await db.QuestionnaireTemplates.AsNoTracking().OrderBy(x => x.Bank).ToListAsync();
      Assert.Collection(templates,
        ce => { Assert.Equal("CE", ce.Bank); Assert.Equal("1.0", ce.Version); Assert.True(ce.IsActive); },
        rv => { Assert.Equal("RV", rv.Bank); Assert.Equal("1.1", rv.Version); Assert.True(rv.IsActive); });

      var questions = await db.QuestionDefinitions.AsNoTracking().ToListAsync();
      Assert.Equal(94, questions.Count);
      Assert.Equal(62, questions.Count(x => x.TemplateId == QuestionnaireSeed.CeTemplateId));
      Assert.Equal(32, questions.Count(x => x.TemplateId == QuestionnaireSeed.RvTemplateId));
      originalIds = questions.ToDictionary(x => x.QuestionCode, x => x.Id, StringComparer.Ordinal);
      ceTemplateId = templates.Single(x => x.Bank == "CE").Id;

      var checklist = await AcceptanceChecklistService.LoadAsync(db, Guid.NewGuid(), Guid.NewGuid(), null, 1, CancellationToken.None);
      Assert.Equal(62, checklist.Items.Count);
      Assert.All(checklist.Items, item => Assert.Null(item.Answer));
      Assert.Null(checklist.CurrentDecision);
      Assert.False(checklist.Ready);
    }

    var client = await SupportedAuditCompletionWorldBuilder.CreateClientBeforeAcceptanceAsync(pg, "Partner");
    const string answeredCode = "CE-001";
    await using var answers = new AuditSphereDbContext(pg.Options);
    var generation = await answers.ClientSafetyStates.AsNoTracking()
      .Where(x => x.FirmId == client.FirmId && x.Id == client.ClientId).Select(x => x.InputGeneration).SingleAsync();
    var initial = await AcceptanceChecklistService.LoadAsync(answers, client.FirmId, client.ClientId, null, generation, default);
    Assert.Equal(62, initial.Items.Count);
    Assert.All(initial.Items, item => Assert.Null(item.Answer));
    Assert.Null(initial.CurrentDecision);
    var saved = await AcceptanceChecklistService.RecordAnswerAsync(answers, client.Actor, client.ClientId,
      answeredCode, "Yes", "SYNTHETIC-CATALOGUE-REPLAY-EVIDENCE", expectedGeneration: generation, expectedRevision: 0);
    Assert.True(saved.Succeeded, saved.Message);
    var originalAnswer = await answers.EvaluationResponses.AsNoTracking().SingleAsync();
    Assert.Equal(answeredCode, originalAnswer.QuestionId);
    var pendingDecision = await answers.AcceptanceDecisions.AsNoTracking().SingleAsync();
    Assert.Equal((client.FirmId, client.ClientId, "Pending"),
      (pendingDecision.FirmId, pendingDecision.PracticeClientId, pendingDecision.Decision));

    async Task AssertAnswerPreservedAsync(AuditSphereDbContext db)
    {
      Assert.Equivalent(originalAnswer, await db.EvaluationResponses.AsNoTracking().SingleAsync(), strict: true);
      Assert.Equivalent(pendingDecision, await db.AcceptanceDecisions.AsNoTracking().SingleAsync(), strict: true);
    }

    // Replaying the initializer against a complete pre-existing bank must preserve all retained IDs.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await RemoveProvisioningHistoryAsync(db);
      await db.Database.MigrateAsync();
      var currentIds = await db.QuestionDefinitions.AsNoTracking()
        .ToDictionaryAsync(x => x.QuestionCode, x => x.Id, StringComparer.Ordinal);
      Assert.Equal(originalIds, currentIds);
      await AssertAnswerPreservedAsync(db);
    }

    // A compatible partial bank is completed; two concurrent deployment migrators converge on one bank.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await RemoveProvisioningHistoryAsync(db);
      Assert.Equal(1, await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM question_definitions WHERE template_id = {ceTemplateId} AND question_code = {"CE-062"}"));
    }
    await using (var first = new AuditSphereDbContext(pg.Options))
    await using (var second = new AuditSphereDbContext(pg.Options))
      await Task.WhenAll(first.Database.MigrateAsync(), second.Database.MigrateAsync());

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var current = await db.QuestionDefinitions.AsNoTracking().ToListAsync();
      Assert.Equal(94, current.Count);
      Assert.Equal(62, current.Count(x => x.TemplateId == QuestionnaireSeed.CeTemplateId));
      Assert.Equal(32, current.Count(x => x.TemplateId == QuestionnaireSeed.RvTemplateId));
      foreach (var question in current.Where(x => x.QuestionCode != "CE-062"))
        Assert.Equal(originalIds[question.QuestionCode], question.Id);
      Assert.Contains(current, x => x.QuestionCode == "CE-062");
      await AssertAnswerPreservedAsync(db);
    }

    // A conflicting prompt and a missing code refuse atomically: no missing row or migration marker is written.
    var canonicalPrompt = QuestionnaireSeed.CeQuestions.Single(x => x.QuestionCode == answeredCode).PromptText;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await RemoveProvisioningHistoryAsync(db);
      await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE question_definitions SET prompt_text = {"conflicting retained prompt"} WHERE template_id = {ceTemplateId} AND question_code = {answeredCode}");
      await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM question_definitions WHERE template_id = {ceTemplateId} AND question_code = {"CE-060"}");
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
      Assert.Equal("P0001", exception.SqlState);
      Assert.Equal(61, await db.QuestionDefinitions.CountAsync(x => x.TemplateId == ceTemplateId));
      Assert.False(await db.QuestionDefinitions.AnyAsync(x => x.TemplateId == ceTemplateId && x.QuestionCode == "CE-060"));
      Assert.DoesNotContain(await db.Database.GetAppliedMigrationsAsync(), x => x == ProvisioningMigration);
      Assert.Equal("conflicting retained prompt", await db.QuestionDefinitions.AsNoTracking()
        .Where(x => x.TemplateId == ceTemplateId && x.QuestionCode == answeredCode).Select(x => x.PromptText).SingleAsync());
      await AssertAnswerPreservedAsync(db);

      // Restore the existing approved text and retry; the missing code is then safely completed.
      await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE question_definitions SET prompt_text = {canonicalPrompt} WHERE template_id = {ceTemplateId} AND question_code = {answeredCode}");
      await db.Database.MigrateAsync();
      Assert.Equal(62, await db.QuestionDefinitions.CountAsync(x => x.TemplateId == ceTemplateId));
      Assert.Equal(94, await db.QuestionDefinitions.CountAsync());
      await AssertAnswerPreservedAsync(db);
    }
  }

  private static Task<int> RemoveProvisioningHistoryAsync(AuditSphereDbContext db) =>
    db.Database.ExecuteSqlRawAsync(
      "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {0}", ProvisioningMigration);
}
