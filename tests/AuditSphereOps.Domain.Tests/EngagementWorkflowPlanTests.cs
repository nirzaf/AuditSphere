using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
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
          Step("PLANNING", 1, -10) with { TaskGroupName = "Planning" },
          Step("FIELDWORK", 2, -5, ["PLANNING"], requiredTask: false) with { TaskGroupName = "Fieldwork" }
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
      Assert.Equal("Planning", preview.Value.Steps[0].TaskGroupName);
      Assert.True(preview.Value.Steps[0].RequiredTask);
      Assert.Equal("Fieldwork", preview.Value.Steps[1].TaskGroupName);
      Assert.False(preview.Value.Steps[1].RequiredTask);
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

      // A completed operational checklist is not professional acceptance, planning approval, an opinion or release.
      // Those gates must survive both template publication and completion of every instantiated task.
      var activation = await EngagementLifecycleService.ActivateAsync(db, partnerActor, engagementId);
      Assert.Equal(ErrorCodes.GateBlocked, activation.ErrorCode);
      Assert.Contains("No Partner acceptance decision exists", activation.Message);
      Assert.Equal("Draft", (await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == engagementId)).Status);
      Assert.Empty(await db.EngagementActivations.Where(x => x.EngagementId == engagementId).ToListAsync());
      Assert.Empty(await db.MaterialityApprovals.Where(x => x.EngagementId == engagementId).ToListAsync());
      Assert.Empty(await db.AuditOpinionDecisions.Where(x => x.EngagementId == engagementId).ToListAsync());
      Assert.Empty(await db.Releases.Where(x => x.EngagementId == engagementId).ToListAsync());
      Assert.Empty(await db.EngagementFileFreezes.Where(x => x.EngagementId == engagementId).ToListAsync());

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
          Step("PLANNING", 1, -10) with { TaskGroupName = "Revised planning" },
          Step("FIELDWORK", 2, -5) with { TaskGroupName = "Revised fieldwork" },
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
      var completedQueue = await WorkflowTaskQueueQuery.GetAsync(db, adminActor,
        new WorkflowTaskQueueRequest("COMPLETED"));
      Assert.True(completedQueue.Succeeded, completedQueue.Message);
      Assert.Equal(true, Assert.Single(completedQueue.Value!.Tasks, x => x.TaskId == predecessor.Id).RequiredTask);
      Assert.Equal(false, Assert.Single(completedQueue.Value.Tasks, x => x.TaskId == successor.Id).RequiredTask);
      Assert.Equal("Planning", Assert.Single(completedQueue.Value.Tasks, x => x.TaskId == predecessor.Id).TaskGroupName);
      Assert.Equal("Fieldwork", Assert.Single(completedQueue.Value.Tasks, x => x.TaskId == successor.Id).TaskGroupName);
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
      var teamQueue = await WorkflowTaskQueueQuery.GetAsync(db, adminActor,
        new WorkflowTaskQueueRequest("TEAM", PageSize: 100));
      Assert.True(teamQueue.Succeeded, teamQueue.Message);
      Assert.Contains("Revised planning", teamQueue.Value!.Tasks.Select(x => x.TaskGroupName));
      Assert.Contains("Revised fieldwork", teamQueue.Value.Tasks.Select(x => x.TaskGroupName));
      var originalQueue = await WorkflowTaskQueueQuery.GetAsync(db, adminActor,
        new WorkflowTaskQueueRequest("COMPLETED", PageSize: 100));
      Assert.True(originalQueue.Succeeded, originalQueue.Message);
      Assert.Equal("Planning", Assert.Single(originalQueue.Value!.Tasks, x => x.TaskId == predecessor.Id).TaskGroupName);
      Assert.Equal("Fieldwork", Assert.Single(originalQueue.Value.Tasks, x => x.TaskId == successor.Id).TaskGroupName);
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

  [Fact]
  public async Task TemplateDeclaredEvidenceRequirementsBindExactSourcesAndPreserveProvenance()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var admin = PbcSeed.Actor(f.Admin, "Administrator");
    var partner = PbcSeed.Actor(f.Reviewer, "Partner");
    Guid revisionId;
    Guid pbcA;
    Guid pbcB;
    Guid foreignPbc;
    Guid procedureId;
    Guid foreignProcedure;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var engagement = await db.Engagements.SingleAsync(x => x.Id == f.EngagementId);
      engagement.ServiceRoute = "FinancialStatementAudit";
      engagement.PeriodStart = "2026-01-01";
      engagement.PeriodEnd = "2026-12-31";
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "Partner"));
      await db.SaveChangesAsync();

      async Task<Guid> CreatePbcAsync(PbcSeed.Fixture scope, string label)
      {
        var created = await PbcService.CreateRequestAsync(db, PbcSeed.Actor(scope.Staff, "Staff"),
          new(scope.EngagementId, $"Synthetic plan source {label}", "TEST ENTITY", "2026-01-01", "2026-12-31",
            "Cash", "PDF", "", scope.Client.Id, scope.Staff.Id, scope.Reviewer.Id,
            "2027-01-31", "Confidential", "Signed and readable."));
        Assert.True(created.Succeeded, created.Message);
        return created.Value;
      }

      pbcA = await CreatePbcAsync(f, "A");
      pbcB = await CreatePbcAsync(f, "B");
      foreignPbc = await CreatePbcAsync(foreign, "foreign");
      procedureId = Guid.NewGuid();
      foreignProcedure = Guid.NewGuid();
      db.AuditProcedures.AddRange(
        new AuditProcedure
        {
          Id = procedureId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
          SourceProcedureId = "SYN-PLAN-01", Title = "Synthetic exact procedure source",
          Status = AuditProcedureStatuses.Planned, CreatedAt = DateTimeOffset.UtcNow
        },
        new AuditProcedure
        {
          Id = foreignProcedure, FirmId = foreign.FirmId, ClientId = foreign.ClientId,
          EngagementId = foreign.EngagementId, SourceProcedureId = "SYN-PLAN-FOR-01",
          Title = "Synthetic foreign procedure source", Status = AuditProcedureStatuses.Planned,
          CreatedAt = DateTimeOffset.UtcNow
        });
      await db.SaveChangesAsync();

      var declarations = new[]
      {
        new WorkflowTemplateEvidenceRequirementDraft("CURRENT_MATERIALITY", WorkflowEvidenceKinds.CurrentMateriality),
        new WorkflowTemplateEvidenceRequirementDraft("PBC_A", WorkflowEvidenceKinds.AcceptedPbc),
        new WorkflowTemplateEvidenceRequirementDraft("PBC_B", WorkflowEvidenceKinds.AcceptedPbc),
        new WorkflowTemplateEvidenceRequirementDraft("PROCEDURE", WorkflowEvidenceKinds.ReviewedProcedure)
      };
      var draft = await WorkflowTemplateService.CreateDraftAsync(db, admin,
        new SaveWorkflowTemplateDraftRequest(null, "Exact evidence prerequisites", "FinancialStatementAudit",
          "ProfessionalServices", [Step("EVIDENCE", 1, 0) with
          {
            DueDateOffsetDays = null,
            EvidenceRequirements = declarations
          }]));
      Assert.True(draft.Succeeded, draft.Message);
      revisionId = draft.Value;
      var revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == revisionId);
      var reviewed = await WorkflowTemplateService.ReviewAsync(db, partner, revision.TemplateId, revisionId,
        revision.Revision, "Review exact operational source declarations.");
      Assert.True(reviewed.Succeeded, reviewed.Message);
      revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == revisionId);
      var published = await WorkflowTemplateService.PublishAsync(db, partner, revision.TemplateId, revisionId,
        revision.Revision, "Publish exact operational source declarations.");
      Assert.True(published.Succeeded, published.Message);

      var templateView = await WorkflowTemplateService.GetRevisionAsync(db, admin, revision.TemplateId, revisionId);
      Assert.True(templateView.Succeeded, templateView.Message);
      Assert.Equal(declarations.Select(x => new WorkflowTemplateEvidenceRequirementView(x.RequirementKey, x.Kind)),
        Assert.Single(templateView.Value!.Steps).EvidenceRequirements);

      var missing = await EngagementWorkflowPlanService.PreviewAsync(db, admin, f.EngagementId,
        new PreviewEngagementWorkflowPlanRequest(revisionId));
      Assert.True(missing.Succeeded, missing.Message);
      var missingPreview = missing.Value!;
      Assert.False(missingPreview.CanExecute);
      var missingByKey = Assert.Single(missingPreview.Steps).EvidenceRequirements!
        .ToDictionary(x => x.RequirementKey);
      Assert.False(missingByKey["CURRENT_MATERIALITY"].BindingRequired);
      Assert.Null(missingByKey["CURRENT_MATERIALITY"].SourceId);
      Assert.True(missingByKey["PBC_A"].BindingRequired);
      Assert.Equal("SOURCE_REQUIRED", missingByKey["PBC_A"].SourceState);

      WorkflowPlanEvidenceSelection[] selections =
      [
        new("EVIDENCE", "PBC_A", pbcA),
        new("EVIDENCE", "PBC_B", pbcB),
        new("EVIDENCE", "PROCEDURE", procedureId)
      ];
      var wrongPbc = await EngagementWorkflowPlanService.PreviewAsync(db, admin, f.EngagementId,
        new PreviewEngagementWorkflowPlanRequest(revisionId, EvidenceSelections: selections.Select(x =>
          x.RequirementKey == "PBC_A" ? x with { SourceId = foreignPbc } : x).ToArray()));
      Assert.False(wrongPbc.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, wrongPbc.ErrorCode);
      Assert.Equal("Access denied.", wrongPbc.Message);

      var wrongProcedure = await EngagementWorkflowPlanService.PreviewAsync(db, admin, f.EngagementId,
        new PreviewEngagementWorkflowPlanRequest(revisionId, EvidenceSelections: selections.Select(x =>
          x.RequirementKey == "PROCEDURE" ? x with { SourceId = foreignProcedure } : x).ToArray()));
      Assert.False(wrongProcedure.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, wrongProcedure.ErrorCode);
      Assert.Equal("Access denied.", wrongProcedure.Message);

      var duplicateSource = await EngagementWorkflowPlanService.PreviewAsync(db, admin, f.EngagementId,
        new PreviewEngagementWorkflowPlanRequest(revisionId, EvidenceSelections: selections.Select(x =>
          x.RequirementKey == "PBC_B" ? x with { SourceId = pbcA } : x).ToArray()));
      Assert.False(duplicateSource.Succeeded);
      Assert.Equal("workflow.plan-invalid", duplicateSource.ErrorCode);

      var preview = await EngagementWorkflowPlanService.PreviewAsync(db, admin, f.EngagementId,
        new PreviewEngagementWorkflowPlanRequest(revisionId, EvidenceSelections: selections));
      Assert.True(preview.Succeeded, preview.Message);
      var selectedPreview = preview.Value!;
      Assert.True(selectedPreview.CanExecute);
      var previewByKey = Assert.Single(selectedPreview.Steps).EvidenceRequirements!.ToDictionary(x => x.RequirementKey);
      Assert.True(previewByKey["PBC_A"].BindingRequired);
      Assert.Equal(pbcA, previewByKey["PBC_A"].SourceId);
      Assert.False(previewByKey["PBC_A"].Satisfied);

      var executeRequest = new ExecuteEngagementWorkflowPlanRequest(revisionId, selectedPreview.PreviewDigest,
        Guid.NewGuid(), EvidenceSelections: selections);
      var sentPbc = await PbcService.ChangeStateAsync(db, PbcSeed.Actor(f.Staff, "Staff"),
        new PbcStateChangeRequest(pbcA, PbcStates.Sent, 1));
      Assert.True(sentPbc.Succeeded, sentPbc.Message);
      var staleExecute = await EngagementWorkflowPlanService.ExecuteAsync(db, admin, f.EngagementId, executeRequest);
      Assert.False(staleExecute.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, staleExecute.ErrorCode);
      Assert.Empty(await db.WorkflowTaskBindings.Where(x => x.EngagementId == f.EngagementId).ToListAsync());
      Assert.Empty(await db.WorkflowTaskEvidenceRules.Where(x => x.EngagementId == f.EngagementId).ToListAsync());
      Assert.Empty(await db.WorkflowTaskPlanReceipts.Where(x => x.EngagementId == f.EngagementId).ToListAsync());

      var currentPreview = await EngagementWorkflowPlanService.PreviewAsync(db, admin, f.EngagementId,
        new PreviewEngagementWorkflowPlanRequest(revisionId, EvidenceSelections: selections));
      Assert.True(currentPreview.Succeeded, currentPreview.Message);
      var refreshedPreview = currentPreview.Value!;
      Assert.NotEqual(selectedPreview.PreviewDigest, refreshedPreview.PreviewDigest);
      executeRequest = executeRequest with { ExpectedPreviewDigest = refreshedPreview.PreviewDigest };
      var executed = await EngagementWorkflowPlanService.ExecuteAsync(db, admin, f.EngagementId, executeRequest);
      Assert.True(executed.Succeeded, executed.Message);
      var executeReceipt = executed.Value!;
      Assert.False(executeReceipt.Replayed);
      Assert.Equal(1, executeReceipt.TaskCount);
      var binding = await db.WorkflowTaskBindings.SingleAsync(x => x.EngagementId == f.EngagementId &&
        x.TemplateRevisionId == revisionId);
      var declarationIds = await db.WorkflowTemplateEvidenceRequirements.AsNoTracking()
        .Where(x => x.TemplateRevisionId == revisionId).ToDictionaryAsync(x => x.RequirementKey, x => x.Id);
      var rules = await db.WorkflowTaskEvidenceRules.Where(x => x.WorkflowTaskBindingId == binding.Id)
        .OrderBy(x => x.TaskRevision).ToListAsync();
      Assert.Equal(4, rules.Count);
      Assert.All(rules, rule =>
      {
        Assert.Equal(1, rule.Version);
        Assert.Equal(WorkflowEvidenceRuleStates.Required, rule.State);
        Assert.NotNull(rule.WorkflowTemplateEvidenceRequirementId);
        Assert.Contains(rule.WorkflowTemplateEvidenceRequirementId!.Value, declarationIds.Values);
      });
      var ruleByKey = rules.ToDictionary(x => declarationIds.Single(pair =>
        pair.Value == x.WorkflowTemplateEvidenceRequirementId).Key);
      Assert.Equal(Guid.Empty, ruleByKey["CURRENT_MATERIALITY"].SourceId);
      Assert.Null(ruleByKey["CURRENT_MATERIALITY"].PbcRequestId);
      Assert.Equal(pbcA, ruleByKey["PBC_A"].SourceId);
      Assert.Equal(pbcA, ruleByKey["PBC_A"].PbcRequestId);
      Assert.Equal(pbcB, ruleByKey["PBC_B"].SourceId);
      Assert.Equal(procedureId, ruleByKey["PROCEDURE"].SourceId);
      Assert.Equal(procedureId, ruleByKey["PROCEDURE"].AuditProcedureId);

      var workspace = await WorkflowTaskEvidenceQuery.GetAsync(db, admin, f.EngagementId, binding.WorkTaskId);
      Assert.True(workspace.Succeeded, workspace.Message);
      Assert.Equal(4, workspace.Value!.Rules.Count);
      var materialityView = Assert.Single(workspace.Value.Rules, x => x.TemplateRequirementKey == "CURRENT_MATERIALITY");
      Assert.Equal(declarationIds["CURRENT_MATERIALITY"], materialityView.TemplateRequirementId);
      Assert.Null(materialityView.SourceId);
      var pbcView = Assert.Single(workspace.Value.Rules, x => x.TemplateRequirementKey == "PBC_A");
      Assert.Equal(declarationIds["PBC_A"], pbcView.TemplateRequirementId);
      Assert.Equal(pbcA, pbcView.SourceId);
      Assert.Empty(await db.MaterialityApprovals.Where(x => x.EngagementId == f.EngagementId).ToListAsync());
      Assert.Empty(await db.AuditProcedureReviews.Where(x => x.EngagementId == f.EngagementId).ToListAsync());
      Assert.Empty(await db.AuditOpinionDecisions.Where(x => x.EngagementId == f.EngagementId).ToListAsync());
      Assert.Empty(await db.Releases.Where(x => x.EngagementId == f.EngagementId).ToListAsync());

      // Receipt recovery remains ahead of today's binding gate after a lost response.
      var replay = await EngagementWorkflowPlanService.ExecuteAsync(db, admin, f.EngagementId,
        executeRequest with { EvidenceSelections = null });
      Assert.True(replay.Succeeded, replay.Message);
      var replayReceipt = replay.Value!;
      Assert.True(replayReceipt.Replayed);
      Assert.Equal(executeReceipt.ReceiptId, replayReceipt.ReceiptId);
      Assert.Equal(4, await db.WorkflowTaskEvidenceRules.CountAsync(x => x.WorkflowTaskBindingId == binding.Id));

      var withdrawn = await PracticeTimeService.ChangeWorkflowEvidenceAsync(db, admin, f.EngagementId,
        binding.WorkTaskId, new WorkflowEvidenceChangeRequest(WorkflowEvidenceKinds.AcceptedPbc, pbcA,
          false, "Withdraw the operational coordination rule.", binding.Revision, Guid.NewGuid()));
      Assert.True(withdrawn.Succeeded, withdrawn.Message);
      var withdrawalReceipt = withdrawn.Value!;
      var successor = await db.WorkflowTaskEvidenceRules.SingleAsync(x => x.Id == withdrawalReceipt.RuleRevisionId);
      Assert.Equal(2, successor.Version);
      Assert.Equal(WorkflowEvidenceRuleStates.Withdrawn, successor.State);
      Assert.Equal(declarationIds["PBC_A"], successor.WorkflowTemplateEvidenceRequirementId);
      var afterWithdrawal = await WorkflowTaskEvidenceQuery.GetAsync(db, admin, f.EngagementId, binding.WorkTaskId);
      var afterWithdrawalWorkspace = afterWithdrawal.Value!;
      Assert.Equal(declarationIds["PBC_A"],
        Assert.Single(afterWithdrawalWorkspace.Rules, x => x.TemplateRequirementKey == "PBC_A").TemplateRequirementId);
      Assert.Equal(2, Assert.Single(afterWithdrawalWorkspace.Rules, x => x.TemplateRequirementKey == "PBC_A").Version);
      Assert.Empty(await db.MaterialityApprovals.Where(x => x.EngagementId == f.EngagementId).ToListAsync());
      Assert.Empty(await db.AuditProcedureReviews.Where(x => x.EngagementId == f.EngagementId).ToListAsync());
      Assert.Empty(await db.AuditOpinionDecisions.Where(x => x.EngagementId == f.EngagementId).ToListAsync());
      Assert.Empty(await db.Releases.Where(x => x.EngagementId == f.EngagementId).ToListAsync());
    }
  }

  [Fact]
  public async Task ProgressCountsApplicableCompletedStepsAndStaysBelowCompletionWhileGatesAreOpen()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var secondEngagementId = Guid.NewGuid();
    var admin = User(firmId, "ProgressAdmin");
    var partner = User(firmId, "ProgressPartner");
    var staff = User(firmId, "ProgressStaff");
    var adminActor = Actor(admin, "Administrator");
    var partnerActor = Actor(partner, "Partner");
    await SeedAsync(pg.Options, firmId, clientId, engagementId, secondEngagementId, admin, partner, staff);
    await using var db = new AuditSphereDbContext(pg.Options);

    var draft = await WorkflowTemplateService.CreateDraftAsync(db, adminActor,
      new SaveWorkflowTemplateDraftRequest(null, "Progress audit plan", "FinancialStatementAudit", null,
      [
        Step("SCOPE", 1, -20),
        Step("INVENTORY", 2, -15) with { ApplicabilityCondition = "Only where the entity holds inventory" },
        Step("EXTRA", 3, -12, requiredTask: false),
        Step("CLOSE", 4, -10, ["SCOPE"])
      ]));
    Assert.True(draft.Succeeded, draft.Message);
    var revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == draft.Value);
    Assert.True((await WorkflowTemplateService.ReviewAsync(db, partnerActor, revision.TemplateId, revision.Id,
      revision.Revision, "Review the progress plan.")).Succeeded);
    revision = await db.WorkflowTemplateRevisions.SingleAsync(x => x.Id == draft.Value);
    Assert.True((await WorkflowTemplateService.PublishAsync(db, partnerActor, revision.TemplateId, revision.Id,
      revision.Revision, "Publish the progress plan.")).Succeeded);
    Assert.True((await StatutoryMilestoneService.SaveMilestonesAsync(db, adminActor,
      new SaveMilestonesRequest(engagementId, new DateOnly(2027, 4, 30)))).Succeeded);
    var preview = await EngagementWorkflowPlanService.PreviewAsync(db, adminActor, engagementId,
      new PreviewEngagementWorkflowPlanRequest(revision.Id));
    Assert.True(preview.Succeeded, preview.Message);
    Assert.Equal("Only where the entity holds inventory",
      preview.Value!.Steps.Single(x => x.StepKey == "INVENTORY").ApplicabilityCondition);
    Assert.Null(preview.Value.Steps.Single(x => x.StepKey == "SCOPE").ApplicabilityCondition);
    Assert.True((await EngagementWorkflowPlanService.ExecuteAsync(db, adminActor, engagementId,
      new ExecuteEngagementWorkflowPlanRequest(revision.Id, preview.Value!.PreviewDigest, Guid.NewGuid()))).Succeeded);
    var team = await WorkflowTaskQueueQuery.GetAsync(db, adminActor, new WorkflowTaskQueueRequest("TEAM"));
    Assert.True(team.Succeeded, team.Message);
    Assert.Equal("Only where the entity holds inventory",
      team.Value!.Tasks.Single(x => x.Title == "Prepare inventory work").ApplicabilityCondition);

    var initial = (await EngagementProgressQuery.GetAsync(db, adminActor, engagementId)).Value!;
    Assert.True(initial.LifecycleRoute);
    Assert.Equal((3, 0, 2, 1, 0, 1, 0), (initial.ApplicableRequired, initial.CompletedRequired, initial.PendingRequired,
      initial.BlockedRequired, initial.NotApplicable, initial.OptionalApplicable, initial.OptionalCompleted));
    Assert.Equal(0, initial.PercentComplete);
    Assert.Equal("Prepare scope work", initial.NextAction);
    Assert.Equal(WorkflowStepRequirements.Conditional, initial.Steps.Single(s => s.Title == "Prepare inventory work").Requirement);

    async Task<(Guid Id, long Revision)> TaskAsync(string title)
    {
      var task = await db.WorkTasks.AsNoTracking().SingleAsync(x => x.EngagementId == engagementId && x.Title == title);
      var binding = await db.WorkflowTaskBindings.AsNoTracking().SingleAsync(x => x.WorkTaskId == task.Id);
      return (task.Id, binding.Revision);
    }
    var scope = await TaskAsync("Prepare scope work");
    Assert.True((await EngagementWorkflowTaskService.CompleteAsync(db, adminActor, scope.Id, scope.Revision)).Succeeded);
    var inventory = await TaskAsync("Prepare inventory work");
    const string noInventoryReason = "Not applicable: The entity holds no inventory for this period.";
    Assert.True((await EngagementWorkflowTaskService.CancelAsync(db, adminActor, inventory.Id,
      noInventoryReason, inventory.Revision)).Succeeded);
    var half = (await EngagementProgressQuery.GetAsync(db, adminActor, engagementId)).Value!;
    Assert.Equal((2, 1, 1, 0, 1), (half.ApplicableRequired, half.CompletedRequired, half.PendingRequired,
      half.BlockedRequired, half.NotApplicable));
    Assert.Equal(50, half.PercentComplete);
    Assert.Equal("Prepare close work", half.NextAction);

    // Reopening invalidates the prior waiver; a later generic cancellation cannot inherit it.
    Assert.True((await EngagementWorkflowTaskService.ReopenAsync(db, adminActor, inventory.Id,
      "Reassess applicability.", (await TaskAsync("Prepare inventory work")).Revision)).Succeeded);
    Assert.True((await EngagementWorkflowTaskService.CancelAsync(db, adminActor, inventory.Id,
      "Work paused pending records.", (await TaskAsync("Prepare inventory work")).Revision)).Succeeded);
    var genericCancellation = (await EngagementProgressQuery.GetAsync(db, adminActor, engagementId)).Value!;
    Assert.Equal((3, 1, 1, 1, 0), (genericCancellation.ApplicableRequired, genericCancellation.CompletedRequired,
      genericCancellation.PendingRequired, genericCancellation.BlockedRequired, genericCancellation.NotApplicable));
    Assert.Equal(33, genericCancellation.PercentComplete);

    Assert.True((await EngagementWorkflowTaskService.ReopenAsync(db, adminActor, inventory.Id,
      "Reconsider applicability.", (await TaskAsync("Prepare inventory work")).Revision)).Succeeded);
    Assert.True((await EngagementWorkflowTaskService.CancelAsync(db, adminActor, inventory.Id,
      noInventoryReason, (await TaskAsync("Prepare inventory work")).Revision)).Succeeded);
    var restoredWaiver = (await EngagementProgressQuery.GetAsync(db, adminActor, engagementId)).Value!;
    Assert.Equal((2, 1, 1, 0, 1), (restoredWaiver.ApplicableRequired, restoredWaiver.CompletedRequired,
      restoredWaiver.PendingRequired, restoredWaiver.BlockedRequired, restoredWaiver.NotApplicable));

    var close = await TaskAsync("Prepare close work");
    Assert.True((await EngagementWorkflowTaskService.CompleteAsync(db, adminActor, close.Id, close.Revision)).Succeeded);
    var planDone = (await EngagementProgressQuery.GetAsync(db, adminActor, engagementId)).Value!;
    // Every applicable step is complete, but the derived audit lifecycle has not reached release.
    Assert.Equal((99, true), (planDone.PercentComplete, planDone.GateOpen));
    Assert.NotEmpty(planDone.Gates);
    Assert.Equal("Client onboarding", planDone.Phase);
    Assert.Equal((1, 0), (planDone.OptionalApplicable, planDone.OptionalCompleted));

    // A valid engagement plan may span multiple bounded evidence-reader pages.
    for (var ordinal = 5; ordinal <= 101; ordinal++)
    {
      var added = await EngagementWorkflowTaskService.CreateAsync(db, adminActor, engagementId,
        new($"Additional required commitment {ordinal}", WorkflowTaskPhases.AuditPlanning, ordinal,
          IdempotencyKey: Guid.NewGuid()));
      Assert.True(added.Succeeded, added.Message);
    }
    var largerPlan = await EngagementProgressQuery.GetAsync(db, adminActor, engagementId);
    Assert.True(largerPlan.Succeeded, largerPlan.Message);
    Assert.Equal(101, largerPlan.Value!.Steps.Count);
    Assert.Equal((99, 2, 97, 0), (largerPlan.Value.ApplicableRequired, largerPlan.Value.CompletedRequired,
      largerPlan.Value.PendingRequired, largerPlan.Value.BlockedRequired));
    Assert.Equal(2, largerPlan.Value.PercentComplete);

    var client = (await EngagementProgressQuery.ForClientAsync(db, adminActor, clientId)).Value!;
    Assert.Equal(2, client.Total);
    Assert.Null(client.Items.Single(x => x.EngagementId == secondEngagementId).PercentComplete);
    Assert.All(client.Items, x => Assert.Empty(x.Steps));

    // One oversized plan must have the same read bound in every surface, without gating its neighbour.
    var now = DateTimeOffset.UtcNow;
    var overflowTasks = Enumerable.Range(102, 900).Select(ordinal => new WorkTask
    {
      Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      Title = $"Oversized plan commitment {ordinal}", Status = PracticeTimeStates.TaskOpen,
      Phase = BudgetPhases.Unassigned, CreatedAt = now
    }).ToArray();
    db.WorkTasks.AddRange(overflowTasks);
    db.WorkflowTaskBindings.AddRange(overflowTasks.Select((task, index) => new WorkflowTaskBinding
    {
      Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      WorkTaskId = task.Id, Phase = WorkflowTaskPhases.AuditPlanning, Ordinal = index + 102,
      SourceKey = $"TEST-PROGRESS-OVERFLOW:{index + 102}", CreationRequestDigest = new string('a', 64),
      CreatedAt = now, CreatedByUserId = admin.Id
    }));
    await db.SaveChangesAsync();
    var boundedDetail = (await EngagementProgressQuery.GetAsync(db, adminActor, engagementId)).Value!;
    Assert.Equal(500, boundedDetail.Steps.Count);
    Assert.Contains(boundedDetail.Gates, x => x.StartsWith("More than 500", StringComparison.Ordinal));
    var boundedClient = (await EngagementProgressQuery.ForClientAsync(db, adminActor, clientId)).Value!;
    var boundedPortfolio = (await PortfolioQuery.ProgressAsync(db, adminActor)).Value!;
    foreach (var summaries in new[] { boundedClient.Items, boundedPortfolio.Items })
    {
      var oversized = summaries.Single(x => x.EngagementId == engagementId);
      Assert.Equal(boundedDetail.ApplicableRequired, oversized.ApplicableRequired);
      Assert.Equal(boundedDetail.CompletedRequired, oversized.CompletedRequired);
      Assert.Contains(oversized.Gates, x => x.StartsWith("More than 500", StringComparison.Ordinal));
      Assert.DoesNotContain(summaries.Single(x => x.EngagementId == secondEngagementId).Gates,
        x => x.StartsWith("More than 500", StringComparison.Ordinal));
    }
    var outsider = User(firmId, "ProgressOutsider");
    db.Users.Add(outsider);
    await db.SaveChangesAsync();
    Assert.False((await EngagementProgressQuery.GetAsync(db, Actor(outsider, "Staff"), engagementId)).Succeeded);
    Assert.False((await EngagementProgressQuery.ForClientAsync(db, Actor(outsider, "Staff"), clientId)).Succeeded);

    // Derived lifecycle stages can omit holds; progress still reports an outstanding hold gate.
    db.EngagementHolds.Add(new EngagementHold
    {
      Id = Guid.NewGuid(), FirmId = firmId, EngagementId = engagementId,
      HoldKind = "Continuance", Reason = "Confidential continuance review.",
      CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var held = (await EngagementProgressQuery.GetAsync(db, adminActor, engagementId)).Value!;
    Assert.True(held.GateOpen);
    Assert.Contains(held.Gates, g => g.Contains("hold", StringComparison.OrdinalIgnoreCase));
  }

  private static WorkflowTemplateStepDraft Step(string key, int order, int dueOffset, IReadOnlyList<string>? dependsOn = null,
    bool requiredTask = true) =>
    new(key, WorkflowTaskPhases.AuditPlanning, $"Prepare {key.ToLowerInvariant()} work", $"Complete the {key.ToLowerInvariant()} task.",
      null, 60, dueOffset, null, requiredTask, order, dependsOn);

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
