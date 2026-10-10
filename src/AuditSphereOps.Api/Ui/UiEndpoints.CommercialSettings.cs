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
  public sealed record PricingPolicyInput(string Currency, string? MinimumFee, string? MaximumFee, string? MaxDiscount,
    string ValidityDays, long? ExpectedRevision, bool Reviewed);
  public sealed record PricingPolicyDecisionInput(string Reason, bool Reviewed);
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
    group.MapGet("/commercial-settings/pricing-policy", async (HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FirmPricingPolicyService.WorkspaceAsync(db, actor, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapPost("/commercial-settings/pricing-policy", async (PricingPolicyInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || input.Currency is null || input.Currency.Length != 3
        || !int.TryParse(input.ValidityDays, NumberStyles.None, CultureInfo.InvariantCulture, out var validityDays))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      if (!OptionalDecimal(input.MinimumFee, out var minimumFee) || !OptionalDecimal(input.MaximumFee, out var maximumFee) ||
        !OptionalDecimal(input.MaxDiscount, out var maxDiscount))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FirmPricingPolicyService.SaveAsync(db, actor,
        new(input.Currency, minimumFee, maximumFee, maxDiscount, validityDays, input.ExpectedRevision), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/commercial-settings/pricing-policy/{id:guid}/submit", async (Guid id, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FirmPricingPolicyService.SubmitAsync(db, actor, id, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/commercial-settings/pricing-policy/{id:guid}/approve", async (Guid id, PricingPolicyDecisionInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FirmPricingPolicyService.ApproveAsync(db, actor, id, input.Reason, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
  }
  private static bool RulesProof(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool OptionalDecimal(string? text, out decimal? value)
  {
    value = null;
    if (string.IsNullOrEmpty(text)) return true;
    return DecimalInput(text, out var parsed) && (value = parsed) is not null;
  }
}
