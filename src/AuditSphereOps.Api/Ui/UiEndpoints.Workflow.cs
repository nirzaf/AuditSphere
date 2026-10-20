namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  internal static int WorkflowReadFailureStatusCode(string? errorCode) => errorCode switch
  {
    "request.invalid" => StatusCodes.Status400BadRequest,
    "scope.denied" => StatusCodes.Status403Forbidden,
    "gate.blocked" or "revision.stale" or "generation.stale" or "protected-state.denied" => StatusCodes.Status409Conflict,
    _ => StatusCodes.Status500InternalServerError,
  };
}
