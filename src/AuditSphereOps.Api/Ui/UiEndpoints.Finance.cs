using System.Globalization;
using System.Text;
using System.Text.Json;
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
  public sealed record FirmAccountInput(string Code, string Name, string AccountType, string NormalSide, bool PostingAllowed = true);
  public sealed record FirmPeriodInput(string PeriodCode);
  public sealed record FirmJournalLineInput(Guid FirmAccountId, string Description, string Debit, string Credit);
  public sealed record FirmJournalInput(Guid PeriodId, string JournalNumber, string SourceKey,
    string PostingPurpose, string Currency, IReadOnlyList<FirmJournalLineInput> Lines);
  public sealed record FirmJournalActionInput(bool Confirmed);
  public sealed record FiscalCloseInput(string Reason);
  public sealed record ReversePostingInput(Guid PeriodId, string Reason);
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
          CanCreateSetup = r.Value!.CanCreateSetup,
          r.Value!.CanClosePeriod,
          r.Value.CanReviewJournals,
          r.Value.CanPostJournals,
          CanCreateJournals = r.Value.CanCreateJournals,
          r.Value.CanReversePostings,
          r.Value.CanReopenPeriod,
            Periods = r.Value.Periods.Select(p => new { p.Id, p.PeriodCode, p.Status, p.Revision, p.ClosedAt }),
            Accounts = r.Value.Accounts.Select(a => new { a.Id, a.Code, a.Name, a.AccountType, a.NormalSide, a.PostingAllowed }),
            Postings = r.Value.RecentPostings.Select(p => new { p.Id, p.PeriodId, p.JournalId, p.PostedAt, p.Currency, p.PostedByUserId, p.ReversalOfPostingId }),
            Journals = r.Value.RecentJournals.Select(j => new
            {
              j.Id, j.PeriodId, j.PeriodCode, j.JournalNumber, j.SourceKind, j.SourceKey, j.SourceRevision,
              j.PostingPurpose, j.Currency, j.Status, j.CreatedByUserId, j.ApprovedByUserId,
              j.SupportingEvidenceFileName, j.SupportingEvidenceSha256,
              j.CreatedAt, j.ApprovedAt, j.PostedAt,
              Lines = j.Lines.Select(l => new
              {
                l.FirmAccountId, l.AccountCode, l.AccountName, l.Description,
                Debit = l.Debit.ToString(CultureInfo.InvariantCulture),
                Credit = l.Credit.ToString(CultureInfo.InvariantCulture)
              })
            })
          })
        : CommandResult<object>.Fail(r.ErrorCode!, r.Message!);
    }));
    group.MapPost("/finance/accounts", (FirmAccountInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => LedgerService.CreateFirmAccountAsync(db, actor,
        new CreateFirmAccountRequest(input.Code ?? "", input.Name ?? "", input.AccountType ?? "", input.NormalSide ?? "", input.PostingAllowed), ct)));
    group.MapPost("/finance/periods", (FirmPeriodInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => LedgerService.CreateFirmPeriodAsync(db, actor,
        new CreateFirmPeriodRequest(input.PeriodCode ?? ""), ct)));
    group.MapUiPost("/finance/journals", async http =>
    {
      if (!http.Request.HasFormContentType) return Invalid("Send the journal details and supporting document as a multipart form.");
      var form = await http.Request.ReadFormAsync(http.RequestAborted);
      FirmJournalInput? input;
      try { input = JsonSerializer.Deserialize<FirmJournalInput>(form["journal"].ToString(), UiJson); }
      catch (JsonException) { return Invalid("The journal details are invalid."); }
      if (input is null) return Invalid("Journal details are required.");
      var evidenceFile = form.Files.GetFile("evidence");
      var evidence = await ReadUploadAsync(http, "evidence", LedgerService.MaxJournalEvidenceBytes);
      if (evidenceFile is not null && evidence is null)
        return Invalid("The supporting document must be between 1 byte and 5 MB.");
      if (input.Lines is null || input.Lines.Count is < 2 or > 200)
        return Invalid("A journal needs 2 to 200 reviewed lines.");
      var lines = new List<FirmJournalLineRequest>(input.Lines.Count);
      foreach (var line in input.Lines)
      {
        if (line is null || !TryDecimal(line.Debit, out var debit) || !TryDecimal(line.Credit, out var credit))
          return Invalid("Enter each debit and credit as an exact decimal amount.");
        lines.Add(new FirmJournalLineRequest(line.FirmAccountId, line.Description ?? "", debit, credit));
      }
      return await CommandAsync(http, (db, actor, ct) => LedgerService.CreateFirmJournalDraftAsync(db, actor,
        new CreateFirmJournalDraftRequest(input.PeriodId, input.JournalNumber ?? "", "MANUAL",
          input.SourceKey ?? "", 1, input.PostingPurpose ?? "", input.Currency ?? "", lines,
          evidence?.Name, evidence?.ContentType, evidence?.Content), ct));
    });
    group.MapPost("/finance/journals/{id:guid}/evidence", async (Guid id, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Failure("csrf.invalid", "Refresh the page and try again.", 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await FirmFinanceQuery.GetJournalEvidenceAsync(db, actor, id, http.RequestAborted);
      if (!result.Succeeded || result.Value is null) return Failure(result.ErrorCode, result.Message);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Failure("session.unavailable", "Sign in again.", 401);
      http.Response.Headers["X-Firm-Journal-Evidence-SHA256"] = result.Value.Sha256;
      http.Response.Headers["X-Content-Type-Options"] = "nosniff";
      return Results.File(result.Value.Content, "application/octet-stream", result.Value.FileName);
    });
    group.MapPost("/finance/journals/{id:guid}/submit", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => LedgerService.SubmitFirmJournalAsync(db, actor, id, ct)));
    group.MapPost("/finance/journals/{id:guid}/review", (Guid id, FirmJournalActionInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => input.Confirmed
        ? LedgerService.ApproveFirmJournalAsync(db, actor, id, ct)
        : Task.FromResult(CommandResult.Fail("ledger.review-unconfirmed", "Confirm the journal review before approval."))));
    group.MapPost("/finance/journals/{id:guid}/post", (Guid id, FirmJournalActionInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => input.Confirmed
        ? LedgerService.PostFirmJournalAsync(db, actor, id, ct)
        : Task.FromResult(CommandResult<Guid>.Fail("ledger.post-unconfirmed", "Confirm the journal posting before continuing."))));
    group.MapPost("/finance/periods/{id:guid}/close", (Guid id, FiscalCloseInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => LedgerService.CloseFiscalPeriodAsync(db, actor, id, i.Reason ?? "", ct)));
    group.MapPost("/finance/periods/{id:guid}/reopen", (Guid id, FiscalCloseInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => LedgerService.RequestPeriodReopenAsync(db, actor, id, i.Reason ?? "", ct)));
    group.MapPost("/finance/postings/{id:guid}/reverse", (Guid id, ReversePostingInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => LedgerService.ReverseFirmPostingAsync(db, actor,
        new ReverseFirmPostingRequest(id, input.PeriodId, input.Reason ?? ""), ct)));
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

  private static async Task<CommandResult> AsCommand<T>(Task<CommandResult<T>> command)
  {
    var result = await command;
    return result.Succeeded ? CommandResult.Ok() : CommandResult.Fail(result.ErrorCode!, result.Message!);
  }
}
