using AuditSphereOps.Application.Acceptance;
namespace AuditSphereOps.Api.Ui;
public static partial class UiEndpoints
{
  private static void MapEngagementActivationReviewEndpoints(RouteGroupBuilder group)
  {
    const string route="/engagements/{id:guid}/activation-review";
    var state = group.MapGet(route,(Guid id,HttpContext http)=>ReadAsync(http,(db,a,ct)=>EngagementActivationWorkspace.StateAsync(db,a,id,ct)))
      .WithSummary("Read the engagement activation review")
      .WithDescription("Returns current engagement activation prerequisites and the exact review basis.");
    WithUiDirectResponse<EngagementActivationState>(state);

    var preview = group.MapPost(route+"/preview",(Guid id,EngagementActivationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>EngagementActivationWorkspace.PreviewAsync(db,a,id,input,ct)))
      .WithSummary("Preview engagement activation")
      .WithDescription("Returns the exact activation decision and current prerequisites without activating the engagement.");
    WithUiValueResponse<EngagementActivationPreview>(preview);

    var execute = group.MapPost(route,(Guid id,EngagementActivationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>EngagementActivationWorkspace.ExecuteAsync(db,a,id,input,ct)))
      .WithSummary("Record engagement activation")
      .WithDescription("Records the reviewed activation and returns its immutable receipt in the UI value envelope.");
    WithUiValueResponse<EngagementActivationReceipt>(execute);

    var receipt = group.MapGet(route+"/receipts/{requestId:guid}",(Guid id,Guid requestId,string requestHash,HttpContext http)=>ReadAsync(http,(db,a,ct)=>EngagementActivationWorkspace.LookupAsync(db,a,id,requestId,requestHash,ct)))
      .WithSummary("Read an engagement activation receipt")
      .WithDescription("Looks up the exact activation outcome for the supplied request identity and hash.");
    WithUiDirectResponse<EngagementActivationLookup>(receipt, includeConflict: true, includeBindingProblem: true);
  }
}
