using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapStatementLayoutEndpoints(RouteGroupBuilder group)
  {
    const string root = "/accounting/clients/{clientId:guid}/statement-layouts";

    group.MapGet(root, (Guid clientId, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => StatementLayoutWorkspace.CatalogueAsync(db, actor, clientId, page ?? 0, ct)))
      .WithDescription("Reads one bounded page of exact-client statement-layout versions. Catalogue metadata is scoped to the signed-in staff member's current client grant.")
      .WithT046Response<StatementLayoutCataloguePage>();

    group.MapGet(root + "/{layoutId:guid}", (Guid clientId, Guid layoutId, HttpContext http) =>
      ReadAsync(http, async (db, actor, ct) =>
      {
        var result = await StatementLayoutWorkspace.DetailAsync(db, actor, layoutId, ct);
        return result.Succeeded && result.Value!.ClientId != clientId
          ? CommandResult<StatementLayoutDetail>.Fail(ErrorCodes.ScopeDenied, "Access denied.")
          : result;
      }))
      .WithDescription("Reads typed lines and current grammar validation for the exact scoped layout version. Published versions remain immutable.")
      .WithT046Response<StatementLayoutDetail>(includeConflict: true, includeNotFound: true);

    group.MapPost(root + "/drafts", (Guid clientId, StatementLayoutSaveInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => StatementLayoutWorkspace.SaveDraftAsync(db, actor, clientId, input, ct)))
      .WithDescription("Creates a new client-owned draft or updates only the exact author-owned draft and hash. A new successor must name the current published predecessor explicitly. The reviewed mutation and immutable receipt commit together.")
      .WithT046Response<UiValueResponse<StatementLayoutCommandReceiptView>>(includeConflict: true, includeNotFound: true);

    group.MapPost(root + "/{layoutId:guid}/publish", (Guid clientId, Guid layoutId,
      StatementLayoutPublishInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => StatementLayoutWorkspace.PublishAsync(db, actor, clientId, layoutId, input, ct)))
      .WithDescription("Independently publishes an exact current validated layout after an authorized reviewer reviews its hash and reason. The author cannot publish their own version.")
      .WithT046Response<UiValueResponse<StatementLayoutCommandReceiptView>>(includeConflict: true, includeNotFound: true);

    group.MapGet(root + "/receipts/{requestId:guid}", (Guid clientId, Guid requestId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => StatementLayoutWorkspace.LookupAsync(db, actor, clientId, requestId, ct)))
      .WithDescription("Recovers only the current actor's exact retained statement-layout intent and result under current client authority. Lookup never resubmits the command.")
      .WithT046Response<StatementLayoutCommandLookup>(includeConflict: true, includeNotFound: true);
  }
}
