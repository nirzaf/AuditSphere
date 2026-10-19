using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapWorkflowReminderRuleEndpoints(RouteGroupBuilder group)
  {
    var rules = group.MapGroup("/practice/reminder-rules")
      .AddEndpointFilter(async (context, next) =>
      {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        return configuration.GetValue<bool>("WorkflowExperience:Enabled")
          ? await next(context)
          : Results.NotFound();
      });

    rules.MapGet("", async (DateTimeOffset? afterCreatedAt, Guid? afterRuleId, int? pageSize,
      HttpContext http, WorkflowReminderOptions options) =>
    {
      if ((afterCreatedAt is null) != (afterRuleId is null) || !ValidPageSize(pageSize))
        return Invalid("Provide the complete rule cursor and a page size from 1 to 100.");
      var cursor = afterCreatedAt is { } createdAt && afterRuleId is { } ruleId
        ? new WorkflowReminderRuleCursor(createdAt, ruleId) : null;
      return await ReadAsync(http, (db, actor, ct) => WorkflowReminderRuleService.ListAsync(
        db, actor, options, cursor, pageSize ?? 25, ct));
    }).WithT046Response<WorkflowReminderRuleCatalog>(includeNotFound: true);

    rules.MapGet("/{ruleId:guid}/revisions/{revisionId:guid}",
      (Guid ruleId, Guid revisionId, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => WorkflowReminderRuleService.GetAsync(
          db, actor, ruleId, revisionId, ct)))
      .WithT046Response<WorkflowReminderRuleRevisionView>(includeNotFound: true);

    rules.MapPost("/drafts", (WorkflowReminderRuleDraftRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowReminderRuleService.SaveDraftAsync(db, actor, input, ct)))
      .WithT046Response<UiValueResponse<WorkflowReminderRuleRevisionView>>(includeConflict: true, includeNotFound: true);

    rules.MapGet("/drafts/{requestKey:guid}/receipt", (Guid requestKey, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => WorkflowReminderRuleService.ReceiptAsync(db, actor, requestKey, ct)))
      .WithT046Response<WorkflowReminderRuleRevisionView>(includeNotFound: true);

    rules.MapPost("/{ruleId:guid}/revisions/{revisionId:guid}/review",
      (Guid ruleId, Guid revisionId, WorkflowReminderRuleDecisionRequest input, HttpContext http) =>
        CommandAsync(http, (db, actor, ct) => WorkflowReminderRuleService.ReviewAsync(
          db, actor, ruleId, revisionId, input, ct)))
      .WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);

    rules.MapPost("/{ruleId:guid}/revisions/{revisionId:guid}/publish",
      (Guid ruleId, Guid revisionId, WorkflowReminderRuleDecisionRequest input, HttpContext http) =>
        CommandAsync(http, (db, actor, ct) => WorkflowReminderRuleService.PublishAsync(
          db, actor, ruleId, revisionId, input, ct)))
      .WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);

    rules.MapPost("/{ruleId:guid}/suspend", (Guid ruleId, WorkflowReminderRuleStateRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowReminderRuleService.SuspendAsync(db, actor, ruleId, input, ct)))
      .WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);

    rules.MapPost("/{ruleId:guid}/resume", (Guid ruleId, WorkflowReminderRuleStateRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => WorkflowReminderRuleService.ResumeAsync(db, actor, ruleId, input, ct)))
      .WithT046Response<UiValueResponse<bool>>(includeConflict: true, includeNotFound: true);

    rules.MapGet("/{ruleId:guid}/history", async (Guid ruleId, Guid? afterId, int? pageSize, HttpContext http) =>
    {
      if (!ValidPageSize(pageSize)) return Invalid("Page size must be from 1 to 100.");
      return await ReadAsync(http, (db, actor, ct) => WorkflowReminderRuleService.HistoryAsync(
        db, actor, ruleId, afterId, pageSize ?? 25, ct));
    }).WithT046Response<WorkflowReminderRuleHistory>(includeNotFound: true);

    rules.MapGet("/{ruleId:guid}/executions", async (Guid ruleId, DateTimeOffset? afterCreatedAt,
      Guid? afterOperationId, int? pageSize, HttpContext http) =>
    {
      if ((afterCreatedAt is null) != (afterOperationId is null) || !ValidPageSize(pageSize))
        return Invalid("Provide the complete execution cursor and a page size from 1 to 100.");
      var cursor = afterCreatedAt is { } createdAt && afterOperationId is { } operationId
        ? new WorkflowReminderExecutionCursor(createdAt, operationId) : null;
      return await ReadAsync(http, (db, actor, ct) => WorkflowReminderRuleService.ExecutionsAsync(
        db, actor, ruleId, cursor, pageSize ?? 25, ct));
    }).WithT046Response<WorkflowReminderExecutions>(includeNotFound: true);
  }

  private static bool ValidPageSize(int? pageSize) => pageSize is null or >= 1 and <= 100;
}
