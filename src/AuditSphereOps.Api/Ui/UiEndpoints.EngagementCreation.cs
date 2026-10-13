using AuditSphereOps.Application.Acceptance;
namespace AuditSphereOps.Api.Ui;
public static partial class UiEndpoints
{
  private static void MapEngagementCreationEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/clients/{id:guid}/annual-engagement/{sourceEngagementId:guid}",
      (Guid id,Guid sourceEngagementId,HttpContext http)=>ReadAsync(http,(db,a,ct)=>AnnualEngagementPreparationWorkspace.StateAsync(db,a,id,sourceEngagementId,ct)));
    const string route="/clients/{id:guid}/engagement-creation";
    group.MapGet(route,(Guid id,HttpContext http)=>ReadAsync(http,(db,a,ct)=>EngagementCreationWorkspace.StateAsync(db,a,id,ct)));
    group.MapPost(route+"/preview",(Guid id,EngagementCreationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>
      input.Fields?.AnnualSourceEngagementId is null ? EngagementCreationWorkspace.PreviewAsync(db,a,id,input,ct) : AnnualEngagementPreparationWorkspace.PreviewAsync(db,a,id,input,ct)));
    group.MapPost(route,(Guid id,EngagementCreationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>
      input.Fields?.AnnualSourceEngagementId is null ? EngagementCreationWorkspace.ExecuteAsync(db,a,id,input,ct) : AnnualEngagementPreparationWorkspace.ExecuteAsync(db,a,id,input,ct)));
    group.MapGet(route+"/receipts/{requestId:guid}",(Guid id,Guid requestId,string requestHash,HttpContext http)=>ReadAsync(http,(db,a,ct)=>EngagementCreationWorkspace.LookupAsync(db,a,id,requestId,requestHash,ct)));
  }
}
