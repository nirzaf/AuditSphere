using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public static partial class PracticeAnalyticsQuery
{
  // Select the same eligibility sources as the economics report before loading financial evidence.
  // The cursor narrows firm-owned engagement identities; it never changes period-wide workload denominators.
  private static async Task<List<Guid>> SelectEconomicsPageAsync(IClientAccountingDbContext db, Guid firmId,
    DateOnly from, DateOnly to, PracticeAnalyticsPageRequest page, CancellationToken ct)
  {
    var approvedTime = db.TimeEntries.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.Status == PracticeTimeStates.TimeApproved && x.WorkDate >= from && x.WorkDate <= to && x.EngagementId != null)
      .Select(x => x.EngagementId!.Value);
    var agreements = db.EngagementFeeAgreements.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId != null)
      .Select(x => x.EngagementId!.Value);
    var timeBilling = from line in db.InvoiceLines.AsNoTracking()
      join invoice in db.Invoices.AsNoTracking() on line.InvoiceId equals invoice.Id
      join entry in db.TimeEntries.AsNoTracking() on line.SourceId equals (Guid?)entry.Id
      where invoice.FirmId == firmId && (invoice.Status == BillingStates.InvoicePosted || invoice.Status == BillingStates.InvoiceSent) && invoice.PostedAt != null &&
        line.SourceKind == "TIME" && entry.FirmId == firmId && entry.EngagementId != null
      select entry.EngagementId!.Value;
    var feeBilling = from line in db.InvoiceLines.AsNoTracking()
      join invoice in db.Invoices.AsNoTracking() on line.InvoiceId equals invoice.Id
      join milestone in db.FeeMilestones.AsNoTracking() on line.SourceId equals (Guid?)milestone.Id
      join agreement in db.EngagementFeeAgreements.AsNoTracking() on milestone.AgreementId equals agreement.Id
      where invoice.FirmId == firmId && (invoice.Status == BillingStates.InvoicePosted || invoice.Status == BillingStates.InvoiceSent) && invoice.PostedAt != null &&
        line.SourceKind == FeeAgreementService.MilestoneSourceKind && milestone.FirmId == firmId &&
        agreement.FirmId == firmId && agreement.EngagementId != null
      select agreement.EngagementId!.Value;
    var candidates = approvedTime.Union(agreements).Union(timeBilling).Union(feeBilling);
    var query = db.Engagements.AsNoTracking().Where(x => x.FirmId == firmId && candidates.Contains(x.Id));
    if (page.AfterEngagementId is { } after)
      query = query.Where(x => x.Id.CompareTo(after) > 0);
    return await query.OrderBy(x => x.Id).Select(x => x.Id).Take(page.PageSize + 1).ToListAsync(ct);
  }

  private static Task<List<Guid>> SelectEconomicsInvoiceIdsAsync(IClientAccountingDbContext db, Guid firmId,
    IReadOnlyCollection<Guid> engagementIds, CancellationToken ct) =>
    (from line in db.InvoiceLines.AsNoTracking()
     join invoice in db.Invoices.AsNoTracking() on line.InvoiceId equals invoice.Id
     where invoice.FirmId == firmId && (invoice.Status == BillingStates.InvoicePosted || invoice.Status == BillingStates.InvoiceSent) && invoice.PostedAt != null &&
       ((line.SourceKind == "TIME" && db.TimeEntries.Any(t => t.FirmId == firmId && t.Id == line.SourceId &&
         t.EngagementId != null && engagementIds.Contains(t.EngagementId.Value))) ||
        (line.SourceKind == FeeAgreementService.MilestoneSourceKind && db.FeeMilestones.Any(m =>
          m.FirmId == firmId && m.Id == line.SourceId && db.EngagementFeeAgreements.Any(a =>
            a.FirmId == firmId && a.Id == m.AgreementId && a.EngagementId != null && engagementIds.Contains(a.EngagementId.Value)))))
     select invoice.Id).Distinct().ToListAsync(ct);
}
