using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class WorkflowTemplateTests
{
  [Fact]
  public async Task TemplateReviewPublicationAndSupersessionPinImmutableRevisionContent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    var author = User(firmId, "Author");
    var reviewer = User(firmId, "Reviewer");
    var authorActor = Actor(author, "Administrator");
    var reviewerActor = Actor(reviewer, "Partner");
    await SeedAsync(pg.Options, firmId, author, reviewer);

    Guid templateId;
    Guid firstRevisionId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var pbcDefinition = new WorkflowTemplatePbcDefinition("Reconcile revenue to the trial balance.",
        "Gulf Trading LLC", "Revenue", "Excel workbook", "Total must agree to the QAR 535,000 adjusted trial balance.",
        "Confidential audit evidence", "Provide a signed reconciliation with source-ledger references.");
      var incompletePbc = await WorkflowTemplateService.CreateDraftAsync(db, authorActor,
        Request(null, [Step("PBC", 1) with { PbcDefinition = pbcDefinition with { Objective = " " } }]));
      Assert.False(incompletePbc.Succeeded);
      Assert.Equal("workflow.template-invalid", incompletePbc.ErrorCode);
      Assert.Empty(await db.WorkflowTemplates.ToListAsync());

      var cyclic = await WorkflowTemplateService.CreateDraftAsync(db, authorActor,
        Request(null, [Step("A", 1, "B"), Step("B", 2, "A")]));
      Assert.False(cyclic.Succeeded);
      Assert.Equal("workflow.template-invalid", cyclic.ErrorCode);
      Assert.Empty(await db.WorkflowTemplates.ToListAsync());

      var draft = await WorkflowTemplateService.CreateDraftAsync(db, authorActor,
        Request(null, [Step("A", 1) with { PbcDefinition = pbcDefinition }, Step("B", 2, "A")]));
      Assert.True(draft.Succeeded, draft.Message);
      firstRevisionId = draft.Value;
      var revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == firstRevisionId);
      templateId = revision.TemplateId;
      Assert.Equal(1, revision.Version);
      Assert.Equal(2, await db.WorkflowTemplateSteps.CountAsync(x => x.TemplateRevisionId == firstRevisionId));
      Assert.Equal(1, await db.WorkflowTemplateDependencies.CountAsync(x => x.TemplateRevisionId == firstRevisionId));
      var persisted = await WorkflowTemplateService.GetRevisionAsync(db, authorActor, templateId, firstRevisionId);
      Assert.True(persisted.Succeeded, persisted.Message);
      Assert.Equal(pbcDefinition, Assert.Single(persisted.Value!.Steps, x => x.StableKey == "A").PbcDefinition);

      var selfReview = await WorkflowTemplateService.ReviewAsync(db, authorActor, templateId, firstRevisionId, 1,
        "Review the initial template.");
      Assert.False(selfReview.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, selfReview.ErrorCode);
      var reviewed = await WorkflowTemplateService.ReviewAsync(db, reviewerActor, templateId, firstRevisionId, 1,
        "Reviewed the scoped task definition.");
      Assert.True(reviewed.Succeeded, reviewed.Message);
      var row = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == firstRevisionId);
      Assert.Equal(WorkflowTemplateStates.Reviewed, row.Status);
      Assert.Equal(reviewer.Id, row.ReviewedByUserId);
      Assert.Equal(2, row.Revision);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var publish = await WorkflowTemplateService.PublishAsync(db, reviewerActor, templateId, firstRevisionId, 2,
        "Publish reviewed revision one.");
      Assert.True(publish.Succeeded, publish.Message);
      var published = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == firstRevisionId);
      Assert.Equal(WorkflowTemplateStates.Published, published.Status);
      Assert.Equal(3, published.Revision);
      Assert.Equal(3, await db.WorkflowTemplateDecisions.CountAsync(x => x.TemplateRevisionId == firstRevisionId));

      published.Name = "Mutated after publication";
      await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    Guid secondRevisionId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var next = await WorkflowTemplateService.CreateDraftAsync(db, authorActor,
        Request(templateId, [Step("A", 1), Step("C", 2, "A")]));
      Assert.True(next.Succeeded, next.Message);
      secondRevisionId = next.Value;
      var second = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == secondRevisionId);
      Assert.Equal(2, second.Version);
      Assert.Equal(WorkflowTemplateStates.Draft, second.Status);
      Assert.Equal(2, await db.WorkflowTemplateSteps.CountAsync(x => x.TemplateRevisionId == firstRevisionId));
      Assert.Equal("A", (await db.WorkflowTemplateSteps.SingleAsync(x => x.TemplateRevisionId == firstRevisionId && x.StableKey == "A")).StableKey);

      var reviewed = await WorkflowTemplateService.ReviewAsync(db, reviewerActor, templateId, secondRevisionId,
        second.Revision, "Reviewed changed checklist for publication.");
      Assert.True(reviewed.Succeeded, reviewed.Message);
      var revisionTwo = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == secondRevisionId);
      var published = await WorkflowTemplateService.PublishAsync(db, authorActor, templateId, secondRevisionId,
        revisionTwo.Revision, "Publish revision two after independent review.");
      Assert.True(published.Succeeded, published.Message);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(WorkflowTemplateStates.Superseded,
        (await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == firstRevisionId)).Status);
      Assert.Equal(WorkflowTemplateStates.Published,
        (await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == secondRevisionId)).Status);
      Assert.Equal(1, await db.WorkflowTemplateRevisions.CountAsync(x => x.TemplateId == templateId && x.Status == WorkflowTemplateStates.Published));
      Assert.Equal(7, await db.WorkflowTemplateDecisions.CountAsync());

      var archive = await WorkflowTemplateService.ArchiveAsync(db, authorActor, templateId,
        "Archive catalog after review.");
      Assert.True(archive.Succeeded, archive.Message);
      Assert.True((await db.WorkflowTemplates.SingleAsync(x => x.Id == templateId)).ArchivedAt.HasValue);
      Assert.Equal(8, await db.WorkflowTemplateDecisions.CountAsync());
    }
  }

  private static SaveWorkflowTemplateDraftRequest Request(Guid? templateId, IReadOnlyList<WorkflowTemplateStepDraft> steps) =>
    new(templateId, "Statutory audit base", "FinancialStatementAudit", "ProfessionalServices", steps);

  private static WorkflowTemplateStepDraft Step(string key, int order, params string[] dependencies) =>
    new(key, WorkflowTaskPhases.AuditPlanning, $"Task {key}", $"Instructions for {key}.",
      StaffingLevels.AuditManager, 60, 5, null, true, order, dependencies);

  private static async Task SeedAsync(DbContextOptions<AuditSphereDbContext> options, Guid firmId, params AppUser[] users)
  {
    await using var db = new AuditSphereDbContext(options);
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.Users.AddRange(users);
    await db.SaveChangesAsync();
    db.RoleGrants.AddRange(users.Select((user, index) => new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id,
      Role = index == 0 ? "Administrator" : "Partner", GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = user.Id
    }));
    await db.SaveChangesAsync();
  }

  private static AppUser User(Guid firmId, string label) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = $"workflow-{label}-{Guid.NewGuid():N}",
    TenantId = "tenant-test", Email = $"{label.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test",
    DisplayName = label, UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
  };

  private static ActorContext Actor(AppUser user, string role) => new(user.Id, user.FirmId, user.SessionEpoch, [role]);
}
