using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>
/// Per-payment vouchers (CP-05, F-09; STE 2.2 line 411). Each actual payment receives one immutable voucher showing the amount received,
/// its date and reference, the amount applied to the milestone invoice and the balance remaining after it, plus one dispatch intent. A
/// QAR 10,000 advance paid as 4,000 and 6,000 therefore has two vouchers that sum to 10,000; the milestone settlement receipt is separate
/// and lists both payments. Retrying the same payment reference never creates a second voucher or email.
/// </summary>
public static partial class FeeAgreementService
{
  internal sealed record PaymentVoucherOutcome(Guid? DocumentId, bool EmailQueued, string Message);

  /// <summary>One payment applied to an invoice, net of approved allocation reversals.</summary>
  internal sealed record InvoicePayment(Guid ReceiptId, DateTimeOffset ReceivedAt, string Reference, decimal Amount, decimal Allocated);

  /// <summary>Payments applied to the invoice, in the order they were received (then by receipt identity), net of approved reversals.</summary>
  internal static async Task<IReadOnlyList<InvoicePayment>> InvoicePaymentsAsync(IAuditSphereDbContext db, Guid firmId, Invoice invoice,
    CancellationToken ct)
  {
    var allocations = await (from allocation in db.ReceiptAllocations.AsNoTracking()
      join receipt in db.Receipts.AsNoTracking() on new { allocation.FirmId, Id = allocation.ReceiptId } equals new { receipt.FirmId, receipt.Id }
      where allocation.FirmId == firmId && allocation.InvoiceId == invoice.Id
      select new { AllocationId = allocation.Id, allocation.Amount, receipt.Id, receipt.ReceivedAt, receipt.Reference, ReceiptAmount = receipt.Amount })
      .ToListAsync(ct);
    var allocationIds = allocations.Select(x => x.AllocationId).ToArray();
    var reversals = await db.ReceiptAllocationReversals.AsNoTracking()
      .Where(x => x.FirmId == firmId && allocationIds.Contains(x.ReceiptAllocationId) && x.Status == ReceiptAllocationReversalStates.Approved)
      .GroupBy(x => x.ReceiptAllocationId).Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
    var reversed = reversals.ToDictionary(x => x.Key, x => x.Amount);
    return allocations
      .GroupBy(x => x.Id)
      .Select(g => new InvoicePayment(g.Key, g.First().ReceivedAt, g.First().Reference, g.First().ReceiptAmount,
        MoneyPolicy.Normalize(Math.Max(0m, g.Sum(x => x.Amount - reversed.GetValueOrDefault(x.AllocationId))))))
      .Where(x => x.Allocated > 0m)
      .OrderBy(x => x.ReceivedAt).ThenBy(x => x.ReceiptId)
      .ToList();
  }

