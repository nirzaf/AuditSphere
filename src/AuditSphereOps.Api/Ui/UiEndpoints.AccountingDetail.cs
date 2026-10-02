using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record MappingApprovalInput(long Version);

  private static void MapAccountingDetailEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/accounting/mappings/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => MappingWorkbenchQuery.GetAsync(db, actor, id, ct)));
    group.MapPost("/accounting/mappings/{id:guid}/approve", (Guid id, MappingApprovalInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FinancialStatementService.ApproveMappingAsync(db, actor, id, i.Version, ct)));
    group.MapGet("/accounting/periods/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AccountingPeriodRecordQuery.GetAsync(db, actor, id, ct)));
  }
}
