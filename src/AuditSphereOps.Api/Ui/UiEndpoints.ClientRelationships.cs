using System.Globalization;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record CreateRelationshipInput(
    Guid RelatedClientId,
    string RelationshipKind,
    decimal? OwnershipPercentage,
    string? EffectiveFrom,
    string? EffectiveTo,
    string? Notes);

  public sealed record RevokeRelationshipInput(string Reason);

  public sealed record AssignRoutingInput(
    Guid ClientContactId,
    string Purpose,
    string? EffectiveFrom,
    string? EffectiveTo,
    bool IsPrimaryForPurpose = true);

  public sealed record RevokeRoutingInput(string Reason);
  public sealed record SetClientArchiveInput(bool IsArchived, string ExpectedRevision);
  public sealed record ClientArchiveStateResponse(bool IsArchived, DateTimeOffset? LastArchivedAt, string Revision);

  /// <summary>Direct success body returned after a client relationship or routing is revoked.</summary>
  public sealed record ClientRelationshipMutationResponse(bool Success);

  /// <summary>Direct refusal body that allows the existing optional message field.</summary>
  public sealed record ClientRelationshipErrorResponse(string Code, string? Message = null);

  private static void MapClientRelationshipEndpoints(RouteGroupBuilder group)
  {
    var archiveState = group.MapPut("/clients/{id:guid}/archive-state", async (Guid id, SetClientArchiveInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (input is null || id == Guid.Empty || string.IsNullOrEmpty(input.ExpectedRevision) || input.ExpectedRevision.Length > 19 ||
          !long.TryParse(input.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedRevision) || expectedRevision < 0)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);

      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.SetClientArchivedAsync(db, actor,
        new SetClientArchiveRequest(id, input.IsArchived, expectedRevision), http.RequestAborted);
      if (!result.Succeeded)
      {
        var status = result.ErrorCode == ErrorCodes.GenerationStale ? 409 :
          result.ErrorCode == "crm.invalid" ? 400 : 403;
        return Results.Json(new { code = result.ErrorCode, message = result.Message }, statusCode: status);
      }
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      var state = result.Value!;
      return Results.Ok(new ClientArchiveStateResponse(state.IsArchived, state.LastArchivedAt,
        state.Revision.ToString(CultureInfo.InvariantCulture)));
    }).WithSummary("Set client archive state")
      .WithDescription("Records an authorized, reversible archive-state change. Client, engagement and evidence history remains available in its current authorized scope.")
      .WithT046Response<ClientArchiveStateResponse>();

    // Hierarchy
    var candidates = group.MapGet("/clients/{id:guid}/relationship-candidates", async (Guid id, string? search, int? page, int? pageSize,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);

      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.GetClientRelationshipCandidatesAsync(db, actor, id, search,
        page ?? 0, pageSize ?? 25, http.RequestAborted);
      if (!result.Succeeded)
      {
        var status = result.ErrorCode == "request.invalid" ? 400 : 403;
        return Results.Json(new { code = result.ErrorCode, message = result.Message }, statusCode: status);
      }

      return Results.Ok(result.Value);
    }).WithSummary("List client relationship candidates")
      .WithDescription("Returns a bounded page of relationship candidates authorized for this client.");
    candidates.Produces<ClientRelationshipCandidates>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json");

    var hierarchy = group.MapGet("/clients/{id:guid}/hierarchy", async (Guid id, HttpContext http,
      TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);

      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.GetClientHierarchyAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);

      return Results.Ok(result.Value);
    }).WithSummary("Read the client organization hierarchy")
      .WithDescription("Returns the client and its currently authorized parent, subsidiary and affiliate relationships.");
    hierarchy.Produces<ClientOrganizationHierarchyDto>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json");

    // Create relationship
    var createRelationship = group.MapPost("/clients/{id:guid}/relationships", async (Guid id, CreateRelationshipInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);

      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }

      if (input.RelatedClientId == Guid.Empty || string.IsNullOrWhiteSpace(input.RelationshipKind))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);

      DateOnly? from = null;
      if (!string.IsNullOrWhiteSpace(input.EffectiveFrom))
      {
        if (!DateOnly.TryParse(input.EffectiveFrom, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
          return Results.Json(new { code = "request.invalid" }, statusCode: 400);
        from = f;
      }

      DateOnly? to = null;
      if (!string.IsNullOrWhiteSpace(input.EffectiveTo))
      {
        if (!DateOnly.TryParse(input.EffectiveTo, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
          return Results.Json(new { code = "request.invalid" }, statusCode: 400);
        to = t;
      }

      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.CreateClientRelationshipAsync(db, actor,
        new CreateClientRelationshipRequest(id, input.RelatedClientId, input.RelationshipKind.Trim().ToUpperInvariant(),
          input.OwnershipPercentage, from, to, input.Notes), http.RequestAborted);

      return result.Succeeded
        ? Results.Ok(new { id = result.Value })
        : Results.Json(new { code = result.ErrorCode, message = result.Message }, statusCode: 400);
    }).WithSummary("Create a client relationship")
      .WithDescription("Creates the requested client relationship after validating both client identities and its effective dates.");
    createRelationship.Produces<UiIdentifierResponse>(StatusCodes.Status200OK, "application/json")
      .Produces<ClientRelationshipErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

    // Revoke relationship
    var revokeRelationship = group.MapDelete("/clients/{id:guid}/relationships/{relationshipId:guid}", async (Guid id, Guid relationshipId,
      [Microsoft.AspNetCore.Mvc.FromBody] RevokeRelationshipInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);

      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }

      if (string.IsNullOrWhiteSpace(input?.Reason))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);

      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.RevokeClientRelationshipAsync(db, actor,
        new RevokeClientRelationshipRequest(id, relationshipId, input.Reason), http.RequestAborted);

      return result.Succeeded
        ? Results.Ok(new { success = true })
        : Results.Json(new { code = result.ErrorCode, message = result.Message }, statusCode: 400);
    }).WithSummary("Revoke a client relationship")
      .WithDescription("Revokes the relationship with its stated reason; the response confirms the action without returning a value envelope.");
    revokeRelationship.Produces<ClientRelationshipMutationResponse>(StatusCodes.Status200OK, "application/json")
      .Produces<ClientRelationshipErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

    // List routings
    var routings = group.MapGet("/clients/{id:guid}/routings", async (Guid id, HttpContext http,
      TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);

      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.GetClientContactRoutingsAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);

      return Results.Ok(result.Value);
    }).WithSummary("List client contact routings")
      .WithDescription("Returns the client's correspondence routing assignments without exposing other clients' contacts.");
    routings.Produces<IReadOnlyList<ClientContactRoutingDto>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json");

    // Assign routing
    var assignRouting = group.MapPost("/clients/{id:guid}/routings", async (Guid id, AssignRoutingInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);

      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }

      if (input.ClientContactId == Guid.Empty || string.IsNullOrWhiteSpace(input.Purpose))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);

      DateOnly? from = null;
      if (!string.IsNullOrWhiteSpace(input.EffectiveFrom))
      {
        if (!DateOnly.TryParse(input.EffectiveFrom, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
          return Results.Json(new { code = "request.invalid" }, statusCode: 400);
        from = f;
      }

      DateOnly? to = null;
      if (!string.IsNullOrWhiteSpace(input.EffectiveTo))
      {
        if (!DateOnly.TryParse(input.EffectiveTo, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
          return Results.Json(new { code = "request.invalid" }, statusCode: 400);
        to = t;
      }

      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.AssignContactRoutingAsync(db, actor,
        new AssignContactRoutingRequest(id, input.ClientContactId, input.Purpose, from, to, input.IsPrimaryForPurpose),
        http.RequestAborted);

      return result.Succeeded
        ? Results.Ok(new { id = result.Value })
        : Results.Json(new { code = result.ErrorCode, message = result.Message }, statusCode: 400);
    }).WithSummary("Assign a client contact routing")
      .WithDescription("Assigns a contact to a correspondence purpose over the supplied effective dates.");
    assignRouting.Produces<UiIdentifierResponse>(StatusCodes.Status200OK, "application/json")
      .Produces<ClientRelationshipErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

    // Revoke routing
    var revokeRouting = group.MapDelete("/clients/{id:guid}/routings/{routingId:guid}", async (Guid id, Guid routingId,
      [Microsoft.AspNetCore.Mvc.FromBody] RevokeRoutingInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);

      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }

      if (string.IsNullOrWhiteSpace(input?.Reason))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);

      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.RevokeContactRoutingAsync(db, actor,
        new RevokeContactRoutingRequest(id, routingId, input.Reason), http.RequestAborted);

      return result.Succeeded
        ? Results.Ok(new { success = true })
        : Results.Json(new { code = result.ErrorCode, message = result.Message }, statusCode: 400);
    }).WithSummary("Revoke a client contact routing")
      .WithDescription("Revokes the routing assignment with its stated reason; the response confirms the action without a value envelope.");
    revokeRouting.Produces<ClientRelationshipMutationResponse>(StatusCodes.Status200OK, "application/json")
      .Produces<ClientRelationshipErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

    // Resolve routing
    var resolveRouting = group.MapGet("/clients/{id:guid}/routings/resolve", async (Guid id, string purpose, string? asOfDate, Guid? overrideContactId,
      string? overrideReason, HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);

      if (string.IsNullOrWhiteSpace(purpose))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);

      var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);
      if (!string.IsNullOrWhiteSpace(asOfDate) && DateOnly.TryParse(asOfDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
      {
        effectiveDate = d;
      }

      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, actor, id, purpose, effectiveDate,
        overrideContactId, overrideReason, http.RequestAborted);

      // A denied resolution is a boundary refusal, never a business answer (STE-REM-03); the
      // refusal message is nondisclosing and no client/contact identity is returned.
      return result.Status == RecipientResolutionStatus.Denied
        ? Results.Json(new { code = ErrorCodes.ScopeDenied, message = result.Message }, statusCode: 403)
        : Results.Ok(result);
    }).WithSummary("Resolve a correspondence recipient")
      .WithDescription("Returns the exact recipient resolution for the requested purpose and date, or a nondisclosing refusal.");
    resolveRouting.Produces<RecipientResolutionResult>(StatusCodes.Status200OK, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiCodeErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");
  }
}
