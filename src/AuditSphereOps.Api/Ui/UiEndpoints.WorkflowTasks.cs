using AuditSphereOps.Application.Practice;
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

    var plans = group.MapGroup("/engagements/{engagementId:guid}/task-plan")
      .AddEndpointFilter(async (context, next) =>
      {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        return configuration.GetValue<bool>("WorkflowExperience:Enabled")
          ? await next(context)
          : Results.NotFound();
      });
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
