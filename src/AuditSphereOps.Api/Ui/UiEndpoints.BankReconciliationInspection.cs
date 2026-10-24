using AuditSphereOps.Application.Audit;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapBankReconciliationInspectionEndpoints(RouteGroupBuilder group)
  {
    const string path = "/engagements/{id:guid}/fieldwork/bank-reconciliations";
    group.MapGet(path, (Guid id, int? offset, int? limit, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => BankReconciliationInspectionQuery.ListAsync(db, actor, id, offset ?? 0, limit ?? 25, ct)))
      .WithUiResponse<BankReconciliationInspectionPage>();
    group.MapGet(path + "/{reconciliationId:guid}", (Guid id, Guid reconciliationId, int? offset, int? limit, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => BankReconciliationInspectionQuery.DetailAsync(db, actor, id, reconciliationId, offset ?? 0, limit ?? 100, ct)))
      .WithUiResponse<BankReconciliationInspectionDetail>();
  }
}
