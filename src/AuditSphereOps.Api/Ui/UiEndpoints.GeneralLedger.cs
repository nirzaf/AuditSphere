using AuditSphereOps.Application.Accounting;
namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapGeneralLedgerEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/engagements/{id:guid}/general-ledger", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => GeneralLedgerWorkspace.CatalogueAsync(db, actor, id, page ?? 1, ct)));
    group.MapGet("/engagements/{id:guid}/general-ledger/{batchId:guid}", (Guid id, Guid batchId,
      string? account, DateOnly? from, DateOnly? to, string? journal, string? counterparty, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => GeneralLedgerWorkspace.SourceAsync(db, actor, id, batchId,
        new(account, from, to, journal, counterparty), page ?? 1, ct)));
    group.MapGet("/engagements/{id:guid}/general-ledger/{batchId:guid}/journal", (Guid id, Guid batchId, string journal, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => GeneralLedgerWorkspace.JournalAsync(db, actor, id, batchId, journal, ct)));
  }
}
