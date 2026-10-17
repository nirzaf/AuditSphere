using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapBudgetApprovalEndpoints(RouteGroupBuilder group)
  {
    const string route = "/engagements/{id:guid}/budget-approval";
    var preview = group.MapPost(route + "/preview", (Guid id, BudgetApprovalRequest input, HttpContext http) =>
      CommandAsync(http, (db, a, ct) => BudgetApprovalWorkspace.PreviewAsync(db, a, id, input, ct)));
    preview.WithSummary("Preview engagement budget approval")
      .WithDescription("Returns the exact draft budget and independent approval basis without approving it.");
    WithUiValueResponse<BudgetApprovalPreview>(preview);

    var execute = group.MapPost(route, (Guid id, BudgetApprovalRequest input, HttpContext http) =>
      CommandAsync(http, (db, a, ct) => BudgetApprovalWorkspace.ExecuteAsync(db, a, id, input, ct)));
    execute.WithSummary("Approve an engagement budget")
      .WithDescription("Records independent approval of the exact budget draft and returns its immutable receipt in the UI value envelope.");
    WithUiValueResponse<BudgetApprovalReceipt>(execute);

    var receipt = group.MapGet(route + "/receipts/{requestId:guid}", (Guid id, Guid requestId, string requestHash, HttpContext http) =>
      ReadAsync(http, (db, a, ct) => BudgetApprovalWorkspace.LookupAsync(db, a, id, requestId, requestHash, ct)));
    receipt.WithSummary("Read an engagement budget approval receipt")
      .WithDescription("Looks up the exact budget-approval outcome for the supplied request identity and hash.");
    WithUiDirectResponse<BudgetApprovalLookup>(receipt, includeConflict: true, includeBindingProblem: true);
  }
}
