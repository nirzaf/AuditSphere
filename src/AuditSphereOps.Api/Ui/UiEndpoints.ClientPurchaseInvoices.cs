using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record PurchaseDraftHttpInput(Guid CommandId, Guid? InvoiceId, long ExpectedRevision, Guid PeriodId,
    Guid SupplierId, string VoucherReference, string SupplierInvoiceReference, DateOnly ReceiptDate, DateOnly DocumentDate,
    DateOnly AccountingDate, DateOnly SupplyDate, DateOnly DueDate, string Currency, ClientInvoiceMoneyPolicy Policy,
    IReadOnlyList<ClientInvoiceLineInput> Lines, decimal StatedNet, decimal StatedTax, decimal StatedGross,
    Guid? SourceReceiptId, string EvidenceReference);
  public sealed record PurchaseSubmitHttpInput(Guid CommandId, string ExpectedDraftRevision, Guid PayableRoleId, string PreviewDigest, bool Reviewed);
  public sealed record PurchaseReviewHttpInput(Guid CommandId, Guid SubmissionId, string Decision, string Reason,
    string DuplicateResolutionReason, string PreviewDigest, bool Reviewed);

  private static void MapClientPurchaseInvoiceEndpoints(RouteGroupBuilder group)
  {
    group.MapPost("/accounting/clients/{clientId:guid}/purchase-invoice-drafts", async (Guid clientId, PurchaseDraftHttpInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientPurchaseInvoiceWorkflow.SaveDraftAsync(db, actor, clientId, new(input.CommandId, input.InvoiceId,
        input.ExpectedRevision, input.PeriodId, input.SupplierId, input.VoucherReference, input.SupplierInvoiceReference,
        input.ReceiptDate, input.DocumentDate, input.AccountingDate, input.SupplyDate, input.DueDate, input.Currency,
        input.Policy, input.Lines, input.StatedNet, input.StatedTax, input.StatedGross, input.SourceReceiptId, input.EvidenceReference), http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientPurchaseInvoiceDraftView>();

    group.MapGet("/accounting/clients/{clientId:guid}/purchase-invoice-drafts/{invoiceId:guid}", async (Guid clientId, Guid invoiceId,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientPurchaseInvoiceWorkflow.GetDraftAsync(db, actor, clientId, invoiceId, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientPurchaseInvoiceDraftView>();

    group.MapGet("/accounting/clients/{clientId:guid}/purchase-invoices", async (Guid clientId,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientPurchaseInvoiceWorkflow.GetInvoicesAsync(db, actor, clientId, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<IReadOnlyList<ClientPurchaseInvoiceView>>();

    group.MapPost("/accounting/clients/{clientId:guid}/purchase-invoices/{invoiceId:guid}/preview", async (Guid clientId, Guid invoiceId,
      PurchaseSubmitHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientPurchaseInvoiceWorkflow.PreviewAsync(db, actor, clientId,
        new(input.CommandId, invoiceId, input.ExpectedDraftRevision, input.PayableRoleId, input.PreviewDigest), http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientPurchaseInvoicePreview>();

    group.MapPost("/accounting/clients/{clientId:guid}/purchase-invoices/{invoiceId:guid}/submit", async (Guid clientId, Guid invoiceId,
      PurchaseSubmitHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed) return SalesFailure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientPurchaseInvoiceWorkflow.SubmitAsync(db, actor, clientId,
        new(input.CommandId, invoiceId, input.ExpectedDraftRevision, input.PayableRoleId, input.PreviewDigest), http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientPurchaseInvoiceReceipt>();

    group.MapGet("/accounting/clients/{clientId:guid}/purchase-invoice-submissions/{submissionId:guid}/preview", async (Guid clientId, Guid submissionId,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientPurchaseInvoiceWorkflow.PreviewReviewAsync(db, actor, clientId, submissionId, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientPurchaseInvoiceReviewPreview>();

    group.MapPost("/accounting/clients/{clientId:guid}/purchase-invoice-reviews", async (Guid clientId, PurchaseReviewHttpInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed) return SalesFailure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientPurchaseInvoiceWorkflow.ReviewAsync(db, actor, clientId,
        new(input.CommandId, input.SubmissionId, input.Decision, input.Reason, input.DuplicateResolutionReason, input.PreviewDigest), http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : SalesFailure(result.ErrorCode);
    }).Produces<ClientPurchaseInvoiceReceipt>();
  }
}
