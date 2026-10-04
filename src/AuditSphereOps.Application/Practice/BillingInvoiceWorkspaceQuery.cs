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

public sealed record BillingInvoiceWorkspace(
  InvoiceDetailView Detail, Guid BillingAccountId,
  IReadOnlyList<BillingReceiptOption> Receipts, bool ReceiptsHaveMore,
  IReadOnlyList<BillingCreditNoteSummary> CreditNotes, bool CreditNotesHaveMore,
  bool CanIssueCreditNote);

/// <summary>Bounded invoice payment and credit-note context for the authorized finance workspace.</summary>
public static class BillingInvoiceWorkspaceQuery
{
  private const int PageLimit = 100;
  private static readonly string[] CreditRoles = ["FinanceManager"];

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
    var receipts = visibleReceipts.Select(x =>
    {
      var allocated = allocatedByReceipt.GetValueOrDefault(x.Id);
      return new BillingReceiptOption(x.Id, x.Reference, x.Currency ?? account.Currency, x.Amount,
        allocated, MoneyPolicy.Normalize(x.Amount - allocated), x.ReceivedAt);
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
    var canIssue = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, account.PracticeClientId, RequiredRoles: CreditRoles, InternalOnly: true), ct)).Succeeded;

    if (!await BillingService.CanOpenInvoiceAsync(db, actor, invoiceId, ct))
      return CommandResult<BillingInvoiceWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    return CommandResult<BillingInvoiceWorkspace>.Ok(new BillingInvoiceWorkspace(
      detail.Value, account.Id, receipts, hasMoreReceipts,
      creditRows.Take(PageLimit).ToArray(), creditRows.Count > PageLimit, canIssue));
  }
}
