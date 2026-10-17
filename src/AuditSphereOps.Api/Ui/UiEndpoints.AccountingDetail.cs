using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record MappingApprovalInput(long Version, string? Revision = null, bool Reviewed = false);
  public sealed record MappingReviewInput(string Revision, bool Reviewed);

  private static void MapAccountingDetailEndpoints(RouteGroupBuilder group)
  {
    MapMappingDraftEndpoints(group);
    group.MapGet("/accounting/mappings/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => MappingWorkbenchQuery.GetAsync(db, actor, id, ct)))
      .Produces<MappingWorkbench>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapGet("/accounting/mappings/{id:guid}/approval", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => MappingApprovalWorkspace.GetAsync(db, actor, id, ct)))
      .Produces<MappingApprovalReview>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapPost("/accounting/mappings/{id:guid}/approval", (Guid id, MappingReviewInput? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => MappingApprovalWorkspace.ApproveAsync(db, actor, id,
        input?.Revision ?? "", input?.Reviewed == true, ct)))
      .Produces<UiValueResponse<MappingApprovalReview>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapPost("/accounting/mappings/{id:guid}/approve", (Guid id, MappingApprovalInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FinancialStatementService.ApproveMappingAsync(db, actor, id, i.Version, ct, i.Revision ?? "", i.Reviewed)))
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapGet("/accounting/periods/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AccountingPeriodRecordQuery.GetAsync(db, actor, id, ct)))
      .Produces<AccountingPeriodRecord>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
  }
}
