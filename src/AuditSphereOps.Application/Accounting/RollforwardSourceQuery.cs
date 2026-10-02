using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record RollforwardPackageItem(Guid Id, string Hash, string Currency);
public sealed record RollforwardSources(Guid PeriodId, string Revision, IReadOnlyList<RollforwardPackageItem> Items, bool HasMore);
public static class RollforwardSourceQuery
{
  public static async Task<CommandResult<RollforwardSources>> GetAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid periodId, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, clientId,
      RequiredRoles: ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"], InternalOnly: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<RollforwardSources>.Fail(ErrorCodes.ScopeDenied, "Source packages unavailable.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == periodId, ct);
    if (period is null || period.Status != AccountingWorkflowStates.Closed)
      return CommandResult<RollforwardSources>.Fail(ErrorCodes.ScopeDenied, "Source packages unavailable.");
    var start = period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var end = period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var packages = await db.FinancialPackages.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
      x.PeriodStart == start && x.PeriodEnd == end && x.Currency == period.Currency && x.Status == AccountingPackageStates.PackageValidated)
      .OrderBy(x => x.Id).Take(101).Select(x => new RollforwardPackageItem(x.Id, x.CalculationHash, x.Currency)).ToListAsync(ct);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<RollforwardSources>.Fail(ErrorCodes.ScopeDenied, "Source packages unavailable.");
    return CommandResult<RollforwardSources>.Ok(new(period.Id, period.Revision.ToString(CultureInfo.InvariantCulture), packages.Take(100).ToArray(), packages.Count > 100));
  }
}
