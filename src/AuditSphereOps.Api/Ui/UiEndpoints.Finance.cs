using System.Globalization;
using System.Text;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record FiscalCloseInput(string Reason);
  public sealed record ReceiptInput(string Amount, string Reference, bool Reviewed);
  public sealed record ReceiptAllocationInput(Guid InvoiceId, string Amount, bool Reviewed);
  public sealed record CreditNoteInput(string NoteNumber, string Amount, string Reason, bool Reviewed);
  public sealed record PaymentTermsInput(DateOnly DueDate, string Basis, string TermsDescription, string EvidenceReference, long ExpectedRevision, bool Reviewed);
  public sealed record PaymentTermsReviewInput(bool Approve, string Reason, bool Reviewed);
  public sealed record AllocationReversalInput(string Amount, string Reference, string Reason, long ExpectedRevision, bool Reviewed);
  public sealed record AllocationReversalReviewInput(bool Approve, string Reason, bool Reviewed);
  public sealed record ReceivablesAgingExportInput(string AsOfDate);

  private static void MapFinanceEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/finance/receivables-aging", http => ReadAsync(http, (db, actor, ct) =>
    {
      var asOf = ParseAsOfDate(http.Request.Query["asOf"].FirstOrDefault());
      return asOf is null
        ? Task.FromResult(CommandResult<FirmReceivablesAgingReport>.Fail("finance.as-of-invalid", "Provide the report date in YYYY-MM-DD format."))
        : FirmReceivablesAgingQuery.GetAsync(db, actor, asOf.Value, ct);
    }));
    group.MapPost("/finance/receivables-aging/export", async (ReceivablesAgingExportInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Failure("csrf.invalid", "Refresh the page and try again.", 403); }
      var asOf = ParseAsOfDate(input?.AsOfDate);
      if (asOf is null) return Failure("finance.as-of-invalid", "Provide the report date in YYYY-MM-DD format.", 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FirmReceivablesAgingQuery.ExportAsync(db, actor, asOf.Value, http.RequestAborted);
      if (!result.Succeeded) return Failure(result.ErrorCode, result.Message,
        result.ErrorCode == ErrorCodes.ScopeDenied ? 403 : 400);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Failure("session.unavailable", "Sign in again.", 401);
      http.Response.Headers["X-Receivables-As-Of"] = asOf.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      return Results.File(Encoding.UTF8.GetBytes(result.Value!.Csv), "text/csv; charset=utf-8", result.Value.FileName);
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export));

    group.MapUiGet("/finance", http => ReadAsync(http, async (db, actor, ct) =>
    {
      var r = await FirmFinanceQuery.GetAsync(db, actor, ct);
      return r.Succeeded
        ? CommandResult<object>.Ok(new
          {
            r.Value!.CanClosePeriod,
            Periods = r.Value.Periods.Select(p => new { p.Id, p.PeriodCode, p.Status, p.Revision, p.ClosedAt }),
            Accounts = r.Value.Accounts.Select(a => new { a.Id, a.Code, a.Name, a.AccountType, a.NormalSide, a.PostingAllowed }),
            Postings = r.Value.RecentPostings.Select(p => new { p.Id, p.PostedAt, p.Currency, p.PostedByUserId, p.ReversalOfPostingId }),
          })
        : CommandResult<object>.Fail(r.ErrorCode!, r.Message!);
    }));
    group.MapPost("/finance/periods/{id:guid}/close", (Guid id, FiscalCloseInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => LedgerService.CloseFiscalPeriodAsync(db, actor, id, i.Reason ?? "", ct)));
    group.MapGet("/finance/invoices/{id:guid}", (
      Guid id, DateTimeOffset? receiptBefore, Guid? receiptBeforeId,
      DateTimeOffset? creditBefore, Guid? creditBeforeId, HttpContext http) => ReadAsync(http, async (db, actor, ct) =>
    {
      var r = await BillingInvoiceWorkspaceQuery.GetAsync(db, actor, id,
        receiptBefore, receiptBeforeId, creditBefore, creditBeforeId, ct);
      if (!r.Succeeded || r.Value is null) return CommandResult<object>.Fail(r.ErrorCode ?? ErrorCodes.ScopeDenied, r.Message ?? "Access denied.");
      var v = r.Value.Detail;
      return CommandResult<object>.Ok(new
      {
        v.Invoice.Id, v.Invoice.BillingAccountId, v.Invoice.InvoiceNumber, v.Invoice.Currency, v.Invoice.Subtotal, v.Invoice.Tax, v.Invoice.Total, v.Invoice.Revision, v.Invoice.Status,
        v.Invoice.CreatedAt, v.Invoice.PostedAt, Outstanding = v.Balance.Outstanding,
        Credited = v.Balance.Credited, Allocated = v.Balance.Allocated,
        Lines = v.Lines.Select(l => new { l.Description, l.Quantity, l.UnitPrice, l.LineTotal }),
        Allocations = r.Value.Allocations,
        Receipts = r.Value.Receipts,
        r.Value.ReceiptsHaveMore,
        CreditNotes = r.Value.CreditNotes,
        r.Value.CreditNotesHaveMore,
        PaymentTerms = r.Value.PaymentTerms,
        r.Value.PaymentTermsHaveMore,
        r.Value.CanSubmitPaymentTerms,
        r.Value.CanReviewPaymentTerms,
        r.Value.CanIssueCreditNote,
        CanApproveInvoice = r.Value.CanApproveInvoice,
        CanPostInvoice = r.Value.CanPostInvoice,
        CanSendInvoice = r.Value.CanSendInvoice,
        CanAct = true,
      });
    }));
    group.MapPost("/finance/invoices/{id:guid}/payment-terms", (Guid id, PaymentTermsInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => InvoicePaymentTermsService.SubmitAsync(db, actor, id,
        new SetInvoicePaymentTermsRequest(input.DueDate, input.Basis ?? "", input.TermsDescription ?? "",
          input.EvidenceReference ?? "", input.ExpectedRevision, input.Reviewed), ct)));
    group.MapPost("/finance/invoice-payment-terms/{id:guid}/review", (Guid id, PaymentTermsReviewInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => InvoicePaymentTermsService.ReviewAsync(db, actor, id,
        new ReviewInvoicePaymentTermsRequest(input.Approve, input.Reason ?? "", input.Reviewed), ct)));
    group.MapPost("/finance/allocations/{id:guid}/reversals", (Guid id, AllocationReversalInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) =>
      {
        if (!TryDecimal(input.Amount, out var amount))
          return Task.FromResult(CommandResult<Guid>.Fail("billing.reversal-invalid", "Review a positive exact reversal amount."));
        return ReceiptAllocationReversalService.SubmitAsync(db, actor, id,
          new RequestReceiptAllocationReversal(amount, input.Reference ?? "", input.Reason ?? "", input.ExpectedRevision, input.Reviewed), ct);
      }));
    group.MapPost("/finance/allocation-reversals/{id:guid}/review", (Guid id, AllocationReversalReviewInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ReceiptAllocationReversalService.ReviewAsync(db, actor, id,
        new ReviewReceiptAllocationReversal(input.Approve, input.Reason ?? "", input.Reviewed), ct)));
    group.MapPost("/finance/billing-accounts/{id:guid}/receipts", (Guid id, ReceiptInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) =>
      {
        if (!input.Reviewed || !TryDecimal(input.Amount, out var amount) || string.IsNullOrWhiteSpace(input.Reference) || input.Reference.Trim().Length > 200)
          return Task.FromResult(CommandResult<Guid>.Fail("billing.invalid", "Review a positive receipt amount and a transaction reference of at most 200 characters."));
        return BillingService.RecordReceiptAsync(db, actor, new RecordReceiptRequest(id, amount, input.Reference.Trim()), ct);
      }));
    group.MapPost("/finance/receipts/{id:guid}/allocations", (Guid id, ReceiptAllocationInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) =>
      {
        if (!input.Reviewed || !TryDecimal(input.Amount, out var amount) || input.InvoiceId == Guid.Empty)
          return Task.FromResult(CommandResult<bool>.Fail("billing.invalid", "Review a positive exact allocation amount for this invoice."));
        return AsBoolean(BillingService.AllocateReceiptAsync(db, actor, new AllocateReceiptRequest(id, input.InvoiceId, amount), ct));
      }));
    group.MapPost("/finance/invoices/{id:guid}/credit-notes", (Guid id, CreditNoteInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) =>
      {
        if (!input.Reviewed || !TryDecimal(input.Amount, out var amount) || string.IsNullOrWhiteSpace(input.NoteNumber) ||
            input.NoteNumber.Trim().Length > 64 || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 1000)
          return Task.FromResult(CommandResult<Guid>.Fail("billing.invalid", "Review a positive credit amount, unique note number, and reason."));
        return BillingService.IssueCreditNoteAsync(db, actor, new IssueCreditNoteRequest(id,
          input.NoteNumber.Trim(), amount, input.Reason.Trim()), ct);
      }));
    foreach (var (action, command) in new (string, Func<Application.Operations.IAuditSphereDbContext, Application.Abstractions.ActorContext, Guid, CancellationToken, Task<CommandResult>>)[]
    {
      ("approve", (db, a, id, ct) => BillingService.ApproveInvoiceAsync(db, a, id, ct)),
      ("post", (db, a, id, ct) => BillingService.PostInvoiceAsync(db, a, id, ct)),
      ("send", (db, a, id, ct) => BillingService.SendInvoiceAsync(db, a, id, ct)),
    })
      group.MapPost("/finance/invoices/{id:guid}/" + action, (Guid id, HttpContext http) => CommandAsync(http, (db, actor, ct) => command(db, actor, id, ct)));
  }

  private static DateOnly? ParseAsOfDate(string? value) =>
    DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
      ? parsed : null;

  private static async Task<CommandResult<bool>> AsBoolean(Task<CommandResult> command)
  {
    var result = await command;
    return result.Succeeded ? CommandResult<bool>.Ok(true) : CommandResult<bool>.Fail(result.ErrorCode!, result.Message!);
  }
}
