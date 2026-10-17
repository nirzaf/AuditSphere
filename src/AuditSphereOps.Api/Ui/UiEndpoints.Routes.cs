using AuditSphereOps.Application.Acceptance;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapRouteResolutionEndpoints(RouteGroupBuilder group)
  {
    var assessment = group.MapGet("/clients/{id:guid}/assessment",
      (Guid id, Guid? decisionId, Guid? engagementId, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => AssessmentWorkspaceQuery.GetAsync(db, actor, id, decisionId, ct, engagementId)))
      .WithSummary("Read a client assessment workspace")
      .WithDescription("Returns the scoped client assessment and optional exact decision or engagement context.");
    WithUiDirectResponse<AssessmentWorkspace>(assessment, includeBadRequest: true);

    var route = group.MapGet("/assessments/{id:guid}",
      (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AssessmentRouteQuery.ResolveAsync(db, actor, id, ct)))
      .WithSummary("Resolve an assessment route")
      .WithDescription("Resolves an authorized client or assessment-decision identity to its client assessment route.");
    WithUiDirectResponse<AssessmentRoute>(route);

    var decision = group.MapGet("/assessments/{id:guid}/decision",
      (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AssessmentRouteQuery.ResolveDecisionAsync(db, actor, id, ct)))
      .WithSummary("Resolve an assessment decision route")
      .WithDescription("Resolves an authorized Partner decision identity to its client assessment route.");
    WithUiDirectResponse<AssessmentRoute>(decision);
  }

  private static RouteHandlerBuilder WithUiDirectResponse<T>(RouteHandlerBuilder endpoint,
    bool includeBadRequest = false, bool includeConflict = false, bool includeBindingProblem = false)
  {
    endpoint.Produces<T>(StatusCodes.Status200OK, "application/json");
    if (includeBadRequest)
      endpoint.Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json");
    endpoint.Produces<UiFailureResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");
    if (includeConflict)
      endpoint.Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    if (includeBindingProblem)
      endpoint.Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");
    return endpoint;
  }

  private static RouteHandlerBuilder WithUiValueResponse<T>(RouteHandlerBuilder endpoint, bool includeConflict = true)
  {
    endpoint.Produces<UiValueResponse<T>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");
    if (includeConflict)
      endpoint.Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json");
    return endpoint;
  }

  private static RouteHandlerBuilder WithUiNoContentResponse(RouteHandlerBuilder endpoint, bool includeBindingProblem = true)
  {
    endpoint.Produces(StatusCodes.Status204NoContent)
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json");
    if (includeBindingProblem)
      endpoint.Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");
    return endpoint;
  }
}
