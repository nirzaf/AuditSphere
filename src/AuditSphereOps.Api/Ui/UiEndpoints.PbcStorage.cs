using AuditSphereOps.Application.Documents;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  // Administrator-only SharePoint-primary storage commands. No screen uses them; they keep the reviewed
  // state/preview/assent/receipt contract so an operator can recover a lost response without repeating an effect.
  private static void MapPbcStorageEndpoints(RouteGroupBuilder group)
  {
    const string root = "/administration/pbc-storage";
    group.MapGet(root + "/commands/state", (Guid uploadIntentId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PbcStorageCommandWorkspace.StateAsync(db, actor, CleanupOptions(http), uploadIntentId, ct)))
      .WithUiResponse<PbcStorageCommandState>(includeConflict: true);
    group.MapPost(root + "/commands/preview", (PbcStorageCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PbcStorageCommandWorkspace.PreviewAsync(db, actor, CleanupOptions(http), input, ct)))
      .WithUiResponse<UiValueResponse<PbcStorageCommandPreview>>(includeConflict: true);
    group.MapPost(root + "/commands/actions", (PbcStorageCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PbcStorageCommandWorkspace.ExecuteAsync(db, actor, CleanupOptions(http),
        http.RequestServices.GetRequiredService<IPbcRemoteContentReader>(),
        http.RequestServices.GetRequiredService<IPbcRegistrationVerifier>(), input, ct)))
      .WithUiResponse<UiValueResponse<PbcStorageCommandReceiptView>>(includeConflict: true);
    group.MapGet(root + "/commands/receipts/{requestId:guid}", (Guid requestId, string? requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PbcStorageCommandWorkspace.LookupAsync(db, actor, requestId, requestHash, ct)))
      .WithUiResponse<PbcStorageCommandLookup>(includeConflict: true);
    group.MapGet(root + "/cleanup/plan", (Guid[] uploadIntentId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PbcService.PlanLocalCopyCleanupAsync(db, actor, CleanupOptions(http), uploadIntentId, ct)))
      .WithUiResponse<IReadOnlyList<PbcLocalCopyPlanItem>>();
    group.MapGet(root + "/backfill/plan", (int? limit, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PbcService.PlanRegistrationBackfillAsync(db, actor, limit ?? 50, ct)))
      .WithUiResponse<IReadOnlyList<PbcRegistrationBackfillItem>>();
  }

  private static PbcLocalCopyCleanupOptions CleanupOptions(HttpContext http) =>
    http.RequestServices.GetRequiredService<PbcLocalCopyCleanupOptions>();
}
