using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record PurchaseCreditLineHttpInput(int? OriginalLineNumber, string AccountCode, decimal Amount);
  public sealed record PurchaseCreditPreviewHttpInput(Guid CreditNoteId, string CreditNoteReference, Guid? OriginalInvoiceId,
    Guid SupplierId, Guid PeriodId, Guid PayableRoleId, DateOnly PostingDate, string Reason, string UnlinkedExceptionRationale,
    Guid? SourceReceiptId, string SourceBasis, IReadOnlyList<PurchaseCreditLineHttpInput> Lines);
  public sealed record PurchaseCreditSubmitHttpInput(Guid CommandId, PurchaseCreditPreviewHttpInput Credit, string PreviewDigest, bool Reviewed);
  public sealed record PurchaseCreditReviewHttpInput(Guid CommandId, Guid SubmissionId, string Decision, string Reason,
    string DuplicateResolutionReason, string PreviewDigest, bool Reviewed);

  private static void MapClientPurchaseCreditNoteEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/accounting/clients/{clientId:guid}/purchase-credit-notes", async (Guid clientId,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientPurchaseCreditNoteWorkflow.GetCreditsAsync(db, actor, clientId, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<IReadOnlyList<ClientPurchaseCreditNoteView>>();

    group.MapPost("/accounting/clients/{clientId:guid}/purchase-credit-notes/preview", async (Guid clientId,
      PurchaseCreditPreviewHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (input is null || input.CreditNoteReference is null || input.Reason is null || input.UnlinkedExceptionRationale is null || input.SourceBasis is null || input.Lines is null || input.Lines.Any(x => x is null || x.AccountCode is null))
        return SalesFailure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var request = ToRequest(input);
      var result = await ClientPurchaseCreditNoteWorkflow.PreviewAsync(db, actor, clientId, request, http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientPurchaseCreditPreview>();

    group.MapPost("/accounting/clients/{clientId:guid}/purchase-credit-notes", async (Guid clientId,
      PurchaseCreditSubmitHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (input is null || input.Credit is null || input.Credit.CreditNoteReference is null || input.Credit.Reason is null ||
          input.Credit.UnlinkedExceptionRationale is null || input.Credit.SourceBasis is null || input.Credit.Lines is null ||
          input.Credit.Lines.Any(x => x is null || x.AccountCode is null) || !input.Reviewed) return SalesFailure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientPurchaseCreditNoteWorkflow.SubmitAsync(db, actor, clientId,
        new(input.CommandId, ToRequest(input.Credit), input.PreviewDigest), http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientPurchaseCreditReceipt>();

    group.MapGet("/accounting/clients/{clientId:guid}/purchase-credit-note-submissions/{submissionId:guid}/preview", async (Guid clientId,
      Guid submissionId, HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientPurchaseCreditNoteWorkflow.PreviewReviewAsync(db, actor, clientId, submissionId, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientPurchaseCreditReviewPreview>();

    group.MapPost("/accounting/clients/{clientId:guid}/purchase-credit-note-reviews", async (Guid clientId,
      PurchaseCreditReviewHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (input is null || input.Decision is null || input.Reason is null || input.DuplicateResolutionReason is null || !input.Reviewed) return SalesFailure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientPurchaseCreditNoteWorkflow.ReviewAsync(db, actor, clientId,
        new(input.CommandId, input.SubmissionId, input.Decision, input.Reason, input.DuplicateResolutionReason, input.PreviewDigest), http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientPurchaseCreditReceipt>();
  }

  private static ClientPurchaseCreditPreviewRequest ToRequest(PurchaseCreditPreviewHttpInput input) =>
    new(input.CreditNoteId, input.CreditNoteReference, input.OriginalInvoiceId, input.SupplierId, input.PeriodId, input.PayableRoleId,
      input.PostingDate, input.Reason, input.UnlinkedExceptionRationale, input.SourceReceiptId, input.SourceBasis,
      (input.Lines ?? []).Select(x => new ClientPurchaseCreditLineInput(x.OriginalLineNumber, x.AccountCode, x.Amount)).ToArray());
}
