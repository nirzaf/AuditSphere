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
  }
}
