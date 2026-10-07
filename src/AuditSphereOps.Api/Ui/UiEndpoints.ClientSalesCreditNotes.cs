using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record SalesCreditPreviewHttpInput(Guid CreditNoteId, string CreditNoteReference, Guid PeriodId,
    DateOnly PostingDate, string Reason, Guid? SourceReceiptId, string SourceBasis,
    IReadOnlyList<ClientSalesCreditLineInput> Lines, string PreviewDigest, bool Reviewed);
  public sealed record SalesCreditSubmitHttpInput(Guid CommandId, Guid CreditNoteId, string CreditNoteReference,
    Guid PeriodId, DateOnly PostingDate, string Reason, Guid? SourceReceiptId, string SourceBasis,
    IReadOnlyList<ClientSalesCreditLineInput> Lines, string PreviewDigest, bool Reviewed);
  public sealed record SalesCreditReviewHttpInput(Guid CommandId, Guid SubmissionId, string Decision, string Reason,
    string PreviewDigest, bool Reviewed);

  private static void MapClientSalesCreditNoteEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/accounting/clients/{clientId:guid}/sales-invoices/{invoiceId:guid}/credit-notes", async (Guid clientId,
      Guid invoiceId, HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientSalesInvoiceWorkflow.GetCreditNotesAsync(db, actor, clientId, invoiceId, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<IReadOnlyList<ClientSalesCreditNoteView>>();

    group.MapPost("/accounting/clients/{clientId:guid}/sales-invoices/{invoiceId:guid}/credit-notes/preview", async (Guid clientId,
      Guid invoiceId, SalesCreditPreviewHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (input.CreditNoteId == Guid.Empty || input.CreditNoteReference is null || input.Reason is null || input.SourceBasis is null)
        return SalesFailure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var request = new ClientSalesCreditPreviewRequest(input.CreditNoteId, input.CreditNoteReference, invoiceId,
        input.PeriodId, input.PostingDate, input.Reason, input.SourceReceiptId, input.SourceBasis, input.Lines);
      var result = await ClientSalesInvoiceWorkflow.PreviewCreditNoteAsync(db, actor, clientId, request, http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientSalesCreditPreview>();

    group.MapPost("/accounting/clients/{clientId:guid}/sales-invoices/{invoiceId:guid}/credit-notes", async (Guid clientId,
      Guid invoiceId, SalesCreditSubmitHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed) return SalesFailure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var credit = new ClientSalesCreditPreviewRequest(input.CreditNoteId, input.CreditNoteReference, invoiceId,
        input.PeriodId, input.PostingDate, input.Reason, input.SourceReceiptId, input.SourceBasis, input.Lines);
      var result = await ClientSalesInvoiceWorkflow.SubmitCreditNoteAsync(db, actor, clientId,
        new(input.CommandId, credit, input.PreviewDigest), http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientSalesCreditCommandReceipt>();

    group.MapGet("/accounting/clients/{clientId:guid}/sales-credit-note-submissions/{submissionId:guid}/preview", async (Guid clientId,
      Guid submissionId, HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientSalesInvoiceWorkflow.PreviewCreditNoteReviewAsync(db, actor, clientId, submissionId, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientSalesCreditReviewPreview>();

    group.MapPost("/accounting/clients/{clientId:guid}/sales-credit-note-reviews", async (Guid clientId,
      SalesCreditReviewHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed) return SalesFailure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientSalesInvoiceWorkflow.ReviewCreditNoteAsync(db, actor, clientId,
        new(input.CommandId, input.SubmissionId, input.Decision, input.Reason, input.PreviewDigest), http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientSalesCreditCommandReceipt>();

    group.MapGet("/accounting/clients/{clientId:guid}/sales-credit-note-requests/{commandId:guid}", async (Guid clientId,
      Guid commandId, string kind, HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientSalesInvoiceWorkflow.GetCreditNoteCommandAsync(db, actor, clientId, commandId, kind, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientSalesCreditCommandReceipt>();
  }
}
