using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Api.Contracts;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record MappingDraftCreateInput(MappingDraftIntent Intent, string Revision, bool Reviewed);

  private static void MapMappingDraftEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/accounting/mappings/{id:guid}/editor", (Guid id, int? page, string? search, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => MappingDraftWorkspace.GetAsync(db, actor, id, page ?? 1, search, ct)))
      .Produces<MappingDraftEditor>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapPost("/accounting/mappings/{id:guid}/draft-preview", (Guid id, MappingDraftIntent? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => input is null
        ? Task.FromResult(CommandResult<MappingDraftPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide an explicit bounded mapping intent."))
        : MappingDraftWorkspace.PreviewAsync(db, actor, id, input, ct)))
      .Produces<UiValueResponse<MappingDraftPreview>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapPost("/accounting/mappings/{id:guid}/draft", (Guid id, MappingDraftCreateInput? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => input?.Intent is null
        ? Task.FromResult(CommandResult<MappingDraftReceipt>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide an explicit reviewed mapping intent."))
        : MappingDraftWorkspace.CreateAsync(db, actor, id, input.Intent, input.Revision ?? "", input.Reviewed, ct)))
      .Produces<UiValueResponse<MappingDraftReceipt>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapGet("/accounting/mappings/{id:guid}/draft-receipts/{requestId:guid}", (Guid id, Guid requestId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => MappingDraftWorkspace.ReadReceiptAsync(db, actor, id, requestId, ct)))
      .Produces<MappingDraftReceipt?>(StatusCodes.Status200OK, "application/json")
      .WithMetadata(new NullableJsonSuccessBodyMetadata())
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
  }
}
