using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class EngagementWorkflowPlanTests
{
  [Fact]
  public async Task PlanPinsPublishedTemplateAndSchedule_AndRecoversRetryWithoutDuplicateTasks()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var secondEngagementId = Guid.NewGuid();
    var admin = User(firmId, "PlanAdmin");
    var partner = User(firmId, "PlanPartner");
    var staff = User(firmId, "PlanStaff");
    var adminActor = Actor(admin, "Administrator");
    var partnerActor = Actor(partner, "Partner");
    var staffActor = Actor(staff, "Staff");
    await SeedAsync(pg.Options, firmId, clientId, engagementId, secondEngagementId, admin, partner, staff);

    Guid templateId;
    Guid revisionId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var draft = await WorkflowTemplateService.CreateDraftAsync(db, adminActor,
        new SaveWorkflowTemplateDraftRequest(null, "Annual statutory audit", "FinancialStatementAudit", "ProfessionalServices",
        [
          Step("PLANNING", 1, -10),
          Step("FIELDWORK", 2, -5, ["PLANNING"])
        ]));
      Assert.True(draft.Succeeded, draft.Message);
      revisionId = draft.Value;
      var revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == revisionId);
      templateId = revision.TemplateId;
      var initialCatalog = await WorkflowTemplateService.ListAsync(db, adminActor);
      Assert.True(initialCatalog.Succeeded, initialCatalog.Message);
      var initialCatalogItem = Assert.Single(initialCatalog.Value!, x => x.TemplateId == templateId);
      Assert.Null(initialCatalogItem.CurrentPublishedRevision);
      var reviewed = await WorkflowTemplateService.ReviewAsync(db, partnerActor, templateId, revisionId,
        revision.Revision, "Review the annual audit task plan.");
      Assert.True(reviewed.Succeeded, reviewed.Message);
      revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == revisionId);
      var published = await WorkflowTemplateService.PublishAsync(db, partnerActor, templateId, revisionId,
        revision.Revision, "Publish the reviewed annual task plan.");
      Assert.True(published.Succeeded, published.Message);
      var catalog = await WorkflowTemplateService.ListAsync(db, adminActor);
      Assert.True(catalog.Succeeded, catalog.Message);
      var catalogItem = Assert.Single(catalog.Value!, x => x.TemplateId == templateId);
      Assert.Equal(revisionId, catalogItem.CurrentPublishedRevision?.RevisionId);
      Assert.Equal(1, catalogItem.CurrentPublishedRevision?.Version);

      var missingSchedule = await EngagementWorkflowPlanService.PreviewAsync(db, adminActor, engagementId,
        new PreviewEngagementWorkflowPlanRequest(revisionId));
      Assert.False(missingSchedule.Succeeded);
      Assert.Equal("workflow.plan-schedule-required", missingSchedule.ErrorCode);

      var schedule = await StatutoryMilestoneService.SaveMilestonesAsync(db, adminActor,
        new SaveMilestonesRequest(engagementId, new DateOnly(2027, 4, 30)));
      Assert.True(schedule.Succeeded, schedule.Message);

      var preview = await EngagementWorkflowPlanService.PreviewAsync(db, adminActor, engagementId,
        new PreviewEngagementWorkflowPlanRequest(revisionId));
      Assert.True(preview.Succeeded, preview.Message);
      Assert.True(preview.Value!.CanExecute);
      Assert.Equal(new DateOnly(2027, 4, 20), preview.Value!.Steps[0].DueDate);
      Assert.Equal(new DateOnly(2027, 4, 25), preview.Value.Steps[1].DueDate);
      Assert.Equal(schedule.Value!.Id, preview.Value!.MilestonePlanId);
      Assert.Equal(schedule.Value.Revision, preview.Value.MilestonePlanRevision);
      var staffPreview = await EngagementWorkflowPlanService.PreviewAsync(db, staffActor, engagementId,
        new PreviewEngagementWorkflowPlanRequest(revisionId));
      Assert.True(staffPreview.Succeeded, staffPreview.Message);
      Assert.False(staffPreview.Value!.CanExecute);

      var idempotencyKey = Guid.NewGuid();
      var request = new ExecuteEngagementWorkflowPlanRequest(revisionId, preview.Value.PreviewDigest, idempotencyKey);
      await using var concurrentA = new AuditSphereDbContext(pg.Options);
      await using var concurrentB = new AuditSphereDbContext(pg.Options);
      var concurrent = await Task.WhenAll(
        EngagementWorkflowPlanService.ExecuteAsync(concurrentA, adminActor, engagementId, request),
        EngagementWorkflowPlanService.ExecuteAsync(concurrentB, adminActor, engagementId, request));
      Assert.All(concurrent, result => Assert.True(result.Succeeded, result.Message));
      Assert.Single(concurrent, result => result.Value!.Replayed);
      Assert.Equal(concurrent[0].Value!.ReceiptId, concurrent[1].Value!.ReceiptId);
      var executed = Assert.Single(concurrent, result => !result.Value!.Replayed);
      Assert.False(executed.Value!.Replayed);
      Assert.Equal(2, executed.Value.TaskCount);
      Assert.Equal(2, await db.WorkflowTaskBindings.CountAsync(x => x.EngagementId == engagementId));

      Assert.Equal(2, await db.WorkTasks.CountAsync(x => x.EngagementId == engagementId));
      Assert.Single(await db.WorkflowTaskDependencies.Where(x => x.EngagementId == engagementId).ToListAsync());
      var plannedTasks = await EngagementWorkflowTaskService.GetAsync(db, adminActor, engagementId);
      Assert.True(plannedTasks.Succeeded, plannedTasks.Message);
      var predecessor = plannedTasks.Value!.Tasks.Single(x => x.Ordinal == 1);
      var successor = plannedTasks.Value.Tasks.Single(x => x.Ordinal == 2);
      var prematureCompletion = await EngagementWorkflowTaskService.CompleteAsync(
        db, adminActor, successor.Id, successor.Revision);
      Assert.False(prematureCompletion.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, prematureCompletion.ErrorCode);
      Assert.Equal(PracticeTimeStates.TaskOpen,
        (await db.WorkTasks.SingleAsync(x => x.Id == successor.Id)).Status);
      var predecessorCompletion = await EngagementWorkflowTaskService.CompleteAsync(
        db, adminActor, predecessor.Id, predecessor.Revision);
      Assert.True(predecessorCompletion.Succeeded, predecessorCompletion.Message);
      var refreshedTasks = await EngagementWorkflowTaskService.GetAsync(db, adminActor, engagementId);
      var refreshedSuccessor = refreshedTasks.Value!.Tasks.Single(x => x.Ordinal == 2);
      var successorCompletion = await EngagementWorkflowTaskService.CompleteAsync(
        db, adminActor, refreshedSuccessor.Id, refreshedSuccessor.Revision);
      Assert.True(successorCompletion.Succeeded, successorCompletion.Message);

      // A later schedule revision does not silently move promised task dates. An ambiguous retry still recovers
      // the original receipt before checking today's changed preview inputs.
      var revisedSchedule = await StatutoryMilestoneService.SaveMilestonesAsync(db, adminActor,
        new SaveMilestonesRequest(engagementId, new DateOnly(2027, 5, 15)));
      Assert.True(revisedSchedule.Succeeded, revisedSchedule.Message);
      Assert.True(revisedSchedule.Value!.Revision > schedule.Value!.Revision);
      var replay = await EngagementWorkflowPlanService.ExecuteAsync(db, adminActor, engagementId, request);
      Assert.True(replay.Succeeded, replay.Message);
      Assert.True(replay.Value!.Replayed);
      Assert.Equal(executed.Value.ReceiptId, replay.Value.ReceiptId);
      Assert.Equal(executed.Value.TaskIds, replay.Value.TaskIds);
      Assert.Equal(2, await db.WorkflowTaskBindings.CountAsync(x => x.EngagementId == engagementId));
      Assert.Equal(2, await db.WorkTasks.CountAsync(x => x.EngagementId == engagementId));

      var staleExecute = await EngagementWorkflowPlanService.ExecuteAsync(db, adminActor, engagementId,
        request with { IdempotencyKey = Guid.NewGuid() });
      Assert.False(staleExecute.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, staleExecute.ErrorCode);
      var tasks = await db.WorkTasks.Where(x => x.EngagementId == engagementId).OrderBy(x => x.DueDate).ToListAsync();
      Assert.Equal(new[] { new DateOnly(2027, 4, 20), new DateOnly(2027, 4, 25) }, tasks.Select(x => x.DueDate!.Value).ToArray());

      var nextDraft = await WorkflowTemplateService.CreateDraftAsync(db, adminActor,
        new SaveWorkflowTemplateDraftRequest(templateId, "Annual statutory audit", "FinancialStatementAudit", "ProfessionalServices",
        [
          Step("PLANNING", 1, -10),
          Step("FIELDWORK", 2, -5),
          Step("REPORT", 3, -2)
        ]));
      Assert.True(nextDraft.Succeeded, nextDraft.Message);
      var catalogWithDraft = await WorkflowTemplateService.ListAsync(db, adminActor);
      Assert.True(catalogWithDraft.Succeeded, catalogWithDraft.Message);
      var draftCatalogItem = Assert.Single(catalogWithDraft.Value!, x => x.TemplateId == templateId);
      Assert.Equal(nextDraft.Value, draftCatalogItem.LatestRevisionId);
      Assert.Equal("DRAFT", draftCatalogItem.RevisionStatus);
      Assert.Equal(revisionId, draftCatalogItem.CurrentPublishedRevision?.RevisionId);
      Assert.Equal(1, draftCatalogItem.CurrentPublishedRevision?.Version);
      var nextRevision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == nextDraft.Value);
      var nextReviewed = await WorkflowTemplateService.ReviewAsync(db, partnerActor, templateId, nextDraft.Value,
        nextRevision.Revision, "Review the revised annual task plan.");
      Assert.True(nextReviewed.Succeeded, nextReviewed.Message);
      nextRevision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == nextDraft.Value);
      var nextPublished = await WorkflowTemplateService.PublishAsync(db, partnerActor, templateId, nextDraft.Value,
        nextRevision.Revision, "Publish the revised annual task plan.");
      Assert.True(nextPublished.Succeeded, nextPublished.Message);
      var secondSchedule = await StatutoryMilestoneService.SaveMilestonesAsync(db, adminActor,
        new SaveMilestonesRequest(secondEngagementId, new DateOnly(2027, 4, 30)));
      Assert.True(secondSchedule.Succeeded, secondSchedule.Message);
      var secondPreview = await EngagementWorkflowPlanService.PreviewAsync(db, adminActor, secondEngagementId,
        new PreviewEngagementWorkflowPlanRequest(nextDraft.Value));
      Assert.True(secondPreview.Succeeded, secondPreview.Message);
      var secondPlan = await EngagementWorkflowPlanService.ExecuteAsync(db, adminActor, secondEngagementId,
        new ExecuteEngagementWorkflowPlanRequest(nextDraft.Value, secondPreview.Value!.PreviewDigest, Guid.NewGuid()));
      Assert.True(secondPlan.Succeeded, secondPlan.Message);
      Assert.Equal(3, secondPlan.Value!.TaskCount);
      Assert.Equal(1, (await db.WorkflowTaskPlanReceipts.SingleAsync(x => x.EngagementId == engagementId)).TemplateVersion);
      Assert.Equal(2, (await db.WorkflowTaskPlanReceipts.SingleAsync(x => x.EngagementId == secondEngagementId)).TemplateVersion);
      Assert.Equal(2, await db.WorkflowTaskBindings.CountAsync(x => x.EngagementId == engagementId));
      Assert.Equal(3, await db.WorkflowTaskBindings.CountAsync(x => x.EngagementId == secondEngagementId));
      var annual = await AnnualEngagementPreparationWorkspace.StateAsync(db, partnerActor, clientId, engagementId);
      Assert.True(annual.Succeeded, annual.Message);
      Assert.Contains(annual.Value!.CopiedReferences, x => x.Contains(nextDraft.Value.ToString()) && x.Contains("version 2"));
      Assert.DoesNotContain(annual.Value.CopiedReferences, x => x.Contains(revisionId.ToString()));
      Assert.Contains(annual.Value.CopiedReferences, x => x.StartsWith("Prior task-plan schedule basis:"));
      Assert.Contains("Reviewed task template and current-period schedule", annual.Value.RequiresFreshWork);
      Assert.Equal(nextDraft.Value, Assert.Single(annual.Value.TaskTemplateSuggestions).RevisionId);
      Assert.Equal(nextDraft.Value, annual.Value.Fields.AnnualTemplateRevisionId);
      await db.RoleGrants.Where(x => x.UserId == staff.Id).ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
      var scopedGrant = Grant(firmId, staff, "Manager");
      scopedGrant.ClientId = clientId; scopedGrant.EngagementId = engagementId;
      db.RoleGrants.Add(scopedGrant); await db.SaveChangesAsync();
      var scopedActor = Actor(staff, "Manager");
      Assert.False((await WorkflowTemplateService.ListAsync(db, scopedActor)).Succeeded);
      var scopedChoices = await EngagementWorkflowPlanService.TemplatesAsync(db, scopedActor, engagementId);
      Assert.True(scopedChoices.Succeeded, scopedChoices.Message);
      var choice = Assert.Single(scopedChoices.Value!);
      Assert.Equal(nextDraft.Value, choice.CurrentPublishedRevision!.RevisionId);
      Assert.Equal(WorkflowTemplateStates.Published, choice.RevisionStatus);
      Assert.False((await EngagementWorkflowPlanService.TemplatesAsync(db, scopedActor, secondEngagementId)).Succeeded);
      await db.RoleGrants.Where(x => x.Id == scopedGrant.Id).ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
      Assert.False((await EngagementWorkflowPlanService.TemplatesAsync(db, scopedActor, engagementId)).Succeeded);
      Assert.Equal(2, await db.WorkflowTaskBindings.CountAsync(x => x.EngagementId == engagementId));
      var creation = (await EngagementCreationWorkspace.StateAsync(db, partnerActor, clientId)).Value!;
      var annualRequest = new EngagementCreationRequest(Guid.NewGuid(), creation.ReviewBasis, annual.Value.Fields);
      Assert.False((await AnnualEngagementPreparationWorkspace.PreviewAsync(db, partnerActor, clientId,
        annualRequest with { Fields = annualRequest.Fields with { AnnualTemplateRevisionId = revisionId } })).Succeeded);
      var annualPreview = await AnnualEngagementPreparationWorkspace.PreviewAsync(db, partnerActor, clientId, annualRequest);
      Assert.True(annualPreview.Succeeded, annualPreview.Message);
      annualRequest = annualRequest with { Reviewed = true, ExpectedRequestHash = annualPreview.Value!.RequestHash };
      var archived = await WorkflowTemplateService.ArchiveAsync(db, adminActor, templateId, "Synthetic archive after annual preview.");
      Assert.True(archived.Succeeded, archived.Message);
      var staleAnnual = await AnnualEngagementPreparationWorkspace.ExecuteAsync(db, partnerActor, clientId, annualRequest);
      Assert.False(staleAnnual.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleAnnual.ErrorCode);
      Assert.Empty(await db.EngagementCreations.ToListAsync());
      Assert.Equal(1, (await db.WorkflowTaskPlanReceipts.SingleAsync(x => x.EngagementId == engagementId)).TemplateVersion);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var receipt = await db.WorkflowTaskPlanReceipts.SingleAsync(x => x.EngagementId == engagementId);
      receipt.RequestDigest = new string('a', 64);
      await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var dependency = await db.WorkflowTaskDependencies.SingleAsync(x => x.EngagementId == engagementId);
      dependency.CreatedAt = dependency.CreatedAt.AddSeconds(1);
      await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
  }

  private static WorkflowTemplateStepDraft Step(string key, int order, int dueOffset, IReadOnlyList<string>? dependsOn = null) =>
    new(key, WorkflowTaskPhases.AuditPlanning, $"Prepare {key.ToLowerInvariant()} work", $"Complete the {key.ToLowerInvariant()} task.",
      null, 60, dueOffset, null, true, order, dependsOn);

  private static async Task SeedAsync(DbContextOptions<AuditSphereDbContext> options, Guid firmId,
    Guid clientId, Guid engagementId, Guid secondEngagementId, AppUser admin, AppUser partner, AppUser staff)
  {
    await using var db = new AuditSphereDbContext(options);
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    db.PracticeClients.Add(new PracticeClient
      { Id = clientId, FirmId = firmId, LegalName = "Workflow Plan Client", CreatedAt = DateTimeOffset.UtcNow });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      ServiceRoute = "FinancialStatementAudit", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      ServiceProfileId = "test", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = secondEngagementId, FirmId = firmId, PracticeClientId = clientId,
      ServiceRoute = "FinancialStatementAudit", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      ServiceProfileId = "test", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Users.AddRange(admin, partner, staff);
    await db.SaveChangesAsync();
    db.RoleGrants.AddRange(Grant(firmId, admin, "Administrator"), Grant(firmId, partner, "Partner"), Grant(firmId, staff, "Staff"));
    await db.SaveChangesAsync();
  }

  private static AppUser User(Guid firmId, string label) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = $"workflow-plan-{label}-{Guid.NewGuid():N}",
    TenantId = "tenant-test", Email = $"{label.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test",
    DisplayName = label, UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, params string[] roles) =>
    new(user.Id, user.FirmId, user.SessionEpoch, roles);
}
