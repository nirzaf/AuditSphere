using AuditSphereOps.Application.Accounting;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record AdvancedScheduleInput(string SourceManifestJson, string InputSnapshotJson);

  private static void MapConsolidationEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/consolidation", http => ReadAsync(http, (db, actor, ct) => ConsolidationOverviewQuery.GetAsync(db, actor, ct)));
    group.MapGet("/consolidation/advanced/{scopeId:guid}", (Guid scopeId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdvancedConsolidationWorkspaceQuery.GetAsync(db, actor, scopeId, ct)));
    group.MapPost("/consolidation/advanced/{scopeId:guid}/schedules", (Guid scopeId, AdvancedScheduleInput i, HttpContext http) =>
      CommandAsync(http, async (db, actor, ct) =>
      {
        var workspace = await AdvancedConsolidationWorkspaceQuery.GetAsync(db, actor, scopeId, ct);
        if (!workspace.Succeeded) return Domain.Shared.CommandResult<Guid>.Fail(workspace.ErrorCode!, workspace.Message!);
        return await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, actor, new AdvancedConsolidationMethodScheduleRequest(scopeId, workspace.Value!.Scope.Method,
          "IFRS", i.SourceManifestJson ?? "", i.InputSnapshotJson ?? ""), ct);
      }));
    group.MapPost("/consolidation/schedules/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, actor, id, ct)));
    group.MapPost("/consolidation/advanced/{scopeId:guid}/executions", (Guid scopeId, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AdvancedConsolidationWorkspaceQuery.RunExecutionAsync(db, actor, scopeId, ct)));
    group.MapPost("/consolidation/executions/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ConsolidationService.ApproveAdvancedExecutionAsync(db, actor, id, ct)));
  }
}
