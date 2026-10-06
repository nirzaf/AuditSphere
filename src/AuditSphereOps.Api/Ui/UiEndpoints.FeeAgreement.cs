using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record FeePaymentInput(string Amount, string Reference, bool Reviewed);
  public sealed record FeeLinkInput(Guid EngagementId, bool Reviewed);
  public sealed record FeeReviewInput(bool Reviewed);
  private static void MapFeeAgreementEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/proposals/{id:guid}/fee-agreement", async (Guid id, HttpContext http, TrustedActorResolver resolver,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FeeAgreementWorkspaceQuery.GetAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: 403);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });
    group.MapPost("/proposals/{id:guid}/fee-agreement", async (Guid id, FeeReviewInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FeeAgreementService.CreateAgreementAsync(db, actor, id, http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/fee-agreements/{id:guid}/engagement", async (Guid id, FeeLinkInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || input.EngagementId == Guid.Empty) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FeeAgreementService.LinkEngagementAsync(db, actor, id, input.EngagementId, http.RequestAborted);
      return result.Succeeded ? Results.NoContent() : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    foreach (var kind in new[] { "advance", "balance" })
      group.MapPost("/fee-agreements/{id:guid}/" + kind + "-invoice", async (Guid id, FeeReviewInput input, HttpContext http,
        TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
      {
        var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
        if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
        try { await csrf.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
        if (!input.Reviewed) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
        await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
        var result = kind == "advance" ? await FeeAgreementService.IssueAdvanceInvoiceAsync(db, actor, id, http.RequestAborted)
          : await FeeAgreementService.IssueBalanceInvoiceAsync(db, actor, id, http.RequestAborted);
        return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
      });
    group.MapPost("/fee-agreements/{id:guid}/advance-payment", async (Guid id, FeePaymentInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !DecimalInput(input.Amount, out var amount) || string.IsNullOrWhiteSpace(input.Reference)
        || input.Reference.Length > 120) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FeeAgreementService.RecordAdvancePaymentAsync(db, actor, id, amount, input.Reference, ct: http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value!.ReceiptId, paid = result.Value.MilestonePaid,
        receiptDocumentId = result.Value.ReceiptDocumentId, emailQueued = result.Value.EmailQueued })
        : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
    group.MapPost("/fee-agreements/{id:guid}/balance-payment", async (Guid id, FeePaymentInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !DecimalInput(input.Amount, out var amount) || string.IsNullOrWhiteSpace(input.Reference)
        || input.Reference.Length > 120) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FeeAgreementService.RecordBalancePaymentAsync(db, actor, id, amount, input.Reference, ct: http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value!.ReceiptId, paid = result.Value.MilestonePaid,
        receiptDocumentId = result.Value.ReceiptDocumentId, emailQueued = result.Value.EmailQueued })
        : Results.Json(new { code = result.ErrorCode }, statusCode: 400);
    });
  }
}
