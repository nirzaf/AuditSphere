using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Completion;

public sealed record OpinionFsliOption(Guid Id, string Code, string Name);

public static partial class AuditDeliverableService
{
  public static async Task<IReadOnlyList<OpinionFsliOption>> AffectedFinancialStatementAreasAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid engagementId, CancellationToken ct = default)
  {
    if (!(await AuthorizeAsync(db, actor, engagementId, ReaderRoles, ct)).Succeeded) return [];
    if (db is not IClientAccountingDbContext accounting) return [];
    return await accounting.ReportingTaxonomyNodes.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.IsPosting &&
      accounting.ReportingTaxonomyVersions.Any(v => v.Id == x.TaxonomyVersionId && v.FirmId == actor.FirmId && v.Status == AccountingWorkflowStates.Approved))
      .OrderBy(x => x.Code).ThenBy(x => x.Id).Take(500).Select(x => new OpinionFsliOption(x.Id, x.Code, x.Name)).ToListAsync(ct);
  }
}
