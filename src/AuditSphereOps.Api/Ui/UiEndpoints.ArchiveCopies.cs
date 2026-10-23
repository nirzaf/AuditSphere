using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Records;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapArchiveCopyEndpoints(RouteGroupBuilder group)
  {
    // These commands retain local intent/evidence only. Provider credentials and effects stay in client-sites.
    group.MapPost("/records/archive-copy-targets", (CreateArchiveCopyTargetRevisionRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ArchiveCopyService.CreateTargetRevisionAsync(db, actor, input, ct)))
      .WithUiResponse<UiValueResponse<ArchiveCopyTargetRevisionView>>(includeConflict: true);
    group.MapPost("/records/archive-copies", (EnqueueArchiveCopyRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ArchiveCopyService.EnqueueAsync(db,
        http.RequestServices.GetRequiredService<IOperationStore>(), actor, input, ct)))
      .WithUiResponse<UiValueResponse<ArchiveCopyQueued>>(includeConflict: true);
    group.MapGet("/records/archive-copies/{operationId:guid}/evidence", (Guid operationId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ArchiveCopyService.ReadEvidenceAsync(db, actor, operationId, ct)))
      .WithUiResponse<ArchiveCopyEvidenceReadback>(includeConflict: true);
  }
}