  internal static async Task<PaymentVoucherOutcome> EnsurePaymentVoucherAsync(IAuditSphereDbContext db, ActorContext actor,
    EngagementFeeAgreement agreement, FeeMilestone milestone, Invoice invoice, Receipt receipt, CancellationToken ct)
  {
    var document = await db.CommercialDocuments.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ReceiptId == receipt.Id && x.Kind == CommercialDocumentKinds.PaymentVoucher, ct);
    var profile = await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    var client = await db.PracticeClients.AsNoTracking().SingleAsync(x => x.Id == agreement.PracticeClientId && x.FirmId == actor.FirmId, ct);
    if (document is null)
    {
      if (profile is null)
        return new(null, false, "The payment is recorded. Configure the firm commercial profile, then record the same reference again to issue its voucher.");
      // The balance after this payment counts only payments received up to and including it, so a voucher issued late still states the
      // balance as it stood after this payment, not after later ones.
      var payments = await InvoicePaymentsAsync(db, actor.FirmId, invoice, ct);
      var position = payments.ToList().FindIndex(x => x.ReceiptId == receipt.Id);
      if (position < 0)
        return new(null, false, "The payment is recorded but is not applied to the milestone invoice, so no voucher was issued.");
      var thisPayment = payments[position];
      var appliedThrough = MoneyPolicy.Normalize(payments.Take(position + 1).Sum(x => x.Allocated));
      var remaining = MoneyPolicy.Normalize(Math.Max(0m, invoice.Total - appliedThrough));
      var milestoneLabel = milestone.Kind == FeeMilestoneKinds.Advance ? $"Advance ({agreement.AdvancePercent:0.##}%)" : "Final balance";
      string Money(decimal value) => $"{value.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency}";
      // A UUIDv7 timestamp prefix can be shared by concurrent payments. The full retained
      // payment identity gives new vouchers distinct, stable references without changing issued ones.
      var voucherNumber = $"PV-{receipt.Id.ToString("N").ToUpperInvariant()}";
      var model = new CommercialDocumentModel(profile, "Official payment voucher", voucherNumber,
        DateOnly.FromDateTime(receipt.ReceivedAt.UtcDateTime), client.LegalName,
        [
          new("Payment received", [$"We acknowledge receipt of {Money(receipt.Amount)} from {client.LegalName}."],
            new(["Item", "Detail"],
            [
              ["Amount received", Money(receipt.Amount)],
              ["Received on", receipt.ReceivedAt.UtcDateTime.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture)],
              ["Payment reference", receipt.Reference],
              ["Applied to invoice", invoice.InvoiceNumber],
              ["Milestone", $"{milestoneLabel} — invoice total {Money(invoice.Total)}"],
              ["Amount applied by this payment", Money(thisPayment.Allocated)],
              ["Paid against this invoice to date", Money(appliedThrough)],
              ["Remaining on this invoice after this payment", Money(remaining)],
            ], [1]))
        ], false);
      var bytes = CommercialDocumentRenderer.RenderDocx(model);
      document = new CommercialDocument
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ProposalId = agreement.ProposalId, QuotationVersionId = agreement.QuotationVersionId,
        FeeMilestoneId = milestone.Id, ReceiptId = receipt.Id, Kind = CommercialDocumentKinds.PaymentVoucher,
        TemplateVersion = CommercialDocumentRenderer.PaymentVoucherTemplate, ProfileVersion = profile.Version,
        FileName = $"Voucher-{voucherNumber}.docx", ContentType = CommercialDocumentRenderer.DocxContentType,
        Bytes = bytes, Sha256Hex = Hashing.Sha256Hex(bytes), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      };
      db.CommercialDocuments.Add(document);
      try
      {
        await db.SaveChangesAsync(ct);
      }
      catch (DbUpdateException)
      {
        // A concurrent retry of the same payment issued the voucher first; the unique index keeps exactly one. Removing the unsaved
        // row detaches it, so it is never written later.
        db.CommercialDocuments.Remove(document);
        document = await db.CommercialDocuments.AsNoTracking().SingleAsync(x =>
          x.FirmId == actor.FirmId && x.ReceiptId == receipt.Id && x.Kind == CommercialDocumentKinds.PaymentVoucher, ct);
      }
    }

    if (await db.CommercialNotifications.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.ReceiptId == receipt.Id, ct))
      return new(document.Id, true, "Its voucher is issued and its email is queued.");
    var recipient = await ResolveRecipientAsync(db, actor.FirmId, agreement, ct);
    if (recipient is null)
      return new(document.Id, false, "Its voucher is issued. No client contact email is on file, so no voucher email was queued.");
    var firmName = profile?.LegalName ?? string.Empty;
    var dispatch = new CommercialNotification
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Kind = CommercialNotificationKinds.PaymentVoucher, ReceiptId = receipt.Id,
      PracticeClientId = agreement.PracticeClientId, DocumentId = document.Id, OfferSha256 = document.Sha256Hex, Recipient = recipient.Email,
      RecipientContactId = recipient.ContactId, RecipientRoutingId = recipient.RoutingId, RecipientAuthorityBasis = recipient.AuthorityBasis,
      Subject = $"Payment voucher {document.FileName.Replace("Voucher-", string.Empty).Replace(".docx", string.Empty)} — {firmName}",
      Body = $"Dear {client.LegalName},\n\nWe confirm receipt of your payment of {receipt.Amount.ToString("N2", CultureInfo.InvariantCulture)} {agreement.Currency} " +
        $"(reference {receipt.Reference}) against invoice {invoice.InvoiceNumber}.\n\n" +
        $"Your official payment voucher is available for authenticated download at /api/commercial/documents/{document.Id}/download and is retained on your engagement record.\n\n{firmName}",
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.CommercialNotifications.Add(dispatch);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { db.CommercialNotifications.Remove(dispatch); /* a concurrent retry queued it first: the unique index keeps exactly one */ }
    return new(document.Id, true, "Its voucher is issued and its email is queued.");
  }
}
