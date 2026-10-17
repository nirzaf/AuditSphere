using AuditSphereOps.Application.Practice;
using AuditSphereOps.Api.HttpBoundary;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapClientConversionEndpoints(RouteGroupBuilder group)
  {
    const string route = "/proposals/{id:guid}/client-conversion";
    group.MapGet(route, (Guid id, HttpContext http) =>
      ReadAsync(http, (db,a,ct) => ClientConversionWorkspace.StateAsync(db,a,id,ct)))
      .Produces<ClientConversionState>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapPost(route + "/preview", (Guid id, ClientConversionRequest input, HttpContext http) =>
      CommandAsync(http, (db, a, ct) => ClientConversionWorkspace.PreviewAsync(db, a, id, input, ct)))
      .Produces<UiValueResponse<ClientConversionPreview>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapPost(route, (Guid id, ClientConversionRequest input, HttpContext http) =>
      CommandAsync(http, (db, a, ct) => ClientConversionWorkspace.ExecuteAsync(db, a, id, input, ct)))
      .Produces<UiValueResponse<ClientConversionReceipt>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapGet(route + "/receipts/{requestId:guid}", (Guid id, Guid requestId, string requestHash, HttpContext http) =>
      ReadAsync(http, (db, a, ct) => ClientConversionWorkspace.LookupAsync(db, a, id, requestId, requestHash, ct)))
      .Produces<ClientConversionLookup>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
  }
}
