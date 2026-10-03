using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapBudgetApprovalEndpoints(RouteGroupBuilder group)
  {
    const string route = "/engagements/{id:guid}/budget-approval";
    group.MapPost(route + "/preview", (Guid id, BudgetApprovalRequest input, HttpContext http) =>
      CommandAsync(http, (db, a, ct) => BudgetApprovalWorkspace.PreviewAsync(db, a, id, input, ct)));
    group.MapPost(route, (Guid id, BudgetApprovalRequest input, HttpContext http) =>
      CommandAsync(http, (db, a, ct) => BudgetApprovalWorkspace.ExecuteAsync(db, a, id, input, ct)));
    group.MapGet(route + "/receipts/{requestId:guid}", (Guid id, Guid requestId, string requestHash, HttpContext http) =>
      ReadAsync(http, (db, a, ct) => BudgetApprovalWorkspace.LookupAsync(db, a, id, requestId, requestHash, ct)));
  }
}
