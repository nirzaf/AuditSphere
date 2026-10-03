using AuditSphereOps.Application.Accounting;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapAdjustmentPlanEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/accounting/adjustment-plans", (int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdjustmentPlanWorkspace.ListAsync(db, actor, page ?? 0, ct)));
    group.MapGet("/accounting/adjustment-plans/{id:guid}", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdjustmentPlanWorkspace.GetAsync(db, actor, id, page ?? 0, ct)));
    group.MapGet("/accounting/adjustment-plans/{id:guid}/history", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdjustmentPlanWorkspace.HistoryAsync(db, actor, id, page ?? 0, ct)));
    group.MapGet("/datasets/{id:guid}/adjustment-plans", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdjustmentPlanWorkspace.GetCreationOptionsAsync(db, actor, id, page ?? 0, ct)));
    foreach (var action in new[] { "CREATE", "FINALIZE" })
    {
      var expected = action;
      var path = action == "CREATE" ? "/datasets/{id:guid}/adjustment-plans" : "/accounting/adjustment-plans/{id:guid}/finalization";
      group.MapGet(path + "/receipts/{requestId:guid}", (Guid id, Guid requestId, string requestHash, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => AdjustmentPlanWorkspace.LookupCommandAsync(db, actor, id, expected, requestId, requestHash, ct)));
      group.MapPost(path + "/preview", (Guid id, PlanCommandRequest? input, HttpContext http) => input?.Action != expected ?
        Task.FromResult(Failure("accounting.plan.rejected", "Choose the supported plan action.", 400)) :
        CommandAsync(http, (db, actor, ct) => AdjustmentPlanWorkspace.PreviewCommandAsync(db, actor, id, input, ct)));
      group.MapPost(path, (Guid id, PlanCommandRequest? input, HttpContext http) => input?.Action != expected ?
        Task.FromResult(Failure("accounting.plan.rejected", "Choose the supported plan action.", 400)) :
        CommandAsync(http, (db, actor, ct) => AdjustmentPlanWorkspace.ExecuteCommandAsync(db, actor, id, input, ct)));
    }
  }
}
