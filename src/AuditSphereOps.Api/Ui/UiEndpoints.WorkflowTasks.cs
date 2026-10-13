using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
  public sealed record WorkflowTaskDueDateInput(string? DueDate, long ExpectedRevision);
  public sealed record WorkflowDependencyChangeInput(Guid PredecessorTaskId, bool Required, long ExpectedRevision,
    long ExpectedPredecessorRevision, string Reason, Guid IdempotencyKey);
  public sealed record WorkflowEvidenceChangeInput(string Kind, Guid? SourceId, bool Required,
    string Reason, long ExpectedRevision, Guid IdempotencyKey);
  public sealed record WorkflowTemplateStepInput(
    string StableKey, string Phase, string Title, string Instructions, string? SuggestedStaffingLevel,
    int? EstimatedEffortMinutes, int? DueDateOffsetDays, string? PbcRequestReference, bool RequiredTask,
    int SortOrder, IReadOnlyList<string>? DependsOnStepKeys);
  public sealed record WorkflowTemplateDraftInput(
    Guid? TemplateId, string Name, string ServiceRoute, string? IndustryKey,
    IReadOnlyList<WorkflowTemplateStepInput> Steps, long? ExpectedRevision);
  public sealed record WorkflowTemplateDecisionInput(long ExpectedRevision, string Rationale);
  public sealed record WorkflowTemplateArchiveInput(string Rationale);
  public sealed record WorkflowPlanOwnerInput(string StepKey, Guid AssigneeUserId);
  public sealed record WorkflowPlanPreviewInput(Guid TemplateRevisionId, IReadOnlyList<WorkflowPlanOwnerInput>? Owners);
  public sealed record WorkflowPlanExecuteInput(Guid TemplateRevisionId, string ExpectedPreviewDigest,
    Guid IdempotencyKey, IReadOnlyList<WorkflowPlanOwnerInput>? Owners);

  private static void MapWorkflowTaskEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/work/reviews", (DateTimeOffset? afterSubmittedAt, Guid? afterResultId, int? pageSize, HttpContext http) =>
      ReadAsync(http, async (db, actor, ct) =>
      {
        var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>("WorkflowExperience:Enabled"))
          return CommandResult<ProcedureReviewInbox>.Fail(ErrorCodes.ScopeDenied,
            "Workflow task experience is not enabled on this server.");
        return await ProcedureReviewInboxQuery.GetAsync(db, actor,
          new ProcedureReviewInboxRequest(afterSubmittedAt, afterResultId, pageSize ?? 50), ct);
      }));

    group.MapGet("/work/tasks", (string? view, DateOnly? afterDueDate, Guid? afterTaskId, int? pageSize,
      Guid? assigneeUserId, bool? unassignedOnly, HttpContext http) =>
      WorkflowQueueAsync(http, view, afterDueDate, afterTaskId, pageSize, assigneeUserId, unassignedOnly));

    var tasks = group.MapGroup("/engagements/{engagementId:guid}/workflow/tasks")
      .AddEndpointFilter(async (context, next) =>
      {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        return configuration.GetValue<bool>("WorkflowExperience:Enabled")
          ? await next(context)
          : Results.NotFound();
      });

    tasks.MapGet("", (Guid engagementId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => EngagementWorkflowTaskService.GetAsync(db, actor, engagementId, ct)));

    tasks.MapGet("/{taskId:guid}/dependencies", (Guid engagementId, Guid taskId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => WorkflowTaskDependencyQuery.GetAsync(db, actor, engagementId, taskId, ct)));
    tasks.MapGet("/{taskId:guid}/dependencies/receipts/{key:guid}", (Guid engagementId, Guid taskId, Guid key, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => WorkflowTaskDependencyQuery.ReceiptAsync(db, actor, engagementId, taskId, key, ct)));
    tasks.MapPost("/{taskId:guid}/dependencies", (Guid engagementId, Guid taskId, WorkflowDependencyChangeInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PracticeTimeService.ChangeWorkflowDependencyAsync(db, actor, engagementId, taskId,
        new(input.PredecessorTaskId, input.Required, input.ExpectedRevision, input.ExpectedPredecessorRevision, input.Reason, input.IdempotencyKey), ct)));

    tasks.MapGet("/{taskId:guid}/evidence", (Guid engagementId, Guid taskId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => WorkflowTaskEvidenceQuery.GetAsync(db, actor, engagementId, taskId, ct)));
    tasks.MapGet("/{taskId:guid}/evidence/receipts/{key:guid}", (Guid engagementId, Guid taskId, Guid key, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => WorkflowTaskEvidenceQuery.ReceiptAsync(db, actor, engagementId, taskId, key, ct)));
    tasks.MapPost("/{taskId:guid}/evidence", (
      Guid engagementId, Guid taskId, WorkflowEvidenceChangeInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PracticeTimeService.ChangeWorkflowEvidenceAsync(db, actor,
        engagementId, taskId, new(input.Kind, input.SourceId, input.Required, input.Reason,
          input.ExpectedRevision, input.IdempotencyKey), ct)));

    tasks.MapGet("/{taskId:guid}/activity", (
      Guid engagementId, Guid taskId, DateOnly? from, DateOnly? to,
      DateTimeOffset? afterOccurredAt, Guid? afterActivityId, int? pageSize, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => WorkflowTaskActivityQuery.GetAsync(db, actor,
        engagementId, taskId, new WorkflowTaskActivityRequest(from, to, afterOccurredAt,
          afterActivityId, pageSize ?? 50), ct)));

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
    });

    tasks.MapPost("/{taskId:guid}/assign", (
      Guid engagementId, Guid taskId, WorkflowTaskAssignmentInput input, HttpContext http) =>
      CommandAsync(http, async (db, actor, ct) =>
      {
        var scoped = await EngagementWorkflowTaskService.EnsureTaskScopeAsync(db, actor, engagementId, taskId, ct);
        if (!scoped.Succeeded) return CommandResult<bool>.Fail(scoped.ErrorCode!, scoped.Message!);
        var result = await EngagementWorkflowTaskService.AssignAsync(db, actor, taskId,
          input.AssigneeUserId, input.ExpectedRevision, ct);
        return result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
      }));

    tasks.MapPost("/{taskId:guid}/reassign", (
      Guid engagementId, Guid taskId, WorkflowTaskAssignmentInput input, HttpContext http) =>
      CommandAsync(http, async (db, actor, ct) =>
      {
        var scoped = await EngagementWorkflowTaskService.EnsureTaskScopeAsync(db, actor, engagementId, taskId, ct);
        if (!scoped.Succeeded) return CommandResult<bool>.Fail(scoped.ErrorCode!, scoped.Message!);
        var result = await EngagementWorkflowTaskService.ReassignAsync(db, actor, taskId,
          input.AssigneeUserId, input.ExpectedRevision, ct);
        return result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
      }));

    tasks.MapPost("/{taskId:guid}/complete", (
      Guid engagementId, Guid taskId, WorkflowTaskCompletionInput input, HttpContext http) =>
      CommandAsync(http, async (db, actor, ct) =>
      {
        var scoped = await EngagementWorkflowTaskService.EnsureTaskScopeAsync(db, actor, engagementId, taskId, ct);
        if (!scoped.Succeeded) return CommandResult<bool>.Fail(scoped.ErrorCode!, scoped.Message!);
        var result = await EngagementWorkflowTaskService.CompleteAsync(db, actor, taskId, input.ExpectedRevision, ct);
        return result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
      }));

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
    });

    var plans = group.MapGroup("/engagements/{engagementId:guid}/task-plan")
      .AddEndpointFilter(async (context, next) =>
      {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        return configuration.GetValue<bool>("WorkflowExperience:Enabled")
          ? await next(context)
          : Results.NotFound();
      });
    plans.MapGet("/templates", (Guid engagementId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => EngagementWorkflowPlanService.TemplatesAsync(db, actor, engagementId, ct)));
    plans.MapPost("/preview", (Guid engagementId, WorkflowPlanPreviewInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => EngagementWorkflowPlanService.PreviewAsync(db, actor, engagementId,
        new PreviewEngagementWorkflowPlanRequest(input.TemplateRevisionId,
          (input.Owners ?? []).Select(x => new WorkflowPlanOwnerSelection(x.StepKey, x.AssigneeUserId)).ToArray()), ct)));
    plans.MapPost("/execute", (Guid engagementId, WorkflowPlanExecuteInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => EngagementWorkflowPlanService.ExecuteAsync(db, actor, engagementId,
        new ExecuteEngagementWorkflowPlanRequest(input.TemplateRevisionId, input.ExpectedPreviewDigest,
          input.IdempotencyKey,
          (input.Owners ?? []).Select(x => new WorkflowPlanOwnerSelection(x.StepKey, x.AssigneeUserId)).ToArray()), ct)));
    plans.MapGet("/receipts/{idempotencyKey:guid}",
      (Guid engagementId, Guid idempotencyKey, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => EngagementWorkflowPlanService.GetReceiptAsync(
          db, actor, engagementId, idempotencyKey, ct)));
  }

  private static Task<IResult> WorkflowQueueAsync(HttpContext http, string? view, DateOnly? afterDueDate,
    Guid? afterTaskId, int? pageSize, Guid? assigneeUserId, bool? unassignedOnly) =>
    ReadAsync(http, async (db, actor, ct) =>
    {
      var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
      if (!configuration.GetValue<bool>("WorkflowExperience:Enabled"))
        return CommandResult<WorkflowTaskQueue>.Fail(ErrorCodes.ScopeDenied, "Workflow task experience is not enabled on this server.");
      return await WorkflowTaskQueueQuery.GetAsync(db, actor,
        new WorkflowTaskQueueRequest(view ?? "MY_TASKS", afterDueDate, afterTaskId, pageSize ?? 50,
          assigneeUserId, unassignedOnly ?? false), ct);
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
      ReadAsync(http, (db, actor, ct) => WorkflowTemplateService.ListAsync(db, actor, ct)));
    templates.MapGet("/{templateId:guid}/revisions/{revisionId:guid}",
      (Guid templateId, Guid revisionId, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => WorkflowTemplateService.GetRevisionAsync(db, actor,
          templateId, revisionId, ct)));
    templates.MapPost("/drafts", (WorkflowTemplateDraftInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowTemplateService.CreateDraftAsync(db, actor,
        ToWorkflowTemplateDraft(input), ct)));
    templates.MapPost("/{templateId:guid}/revisions/{revisionId:guid}/edit", (
      Guid templateId, Guid revisionId, WorkflowTemplateDraftInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowTemplateService.UpdateDraftAsync(db, actor,
        templateId, revisionId, ToWorkflowTemplateDraft(input with { TemplateId = templateId }), ct)));
    templates.MapPost("/{templateId:guid}/revisions/{revisionId:guid}/review", (
      Guid templateId, Guid revisionId, WorkflowTemplateDecisionInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowTemplateService.ReviewAsync(db, actor,
        templateId, revisionId, input.ExpectedRevision, input.Rationale, ct)));
    templates.MapPost("/{templateId:guid}/revisions/{revisionId:guid}/publish", (
      Guid templateId, Guid revisionId, WorkflowTemplateDecisionInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowTemplateService.PublishAsync(db, actor,
        templateId, revisionId, input.ExpectedRevision, input.Rationale, ct)));
    templates.MapPost("/{templateId:guid}/archive", (
      Guid templateId, WorkflowTemplateArchiveInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowTemplateService.ArchiveAsync(db, actor,
        templateId, input.Rationale, ct)));
  }

  private static SaveWorkflowTemplateDraftRequest ToWorkflowTemplateDraft(WorkflowTemplateDraftInput input) =>
    new(input.TemplateId, input.Name, input.ServiceRoute, input.IndustryKey,
      (input.Steps ?? []).Select(x => new WorkflowTemplateStepDraft(x.StableKey, x.Phase, x.Title, x.Instructions,
        x.SuggestedStaffingLevel, x.EstimatedEffortMinutes, x.DueDateOffsetDays, x.PbcRequestReference,
        x.RequiredTask, x.SortOrder, x.DependsOnStepKeys)).ToArray(), input.ExpectedRevision);
}
