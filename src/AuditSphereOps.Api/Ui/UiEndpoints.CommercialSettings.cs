using System.Globalization;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record CommercialProfileInput(string Version, string LegalName, string Address, string Email, string Phone,
    string Accent, string Closing, string History, string Credentials, string Methodology, bool Reviewed);
  public sealed record CommercialRuleInput(string RulesRevision, string Kind, string? Threshold, string Role, bool Reviewed);
  public sealed record CommercialRuleDeactivationInput(string RulesRevision, bool Reviewed);
  private static void MapCommercialSettingsEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/commercial-settings", async (HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await CommercialSettingsQuery.GetAsync(db, actor, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapPost("/commercial-settings/profile", async (CommercialProfileInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !long.TryParse(input.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await CommercialDocumentService.SaveProfileAsync(db, actor,
        new(input.LegalName, input.Address, input.Email, input.Phone, input.Accent, input.Closing, input.History, input.Credentials, input.Methodology, version), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/commercial-settings/rules", async (CommercialRuleInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      decimal? threshold = null;
      if (!input.Reviewed || !RulesProof(input.RulesRevision) || input.Role is not ("Partner" or "Manager" or "Administrator"))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      if (input.Kind == "DISCOUNT_OVER_PERCENT")
      {
        if (!DecimalInput(input.Threshold, out var parsed)) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
        threshold = parsed;
      }
      else if (input.Kind != "NON_STANDARD_TERMS" || input.Threshold is not null)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await QuotationService.SaveRuleAsync(db, actor, input.Kind, threshold, input.Role, http.RequestAborted, input.RulesRevision);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/commercial-settings/rules/{id:guid}/deactivate", async (Guid id, CommercialRuleDeactivationInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !RulesProof(input.RulesRevision)) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await QuotationService.DeactivateRuleAsync(db, actor, id, http.RequestAborted, input.RulesRevision);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
  }
  private static bool RulesProof(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
