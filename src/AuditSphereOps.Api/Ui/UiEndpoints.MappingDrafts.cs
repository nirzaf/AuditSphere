using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record MappingDraftCreateInput(MappingDraftIntent Intent, string Revision, bool Reviewed);

  private static void MapMappingDraftEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/accounting/mappings/{id:guid}/editor", (Guid id, int? page, string? search, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => MappingDraftWorkspace.GetAsync(db, actor, id, page ?? 1, search, ct)));
    group.MapPost("/accounting/mappings/{id:guid}/draft-preview", (Guid id, MappingDraftIntent? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => input is null
        ? Task.FromResult(CommandResult<MappingDraftPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide an explicit bounded mapping intent."))
        : MappingDraftWorkspace.PreviewAsync(db, actor, id, input, ct)));
    group.MapPost("/accounting/mappings/{id:guid}/draft", (Guid id, MappingDraftCreateInput? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => input?.Intent is null
        ? Task.FromResult(CommandResult<MappingDraftReceipt>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide an explicit reviewed mapping intent."))
        : MappingDraftWorkspace.CreateAsync(db, actor, id, input.Intent, input.Revision ?? "", input.Reviewed, ct)));
    group.MapGet("/accounting/mappings/{id:guid}/draft-receipts/{requestId:guid}", (Guid id, Guid requestId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => MappingDraftWorkspace.ReadReceiptAsync(db, actor, id, requestId, ct)));
  }
}
