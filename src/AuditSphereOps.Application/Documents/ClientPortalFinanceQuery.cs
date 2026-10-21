using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record PortalClientMilestoneInvoice(
  Guid InvoiceId,
  string Kind,
  string InvoiceNumber,
  string Status,
  string Amount,
  string Currency,
  DateTimeOffset? PostedAt,
  DateTimeOffset? SentAt,
  string DeliveryStatus,
  string CollectionStatus,
  string CashApplied,
  string CreditApplied,
  string OutstandingAmount,
  DateOnly? DueDate,
  string PaymentTermsStatus,
  string AgingStatus);

public sealed record PortalClientReceipt(
  Guid ReceiptId,
  Guid? DocumentId,
  string Kind,
  string Amount,
  string AppliedAmount,
  string Currency,
  string Reference,
  DateTimeOffset ReceivedAt,
  string? DownloadUrl);

public sealed record PortalClientFinanceAgreement(
  Guid AgreementId,
  Guid? EngagementId,
  string AgreedFee,
  string Currency,
  string AdvancePercent,
  string OutstandingBalance,
  IReadOnlyList<PortalClientMilestoneInvoice> Invoices,
  IReadOnlyList<PortalClientReceipt> Receipts);

public sealed record PortalClientFinanceView(
  DateOnly AsOfDateUtc,
  IReadOnlyList<PortalClientFinanceAgreement> Agreements);

