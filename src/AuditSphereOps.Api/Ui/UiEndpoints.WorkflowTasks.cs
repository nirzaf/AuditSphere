using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record WorkflowTaskCreateInput(
    string Title,
    string Phase,
    int Ordinal,
    string? DueDate,
    int? PlannedEffortMinutes,
    Guid? AssigneeUserId,
    Guid? MappingVersionId,
    string? FsliCode,
    string? AssignedRole,
    Guid IdempotencyKey);
  public sealed record WorkflowTaskAssignmentInput(Guid AssigneeUserId, long ExpectedRevision);
  public sealed record WorkflowTaskCompletionInput(long ExpectedRevision);
  /// <summary>The exact reason and reviewed revision for cancelling one operational task.</summary>
  public sealed record WorkflowTaskCancellationInput(string Reason, long ExpectedRevision);
  /// <summary>The exact reason and reviewed revision for reopening one cancelled operational task.</summary>
  public sealed record WorkflowTaskReopeningInput(string Reason, long ExpectedRevision);
  public sealed record WorkflowTaskDueDateInput(string? DueDate, long ExpectedRevision);
  public sealed record WorkflowDependencyChangeInput(Guid PredecessorTaskId, bool Required, long ExpectedRevision,
    long ExpectedPredecessorRevision, string Reason, Guid IdempotencyKey);
  public sealed record WorkflowEvidenceChangeInput(string Kind, Guid? SourceId, bool Required,
    string Reason, long ExpectedRevision, Guid IdempotencyKey);
  public sealed record WorkflowTemplateStepInput(
    string StableKey, string Phase, string Title, string Instructions, string? SuggestedStaffingLevel,
    int? EstimatedEffortMinutes, int? DueDateOffsetDays, string? PbcRequestReference, bool RequiredTask,
    int SortOrder, IReadOnlyList<string>? DependsOnStepKeys, WorkflowTemplatePbcDefinitionInput? PbcDefinition = null,
    string? TaskGroupName = null, IReadOnlyList<WorkflowTemplateEvidenceRequirementInput>? EvidenceRequirements = null,
    string? ApplicabilityCondition = null);
  public sealed record WorkflowTemplateEvidenceRequirementInput(string RequirementKey, string Kind);
  public sealed record WorkflowTemplatePbcDefinitionInput(string Objective, string EntityScope, string Area,
    string RequestedFormat, string? ControlTotals, string Confidentiality, string AcceptanceCriteria);
  public sealed record WorkflowTemplateDraftInput(
    Guid? TemplateId, string Name, string ServiceRoute, string? IndustryKey,
    IReadOnlyList<WorkflowTemplateStepInput> Steps, long? ExpectedRevision);
  public sealed record WorkflowTemplateDecisionInput(long ExpectedRevision, string Rationale);
  public sealed record WorkflowTemplateArchiveInput(string Rationale);
  public sealed record WorkflowPlanEvidenceInput(string StepKey, string RequirementKey, Guid SourceId);
  public sealed record WorkflowPlanOwnerInput(string StepKey, Guid AssigneeUserId);
  public sealed record WorkflowPlanPbcOwnerInput(string StepKey, Guid ClientOwnerUserId, Guid FirmOwnerUserId,
    Guid ReviewerUserId);
  public sealed record WorkflowPlanPreviewInput(Guid TemplateRevisionId, IReadOnlyList<WorkflowPlanOwnerInput>? Owners,
    IReadOnlyList<WorkflowPlanPbcOwnerInput>? PbcOwners = null,
    IReadOnlyList<WorkflowPlanEvidenceInput>? EvidenceSelections = null);
  public sealed record WorkflowPlanExecuteInput(Guid TemplateRevisionId, string ExpectedPreviewDigest,
    Guid IdempotencyKey, IReadOnlyList<WorkflowPlanOwnerInput>? Owners,
    IReadOnlyList<WorkflowPlanPbcOwnerInput>? PbcOwners = null,
    IReadOnlyList<WorkflowPlanEvidenceInput>? EvidenceSelections = null);
  public sealed record WorkflowPlanPbcDraftsInput(Guid TemplateRevisionId, string ExpectedPreviewDigest,
    IReadOnlyList<WorkflowPlanPbcOwnerInput>? PbcOwners = null);

  private static void MapWorkflowTaskEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/engagements/{engagementId:guid}/workflow-evidence-sources",
      (Guid engagementId, string kind, string? query, int? page, int? pageSize, Guid? selectedId, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => WorkflowEvidenceSourceQuery.GetAsync(db, actor, engagementId,
          kind, query, page ?? 0, pageSize ?? 25, selectedId, ct)))
      .AddEndpointFilter(async (context, next) =>
      {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        return configuration.GetValue<bool>("WorkflowExperience:Enabled") ? await next(context) : Results.NotFound();
      })
      .WithDescription("Discovers bounded exact-engagement PBC or procedure references for operational evidence prerequisites. Selection does not approve or satisfy evidence; the existing preview and completion commands evaluate it independently.")
      .WithT046Response<WorkflowEvidenceSourcePage>(includeConflict: true, includeNotFound: true);

    group.MapGet("/work/reviews", (DateTimeOffset? afterSubmittedAt, Guid? afterResultId, int? pageSize, HttpContext http) =>
      ReadAsync(http, async (db, actor, ct) =>
      {
        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>("WorkflowExperience:Enabled"))
          return CommandResult<ProcedureReviewInbox>.Fail(ErrorCodes.ScopeDenied,
            "Workflow task experience is not enabled on this server.");
        return await ProcedureReviewInboxQuery.GetAsync(db, actor,
          new ProcedureReviewInboxRequest(afterSubmittedAt, afterResultId, pageSize ?? 50), ct);
      }))
      .WithT046Response<ProcedureReviewInbox>();

    group.MapGet("/work/tasks", (string? view, DateOnly? afterDueDate, Guid? afterTaskId, int? pageSize,
      Guid? assigneeUserId, bool? unassignedOnly, bool? includeSummary, HttpContext http) =>
      WorkflowQueueAsync(http, view, afterDueDate, afterTaskId, pageSize, assigneeUserId, unassignedOnly, includeSummary))
      .WithT046Response<WorkflowTaskQueue>();

    group.MapGet("/engagements/{id:guid}/deadline-reminders", (Guid id, HttpContext http) =>
      ReadAsync(http, async (db, actor, ct) =>
      {
        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>("WorkflowExperience:Enabled"))
          return CommandResult<EngagementDeadlineReminders>.Fail(ErrorCodes.ScopeDenied,
            "Workflow task experience is not enabled on this server.");
        return await EngagementDeadlineReminderQuery.GetAsync(db, actor, id, ct);
      }))
      .WithT046Response<EngagementDeadlineReminders>();

    var tasks = group.MapGroup("/engagements/{engagementId:guid}/workflow/tasks")
      .AddEndpointFilter(async (context, next) =>
      {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        return configuration.GetValue<bool>("WorkflowExperience:Enabled")
          ? await next(context)
          : Results.NotFound();
      });

    tasks.MapGet("", (Guid engagementId, Guid? taskId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => EngagementWorkflowTaskService.GetAsync(db, actor, engagementId, taskId, ct)))
      .WithDescription("Returns the authorized engagement's bounded task workspace. Optional taskId resolves one exact task in this same scope for recovery independently of the default first-page window.")
      .WithT046Response<WorkflowTaskWorkspace>(includeNotFound: true);

    tasks.MapGet("/assignees", (Guid engagementId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => EngagementWorkflowTaskService.GetAssigneeOptionsAsync(db, actor, engagementId, ct)))
      .WithT046Response<IReadOnlyList<WorkflowTaskAssigneeOption>>(includeNotFound: true);

    tasks.MapGet("/{taskId:guid}/dependencies", (Guid engagementId, Guid taskId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => WorkflowTaskDependencyQuery.GetAsync(db, actor, engagementId, taskId, ct)))
      .WithT046Response<WorkflowDependencyWorkspace>(includeConflict: true, includeNotFound: true);
    tasks.MapGet("/{taskId:guid}/dependencies/receipts/{key:guid}", (Guid engagementId, Guid taskId, Guid key, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => WorkflowTaskDependencyQuery.ReceiptAsync(db, actor, engagementId, taskId, key, ct)))
      .WithT046Response<WorkflowDependencyReceipt>(includeNotFound: true);
    tasks.MapPost("/{taskId:guid}/dependencies", (Guid engagementId, Guid taskId, WorkflowDependencyChangeInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, engagementId, taskId,
        new(input.PredecessorTaskId, input.Required, input.ExpectedRevision, input.ExpectedPredecessorRevision, input.Reason, input.IdempotencyKey), ct)))
      .WithT046Response<UiValueResponse<WorkflowDependencyReceipt>>(includeConflict: true, includeNotFound: true);

    tasks.MapGet("/{taskId:guid}/evidence", (Guid engagementId, Guid taskId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => WorkflowTaskEvidenceQuery.GetAsync(db, actor, engagementId, taskId, ct)))
      .WithT046Response<WorkflowTaskEvidenceWorkspace>(includeNotFound: true);
    tasks.MapGet("/{taskId:guid}/evidence/receipts/{key:guid}", (Guid engagementId, Guid taskId, Guid key, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => WorkflowTaskEvidenceQuery.ReceiptAsync(db, actor, engagementId, taskId, key, ct)))
      .WithT046Response<WorkflowEvidenceChangeReceipt>(includeNotFound: true);
    tasks.MapPost("/{taskId:guid}/evidence", (
      Guid engagementId, Guid taskId, WorkflowEvidenceChangeInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PracticeTimeService.ChangeWorkflowEvidenceAsync(db, actor,
        engagementId, taskId, new(input.Kind, input.SourceId, input.Required, input.Reason,
          input.ExpectedRevision, input.IdempotencyKey), ct)))
      .WithT046Response<UiValueResponse<WorkflowEvidenceChangeReceipt>>(includeConflict: true, includeNotFound: true);

    tasks.MapGet("/{taskId:guid}/activity", (
      Guid engagementId, Guid taskId, DateOnly? from, DateOnly? to,
      DateTimeOffset? afterOccurredAt, Guid? afterActivityId, int? pageSize, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => WorkflowTaskActivityQuery.GetAsync(db, actor,
        engagementId, taskId, new WorkflowTaskActivityRequest(from, to, afterOccurredAt,
          afterActivityId, pageSize ?? 50), ct)))
      .WithT046Response<WorkflowTaskActivityPage>(includeNotFound: true);

    tasks.MapPost("", (
      Guid engagementId, WorkflowTaskCreateInput input, HttpContext http) =>
    {
      DateOnly? due = TryDate(input.DueDate, out var parsed) ? parsed : null;
      if (!string.IsNullOrWhiteSpace(input.DueDate) && due is null)
        return Task.FromResult(Invalid("Enter the due date as a date."));
      return CommandAsync(http, (db, actor, ct) => EngagementWorkflowTaskService.CreateAsync(db, actor,
        engagementId, new WorkflowTaskCreateRequest(input.Title, input.Phase, input.Ordinal, due,
          input.PlannedEffortMinutes, input.AssigneeUserId, input.MappingVersionId, input.FsliCode,
          input.AssignedRole, input.IdempotencyKey), ct));
    }).WithT046Response<UiValueResponse<Guid>>(includeConflict: true, includeNotFound: true);

    tasks.MapPost("/{taskId:guid}/assign", (
      Guid engagementId, Guid taskId, WorkflowTaskAssignmentInput input, HttpContext http) =>
      CommandAsync(http, async (db, actor, ct) =>
      {
        var scoped = await EngagementWorkflowTaskService.EnsureTaskScopeAsync(db, actor, engagementId, taskId, ct);
        if (!scoped.Succeeded) return CommandResult<bool>.Fail(scoped.ErrorCode!, scoped.Message!);
        var result = await EngagementWorkflowTaskService.AssignAsync(db, actor, taskId,
          input.AssigneeUserId, input.ExpectedRevision, ct);
        return result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
      })).WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);

    tasks.MapPost("/{taskId:guid}/reassign", (
      Guid engagementId, Guid taskId, WorkflowTaskAssignmentInput input, HttpContext http) =>
      CommandAsync(http, async (db, actor, ct) =>
      {
        var scoped = await EngagementWorkflowTaskService.EnsureTaskScopeAsync(db, actor, engagementId, taskId, ct);
        if (!scoped.Succeeded) return CommandResult<bool>.Fail(scoped.ErrorCode!, scoped.Message!);
        var result = await EngagementWorkflowTaskService.ReassignAsync(db, actor, taskId,
          input.AssigneeUserId, input.ExpectedRevision, ct);
        return result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
      })).WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);

    tasks.MapPost("/{taskId:guid}/complete", (
      Guid engagementId, Guid taskId, WorkflowTaskCompletionInput input, HttpContext http) =>
      CommandAsync(http, async (db, actor, ct) =>
      {
        var scoped = await EngagementWorkflowTaskService.EnsureTaskScopeAsync(db, actor, engagementId, taskId, ct);
        if (!scoped.Succeeded) return CommandResult<bool>.Fail(scoped.ErrorCode!, scoped.Message!);
        var result = await EngagementWorkflowTaskService.CompleteAsync(db, actor, taskId, input.ExpectedRevision, ct);
        return result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
      })).WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);

    tasks.MapPost("/{taskId:guid}/cancel", (
      Guid engagementId, Guid taskId, WorkflowTaskCancellationInput input, HttpContext http) =>
    {
      if (input.ExpectedRevision < 1 || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 2000)
        return Task.FromResult(Invalid("Review the current task revision and enter a cancellation reason of 1 to 2000 characters."));
      return CommandAsync(http, async (db, actor, ct) =>
      {
        var scoped = await EngagementWorkflowTaskService.EnsureTaskScopeAsync(db, actor, engagementId, taskId, ct);
        if (!scoped.Succeeded) return CommandResult<bool>.Fail(scoped.ErrorCode!, scoped.Message!);
        var result = await EngagementWorkflowTaskService.CancelAsync(db, actor, taskId,
          input.Reason, input.ExpectedRevision, ct);
        return result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
      });
    }).WithDescription("Cancels one active operational task under the current scoped management grant and expected revision. Retains its reason and activity; never approves source work or satisfies a dependent task's prerequisite.")
      .WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);

    tasks.MapPost("/{taskId:guid}/reopen", (
      Guid engagementId, Guid taskId, WorkflowTaskReopeningInput input, HttpContext http) =>
    {
      if (input.ExpectedRevision < 1 || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 2000)
        return Task.FromResult(Invalid("Review the current task revision and enter a reopening reason of 1 to 2000 characters."));
      return CommandAsync(http, async (db, actor, ct) =>
      {
        var scoped = await EngagementWorkflowTaskService.EnsureTaskScopeAsync(db, actor, engagementId, taskId, ct);
        if (!scoped.Succeeded) return CommandResult<bool>.Fail(scoped.ErrorCode!, scoped.Message!);
        var result = await EngagementWorkflowTaskService.ReopenAsync(db, actor, taskId,
          input.Reason, input.ExpectedRevision, ct);
        return result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
      });
    }).WithDescription("Reopens one cancelled operational task, or returns one completed task to active work for correction, under the current scoped management grant and expected revision. A completed task is refused while a dependent task is completed or its engagement is completed or closed. Retains its reopening reason and activity, including the original completion; never approves source work or changes professional decisions.")
      .WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);

    foreach (var (path, hold) in new[] { ("suspend", true), ("resume", false) })
      tasks.MapPost("/{taskId:guid}/" + path, (
        Guid engagementId, Guid taskId, WorkflowTaskReopeningInput input, HttpContext http) =>
      {
        if (input.ExpectedRevision < 1 || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 2000)
          return Task.FromResult(Invalid("Review the current task revision and enter a reason of 1 to 2000 characters."));
        return CommandAsync(http, async (db, actor, ct) =>
        {
          var scoped = await EngagementWorkflowTaskService.EnsureTaskScopeAsync(db, actor, engagementId, taskId, ct);
          if (!scoped.Succeeded) return CommandResult<bool>.Fail(scoped.ErrorCode!, scoped.Message!);
          var result = hold
            ? await EngagementWorkflowTaskService.SuspendAsync(db, actor, taskId, input.Reason, input.ExpectedRevision, ct)
            : await EngagementWorkflowTaskService.ResumeAsync(db, actor, taskId, input.Reason, input.ExpectedRevision, ct);
          return result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
        });
      }).WithDescription(hold
          ? "Puts one open or in-progress operational task on hold under the current scoped management grant and expected revision. A held task keeps its owner, due date, time and evidence, leaves the personal, date and client-waiting queue views and reminders, and cannot be completed or receive new time until resumed. Retains the reason as activity."
          : "Resumes one held operational task under the current scoped management grant and expected revision, returning it to the queue views and reminders. Retains the reason as activity; never approves source work.")
        .WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);

    tasks.MapPost("/{taskId:guid}/due-date", (
      Guid engagementId, Guid taskId, WorkflowTaskDueDateInput input, HttpContext http) =>
    {
      DateOnly? dueDate = null;
      if (!string.IsNullOrWhiteSpace(input.DueDate))
      {
        if (!TryDate(input.DueDate, out var parsed)) return Task.FromResult(Invalid("Enter the due date as a date."));
        dueDate = parsed;
      }
      return CommandAsync(http, async (db, actor, ct) =>
      {
        var scoped = await EngagementWorkflowTaskService.EnsureTaskScopeAsync(db, actor, engagementId, taskId, ct);
        if (!scoped.Succeeded) return CommandResult<bool>.Fail(scoped.ErrorCode!, scoped.Message!);
        var result = await EngagementWorkflowTaskService.ChangeDueDateAsync(db, actor, taskId, dueDate,
          input.ExpectedRevision, ct);
        return result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
      });
    }).WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);

    var plans = group.MapGroup("/engagements/{engagementId:guid}/task-plan")
      .AddEndpointFilter(async (context, next) =>
      {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        return configuration.GetValue<bool>("WorkflowExperience:Enabled")
          ? await next(context)
          : Results.NotFound();
      });
    plans.MapGet("/templates", (Guid engagementId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => EngagementWorkflowPlanService.TemplatesAsync(db, actor, engagementId, ct)))
      .WithT046Response<IReadOnlyList<WorkflowTemplateCatalogItem>>(includeNotFound: true);
    plans.MapPost("/preview", (Guid engagementId, WorkflowPlanPreviewInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => EngagementWorkflowPlanService.PreviewAsync(db, actor, engagementId,
        new PreviewEngagementWorkflowPlanRequest(input.TemplateRevisionId,
          (input.Owners ?? []).Select(x => new WorkflowPlanOwnerSelection(x.StepKey, x.AssigneeUserId)).ToArray(),
          (input.PbcOwners ?? []).Select(x => new WorkflowPlanPbcOwnerSelection(x.StepKey, x.ClientOwnerUserId,
            x.FirmOwnerUserId, x.ReviewerUserId)).ToArray(),
          (input.EvidenceSelections ?? []).Select(x => x is null
            ? new WorkflowPlanEvidenceSelection(string.Empty, string.Empty, Guid.Empty)
            : new WorkflowPlanEvidenceSelection(x.StepKey, x.RequirementKey, x.SourceId)).ToArray()), ct)))
      .WithT046Response<UiValueResponse<EngagementWorkflowPlanPreview>>(includeNotFound: true);
    plans.MapPost("/execute", (Guid engagementId, WorkflowPlanExecuteInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => EngagementWorkflowPlanService.ExecuteAsync(db, actor, engagementId,
        new ExecuteEngagementWorkflowPlanRequest(input.TemplateRevisionId, input.ExpectedPreviewDigest,
          input.IdempotencyKey,
          (input.Owners ?? []).Select(x => new WorkflowPlanOwnerSelection(x.StepKey, x.AssigneeUserId)).ToArray(),
          (input.PbcOwners ?? []).Select(x => new WorkflowPlanPbcOwnerSelection(x.StepKey, x.ClientOwnerUserId,
            x.FirmOwnerUserId, x.ReviewerUserId)).ToArray(),
          (input.EvidenceSelections ?? []).Select(x => x is null
            ? new WorkflowPlanEvidenceSelection(string.Empty, string.Empty, Guid.Empty)
            : new WorkflowPlanEvidenceSelection(x.StepKey, x.RequirementKey, x.SourceId)).ToArray()), ct)))
      .WithT046Response<UiValueResponse<EngagementWorkflowPlanReceipt>>(includeConflict: true, includeNotFound: true);
    plans.MapPost("/pbc-drafts", (Guid engagementId, WorkflowPlanPbcDraftsInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => EngagementWorkflowPlanService.CreatePbcDraftsAsync(db, actor,
        engagementId, new CreateWorkflowPlanPbcDraftsRequest(input.TemplateRevisionId, input.ExpectedPreviewDigest,
          (input.PbcOwners ?? []).Select(x => new WorkflowPlanPbcOwnerSelection(x.StepKey, x.ClientOwnerUserId,
            x.FirmOwnerUserId, x.ReviewerUserId)).ToArray()), ct)))
      .WithT046Response<UiValueResponse<EngagementWorkflowPlanReceipt>>(includeConflict: true, includeNotFound: true);
    plans.MapGet("/receipts/{idempotencyKey:guid}",
      (Guid engagementId, Guid idempotencyKey, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => EngagementWorkflowPlanService.GetReceiptAsync(
          db, actor, engagementId, idempotencyKey, ct)))
      .WithT046Response<EngagementWorkflowPlanReceipt>(includeNotFound: true);
  }

  private static Task<IResult> WorkflowQueueAsync(HttpContext http, string? view, DateOnly? afterDueDate,
    Guid? afterTaskId, int? pageSize, Guid? assigneeUserId, bool? unassignedOnly, bool? includeSummary) =>
    ReadAsync(http, async (db, actor, ct) =>
    {
      var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
      if (!configuration.GetValue<bool>("WorkflowExperience:Enabled"))
        return CommandResult<WorkflowTaskQueue>.Fail(ErrorCodes.ScopeDenied, "Workflow task experience is not enabled on this server.");
      return await WorkflowTaskQueueQuery.GetAsync(db, actor,
        new WorkflowTaskQueueRequest(view ?? "MY_TASKS", afterDueDate, afterTaskId, pageSize ?? 50,
          assigneeUserId, unassignedOnly ?? false, includeSummary ?? false), ct);
    });

  private static void MapWorkflowTemplateEndpoints(RouteGroupBuilder group)
  {
    var templates = group.MapGroup("/practice/workflow-templates")
      .AddEndpointFilter(async (context, next) =>
      {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        return configuration.GetValue<bool>("WorkflowExperience:Enabled")
          ? await next(context)
          : Results.NotFound();
      });

    templates.MapUiGet("", http =>
      ReadAsync(http, (db, actor, ct) => WorkflowTemplateService.ListAsync(db, actor, ct)))
      .WithT046Response<IReadOnlyList<WorkflowTemplateCatalogItem>>(includeNotFound: true);
    templates.MapGet("/{templateId:guid}/revisions/{revisionId:guid}",
      (Guid templateId, Guid revisionId, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => WorkflowTemplateService.GetRevisionAsync(db, actor,
          templateId, revisionId, ct)))
      .WithT046Response<WorkflowTemplateRevisionView>(includeNotFound: true);
    templates.MapPost("/drafts", (WorkflowTemplateDraftInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowTemplateService.CreateDraftAsync(db, actor,
        ToWorkflowTemplateDraft(input), ct)))
      .WithT046Response<UiValueResponse<Guid>>(includeNotFound: true);
    templates.MapPost("/{templateId:guid}/revisions/{revisionId:guid}/edit", (
      Guid templateId, Guid revisionId, WorkflowTemplateDraftInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowTemplateService.UpdateDraftAsync(db, actor,
        templateId, revisionId, ToWorkflowTemplateDraft(input with { TemplateId = templateId }), ct)))
      .WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);
    templates.MapPost("/{templateId:guid}/revisions/{revisionId:guid}/review", (
      Guid templateId, Guid revisionId, WorkflowTemplateDecisionInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowTemplateService.ReviewAsync(db, actor,
        templateId, revisionId, input.ExpectedRevision, input.Rationale, ct)))
      .WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);
    templates.MapPost("/{templateId:guid}/revisions/{revisionId:guid}/publish", (
      Guid templateId, Guid revisionId, WorkflowTemplateDecisionInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowTemplateService.PublishAsync(db, actor,
        templateId, revisionId, input.ExpectedRevision, input.Rationale, ct)))
      .WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);
    templates.MapPost("/{templateId:guid}/archive", (
      Guid templateId, WorkflowTemplateArchiveInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowTemplateService.ArchiveAsync(db, actor,
        templateId, input.Rationale, ct)))
      .WithT046Response<UiValueResponse<bool>>(includeNotFound: true);
  }

  private static SaveWorkflowTemplateDraftRequest ToWorkflowTemplateDraft(WorkflowTemplateDraftInput input) =>
    new(input.TemplateId, input.Name, input.ServiceRoute, input.IndustryKey,
      (input.Steps ?? []).Select(x => new WorkflowTemplateStepDraft(x.StableKey, x.Phase, x.Title, x.Instructions,
        x.SuggestedStaffingLevel, x.EstimatedEffortMinutes, x.DueDateOffsetDays, x.PbcRequestReference,
        x.RequiredTask, x.SortOrder, x.DependsOnStepKeys, x.PbcDefinition is null ? null :
          new WorkflowTemplatePbcDefinition(x.PbcDefinition.Objective, x.PbcDefinition.EntityScope, x.PbcDefinition.Area,
            x.PbcDefinition.RequestedFormat, x.PbcDefinition.ControlTotals, x.PbcDefinition.Confidentiality,
            x.PbcDefinition.AcceptanceCriteria), x.TaskGroupName,
        (x.EvidenceRequirements ?? []).Select(requirement => requirement is null
          ? new WorkflowTemplateEvidenceRequirementDraft(string.Empty, string.Empty)
          : new WorkflowTemplateEvidenceRequirementDraft(requirement.RequirementKey, requirement.Kind)).ToArray(),
        x.ApplicabilityCondition)).ToArray(), input.ExpectedRevision);
}
