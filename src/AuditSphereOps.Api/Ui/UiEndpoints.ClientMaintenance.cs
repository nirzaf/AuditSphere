using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapClientMaintenanceEndpoints(RouteGroupBuilder group)
  {
    const string profile = "/clients/{id:guid}/profile-maintenance";
    var profileState = group.MapGet(profile, (Guid id, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => ClientMaintenanceWorkspace.StateAsync(db, actor, id, ClientMaintenanceKinds.Profile, ct: ct)))
      .WithSummary("Read the client profile maintenance workspace")
      .WithDescription("Returns the exact client profile and generation used to review a scoped update.");
    WithUiDirectResponse<ClientMaintenanceState>(profileState, includeBadRequest: true);

    var profilePreview = group.MapPost(profile + "/preview", (Guid id, ClientMaintenanceRequest input, HttpContext http) =>
        CommandAsync(http, (db, actor, ct) => ClientMaintenanceWorkspace.PreviewAsync(db, actor, id, ClientMaintenanceKinds.Profile, null, input, ct)))
      .WithSummary("Preview a client profile update")
      .WithDescription("Returns the canonical profile diff and current review basis without changing the client.");
    WithUiValueResponse<ClientMaintenancePreview>(profilePreview);

    var profileExecute = group.MapPost(profile, (Guid id, ClientMaintenanceRequest input, HttpContext http) =>
        CommandAsync(http, (db, actor, ct) => ClientMaintenanceWorkspace.ExecuteAsync(db, actor, id, ClientMaintenanceKinds.Profile, null, input, ct)))
      .WithSummary("Save the reviewed client profile")
      .WithDescription("Saves the exact reviewed client profile and returns its immutable receipt.");
    WithUiValueResponse<ClientMaintenanceReceiptValue>(profileExecute);

    var profileReceipt = group.MapGet(profile + "/receipts/{requestId:guid}",
        (Guid id, Guid requestId, string requestHash, HttpContext http) => ReadAsync(http,
          (db, actor, ct) => ClientMaintenanceWorkspace.LookupAsync(db, actor, id, ClientMaintenanceKinds.Profile, null, requestId, requestHash, ct)))
      .WithSummary("Read a client profile maintenance receipt")
      .WithDescription("Looks up the actor-owned exact profile update outcome by request identity and hash.");
    WithUiDirectResponse<ClientMaintenanceLookup>(profileReceipt, includeConflict: true, includeBindingProblem: true);

    const string contact = "/clients/{id:guid}/contacts/{contactId:guid}/maintenance";
    var contactState = group.MapGet(contact, (Guid id, Guid contactId, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => ClientMaintenanceWorkspace.StateAsync(db, actor, id, ClientMaintenanceKinds.Contact, contactId, ct)))
      .WithSummary("Read a client contact maintenance workspace")
      .WithDescription("Returns the exact same-client contact, current primary set and generation for a reviewed edit.");
    WithUiDirectResponse<ClientMaintenanceState>(contactState, includeBadRequest: true);

    var contactPreview = group.MapPost(contact + "/preview", (Guid id, Guid contactId, ClientMaintenanceRequest input, HttpContext http) =>
        CommandAsync(http, (db, actor, ct) => ClientMaintenanceWorkspace.PreviewAsync(db, actor, id, ClientMaintenanceKinds.Contact, contactId, input, ct)))
      .WithSummary("Preview a client contact update")
      .WithDescription("Returns the canonical contact diff and any exact primary-contact replacement set.");
    WithUiValueResponse<ClientMaintenancePreview>(contactPreview);

    var contactExecute = group.MapPost(contact, (Guid id, Guid contactId, ClientMaintenanceRequest input, HttpContext http) =>
        CommandAsync(http, (db, actor, ct) => ClientMaintenanceWorkspace.ExecuteAsync(db, actor, id, ClientMaintenanceKinds.Contact, contactId, input, ct)))
      .WithSummary("Save the reviewed client contact")
      .WithDescription("Saves the exact reviewed client contact and returns its immutable receipt.");
    WithUiValueResponse<ClientMaintenanceReceiptValue>(contactExecute);

    var contactReceipt = group.MapGet(contact + "/receipts/{requestId:guid}",
        (Guid id, Guid contactId, Guid requestId, string requestHash, HttpContext http) => ReadAsync(http,
          (db, actor, ct) => ClientMaintenanceWorkspace.LookupAsync(db, actor, id, ClientMaintenanceKinds.Contact, contactId, requestId, requestHash, ct)))
      .WithSummary("Read a client contact maintenance receipt")
      .WithDescription("Looks up the actor-owned exact contact update outcome by request identity and hash.");
    WithUiDirectResponse<ClientMaintenanceLookup>(contactReceipt, includeConflict: true, includeBindingProblem: true);
  }
}