public static class ClientPortalFinanceQuery
{
  private static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);

  public static async Task<CommandResult<PortalClientFinanceView>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && x.UserKind == "Client" && !x.Disabled, ct);
    if (user is null || user.SessionEpoch != actor.SessionEpoch)
      return CommandResult<PortalClientFinanceView>.Fail(ErrorCodes.ScopeDenied, "Finance documents unavailable.");

    var now = DateTimeOffset.UtcNow;
    var asOfDateUtc = DateOnly.FromDateTime(now.UtcDateTime);
    // Client grants carry an exact scope: a client-wide grant (no engagement) covers the client's
    // agreements, while an engagement-scoped grant only covers that engagement's agreement. The
    // query never widens an engagement-only grant to sibling engagements (STE-REM-03).
    var grants = await db.RoleGrants.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.Role == "ClientUser" &&
        x.ClientId != null && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now))
      .Select(x => new { ClientId = x.ClientId!.Value, x.EngagementId })
      .ToListAsync(ct);

    if (grants.Count == 0)
      return CommandResult<PortalClientFinanceView>.Ok(new(asOfDateUtc, []));

    var clientIds = grants.Select(x => x.ClientId).Distinct().ToList();
    // Apply exact authorization before the result bound: newer sibling agreements must not
    // consume this user's window or hide an older agreement in their authorized engagement.
    var agreements = await db.EngagementFeeAgreements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.PracticeClientId) &&
        db.RoleGrants.Any(grant => grant.FirmId == actor.FirmId && grant.UserId == actor.UserId &&
          grant.Role == "ClientUser" && grant.ClientId == x.PracticeClientId && grant.RevokedAt == null &&
          (grant.ExpiresAt == null || grant.ExpiresAt > now) &&
          (grant.EngagementId == null || grant.EngagementId == x.EngagementId)))
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Take(50)
      .ToListAsync(ct);

    var agreementViews = new List<PortalClientFinanceAgreement>();

    foreach (var agreement in agreements)
    {
      var milestones = await db.FeeMilestones.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.AgreementId == agreement.Id)
        .OrderBy(x => x.Kind == FeeMilestoneKinds.Advance ? 0 : 1)
        .ToListAsync(ct);

      var invoiceList = new List<PortalClientMilestoneInvoice>();
      var receiptList = new List<PortalClientReceipt>();
      decimal totalPaid = 0m;

      foreach (var milestone in milestones)
      {
        if (!milestone.InvoiceId.HasValue) continue;

        var invoiceId = milestone.InvoiceId.Value;
        var invoice = await db.Invoices.AsNoTracking()
          .SingleOrDefaultAsync(x => x.Id == invoiceId && x.FirmId == actor.FirmId, ct);
        var allocations = await (
          from allocation in db.ReceiptAllocations.AsNoTracking()
          join receipt in db.Receipts.AsNoTracking()
            on new { allocation.FirmId, allocation.ReceiptId } equals new { receipt.FirmId, ReceiptId = receipt.Id }
          where allocation.FirmId == actor.FirmId && allocation.InvoiceId == invoiceId &&
            receipt.Status == BillingStates.ReceiptRecorded
          select new
          {
            allocation.ReceiptId,
            allocation.Amount,
            ApprovedReversedAmount = db.ReceiptAllocationReversals.AsNoTracking()
              .Where(reversal => reversal.FirmId == actor.FirmId && reversal.ReceiptAllocationId == allocation.Id &&
                reversal.Status == ReceiptAllocationReversalStates.Approved)
              .Sum(reversal => (decimal?)reversal.Amount) ?? 0m
          }).ToListAsync(ct);
        var allocationAmountsByReceipt = allocations.GroupBy(x => x.ReceiptId).ToDictionary(group => group.Key, group =>
          (Original: group.Sum(x => x.Amount),
            Applied: MoneyPolicy.Normalize(group.Sum(x => x.Amount - x.ApprovedReversedAmount))));
        var receiptIds = allocationAmountsByReceipt.Keys.ToList();
        List<Receipt> receipts = receiptIds.Count == 0 ? [] : await db.Receipts.AsNoTracking()
          .Where(x => x.FirmId == actor.FirmId && x.Status == BillingStates.ReceiptRecorded && receiptIds.Contains(x.Id))
          .OrderBy(x => x.ReceivedAt)
          .ToListAsync(ct);
        var cashApplied = MoneyPolicy.Normalize(allocationAmountsByReceipt.Values.Sum(x => x.Applied));
        totalPaid += cashApplied;

        if (invoice is not null)
        {
          var creditApplied = MoneyPolicy.Normalize(await db.CreditNotes.AsNoTracking()
            .Where(x => x.FirmId == actor.FirmId && x.InvoiceId == invoiceId && x.Status == BillingStates.CreditIssued)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m);
          var termRevisions = await db.InvoicePaymentTermsRevisions.AsNoTracking()
            .Where(x => x.FirmId == actor.FirmId && x.InvoiceId == invoiceId)
            .OrderBy(x => x.Revision)
            .ToListAsync(ct);
          var approvedTerms = termRevisions
            .Where(x => x.Status == InvoicePaymentTermsStates.Approved && x.ReviewedAt is { } reviewedAt && reviewedAt <= now)
            .OrderByDescending(x => x.Revision)
            .FirstOrDefault();
          var latestTerms = termRevisions.LastOrDefault();
          var paymentTermsStatus = latestTerms is null
            ? "NOT_CONFIGURED"
            : latestTerms.ReviewedAt is null || latestTerms.ReviewedAt > now
              ? InvoicePaymentTermsStates.PendingReview
              : latestTerms.Status;
          var isPostedReceivable = invoice.PostedAt is not null &&
            invoice.Status is (BillingStates.InvoicePosted or BillingStates.InvoiceSent);
          var isCancelled = invoice.Status == BillingStates.InvoiceCancelled;
          var dueDate = approvedTerms?.DueDate;
          var invoiceOutstanding = isPostedReceivable
            ? Math.Max(0m, invoice.Total - cashApplied - creditApplied)
            : 0m;
          var collectionStatus = isCancelled
            ? BillingStates.InvoiceCancelled
            : !isPostedReceivable
              ? "NOT_POSTED"
              : invoiceOutstanding > 0m
                ? cashApplied > 0m ? "PARTIALLY_PAID" : creditApplied > 0m ? "PARTIALLY_CREDITED" : "UNPAID"
                : cashApplied >= invoice.Total && invoice.Total > 0m
                  ? "PAID"
                  : creditApplied > 0m ? "SETTLED_WITH_CREDIT" : "SETTLED";
          var agingStatus = isPostedReceivable
            ? FirmReceivablesAgingPolicy.Bucket(dueDate, asOfDateUtc, invoiceOutstanding)
            : "NOT_APPLICABLE";

          invoiceList.Add(new(
            invoice.Id,
            milestone.Kind,
            invoice.InvoiceNumber,
            invoice.Status,
            Exact(invoice.Total),
            invoice.Currency ?? agreement.Currency,
            invoice.PostedAt,
            invoice.SentAt,
            "NOT_TRACKED",
            collectionStatus,
            Exact(cashApplied),
            Exact(creditApplied),
            Exact(invoiceOutstanding),
            dueDate,
            paymentTermsStatus,
            agingStatus));
        }

        var doc = await db.CommercialDocuments.AsNoTracking()
          .Where(x => x.FirmId == actor.FirmId && x.FeeMilestoneId == milestone.Id && x.Kind == CommercialDocumentKinds.PaymentReceipt)
          .OrderByDescending(x => x.CreatedAt)
          .FirstOrDefaultAsync(ct);

        foreach (var receipt in receipts)
        {
          var amounts = allocationAmountsByReceipt[receipt.Id];
          receiptList.Add(new(
            receipt.Id,
            doc?.Id,
            milestone.Kind,
            Exact(amounts.Original),
            Exact(amounts.Applied),
            receipt.Currency ?? agreement.Currency,
            receipt.Reference,
            receipt.ReceivedAt,
            doc is not null ? $"/api/commercial/documents/{doc.Id}/download" : null));
        }
      }

      var outstanding = Math.Max(0m, agreement.AgreedFee - totalPaid);

      agreementViews.Add(new(
        agreement.Id,
        agreement.EngagementId,
        Exact(agreement.AgreedFee),
        agreement.Currency,
        Exact(agreement.AdvancePercent),
        Exact(outstanding),
        invoiceList,
        receiptList));
    }

    return CommandResult<PortalClientFinanceView>.Ok(new(asOfDateUtc, agreementViews));
  }
}
