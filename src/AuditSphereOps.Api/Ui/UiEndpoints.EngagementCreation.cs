using AuditSphereOps.Application.Acceptance;
namespace AuditSphereOps.Api.Ui;
public static partial class UiEndpoints
{
  private static void MapEngagementCreationEndpoints(RouteGroupBuilder group)
  {
    const string route="/clients/{id:guid}/engagement-creation";
    group.MapGet(route,(Guid id,HttpContext http)=>ReadAsync(http,(db,a,ct)=>EngagementCreationWorkspace.StateAsync(db,a,id,ct)));
    group.MapPost(route+"/preview",(Guid id,EngagementCreationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>EngagementCreationWorkspace.PreviewAsync(db,a,id,input,ct)));
    group.MapPost(route,(Guid id,EngagementCreationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>EngagementCreationWorkspace.ExecuteAsync(db,a,id,input,ct)));
    group.MapGet(route+"/receipts/{requestId:guid}",(Guid id,Guid requestId,string requestHash,HttpContext http)=>ReadAsync(http,(db,a,ct)=>EngagementCreationWorkspace.LookupAsync(db,a,id,requestId,requestHash,ct)));
  }
}
