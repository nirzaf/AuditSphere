using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record MaintenanceClient(Guid Id, string Name);
public sealed record MaintenancePeriodRow(Guid Id, Guid ClientId, string ClientName, string PeriodCode, DateOnly StartDate, DateOnly EndDate, string Status, long Revision, string PriorPeriodCode);
public sealed record MaintenanceRestatementRow(Guid Id, Guid ClientId, string ClientName, string PeriodCode, string RevisedBasis, string Status, string EvidenceReference, bool CanReview);
public sealed record PeriodMaintenanceOverview(IReadOnlyList<MaintenanceClient> Clients, IReadOnlyList<MaintenancePeriodRow> Periods, IReadOnlyList<MaintenanceRestatementRow> Restatements);
public sealed record MaintenancePackageOption(Guid Id, string Currency, string HashPrefix);
public sealed record MaintenanceClosedPeriod(Guid Id, string PeriodCode, DateOnly StartDate, DateOnly EndDate, string Basis, string Currency, long Revision,
  IReadOnlyList<MaintenancePackageOption> Packages);

/// <summary>
/// Client-level period maintenance (roll-forward and restatement) projections. Only firm-wide or direct-client accounting
/// grants qualify; engagement-only grants never reach client period maintenance.
/// </summary>
public static class PeriodMaintenanceQuery
{
  private static readonly string[] AccountingRoles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];
  private static readonly string[] ReviewerRoles = ["Administrator", "Partner", "Manager", "AccountingReviewer"];

  public static async Task<CommandResult<PeriodMaintenanceOverview>> OverviewAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: AccountingRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<PeriodMaintenanceOverview>.Fail(ErrorCodes.ScopeDenied, "Sign in with an authorized accounting identity.");
    var grants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null && AccountingRoles.Contains(x.Role))
      .Select(x => new { x.Role, x.ClientId, x.EngagementId }).ToListAsync(ct);
    var firmWide = grants.Any(x => x.ClientId is null && x.EngagementId is null);
    var clientIds = grants.Where(x => x.ClientId.HasValue && x.EngagementId is null).Select(x => x.ClientId!.Value).Distinct().ToArray();
    if (!firmWide && clientIds.Length == 0) return CommandResult<PeriodMaintenanceOverview>.Fail(ErrorCodes.ScopeDenied, "Period maintenance requires a firm-wide or client accounting assignment.");
    var reviewFirmWide = grants.Any(x => ReviewerRoles.Contains(x.Role) && x.ClientId is null && x.EngagementId is null);
    var reviewClients = grants.Where(x => ReviewerRoles.Contains(x.Role) && x.ClientId.HasValue && x.EngagementId is null).Select(x => x.ClientId!.Value).ToHashSet();
    var clients = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == actor.FirmId && (firmWide || clientIds.Contains(x.Id))).OrderBy(x => x.LegalName)
      .Select(x => new MaintenanceClient(x.Id, x.LegalName)).ToListAsync(ct);
    var names = clients.ToDictionary(x => x.Id, x => x.Name);
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && (firmWide || clientIds.Contains(x.ClientId)))
      .OrderByDescending(x => x.EndDate).ToListAsync(ct);
    var byId = periods.ToDictionary(x => x.Id);
    var restatements = await db.ClientPeriodRestatements.AsNoTracking().Where(x => x.FirmId == actor.FirmId && (firmWide || clientIds.Contains(x.ClientId)))
      .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    return CommandResult<PeriodMaintenanceOverview>.Ok(new(clients,
      periods.Select(x => new MaintenancePeriodRow(x.Id, x.ClientId, names.GetValueOrDefault(x.ClientId, "Scoped client"), x.PeriodCode, x.StartDate, x.EndDate, x.Status,
        x.Revision, x.PriorPeriodId is { } p && byId.TryGetValue(p, out var prior) ? prior.PeriodCode : "—")).ToList(),
      restatements.Select(x => new MaintenanceRestatementRow(x.Id, x.ClientId, names.GetValueOrDefault(x.ClientId, "Scoped client"),
        byId.GetValueOrDefault(x.PeriodId)?.PeriodCode ?? "Unknown period", x.RevisedBasis, x.Status, x.EvidenceReference,
        x.Status == AccountingWorkflowStates.Submitted && (reviewFirmWide || reviewClients.Contains(x.ClientId)))).ToList()));
  }

  /// <summary>Closed periods of one client with the validated packages that exactly match each period's dates.</summary>
  public static async Task<CommandResult<IReadOnlyList<MaintenanceClosedPeriod>>> ClosedPeriodsAsync(IClientAccountingDbContext db, ActorContext actor, Guid clientId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: AccountingRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<MaintenanceClosedPeriod>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Status == AccountingWorkflowStates.Closed)
      .OrderByDescending(x => x.EndDate).ToListAsync(ct);
    var packages = await db.FinancialPackages.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Status == AccountingPackageStates.PackageValidated)
      .OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.Currency, x.CalculationHash, x.PeriodStart, x.PeriodEnd }).ToListAsync(ct);
    return CommandResult<IReadOnlyList<MaintenanceClosedPeriod>>.Ok(periods.Select(p =>
    {
      var start = p.StartDate.ToString("yyyy-MM-dd"); var end = p.EndDate.ToString("yyyy-MM-dd");
      return new MaintenanceClosedPeriod(p.Id, p.PeriodCode, p.StartDate, p.EndDate, p.Basis, p.Currency, p.Revision,
        packages.Where(x => x.PeriodStart == start && x.PeriodEnd == end)
          .Select(x => new MaintenancePackageOption(x.Id, x.Currency, x.CalculationHash[..Math.Min(12, x.CalculationHash.Length)])).ToList());
    }).ToList());
  }
}
