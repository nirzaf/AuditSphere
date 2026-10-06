using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record BillingReceiptOption(
  Guid Id, string Reference, string Currency, decimal Amount, decimal Allocated, decimal Remaining, DateTimeOffset ReceivedAt);

public sealed record BillingCreditNoteSummary(
  Guid Id, string NoteNumber, string Currency, decimal Amount, string Reason, DateTimeOffset CreatedAt);

public sealed record BillingPaymentTermsSummary(
  Guid Id, long Revision, DateOnly DueDate, string Basis, string TermsDescription,
  string EvidenceReference, string Status, Guid SubmittedByUserId, DateTimeOffset SubmittedAt,
  Guid? ReviewedByUserId, DateTimeOffset? ReviewedAt, string? ReviewReason);

public sealed record BillingAllocationReversalSummary(
  Guid Id, Guid ReceiptAllocationId, long Revision, decimal Amount, string Reference, string Reason, string Status,
  Guid SubmittedByUserId, DateTimeOffset SubmittedAt, Guid? ReviewedByUserId,
  DateTimeOffset? ReviewedAt, string? ReviewReason);

public sealed record BillingInvoiceAllocationSummary(
  Guid Id, Guid ReceiptId, string ReceiptReference, string Currency, DateTimeOffset CreatedAt, decimal Amount, decimal AppliedAfterReversals,
  decimal Reversed, decimal RemainingToReverse, long LatestReversalRevision,
  bool CanRequestReversal, bool CanReviewReversal, IReadOnlyList<BillingAllocationReversalSummary> Reversals);

public sealed record BillingInvoiceWorkspace(
  InvoiceDetailView Detail, Guid BillingAccountId,
  IReadOnlyList<BillingReceiptOption> Receipts, bool ReceiptsHaveMore,
  IReadOnlyList<BillingInvoiceAllocationSummary> Allocations,
  IReadOnlyList<BillingCreditNoteSummary> CreditNotes, bool CreditNotesHaveMore,
  IReadOnlyList<BillingPaymentTermsSummary> PaymentTerms, bool PaymentTermsHaveMore,
  bool CanSubmitPaymentTerms, bool CanReviewPaymentTerms,
  bool CanIssueCreditNote, bool CanApproveInvoice, bool CanPostInvoice, bool CanSendInvoice);

/// <summary>Bounded invoice payment and credit-note context for the authorized finance workspace.</summary>
public static class BillingInvoiceWorkspaceQuery
{
  private const int PageLimit = 100;
  private const int PaymentTermsHistoryLimit = 20;
  private const int MaxInvoiceAllocations = 5_000;
  private const int MaxInvoiceReversals = 10_000;
  private static readonly string[] CreditRoles = ["FinanceManager"];
  private static readonly string[] ReviewerRoles = ["FinanceReviewer"];
  private static readonly string[] BillingRoles = ["FinanceManager", "FinanceReviewer"];

