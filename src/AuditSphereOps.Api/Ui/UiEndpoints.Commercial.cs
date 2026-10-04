using System.Globalization;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record OpportunityInput(Guid RequestId, string ServiceRoute, string EntityScope,
    string PeriodStart, string PeriodEnd, string ExpectedFee, string Currency);
  public sealed record LeadInput(Guid RequestId, string Name, string Source, string? ContactName, string? ContactEmail);
  public sealed record ProposalResponseInput(string Decision, string? Reason);
  public sealed record ClientConversionInput(string LegalName);
  public sealed record ProposalRevisionInput(string ExpectedRevision, string ServiceProfile, string Scope,
    string Exclusions, string Deliverables, string Dependencies, string Fee, string Currency,
    string PeriodStart, string PeriodEnd);
  private static void MapCommercialEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/leads/{id:guid}", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await CommercialWorkspaceQuery.LeadAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapPost("/leads/{id:guid}/opportunities", async (Guid id, OpportunityInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (input.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(input.ServiceRoute) || input.ServiceRoute.Length > 100
        || string.IsNullOrWhiteSpace(input.EntityScope) || input.EntityScope.Length > 10000
        || input.PeriodStart is null || input.PeriodStart.Length != 10 || input.PeriodEnd is null || input.PeriodEnd.Length != 10
        || input.Currency is null || input.Currency.Length != 3
        || !decimal.TryParse(input.ExpectedFee, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fee))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.CreateOpportunityAsync(db, actor,
        new(id, input.ServiceRoute, input.EntityScope, input.PeriodStart, input.PeriodEnd, fee, input.Currency,
          OwnerUserId: actor.UserId, RequestId: input.RequestId), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/opportunities/{id:guid}/proposals", async (Guid id, ProposalRevisionInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!long.TryParse(input.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
        || revision < 0 || !decimal.TryParse(input.Fee, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fee)
        || string.IsNullOrWhiteSpace(input.ServiceProfile) || input.ServiceProfile.Length > 100
        || string.IsNullOrWhiteSpace(input.Scope) || input.Scope.Length > 10000
        || input.Exclusions is null || input.Exclusions.Length > 10000
        || string.IsNullOrWhiteSpace(input.Deliverables) || input.Deliverables.Length > 10000
        || input.Dependencies is null || input.Dependencies.Length > 10000
        || input.Currency is null || input.Currency.Length != 3
        || input.PeriodStart is null || input.PeriodStart.Length != 10
        || input.PeriodEnd is null || input.PeriodEnd.Length != 10)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.ReviseProposalAsync(db, actor,
        new(id, input.ServiceProfile, input.Scope, input.Exclusions, input.Deliverables, input.Dependencies,
          fee, input.Currency, input.PeriodStart, input.PeriodEnd, revision), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapGet("/proposals/{id:guid}", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await CommercialWorkspaceQuery.ProposalAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapPost("/proposals/{id:guid}/review", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.ApproveProposalAsync(db, actor, id, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/proposals/{id:guid}/sent", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.SendProposalAsync(db, actor, id, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/proposals/{id:guid}/response", async (Guid id, ProposalResponseInput input, HttpContext http, TrustedActorResolver resolver,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.RecordProposalResponseAsync(db, actor, id, new(input.Decision, input.Reason), http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/proposals/{id:guid}/convert", async (Guid id, ClientConversionInput input, HttpContext http, TrustedActorResolver resolver,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.ConvertToClientDraftAsync(db, actor, new(id, input.LegalName), http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapGet("/leads", async (string? search, int? page, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeLeadQuery.PageAsync(db, actor, search, page ?? 0, ct: http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "request.invalid" ? 400 : 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapPost("/leads", async (LeadInput input, HttpContext http, TrustedActorResolver resolver,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (input.RequestId == Guid.Empty || input.Name?.Length > 200 || input.Source?.Length > 200
        || input.ContactName?.Length > 200 || input.ContactEmail?.Length > 254)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.CreateLeadAsync(db, actor,
        new(input.Name!, input.Source!, input.ContactName, input.ContactEmail, actor.UserId, RequestId: input.RequestId), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode },
        statusCode: result.ErrorCode == AuditSphereOps.Domain.Shared.ErrorCodes.IdempotencyConflict ? 409 : 400);
    });
    group.MapPost("/leads/{id:guid}/qualify", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await PracticeCrmService.QualifyLeadAsync(db, actor, id, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
  }
}
