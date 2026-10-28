using System.Globalization;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record WorkflowApplicabilityProposalInput(Guid SourceReceiptId, string ExpectedTaskRevision,
    Guid? ExpectedProposalId, string BasisHash, string Reason, Guid IdempotencyKey, bool Reviewed);
  public sealed record WorkflowApplicabilityDecisionInput(Guid ProposalId, Guid? ExpectedDecisionId,
    string Kind, string ExpectedTaskRevision, string BasisHash, string Reason, Guid IdempotencyKey, bool Reviewed);
  public sealed record WorkflowApplicabilityDecisionResponse(Guid Id, string Version, string Kind, Guid ActorUserId,
    string ActorName, string Reason, DateTimeOffset OccurredAt);
  public sealed record WorkflowApplicabilityProposalResponse(Guid Id, string Version, Guid ActorUserId, string ActorName,
    string Reason, DateTimeOffset OccurredAt, Guid SourceReceiptId, string BasisHash, string BasisJson,
    bool IsLatest, bool BasisCurrent, string? CurrentBasisJson, string? Blocker, WorkflowApplicabilityDecisionResponse? Decision);
  public sealed record WorkflowApplicabilityWorkspaceResponse(Guid EngagementId, Guid TaskId, string TaskRevision,
    Guid? LatestProposalId, WorkflowApplicabilityProposalResponse? Proposal, WorkflowApplicabilityPreview? Preview,
    bool CanPropose, bool CanApprove, bool CanReturn, bool CanRevoke);
  public sealed record WorkflowApplicabilityReceiptResponse(Guid EngagementId, Guid TaskId, Guid IdempotencyKey,
    Guid ProposalId, Guid? DecisionId, string Version);
  public sealed record WorkflowApplicabilityHistoryItemResponse(Guid Id, string RecordType, string Kind, Guid ProposalId,
    string Version, Guid ActorUserId, string ActorName, string Reason, string BasisHash, DateTimeOffset OccurredAt);
  public sealed record WorkflowApplicabilityHistoryResponse(Guid EngagementId, Guid TaskId,
    IReadOnlyList<WorkflowApplicabilityHistoryItemResponse> Items, bool HasMore, WorkflowApplicabilityHistoryCursor? NextCursor);

  // Mounted inside the existing authenticated and WorkflowExperience-filtered task group.
  private static void MapWorkflowApplicabilityEndpoints(RouteGroupBuilder tasks)
  {
    tasks.MapGet("/{taskId:guid}/applicability", (Guid engagementId, Guid taskId,
      Guid? sourceRequestId, Guid? proposalId, HttpContext http) => ReadAsync(http, async (db, actor, ct) =>
      {
        var result = await WorkflowTaskApplicabilityQuery.GetAsync(db, actor, engagementId, taskId, sourceRequestId, proposalId, ct);
        return result.Succeeded ? CommandResult<WorkflowApplicabilityWorkspaceResponse>.Ok(ApplicabilityWorkspace(result.Value!))
          : CommandResult<WorkflowApplicabilityWorkspaceResponse>.Fail(result.ErrorCode!, result.Message!);
      }))
      .WithDescription("Reads current conditional task applicability and an optional exact accepted PBC request preview. Retained proposals and historical receipts do not establish current applicability or professional approval. Revisions are exact Int64 strings; basis JSON remains opaque review text.")
      .WithT046Response<WorkflowApplicabilityWorkspaceResponse>(includeConflict: true, includeNotFound: true);

    tasks.MapGet("/{taskId:guid}/applicability/history", (Guid engagementId, Guid taskId,
      DateTimeOffset? afterOccurredAt, string? afterType, Guid? afterId, HttpContext http) =>
      ReadAsync(http, async (db, actor, ct) =>
      {
        if (afterOccurredAt.HasValue != afterId.HasValue || afterId.HasValue != (afterType is not null))
          return CommandResult<WorkflowApplicabilityHistoryResponse>.Fail("request.invalid", "Use the complete retained history cursor.");
        var cursor = afterOccurredAt.HasValue ? new WorkflowApplicabilityHistoryCursor(afterOccurredAt.Value, afterType!, afterId!.Value) : null;
        var result = await WorkflowTaskApplicabilityQuery.HistoryAsync(db, actor, engagementId, taskId, cursor, ct);
        if (!result.Succeeded) return CommandResult<WorkflowApplicabilityHistoryResponse>.Fail(result.ErrorCode!, result.Message!);
        var page = result.Value!;
        return CommandResult<WorkflowApplicabilityHistoryResponse>.Ok(new(page.EngagementId, page.TaskId,
          page.Items.Select(x => new WorkflowApplicabilityHistoryItemResponse(x.Id, x.RecordType, x.Kind, x.ProposalId,
            x.Version.ToString(CultureInfo.InvariantCulture), x.ActorUserId, x.ActorName, x.Reason, x.BasisHash, x.OccurredAt)).ToArray(),
          page.HasMore, page.NextCursor));
      }))
      .WithDescription("Reads at most 25 retained applicability events in this exact task scope. Cursor order is occurrence/type/identity; a cursor is never authority. No edits, export or command replay.")
      .WithT046Response<WorkflowApplicabilityHistoryResponse>(includeConflict: true, includeNotFound: true);

    tasks.MapGet("/{taskId:guid}/applicability/receipts/{key:guid}", (Guid engagementId, Guid taskId, Guid key, HttpContext http) =>
      ReadAsync(http, async (db, actor, ct) => ApplicabilityReceiptResult(
        await WorkflowTaskApplicabilityQuery.ReceiptAsync(db, actor, engagementId, taskId, key, ct))))
      .WithDescription("Recovers only the current actor's exact original proposal or decision request under current scoped management authority. A retained result is historical evidence, not current exclusion; no command is resubmitted.")
      .WithT046Response<WorkflowApplicabilityReceiptResponse>(includeConflict: true, includeNotFound: true);

    tasks.MapPost("/{taskId:guid}/applicability/proposals", (Guid engagementId, Guid taskId,
      WorkflowApplicabilityProposalInput input, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
      {
        if (input is null || !input.Reviewed || !TryApplicabilityRevision(input.ExpectedTaskRevision, out var revision))
          return CommandResult<WorkflowApplicabilityReceiptResponse>.Fail("request.invalid", "Refresh and assent to the exact task revision, accepted source and reason.");
        return ApplicabilityReceiptResult(await PracticeTimeService.ProposeWorkflowApplicabilityAsync(db, actor, engagementId, taskId,
          new(input.SourceReceiptId, revision, input.ExpectedProposalId, input.BasisHash, input.Reason, input.IdempotencyKey), ct));
      }))
      .WithDescription("Retains a source-bound conditional task proposal with explicit assent, expected head/revision and actor-owned request key. Never changes operational task status, approves source work or waives professional decisions.")
      .WithT046Response<UiValueResponse<WorkflowApplicabilityReceiptResponse>>(includeConflict: true, includeNotFound: true);

    tasks.MapPost("/{taskId:guid}/applicability/decisions", (Guid engagementId, Guid taskId,
      WorkflowApplicabilityDecisionInput input, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
      {
        if (input is null || !input.Reviewed || !TryApplicabilityRevision(input.ExpectedTaskRevision, out var revision))
          return CommandResult<WorkflowApplicabilityReceiptResponse>.Fail("request.invalid", "Refresh and assent to the exact independent decision and retained head.");
        return ApplicabilityReceiptResult(await PracticeTimeService.DecideWorkflowApplicabilityAsync(db, actor, engagementId, taskId,
          new(input.ProposalId, input.ExpectedDecisionId, input.Kind, revision, input.BasisHash, input.Reason, input.IdempotencyKey), ct));
      }))
      .WithDescription("Independently approves, returns or revokes one exact task applicability proposal. Approval rechecks current source basis; removal remains available on stale evidence under current authority. Preserves professional procedure decisions and TASK_COMPLETED dependencies.")
      .WithT046Response<UiValueResponse<WorkflowApplicabilityReceiptResponse>>(includeConflict: true, includeNotFound: true);
  }

  private static bool TryApplicabilityRevision(string? value, out long revision) =>
    long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out revision) && revision > 0 &&
    value == revision.ToString(CultureInfo.InvariantCulture);

  private static WorkflowApplicabilityWorkspaceResponse ApplicabilityWorkspace(WorkflowApplicabilityWorkspace x)
  {
    var p = x.Proposal;
    return new(x.EngagementId, x.TaskId, x.TaskRevision.ToString(CultureInfo.InvariantCulture), x.LatestProposalId,
      p is null ? null : new(p.Id, p.Version.ToString(CultureInfo.InvariantCulture), p.ActorUserId, p.ActorName, p.Reason,
        p.OccurredAt, p.SourceReceiptId, p.BasisHash, p.BasisJson, p.IsLatest, p.BasisCurrent, p.CurrentBasisJson, p.Blocker,
        p.Decision is not { } d ? null : new(d.Id, d.Version.ToString(CultureInfo.InvariantCulture), d.Kind,
          d.ActorUserId, d.ActorName, d.Reason, d.OccurredAt)),
      x.Preview, x.CanPropose, x.CanApprove, x.CanReturn, x.CanRevoke);
  }

  private static CommandResult<WorkflowApplicabilityReceiptResponse> ApplicabilityReceiptResult(CommandResult<WorkflowApplicabilityReceipt> result) =>
    result.Succeeded ? CommandResult<WorkflowApplicabilityReceiptResponse>.Ok(new(result.Value!.EngagementId,
      result.Value.TaskId, result.Value.IdempotencyKey, result.Value.ProposalId, result.Value.DecisionId,
      result.Value.Version.ToString(CultureInfo.InvariantCulture)))
      : CommandResult<WorkflowApplicabilityReceiptResponse>.Fail(result.ErrorCode!, result.Message!);
}
