using System.Globalization;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record QuotationLineInputDto(string Role, string Activity, string Hours, Guid RateCardId);
  public sealed record QuotationInput(string ProposalRevision, string Revision, IReadOnlyList<QuotationLineInputDto> Lines,
    string Complexity, string Risk, string Discount, bool NonStandardTerms, string? Note, string? RequestId = null);
  public sealed record QuotationApprovalInput(string RuleKey, string Reason);
  public sealed record QuotationRevocationInput(string Reason);
  private static bool DecimalInput(string? text, out decimal value)
  {
    value = 0;
    return text is { Length: > 0 and <= 60 } && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
  }
  private static SaveQuotationRequest? ParseQuotation(Guid id, QuotationInput input)
  {
    if (!long.TryParse(input.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
      || !long.TryParse(input.ProposalRevision, NumberStyles.None, CultureInfo.InvariantCulture, out var proposalRevision)
      || !DecimalInput(input.Complexity, out var complexity) || !DecimalInput(input.Risk, out var risk)
      || !DecimalInput(input.Discount, out var discount) || input.Lines is null || input.Lines.Count is < 1 or > 100
      || input.Note?.Length > 2000) return null;
    Guid? requestId = null;
    if (!string.IsNullOrWhiteSpace(input.RequestId))
    {
      if (!Guid.TryParse(input.RequestId, out var parsedReqId) || parsedReqId == Guid.Empty) return null;
      requestId = parsedReqId;
    }
    var lines = new List<QuotationHoursLine>();
    foreach (var l in input.Lines)
    {
      if (l is null || string.IsNullOrWhiteSpace(l.Role) || l.Role.Length > 100 || string.IsNullOrWhiteSpace(l.Activity)
        || l.Activity.Length > 200 || l.RateCardId == Guid.Empty || !DecimalInput(l.Hours, out var hours)) return null;
      lines.Add(new(l.Role, l.Activity, hours, l.RateCardId));
    }
    return new(id, lines, complexity, risk, discount, input.NonStandardTerms, input.Note, revision, proposalRevision, requestId);
  }
  private static void MapQuotationEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/proposals/{id:guid}/quotation", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await QuotationWorkspaceQuery.GetAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapPost("/proposals/{id:guid}/quotation/preview", async (Guid id, QuotationInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      var request = ParseQuotation(id, input);
      if (request is null) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await QuotationWorkspaceQuery.PreviewAsync(db, actor, request, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/proposals/{id:guid}/quotation", async (Guid id, QuotationInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      var request = ParseQuotation(id, input);
      if (request is null) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await QuotationService.SaveAsync(db, actor, request, http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/quotations/{id:guid}/submit", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await QuotationService.SubmitAsync(db, actor, id, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/quotations/{id:guid}/approve", async (Guid id, QuotationApprovalInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (string.IsNullOrWhiteSpace(input.RuleKey) || input.RuleKey.Length > 200 || string.IsNullOrWhiteSpace(input.Reason)
        || input.Reason.Length > 1000) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await QuotationService.ApproveAsync(db, actor, id, input.RuleKey, input.Reason, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/quotations/approvals/{id:guid}/revoke", async (Guid id, QuotationRevocationInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length is < 5 or > 1000)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await QuotationService.RevokeApprovalAsync(db, actor, id, input.Reason, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
  }
}
