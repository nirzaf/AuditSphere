using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapClientConversionEndpoints(RouteGroupBuilder group)
  {
    const string route = "/proposals/{id:guid}/client-conversion";
    group.MapGet(route, (Guid id, HttpContext http) =>
      ReadAsync(http, (db,a,ct) => ClientConversionWorkspace.StateAsync(db,a,id,ct)));
    group.MapPost(route + "/preview", (Guid id, ClientConversionRequest input, HttpContext http) =>
      CommandAsync(http, (db, a, ct) => ClientConversionWorkspace.PreviewAsync(db, a, id, input, ct)));
    group.MapPost(route, (Guid id, ClientConversionRequest input, HttpContext http) =>
      CommandAsync(http, (db, a, ct) => ClientConversionWorkspace.ExecuteAsync(db, a, id, input, ct)));
    group.MapGet(route + "/receipts/{requestId:guid}", (Guid id, Guid requestId, string requestHash, HttpContext http) =>
      ReadAsync(http, (db, a, ct) => ClientConversionWorkspace.LookupAsync(db, a, id, requestId, requestHash, ct)));
  }
}
