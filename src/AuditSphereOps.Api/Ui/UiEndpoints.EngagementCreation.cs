using AuditSphereOps.Application.Acceptance;
namespace AuditSphereOps.Api.Ui;
public static partial class UiEndpoints
{
  private static void MapEngagementCreationEndpoints(RouteGroupBuilder group)
  {
    var annual = group.MapGet("/clients/{id:guid}/annual-engagement/{sourceEngagementId:guid}",
      (Guid id,Guid sourceEngagementId,HttpContext http)=>ReadAsync(http,(db,a,ct)=>AnnualEngagementPreparationWorkspace.StateAsync(db,a,id,sourceEngagementId,ct)))
      .WithSummary("Read annual engagement preparation")
      .WithDescription("Returns the scoped source-engagement context available for preparing a subsequent engagement.");
    WithUiDirectResponse<AnnualEngagementPreparationState>(annual, includeBadRequest: true);

    const string route="/clients/{id:guid}/engagement-creation";
    var state = group.MapGet(route,(Guid id,HttpContext http)=>ReadAsync(http,(db,a,ct)=>EngagementCreationWorkspace.StateAsync(db,a,id,ct)))
      .WithSummary("Read the engagement creation workspace")
      .WithDescription("Returns the scoped client context and review basis for drafting an engagement.");
    WithUiDirectResponse<EngagementCreationState>(state);

    var preview = group.MapPost(route+"/preview",(Guid id,EngagementCreationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>
      input.Fields?.AnnualSourceEngagementId is null ? EngagementCreationWorkspace.PreviewAsync(db,a,id,input,ct) : AnnualEngagementPreparationWorkspace.PreviewAsync(db,a,id,input,ct)))
      .WithSummary("Preview engagement creation")
      .WithDescription("Returns the exact engagement intent and review basis without creating an engagement.");
    WithUiValueResponse<EngagementCreationPreview>(preview);

    var execute = group.MapPost(route,(Guid id,EngagementCreationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>
      input.Fields?.AnnualSourceEngagementId is null ? EngagementCreationWorkspace.ExecuteAsync(db,a,id,input,ct) : AnnualEngagementPreparationWorkspace.ExecuteAsync(db,a,id,input,ct)))
      .WithSummary("Create an engagement")
      .WithDescription("Creates the explicitly reviewed engagement and returns its immutable receipt in the UI value envelope.");
    WithUiValueResponse<EngagementCreationReceipt>(execute);

    var receipt = group.MapGet(route+"/receipts/{requestId:guid}",(Guid id,Guid requestId,string requestHash,HttpContext http)=>ReadAsync(http,(db,a,ct)=>EngagementCreationWorkspace.LookupAsync(db,a,id,requestId,requestHash,ct)))
      .WithSummary("Read an engagement creation receipt")
      .WithDescription("Looks up the exact engagement-creation outcome for the supplied request identity and hash.");
    WithUiDirectResponse<EngagementCreationLookup>(receipt, includeConflict: true, includeBindingProblem: true);
  }
}
