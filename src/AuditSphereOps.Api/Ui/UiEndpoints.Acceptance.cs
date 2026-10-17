using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record ReviewRequestInput(string Area, string Specialist, string Generation);
  public sealed record ReviewResultInput(string Status, string? Evidence, string? Conditions, string Generation, string ExpectedStatus);
  public sealed record AcceptanceDecisionInput(string ServiceRoute, string Decision, string Rationale, string? Conditions, string Generation);
  public sealed record ContinuanceInput(string Generation);
  private static void MapAcceptanceCommands(RouteGroupBuilder group)
  {
    var preview = group.MapPost("/clients/{id:guid}/assessment/preview", (Guid id, AssessmentCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AssessmentCommandWorkspace.PreviewAsync(db, actor, id, input, ct)))
      .WithSummary("Preview a client assessment action")
      .WithDescription("Returns the exact assessment action and review basis without applying it.");
    WithUiValueResponse<AssessmentCommandPreview>(preview);

    var execute = group.MapPost("/clients/{id:guid}/assessment/commands", (Guid id, AssessmentCommandRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AssessmentCommandWorkspace.ExecuteAsync(db, actor, id, input, ct)))
      .WithSummary("Record a client assessment action")
      .WithDescription("Records the reviewed assessment action and returns its immutable receipt.");
    WithUiValueResponse<AssessmentReceiptView>(execute);

    var receipt = group.MapGet("/clients/{id:guid}/assessment/receipts/{requestId:guid}", (Guid id, Guid requestId, string? requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AssessmentCommandWorkspace.LookupAsync(db, actor, id, requestId, requestHash, ct)))
      .WithSummary("Read a client assessment action receipt")
      .WithDescription("Looks up the exact assessment command outcome for the supplied request identity and hash.");
    WithUiDirectResponse<AssessmentReceiptLookup>(receipt, includeConflict: true);

    var requestReview = group.MapPost("/clients/{id:guid}/acceptance/reviews", async (Guid id, ReviewRequestInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!long.TryParse(input.Generation, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out var generation) || generation < 1)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await AcceptanceChecklistService.RequestClearanceAsync(db, actor, id, input.Area, input.Specialist, http.RequestAborted, generation);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    }).WithSummary("Request acceptance specialist review")
      .WithDescription("Requests a specialist clearance review for the current client assessment; success returns no body.");
    WithUiNoContentResponse(requestReview);

    var recordReview = group.MapPost("/clients/{id:guid}/acceptance/reviews/{reviewId:guid}", async (Guid id, Guid reviewId, ReviewResultInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!long.TryParse(input.Generation, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out var generation) || generation < 1)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await AcceptanceWorkspaceQuery.RecordReviewAsync(db, actor, id, reviewId, input.Status, input.Evidence, input.Conditions, generation, input.ExpectedStatus, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    }).WithSummary("Record acceptance specialist review")
      .WithDescription("Records the specialist decision and evidence for this client assessment; success returns no body.");
    WithUiNoContentResponse(recordReview);

    var decision = group.MapPost("/clients/{id:guid}/acceptance/decision", async (Guid id, AcceptanceDecisionInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!long.TryParse(input.Generation, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out var generation) || generation < 1)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await AcceptanceDecisionService.RecordAsync(db, actor, new(id, null, input.ServiceRoute, input.Decision, input.Rationale, input.Conditions, generation), http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    }).WithSummary("Record a client acceptance decision")
      .WithDescription("Records the submitted client acceptance decision; success returns no body.");
    WithUiNoContentResponse(decision);

    var continuance = group.MapPost("/clients/{id:guid}/acceptance/continuance", async (Guid id, ContinuanceInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!long.TryParse(input.Generation, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out var generation) || generation < 1)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await AcceptanceChecklistService.StartContinuanceAsync(db, actor, id, http.RequestAborted, generation);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    }).WithSummary("Start a client continuance assessment")
      .WithDescription("Starts the next client acceptance evaluation generation; success returns no body.");
    WithUiNoContentResponse(continuance);
  }
}
