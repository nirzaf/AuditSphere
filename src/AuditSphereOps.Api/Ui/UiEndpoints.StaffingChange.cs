using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapStaffingChangeEndpoints(RouteGroupBuilder group)
  {
    const string route = "/engagements/{id:guid}/staffing-change";
    group.MapPost(route + "/preview", (Guid id, StaffingChangeRequest input, HttpContext http) =>
      CommandAsync(http, (db, a, ct) => StaffingChangeWorkspace.PreviewAsync(db, a, id, input, ct)));
    group.MapPost(route, (Guid id, StaffingChangeRequest input, HttpContext http) =>
      CommandAsync(http, (db, a, ct) => StaffingChangeWorkspace.ExecuteAsync(db, a, id, input, ct)));
    group.MapGet(route + "/receipts/{requestId:guid}", (Guid id, Guid requestId, string requestHash, HttpContext http) =>
      ReadAsync(http, (db, a, ct) => StaffingChangeWorkspace.LookupAsync(db, a, id, requestId, requestHash, ct)));
  }
}
