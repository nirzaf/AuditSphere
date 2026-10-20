using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapFeeSettlementEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/finance/fee-agreements", (int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) =>
        FeeSettlementWorkspaceQuery.ListAsync(db, actor, page ?? 0, ct)))
      .WithT046Response<FeeSettlementPage>();

    group.MapGet("/finance/fee-agreements/{id:guid}", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FeeSettlementWorkspaceQuery.GetAsync(db, actor, id,
        AutomaticDraftingEnabled(http.RequestServices.GetRequiredService<IConfiguration>()), ct)))
      .WithT046Response<FeeAgreementWorkspace>();
  }
}
