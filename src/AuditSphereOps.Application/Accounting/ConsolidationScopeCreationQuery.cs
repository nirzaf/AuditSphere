using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ConsolidationScopePeriodOption(
  Guid Id, string ClientName, string Code, DateOnly StartDate, DateOnly EndDate, string Basis, string Currency, string Status);
public sealed record ConsolidationScopeMethodOption(string Code, string Label, bool Enabled, string Guidance);
public sealed record ConsolidationFxRateSetOption(Guid Id, string Code, int Version, DateOnly? EffectiveFrom, DateOnly? EffectiveTo);
public sealed record ConsolidationTranslationPolicyOption(
  Guid Id, string Code, string FunctionalCurrency, string PresentationCurrency,
  string ClosingRateType, string AverageRateType, string HistoricalRateType);
public sealed record ConsolidationPriorScopeOption(Guid Id, Guid PeriodId, int Version, string Method, string Currency, string ApprovedRun);
public sealed record ConsolidationExistingScopeOption(
  Guid Id, Guid PeriodId, string ReportingCurrency, string Method, string OpeningBasis,
  Guid? ExchangeRateSetVersionId, Guid? TranslationPolicyVersionId, DateOnly? TranslationRateDate,
  string TranslationRateType, Guid? PriorScopeVersionId, long GroupRevision, string Status);
public sealed record ConsolidationScopeCreationOptions(
  Guid GroupId, string GroupCode, string GroupName, long GroupRevision, bool CanCreateScope,
  IReadOnlyList<ConsolidationScopePeriodOption> Periods,
  IReadOnlyList<string> Currencies,
  IReadOnlyList<ConsolidationScopeMethodOption> Methods,
  IReadOnlyList<ConsolidationFxRateSetOption> ApprovedRateSets,
  IReadOnlyList<ConsolidationTranslationPolicyOption> ApprovedTranslationPolicies,
  IReadOnlyList<ConsolidationPriorScopeOption> ApprovedPriorScopes,
  IReadOnlyList<ConsolidationExistingScopeOption> ExistingScopes,
  string Guidance);

/// <summary>Returns only group-scoped periods and approved inputs needed to create a consolidation version.</summary>
public static class ConsolidationScopeCreationQuery
{
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] CreateRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<ConsolidationScopeCreationOptions>> GetAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid groupId, CancellationToken ct = default)
  {
    var access = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, groupId, ReadRoles, ct);
    if (!access.Succeeded)
      return CommandResult<ConsolidationScopeCreationOptions>.Fail(access.ErrorCode!, access.Message!);

    var group = await db.ClientGroups.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == groupId, ct);
    if (group is null)
      return CommandResult<ConsolidationScopeCreationOptions>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var create = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, groupId, CreateRoles, ct);
    var memberIds = await db.ClientGroupMemberships.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.GroupId == groupId && x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null)
      .Select(x => x.ClientId).Distinct().ToArrayAsync(ct);
    List<ConsolidationScopePeriodOption> periods = memberIds.Length == 0 ? [] : await (from period in db.ClientReportingPeriods.AsNoTracking()
      join client in db.PracticeClients.AsNoTracking() on new { period.FirmId, ClientId = period.ClientId }
        equals new { client.FirmId, ClientId = client.Id }
      where period.FirmId == actor.FirmId && memberIds.Contains(period.ClientId)
      orderby period.EndDate descending, client.LegalName, period.PeriodCode
      select new ConsolidationScopePeriodOption(period.Id, client.LegalName, period.PeriodCode,
        period.StartDate, period.EndDate, period.Basis, period.Currency, period.Status))
      .Take(500).ToListAsync(ct);

    var currencies = periods.Select(x => x.Currency.Trim().ToUpperInvariant()).Where(x => x.Length == 3)
      .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    var approvedPolicies = await db.TranslationPolicyVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.Status == AccountingWorkflowStates.Approved).OrderBy(x => x.PresentationCurrency).ThenBy(x => x.Code)
      .Select(x => new ConsolidationTranslationPolicyOption(x.Id, x.Code, x.FunctionalCurrency,
        x.PresentationCurrency, x.ClosingRateRule, x.AverageRateRule, x.HistoricalRateRule)).Take(200).ToListAsync(ct);
    var approvedRateSets = await db.ExchangeRateSetVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.Status == AccountingWorkflowStates.Approved).OrderBy(x => x.Code).ThenBy(x => x.Version)
      .Select(x => new ConsolidationFxRateSetOption(x.Id, x.Code, x.Version, x.EffectiveFrom, x.EffectiveTo))
      .Take(200).ToListAsync(ct);

    var methods = new List<ConsolidationScopeMethodOption>
    {
      new(ConsolidationCalculator.RestrictedMethod, "Fully owned · same currency", true,
        "Enabled first profile. All submitted components must remain 100% controlled and in the selected reporting currency."),
      new(ConsolidationCalculator.ForeignOperationMethod, "Controlled foreign operation translation", approvedRateSets.Count > 0 && approvedPolicies.Count > 0,
        approvedRateSets.Count > 0 && approvedPolicies.Count > 0
          ? "Requires an approved matching rate set and translation policy; profile acceptance and component translations remain separate gates."
          : "Unavailable until an approved exchange-rate set and translation policy exist.")
    };

    var scopes = await db.ConsolidationScopeVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.GroupId == groupId)
      .OrderByDescending(x => x.Version).Take(500).Select(x => new ConsolidationExistingScopeOption(
        x.Id, x.PeriodId, x.ReportingCurrency, x.Method, x.OpeningBasis, x.ExchangeRateSetVersionId,
        x.TranslationPolicyVersionId, x.TranslationRateDate, x.TranslationRateType, x.PriorScopeVersionId,
        x.GroupRevision, x.Status)).ToListAsync(ct);
    var approvedPriorScopes = await db.ConsolidationScopeVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.GroupId == groupId && x.Status == AccountingWorkflowStates.Approved &&
      db.ConsolidationRuns.Any(run => run.FirmId == actor.FirmId && run.GroupId == groupId &&
        run.ScopeVersionId == x.Id && run.Status == AccountingWorkflowStates.Approved))
      .OrderByDescending(x => x.Version).Take(100).Select(x => new ConsolidationPriorScopeOption(
        x.Id, x.PeriodId, x.Version, x.Method, x.ReportingCurrency, "Approved run available"))
      .ToListAsync(ct);

    return CommandResult<ConsolidationScopeCreationOptions>.Ok(new ConsolidationScopeCreationOptions(
      group.Id, group.Code, group.Name, group.Revision, create.Succeeded, periods, currencies, methods,
      approvedRateSets, approvedPolicies, approvedPriorScopes, scopes,
      memberIds.Length == 0
        ? "An approved, effective group membership is required before a scope can be created."
        : "Options are limited to this explicitly granted group. Advanced methods remain unavailable until their separate owner-acceptance and execution gates are satisfied."));
  }
}