  public static async Task<CommandResult<BillingInvoiceWorkspace>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invoiceId,
    DateTimeOffset? receiptBefore = null, Guid? receiptBeforeId = null,
    DateTimeOffset? creditBefore = null, Guid? creditBeforeId = null,
    CancellationToken ct = default)
  {
    if ((receiptBefore is null) != (receiptBeforeId is null) || receiptBeforeId == Guid.Empty ||
        (creditBefore is null) != (creditBeforeId is null) || creditBeforeId == Guid.Empty)
      return CommandResult<BillingInvoiceWorkspace>.Fail("billing.invalid", "The billing history cursor is invalid.");

    var detail = await BillingService.GetInvoiceDetailAsync(db, actor, invoiceId, ct);
    if (!detail.Succeeded || detail.Value is null)
      return CommandResult<BillingInvoiceWorkspace>.Fail(detail.ErrorCode!, detail.Message!);
    if (detail.Value.Allocations.Count > MaxInvoiceAllocations)
      return CommandResult<BillingInvoiceWorkspace>.Fail("billing.history-limit",
        "This invoice has too many receipt allocations to display safely.");

    var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == detail.Value.Invoice.BillingAccountId, ct);
    if (account is null)
      return CommandResult<BillingInvoiceWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var receiptQuery = db.Receipts.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.BillingAccountId == account.Id && x.Status == BillingStates.ReceiptRecorded);
    if (receiptBefore is { } receivedAt && receiptBeforeId is { } receiptId)
      receiptQuery = receiptQuery.Where(x => x.ReceivedAt < receivedAt ||
        (x.ReceivedAt == receivedAt && x.Id.CompareTo(receiptId) < 0));
    var receiptRows = await receiptQuery
      .OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.Id)
      .Take(PageLimit + 1)
      .Select(x => new { x.Id, x.Reference, x.Currency, x.Amount, x.ReceivedAt })
      .ToListAsync(ct);
    var hasMoreReceipts = receiptRows.Count > PageLimit;
    var visibleReceipts = receiptRows.Take(PageLimit).ToArray();
    var receiptIds = visibleReceipts.Select(x => x.Id).ToArray();
    var allocatedByReceipt = receiptIds.Length == 0
      ? new Dictionary<Guid, decimal>()
      : await db.ReceiptAllocations.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && receiptIds.Contains(x.ReceiptId))
        .GroupBy(x => x.ReceiptId)
        .Select(g => new { ReceiptId = g.Key, Amount = g.Sum(x => x.Amount) })
        .ToDictionaryAsync(x => x.ReceiptId, x => x.Amount, ct);
    var reversedByReceipt = receiptIds.Length == 0 ? new Dictionary<Guid, decimal>() : await (
      from reversal in db.ReceiptAllocationReversals.AsNoTracking()
      join allocation in db.ReceiptAllocations.AsNoTracking()
        on new { reversal.FirmId, reversal.ReceiptAllocationId } equals new { allocation.FirmId, ReceiptAllocationId = allocation.Id }
      where reversal.FirmId == actor.FirmId && receiptIds.Contains(allocation.ReceiptId) &&
        reversal.Status == ReceiptAllocationReversalStates.Approved
      group reversal by allocation.ReceiptId into reversals
      select new { ReceiptId = reversals.Key, Amount = reversals.Sum(x => x.Amount) })
      .ToDictionaryAsync(x => x.ReceiptId, x => x.Amount, ct);
    var receipts = visibleReceipts.Select(x =>
    {
      var allocated = Math.Max(0m, allocatedByReceipt.GetValueOrDefault(x.Id) - reversedByReceipt.GetValueOrDefault(x.Id));
      return new BillingReceiptOption(x.Id, x.Reference, x.Currency ?? account.Currency, x.Amount,
        allocated, MoneyPolicy.Normalize(x.Amount - allocated), x.ReceivedAt);
    }).ToArray();

    var allocationIds = detail.Value.Allocations.Select(x => x.Id).ToArray();
    var allocationReceiptIds = detail.Value.Allocations.Select(x => x.ReceiptId).Distinct().ToArray();
    var allocationReceipts = allocationReceiptIds.Length == 0 ? new Dictionary<Guid, (string Reference, string Currency)>() :
      await db.Receipts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && allocationReceiptIds.Contains(x.Id))
        .Select(x => new { x.Id, x.Reference, x.Currency })
        .ToDictionaryAsync(x => x.Id, x => (Reference: x.Reference, Currency: x.Currency ?? account.Currency), ct);
    var reversalCount = allocationIds.Length == 0 ? 0 : await db.ReceiptAllocationReversals.AsNoTracking()
      .CountAsync(x => x.FirmId == actor.FirmId && allocationIds.Contains(x.ReceiptAllocationId), ct);
    if (reversalCount > MaxInvoiceReversals)
      return CommandResult<BillingInvoiceWorkspace>.Fail("billing.history-limit",
        "This invoice has too much receipt-reversal history to display at once.");
    var reversalRows = allocationIds.Length == 0 ? [] : await db.ReceiptAllocationReversals.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && allocationIds.Contains(x.ReceiptAllocationId))
      .OrderByDescending(x => x.Revision)
      .Select(x => new BillingAllocationReversalSummary(x.Id, x.ReceiptAllocationId, x.Revision, x.Amount, x.Reference, x.Reason,
        x.Status, x.SubmittedByUserId, x.SubmittedAt, x.ReviewedByUserId, x.ReviewedAt, x.ReviewReason))
      .ToListAsync(ct);
    var reversalsByAllocation = reversalRows.GroupBy(x => x.ReceiptAllocationId)
      .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Revision).ToArray());
    var canRequestReversal = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, account.PracticeClientId, RequiredRoles: CreditRoles, InternalOnly: true), ct)).Succeeded;
    var canReviewReversal = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, account.PracticeClientId, RequiredRoles: ReviewerRoles, InternalOnly: true), ct)).Succeeded;
    var allocations = detail.Value.Allocations.Select(allocation =>
    {
      var history = reversalsByAllocation.GetValueOrDefault(allocation.Id, []);
      var reversed = history.Where(x => x.Status == ReceiptAllocationReversalStates.Approved).Sum(x => x.Amount);
      var pending = history.FirstOrDefault(x => x.Status == ReceiptAllocationReversalStates.PendingReview);
      var reserved = pending?.Amount ?? 0m;
      var receipt = allocationReceipts.GetValueOrDefault(allocation.ReceiptId);
      return new BillingInvoiceAllocationSummary(allocation.Id, allocation.ReceiptId, receipt.Reference ?? "Receipt reference unavailable",
        receipt.Currency ?? account.Currency, allocation.CreatedAt,
        allocation.Amount, MoneyPolicy.Normalize(Math.Max(0m, allocation.Amount - reversed)), reversed,
        MoneyPolicy.Normalize(Math.Max(0m, allocation.Amount - reversed - reserved)),
        history.FirstOrDefault()?.Revision ?? 0,
        canRequestReversal && pending is null && reversed < allocation.Amount,
        canReviewReversal && pending is not null && pending.SubmittedByUserId != actor.UserId, history);
    }).ToArray();

    var creditQuery = db.CreditNotes.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.InvoiceId == invoiceId && x.Status == BillingStates.CreditIssued);
    if (creditBefore is { } createdAt && creditBeforeId is { } creditId)
      creditQuery = creditQuery.Where(x => x.CreatedAt < createdAt ||
        (x.CreatedAt == createdAt && x.Id.CompareTo(creditId) < 0));
    var creditRows = await creditQuery
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Take(PageLimit + 1)
      .Select(x => new BillingCreditNoteSummary(x.Id, x.NoteNumber, x.Currency, x.Amount, x.Reason, x.CreatedAt))
      .ToListAsync(ct);
    var termsRows = await db.InvoicePaymentTermsRevisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.InvoiceId == invoiceId)
      .OrderByDescending(x => x.Revision)
      .Take(PaymentTermsHistoryLimit + 1)
      .Select(x => new BillingPaymentTermsSummary(x.Id, x.Revision, x.DueDate, x.Basis,
        x.TermsDescription, x.EvidenceReference, x.Status, x.SubmittedByUserId, x.SubmittedAt,
        x.ReviewedByUserId, x.ReviewedAt, x.ReviewReason))
      .ToListAsync(ct);
    var visibleTerms = termsRows.Take(PaymentTermsHistoryLimit).ToArray();
    var latestTerms = visibleTerms.FirstOrDefault();
    var canSubmitTerms = latestTerms?.Status != InvoicePaymentTermsStates.PendingReview &&
      (await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, account.PracticeClientId, RequiredRoles: CreditRoles, InternalOnly: true), ct)).Succeeded;
    var canReviewTerms = latestTerms is { Status: InvoicePaymentTermsStates.PendingReview } &&
      latestTerms.SubmittedByUserId != actor.UserId &&
      (await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, account.PracticeClientId, RequiredRoles: ReviewerRoles, InternalOnly: true), ct)).Succeeded;
    var canIssue = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, account.PracticeClientId, RequiredRoles: CreditRoles, InternalOnly: true), ct)).Succeeded;
    var canApprove = detail.Value.Invoice.CreatedByUserId != actor.UserId &&
      (await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, account.PracticeClientId, RequiredRoles: ReviewerRoles, InternalOnly: true), ct)).Succeeded;
    var canPost = canIssue && await db.FirmFinanceProfiles.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.Approved && x.FunctionalCurrency == account.Currency, ct);
    var canSend = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, account.PracticeClientId, RequiredRoles: BillingRoles, InternalOnly: true), ct)).Succeeded;

    if (!await BillingService.CanOpenInvoiceAsync(db, actor, invoiceId, ct))
      return CommandResult<BillingInvoiceWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    return CommandResult<BillingInvoiceWorkspace>.Ok(new BillingInvoiceWorkspace(
      detail.Value, account.Id, receipts, hasMoreReceipts,
      allocations,
      creditRows.Take(PageLimit).ToArray(), creditRows.Count > PageLimit,
      visibleTerms, termsRows.Count > PaymentTermsHistoryLimit, canSubmitTerms, canReviewTerms, canIssue,
      canApprove, canPost, canSend));
  }
}
