using AuditSphereOps.Application.Acceptance;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapRouteResolutionEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/clients/{id:guid}/assessment", (Guid id, Guid? decisionId, HttpContext http) => ReadAsync(http, (db, actor, ct) => AssessmentWorkspaceQuery.GetAsync(db, actor, id, decisionId, ct)));
    group.MapGet("/assessments/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AssessmentRouteQuery.ResolveAsync(db, actor, id, ct)));
  }
}
