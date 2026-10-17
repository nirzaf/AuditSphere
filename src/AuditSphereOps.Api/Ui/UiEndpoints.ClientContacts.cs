using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapClientContactCreationEndpoints(RouteGroupBuilder group)
  {
    const string route="/clients/{id:guid}/contact-creation";
    var state = group.MapGet(route,(Guid id,HttpContext http)=>ReadAsync(http,(db,a,ct)=>ClientContactCreationWorkspace.StateAsync(db,a,id,ct)))
      .WithSummary("Read the client contact creation workspace")
      .WithDescription("Returns the scoped contact-creation context and current client generation.");
    WithUiDirectResponse<ContactCreationState>(state);

    var preview = group.MapPost(route+"/preview",(Guid id,ContactCreationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>ClientContactCreationWorkspace.PreviewAsync(db,a,id,input,ct)))
      .WithSummary("Preview client contact creation")
      .WithDescription("Returns the exact contact details and review basis without creating a contact.");
    WithUiValueResponse<ContactCreationPreview>(preview);

    var execute = group.MapPost(route,(Guid id,ContactCreationRequest input,HttpContext http)=>CommandAsync(http,(db,a,ct)=>ClientContactCreationWorkspace.ExecuteAsync(db,a,id,input,ct)))
      .WithSummary("Create a client contact")
      .WithDescription("Creates the reviewed contact and returns its receipt in the UI value envelope.");
    WithUiValueResponse<ContactCreationReceipt>(execute);

    var receipt = group.MapGet(route+"/receipts/{requestId:guid}",(Guid id,Guid requestId,string requestHash,HttpContext http)=>ReadAsync(http,(db,a,ct)=>ClientContactCreationWorkspace.LookupAsync(db,a,id,requestId,requestHash,ct)))
      .WithSummary("Read a client contact creation receipt")
      .WithDescription("Looks up the exact contact-creation outcome for the supplied request identity and hash.");
    WithUiDirectResponse<ContactCreationLookup>(receipt, includeConflict: true, includeBindingProblem: true);
  }
}
