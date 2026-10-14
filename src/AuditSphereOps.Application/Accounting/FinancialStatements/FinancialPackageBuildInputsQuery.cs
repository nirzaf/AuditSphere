using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record FinancialPackageMappingOption(Guid Id, long Version, Guid DatasetId);
public sealed record FinancialPackagePlanOption(Guid Id, Guid BaseDatasetId, string ResultHash);
public sealed record FinancialPackagePeriodOption(Guid Id, string PeriodCode, DateOnly StartDate, DateOnly EndDate, string Currency, string Basis);
public sealed record FinancialPackageComparativeOption(Guid Id, string Framework, string PeriodStart, string PeriodEnd, string Currency, string Status);

/// <summary>
/// The choices a preparer can make for a financial-package build: the approved mappings, the finalized adjustment plans and the
/// client's reporting periods. It reads only; the build itself is still checked by the durable operation and its build service.
/// </summary>
public sealed record FinancialPackageBuildInputs(Guid EngagementId, IReadOnlyList<FinancialPackageMappingOption> Mappings,
  IReadOnlyList<FinancialPackagePlanOption> Plans, IReadOnlyList<FinancialPackagePeriodOption> Periods,
  IReadOnlyList<FinancialPackageComparativeOption> Comparatives);

public static class FinancialPackageBuildInputsQuery
{
  public const int MaxOptions = 100;
  private static readonly string[] Roles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<FinancialPackageBuildInputs>> GetAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null)
      return CommandResult<FinancialPackageBuildInputs>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, Roles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<FinancialPackageBuildInputs>.Fail(auth.ErrorCode!, auth.Message!);
    var mappings = await db.MappingVersions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagement.Id && x.Status == AccountingPackageStates.MappingApproved)
      .OrderByDescending(x => x.Version).ThenBy(x => x.Id).Take(MaxOptions)
      .Select(x => new FinancialPackageMappingOption(x.Id, x.Version, x.DatasetId)).ToListAsync(ct);
    var plans = await db.AdjustmentPlans.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagement.Id && x.Status == "Finalized" && x.ResultHash != null)
      .OrderBy(x => x.Id).Take(MaxOptions)
      .Select(x => new FinancialPackagePlanOption(x.Id, x.BaseDatasetId, x.ResultHash!)).ToListAsync(ct);
    var periods = await db.ClientReportingPeriods.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId)
      .OrderByDescending(x => x.EndDate).ThenBy(x => x.Id).Take(MaxOptions)
      .Select(x => new FinancialPackagePeriodOption(x.Id, x.PeriodCode, x.StartDate, x.EndDate, x.Currency, x.Basis)).ToListAsync(ct);
    var comparatives = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
        x.EngagementId == engagement.Id && x.Status == AccountingPackageStates.PackageValidated)
      .OrderByDescending(x => x.PeriodEnd).ThenBy(x => x.Id).Take(MaxOptions)
      .Select(x => new FinancialPackageComparativeOption(x.Id, x.Framework, x.PeriodStart, x.PeriodEnd, x.Currency, x.Status))
      .ToListAsync(ct);
    var final = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, Roles, InternalOnly: true), ct);
    return final.Succeeded
      ? CommandResult<FinancialPackageBuildInputs>.Ok(new(engagement.Id, mappings, plans, periods, comparatives))
      : CommandResult<FinancialPackageBuildInputs>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  }
}
