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
public sealed record FinancialPackageComparativeOption(Guid Id, Guid EngagementId, string Framework, string PeriodStart, string PeriodEnd, string Currency, string? Basis, string Status);

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
    // Authorize each candidate engagement before exposing its package identity. A client match alone
    // must not enumerate a sibling engagement's financial packages.
    var candidateEngagementIds = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
        x.Status == AccountingPackageStates.PackageValidated)
      .GroupBy(x => x.EngagementId)
      .Select(x => new { EngagementId = x.Key, LatestPeriodEnd = x.Max(p => p.PeriodEnd) })
      .OrderByDescending(x => x.LatestPeriodEnd).ThenBy(x => x.EngagementId).Take(MaxOptions)
      .Select(x => x.EngagementId).ToListAsync(ct);
    var authorizedEngagements = new HashSet<Guid> { engagement.Id };
    foreach (var candidateEngagementId in candidateEngagementIds.Where(x => x != engagement.Id))
    {
      var candidateAuth = await FinancialStatementService.AuthorizeAsync(db, actor, actor.FirmId,
        engagement.PracticeClientId, candidateEngagementId, FinancialStatementService.PackageReadRoles, ct);
      if (candidateAuth.Succeeded)
        authorizedEngagements.Add(candidateEngagementId);
    }
    var comparatives = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId &&
        authorizedEngagements.Contains(x.EngagementId) && x.Status == AccountingPackageStates.PackageValidated)
      .OrderByDescending(x => x.PeriodEnd).ThenBy(x => x.Id).Take(MaxOptions)
      .Select(x => new FinancialPackageComparativeOption(x.Id, x.EngagementId, x.Framework,
        x.PeriodStart, x.PeriodEnd, x.Currency, x.Basis, x.Status)).ToListAsync(ct);
    var final = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, Roles, InternalOnly: true), ct);
    return final.Succeeded
      ? CommandResult<FinancialPackageBuildInputs>.Ok(new(engagement.Id, mappings, plans, periods, comparatives))
      : CommandResult<FinancialPackageBuildInputs>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  }
}
