using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class FeeAgreementService
{
  /// <summary>Retained PAID evidence, adjusted for subsequent independently approved cash reversals.</summary>
  public static async Task<bool> AdvanceCurrentlyPaidAsync(
    IAuditSphereDbContext db, Guid firmId, Guid agreementId, CancellationToken ct = default)
  {
    var milestone = await db.FeeMilestones.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.AgreementId == agreementId && x.Kind == FeeMilestoneKinds.Advance, ct);
    return milestone is not null && await IsMilestoneCurrentlyPaidAsync(db, milestone, ct);
  }

  internal static async Task<bool> IsMilestoneCurrentlyPaidAsync(
    IAuditSphereDbContext db, FeeMilestone milestone, CancellationToken ct)
  {
    if (milestone.State != FeeMilestoneStates.Paid || milestone.InvoiceId is not { } invoiceId) return false;
    // A paid marker is retained history, not proof that cash remains applied.
    var invoiceMatches = await (from invoice in db.Invoices.AsNoTracking()
      join account in db.BillingAccounts.AsNoTracking() on invoice.BillingAccountId equals account.Id
      join agreement in db.EngagementFeeAgreements.AsNoTracking() on account.PracticeClientId equals agreement.PracticeClientId
      where invoice.FirmId == milestone.FirmId && account.FirmId == milestone.FirmId && agreement.FirmId == milestone.FirmId &&
        invoice.Id == invoiceId && agreement.Id == milestone.AgreementId &&
        invoice.Currency == agreement.Currency && account.Currency == agreement.Currency && invoice.Total == milestone.Amount &&
        (invoice.Status == BillingStates.InvoicePosted || invoice.Status == BillingStates.InvoiceSent)
      select invoice.Id).AnyAsync(ct);
    if (!invoiceMatches) return false;
    var applied = await BillingService.GetNetAppliedToInvoiceAsync(db, milestone.FirmId, invoiceId, ct);
    if (milestone.Kind == FeeMilestoneKinds.Balance)
      applied += await db.CreditNotes.AsNoTracking().Where(x => x.FirmId == milestone.FirmId &&
        x.InvoiceId == invoiceId && x.Status == BillingStates.CreditIssued).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
    return applied >= milestone.Amount;
  }
}
