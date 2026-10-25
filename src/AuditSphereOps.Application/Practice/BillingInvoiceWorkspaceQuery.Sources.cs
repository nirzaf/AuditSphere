using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>Exact invoice-bound source evidence; excludes staff narratives and unrelated source records.</summary>
public sealed record BillingInvoiceLineSource(Guid LineId, string Kind, string State, Guid? SourceId,
  long? SourceRevision, Guid? FeeAgreementId, DateOnly? WorkDate, int? DurationMinutes, string Message);

public static partial class BillingInvoiceWorkspaceQuery
{
  private static async Task<IReadOnlyList<BillingInvoiceLineSource>> ReadLineSourcesAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, string currency,
    IReadOnlyList<InvoiceLine> lines, CancellationToken ct)
  {
    var ids = lines.Select(x => x.Id).ToArray();
    var sourceIds = lines.Where(x => x.SourceId.HasValue).Select(x => x.SourceId!.Value).Distinct().ToArray();
    var bindings = await db.BillingSourceAllocations.AsNoTracking()
      .Where(x => x.FirmId == firmId && ids.Contains(x.InvoiceLineId)).ToListAsync(ct);
    var milestones = await (from milestone in db.FeeMilestones.AsNoTracking()
      join agreement in db.EngagementFeeAgreements.AsNoTracking()
        on new { milestone.FirmId, milestone.AgreementId } equals new { agreement.FirmId, AgreementId = agreement.Id }
      where milestone.FirmId == firmId && sourceIds.Contains(milestone.Id) &&
        agreement.PracticeClientId == clientId && agreement.Currency == currency
      select new { milestone.Id, milestone.Amount, milestone.Kind, AgreementId = agreement.Id }).ToListAsync(ct);
    var time = await (from entry in db.TimeEntries.AsNoTracking()
      join task in db.WorkTasks.AsNoTracking()
        on new { entry.FirmId, entry.TaskId } equals new { task.FirmId, TaskId = task.Id }
      where entry.FirmId == firmId && sourceIds.Contains(entry.Id) && entry.ClientId == clientId &&
        task.ClientId == clientId && entry.EngagementId == task.EngagementId
      select new { entry.Id, entry.Revision, entry.WorkDate, entry.DurationMinutes }).ToListAsync(ct);
    return lines.Select(line =>
    {
      if (string.IsNullOrEmpty(line.SourceKind) && line.SourceId is null && line.SourceRevision is null)
        return new BillingInvoiceLineSource(line.Id, "MANUAL", "MANUAL", null, null, null, null, null,
          "Manually described invoice line; no source record was bound.");
      var bound = bindings.Where(x => x.InvoiceLineId == line.Id).ToArray();
      var exact = bound.Length == 1 && bound[0].SourceKind == line.SourceKind && bound[0].SourceId == line.SourceId &&
        bound[0].SourceRevision == line.SourceRevision && bound[0].Quantity == line.Quantity && bound[0].Amount == line.LineTotal;
      if (exact && line.SourceKind == FeeAgreementService.MilestoneSourceKind && line.SourceRevision == 1)
      {
        var source = milestones.SingleOrDefault(x => x.Id == line.SourceId);
        if (source is not null && source.Amount == line.LineTotal)
          return new BillingInvoiceLineSource(line.Id, line.SourceKind, "AVAILABLE", line.SourceId, line.SourceRevision,
            source.AgreementId, null, null, source.Kind + " fee milestone. The settlement workspace rechecks current finance access.");
      }
      if (exact && line.SourceKind == "TIME")
      {
        var source = time.SingleOrDefault(x => x.Id == line.SourceId && x.Revision == line.SourceRevision);
        if (source is not null)
          return new BillingInvoiceLineSource(line.Id, "TIME", "AVAILABLE", line.SourceId, line.SourceRevision,
            null, source.WorkDate, source.DurationMinutes, "Retained invoice-bound time source. Staff narrative and private rates are excluded.");
      }
      return new BillingInvoiceLineSource(line.Id, "SOURCE", "UNAVAILABLE", null, null, null, null, null,
        "Exact source binding is unavailable or requires reconciliation; no destination was inferred.");
    }).ToArray();
  }
}
