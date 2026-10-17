using AuditSphereOps.Application.Operations;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapOperationCommandEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/operations/commands/state", (Guid? operationId, Guid? recoverySessionId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => OperationCommandWorkspace.StateAsync(db, actor, operationId, recoverySessionId, ct)))
      .WithUiResponse<OperationCommandState>(includeConflict: true);
    group.MapPost("/operations/commands/preview", (OperationCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => OperationCommandWorkspace.PreviewAsync(db, actor, input, ct)))
      .WithUiResponse<UiValueResponse<OperationCommandPreview>>(includeConflict: true);
    group.MapPost("/operations/commands/actions", (OperationCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => OperationCommandWorkspace.ExecuteAsync(db, actor, input, ct)))
      .WithUiResponse<UiValueResponse<OperationCommandReceiptView>>(includeConflict: true);
    group.MapGet("/operations/commands/receipts/{requestId:guid}", (Guid requestId, string? requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => OperationCommandWorkspace.LookupAsync(db, actor, requestId, requestHash, ct)))
      .WithUiResponse<OperationCommandLookup>(includeConflict: true);
  }
}
