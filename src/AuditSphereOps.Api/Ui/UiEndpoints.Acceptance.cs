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
    group.MapPost("/clients/{id:guid}/acceptance/reviews", async (Guid id, ReviewRequestInput input, HttpContext http,
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
    });
    group.MapPost("/clients/{id:guid}/acceptance/reviews/{reviewId:guid}", async (Guid id, Guid reviewId, ReviewResultInput input, HttpContext http,
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
    });
    group.MapPost("/clients/{id:guid}/acceptance/decision", async (Guid id, AcceptanceDecisionInput input, HttpContext http,
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
    });
    group.MapPost("/clients/{id:guid}/acceptance/continuance", async (Guid id, ContinuanceInput input, HttpContext http,
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
    });
  }
}
