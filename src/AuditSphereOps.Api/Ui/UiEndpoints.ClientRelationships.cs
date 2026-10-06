using System.Globalization;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
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

  private static void MapClientRelationshipEndpoints(RouteGroupBuilder group)
  {
    // Hierarchy
    group.MapGet("/clients/{id:guid}/hierarchy", async (Guid id, HttpContext http,
      TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);

      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.GetClientHierarchyAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);

      return Results.Ok(result.Value);
    });

    // Create relationship
    group.MapPost("/clients/{id:guid}/relationships", async (Guid id, CreateRelationshipInput input,
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
    });

    // Revoke relationship
    group.MapDelete("/clients/{id:guid}/relationships/{relationshipId:guid}", async (Guid id, Guid relationshipId,
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
        new RevokeClientRelationshipRequest(relationshipId, input.Reason), http.RequestAborted);

      return result.Succeeded
        ? Results.Ok(new { success = true })
        : Results.Json(new { code = result.ErrorCode, message = result.Message }, statusCode: 400);
    });

    // List routings
    group.MapGet("/clients/{id:guid}/routings", async (Guid id, HttpContext http,
      TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);

      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.GetClientContactRoutingsAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);

      return Results.Ok(result.Value);
    });

    // Assign routing
    group.MapPost("/clients/{id:guid}/routings", async (Guid id, AssignRoutingInput input,
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
    });

    // Revoke routing
    group.MapDelete("/clients/{id:guid}/routings/{routingId:guid}", async (Guid id, Guid routingId,
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
        new RevokeContactRoutingRequest(routingId, input.Reason), http.RequestAborted);

      return result.Succeeded
        ? Results.Ok(new { success = true })
        : Results.Json(new { code = result.ErrorCode, message = result.Message }, statusCode: 400);
    });

    // Resolve routing
    group.MapGet("/clients/{id:guid}/routings/resolve", async (Guid id, string purpose, string? asOfDate, Guid? overrideContactId,
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

      return Results.Ok(result);
    });
  }
}
