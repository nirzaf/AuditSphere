using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record MappingApprovalInput(long Version, string? Revision = null, bool Reviewed = false);
  public sealed record MappingReviewInput(string Revision, bool Reviewed);

  private static void MapAccountingDetailEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/accounting/mappings/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => MappingWorkbenchQuery.GetAsync(db, actor, id, ct)));
    group.MapGet("/accounting/mappings/{id:guid}/approval", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => MappingApprovalWorkspace.GetAsync(db, actor, id, ct)));
    group.MapPost("/accounting/mappings/{id:guid}/approval", (Guid id, MappingReviewInput? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => MappingApprovalWorkspace.ApproveAsync(db, actor, id,
        input?.Revision ?? "", input?.Reviewed == true, ct)));
    group.MapPost("/accounting/mappings/{id:guid}/approve", (Guid id, MappingApprovalInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FinancialStatementService.ApproveMappingAsync(db, actor, id, i.Version, ct, i.Revision ?? "", i.Reviewed)));
    group.MapGet("/accounting/periods/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AccountingPeriodRecordQuery.GetAsync(db, actor, id, ct)));
  }
}
