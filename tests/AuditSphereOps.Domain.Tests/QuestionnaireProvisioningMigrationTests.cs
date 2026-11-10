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

    // Replaying the initializer against a complete pre-existing bank must preserve all retained IDs.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await RemoveProvisioningHistoryAsync(db);
      await db.Database.MigrateAsync();
      var currentIds = await db.QuestionDefinitions.AsNoTracking()
        .ToDictionaryAsync(x => x.QuestionCode, x => x.Id, StringComparer.Ordinal);
      Assert.Equal(originalIds, currentIds);
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
    }

    // A conflicting prompt and a missing code refuse atomically: no missing row or migration marker is written.
    var canonicalPrompt = QuestionnaireSeed.CeQuestions.Single(x => x.QuestionCode == "CE-061").PromptText;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await RemoveProvisioningHistoryAsync(db);
      await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE question_definitions SET prompt_text = {"conflicting retained prompt"} WHERE template_id = {ceTemplateId} AND question_code = {"CE-061"}");
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

      // Restore the existing approved text and retry; the missing code is then safely completed.
      await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE question_definitions SET prompt_text = {canonicalPrompt} WHERE template_id = {ceTemplateId} AND question_code = {"CE-061"}");
      await db.Database.MigrateAsync();
      Assert.Equal(62, await db.QuestionDefinitions.CountAsync(x => x.TemplateId == ceTemplateId));
      Assert.Equal(94, await db.QuestionDefinitions.CountAsync());
    }
  }

  private static Task<int> RemoveProvisioningHistoryAsync(AuditSphereDbContext db) =>
    db.Database.ExecuteSqlRawAsync(
      "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {0}", ProvisioningMigration);
}
