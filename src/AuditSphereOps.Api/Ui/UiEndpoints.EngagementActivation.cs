using AuditSphereOps.Application.Acceptance;
namespace AuditSphereOps.Api.Ui;
public static partial class UiEndpoints
{
  private static void MapEngagementActivationReviewEndpoints(RouteGroupBuilder group)
  {
    const string route="/engagements/{id:guid}/activation-review";
    group.MapGet(route,(Guid id,HttpContext http,EngagementActivationOptions activation)=>ReadAsync(http,(db,a,ct)=>EngagementActivationWorkspace.StateAsync(db,a,id,ct,activation)));
    group.MapPost(route+"/preview",(Guid id,EngagementActivationRequest input,HttpContext http,EngagementActivationOptions activation)=>CommandAsync(http,(db,a,ct)=>EngagementActivationWorkspace.PreviewAsync(db,a,id,input,ct,activation)));
    group.MapPost(route,(Guid id,EngagementActivationRequest input,HttpContext http,EngagementActivationOptions activation)=>CommandAsync(http,(db,a,ct)=>EngagementActivationWorkspace.ExecuteAsync(db,a,id,input,ct,activation)));
    group.MapGet(route+"/receipts/{requestId:guid}",(Guid id,Guid requestId,string requestHash,HttpContext http)=>ReadAsync(http,(db,a,ct)=>EngagementActivationWorkspace.LookupAsync(db,a,id,requestId,requestHash,ct)));
  }
}
