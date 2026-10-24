using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapNativeGeneralLedgerSourceEndpoints(RouteGroupBuilder group)
  {
    const string route = "/accounting/clients/{clientId:guid}/native-gl-sources";
    group.MapPost(route + "/preview", (Guid clientId, NativeGeneralLedgerSourceRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => clientId != Guid.Empty && input.ClientId == clientId
        ? NativeGeneralLedgerSourceWorkspace.PreviewAsync(db, actor, input, ct)
        : Task.FromResult(CommandResult<NativeGeneralLedgerSourcePreview>.Fail("request.invalid", "Select the exact native client and retained trial-balance source."))))
      .WithUiResponse<UiValueResponse<NativeGeneralLedgerSourcePreview>>(includeConflict: true);
    group.MapPost(route + "/seal", (Guid clientId, NativeGeneralLedgerSourceRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => clientId != Guid.Empty && input.ClientId == clientId
        ? NativeGeneralLedgerSourceWorkspace.SealAsync(db, actor, input, ct)
        : Task.FromResult(CommandResult<NativeGeneralLedgerSourceView>.Fail("request.invalid", "Select the exact reviewed native ledger source intent."))))
      .WithUiResponse<UiValueResponse<NativeGeneralLedgerSourceView>>(includeConflict: true);
    group.MapGet(route + "/{importBatchId:guid}", (Guid clientId, Guid importBatchId, HttpContext http) =>
    {
      http.Response.Headers.CacheControl = "no-store";
      return ReadAsync(http, async (db, actor, ct) =>
      {
        var result = await NativeGeneralLedgerSourceWorkspace.ViewAsync(db, actor, importBatchId, ct);
        return result.Succeeded && result.Value!.ClientId != clientId
          ? CommandResult<NativeGeneralLedgerSourceView>.Fail(ErrorCodes.ScopeDenied, "Access denied.") : result;
      });
    }).WithUiResponse<NativeGeneralLedgerSourceView>(includeConflict: true);
    group.MapGet(route + "/{importBatchId:guid}/artifact", (Guid clientId, Guid importBatchId, HttpContext http) =>
    {
      http.Response.Headers.CacheControl = "no-store";
      return ReadAsync(http, async (db, actor, ct) =>
      {
        var view = await NativeGeneralLedgerSourceWorkspace.ViewAsync(db, actor, importBatchId, ct);
        if (!view.Succeeded) return CommandResult<NativeGeneralLedgerSourceArtifact>.Fail(view.ErrorCode!, view.Message!);
        if (view.Value!.ClientId != clientId)
          return CommandResult<NativeGeneralLedgerSourceArtifact>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        return await NativeGeneralLedgerSourceWorkspace.ArtifactAsync(db, actor, importBatchId, ct);
      });
    }).WithUiResponse<NativeGeneralLedgerSourceArtifact>(includeConflict: true);
  }
}
