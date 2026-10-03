using AuditSphereOps.Application.Accounting;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapValuationPreparationEndpoints(RouteGroupBuilder group)
  {
    const string route="/accounting/reconciliations/{id:guid}/valuation-preparation/{kind}";
    group.MapGet(route,(Guid id,string kind,HttpContext http)=>
      ReadAsync(http,(db,actor,ct)=>ValuationPreparationWorkspace.StateAsync(db,actor,kind,id,ct)));
    group.MapPost(route+"/preview",(Guid id,string kind,ValuationPreparationRequest input,HttpContext http)=>
      CommandAsync(http,(db,actor,ct)=>ValuationPreparationWorkspace.PreviewAsync(db,actor,kind,id,input,ct)));
    group.MapPost(route,(Guid id,string kind,ValuationPreparationRequest input,HttpContext http)=>
      CommandAsync(http,(db,actor,ct)=>ValuationPreparationWorkspace.ExecuteAsync(db,actor,kind,id,input,ct)));
    group.MapGet(route+"/receipts/{requestId:guid}",(Guid id,string kind,Guid requestId,string requestHash,HttpContext http)=>
      ReadAsync(http,(db,actor,ct)=>ValuationPreparationWorkspace.LookupAsync(db,actor,kind,id,requestId,requestHash,ct)));
  }
}
