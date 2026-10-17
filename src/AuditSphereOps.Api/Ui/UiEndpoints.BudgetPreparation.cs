using AuditSphereOps.Application.Practice;
namespace AuditSphereOps.Api.Ui;
public static partial class UiEndpoints
{
  private static void MapBudgetPreparationEndpoints(RouteGroupBuilder group)
  {
    const string route="/engagements/{id:guid}/budget-preparation";
    var preview = group.MapPost(route+"/preview",(Guid id,BudgetPreparationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>BudgetPreparationWorkspace.PreviewAsync(db,a,id,input,ct)))
      .WithSummary("Preview engagement budget preparation")
      .WithDescription("Returns the bounded budget lines, rates and review basis without preparing a budget.");
    WithUiValueResponse<BudgetPreparationPreview>(preview);

    var execute = group.MapPost(route,(Guid id,BudgetPreparationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>BudgetPreparationWorkspace.ExecuteAsync(db,a,id,input,ct)))
      .WithSummary("Prepare an engagement budget")
      .WithDescription("Records the reviewed budget preparation and returns its immutable receipt in the UI value envelope.");
    WithUiValueResponse<BudgetPreparationReceipt>(execute);

    var receipt = group.MapGet(route+"/receipts/{requestId:guid}",(Guid id,Guid requestId,string requestHash,HttpContext http)=>ReadAsync(http,(db,a,ct)=>BudgetPreparationWorkspace.LookupAsync(db,a,id,requestId,requestHash,ct)))
      .WithSummary("Read an engagement budget preparation receipt")
      .WithDescription("Looks up the exact budget-preparation outcome for the supplied request identity and hash.");
    WithUiDirectResponse<BudgetPreparationLookup>(receipt, includeConflict: true, includeBindingProblem: true);
  }
}
