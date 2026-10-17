using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapStaffingChangeEndpoints(RouteGroupBuilder group)
  {
    const string route = "/engagements/{id:guid}/staffing-change";
    var preview = group.MapPost(route + "/preview", (Guid id, StaffingChangeRequest input, HttpContext http) =>
      CommandAsync(http, (db, a, ct) => StaffingChangeWorkspace.PreviewAsync(db, a, id, input, ct)));
    preview.WithSummary("Preview an engagement staffing change")
      .WithDescription("Returns the exact staffing assignment or revocation and review basis without changing staffing.");
    WithUiValueResponse<StaffingChangePreview>(preview);

    var execute = group.MapPost(route, (Guid id, StaffingChangeRequest input, HttpContext http) =>
      CommandAsync(http, (db, a, ct) => StaffingChangeWorkspace.ExecuteAsync(db, a, id, input, ct)));
    execute.WithSummary("Record an engagement staffing change")
      .WithDescription("Applies the reviewed staffing change and returns its immutable receipt in the UI value envelope.");
    WithUiValueResponse<StaffingChangeReceipt>(execute);

    var receipt = group.MapGet(route + "/receipts/{requestId:guid}", (Guid id, Guid requestId, string requestHash, HttpContext http) =>
      ReadAsync(http, (db, a, ct) => StaffingChangeWorkspace.LookupAsync(db, a, id, requestId, requestHash, ct)));
    receipt.WithSummary("Read an engagement staffing change receipt")
      .WithDescription("Looks up the exact staffing-change outcome for the supplied request identity and hash.");
    WithUiDirectResponse<StaffingChangeLookup>(receipt, includeConflict: true, includeBindingProblem: true);
  }
}
