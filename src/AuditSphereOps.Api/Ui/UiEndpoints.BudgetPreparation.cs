using AuditSphereOps.Application.Practice;
namespace AuditSphereOps.Api.Ui;
public static partial class UiEndpoints
{
  private static void MapBudgetPreparationEndpoints(RouteGroupBuilder group)
  {
    const string route="/engagements/{id:guid}/budget-preparation";
    group.MapPost(route+"/preview",(Guid id,BudgetPreparationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>BudgetPreparationWorkspace.PreviewAsync(db,a,id,input,ct)));
    group.MapPost(route,(Guid id,BudgetPreparationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>BudgetPreparationWorkspace.ExecuteAsync(db,a,id,input,ct)));
    group.MapGet(route+"/receipts/{requestId:guid}",(Guid id,Guid requestId,string requestHash,HttpContext http)=>ReadAsync(http,(db,a,ct)=>BudgetPreparationWorkspace.LookupAsync(db,a,id,requestId,requestHash,ct)));
  }
}
