using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

// Read-side contracts for consolidation workspaces. These are group-authorized,
// paged projections over immutable records only; they never mutate state, never
// expose raw component package contents, and never assert approval of a stale run.
public sealed record TranslationLineDto(
  Guid TranslationId, Guid ComponentId, string CalculationVersion, string Status,
  DateOnly? RateDate, string RateType, decimal? AppliedRate,
  string SourceCurrency, string PresentationCurrency,
  decimal TranslatedAmount, decimal? SourceAmount,
  decimal ForeignExchangeAdjustment, decimal RoundingAdjustment, decimal TranslationReserve);

public sealed record TranslationBridgePage(
  IReadOnlyList<TranslationLineDto> Items, int TotalCount, int Page, int PageSize,
  decimal TotalTranslatedAmount, decimal TotalTranslationReserve);

public sealed record IntercompanyExceptionRow(
  Guid MatchId, Guid SellerClientId, Guid BuyerClientId, string AccountNature, string MatchMode,
  string PeriodCode, string Currency, string TransactionReference,
  decimal SellerAmount, decimal BuyerAmount, decimal MatchedAmount, decimal Difference,
  string DifferenceReason, string Status, bool OutsidePerimeterReview, string EvidenceReference);

public sealed record IntercompanyExceptionsPage(
  IReadOnlyList<IntercompanyExceptionRow> Items, int TotalCount, int Page, int PageSize,
  decimal TotalUnmatchedDifference);

public sealed record ComponentReadinessRow(
  Guid ClientId, string MemberName, string ComponentState, Guid? ComponentId,
  string Currency, string PeriodBasis, string TaxonomyVersion, string MappingVersion,
  bool CurrencyCompatible, string? MismatchReason);

public sealed record ComponentReadinessReport(
  Guid GroupId, Guid ScopeVersionId, string ReportingCurrency, string Method, string ScopeStatus,
  IReadOnlyList<ComponentReadinessRow> Members);

public static class ConsolidationQuery
{
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  /// <summary>Paged rate/rate-type/reserve lineage for the translations of one scope,
  /// per the translation-bridge contract. SourceAmount is recovered from the stored
  /// translated total and pinned rate and is informational; the persisted
  /// TranslatedAmount stays the authoritative figure.</summary>
  public static async Task<CommandResult<TranslationBridgePage>> GetTranslationBridgeAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId,
    int page = 1, int pageSize = 100, CancellationToken ct = default)
  {
    if (page < 1 || pageSize is < 1 or > 500)
      return CommandResult<TranslationBridgePage>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A valid page number and size (1-500) are required.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<TranslationBridgePage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, ReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<TranslationBridgePage>.Fail(auth.ErrorCode!, auth.Message!);

    var rows = db.TranslationResults.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id);
    var totalCount = await rows.CountAsync(ct);
    var totalTranslated = totalCount > 0 ? await rows.SumAsync(x => x.TranslatedAmount, ct) : 0m;
    var totalReserve = totalCount > 0 ? await rows.SumAsync(x => x.TranslationReserve, ct) : 0m;
    var items = await rows
      .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
      .Skip((page - 1) * pageSize).Take(pageSize)
      .Select(x => new TranslationLineDto(
        x.Id, x.ComponentId, x.CalculationVersion, x.Status,
        x.RateDate, x.RateType, x.AppliedRate,
        x.FromCurrency, x.ToCurrency,
        x.TranslatedAmount, x.AppliedRate == null || x.AppliedRate == 0m ? null : MoneyPolicy.Normalize(x.TranslatedAmount / x.AppliedRate.Value),
        x.ForeignExchangeAdjustment, x.RoundingAdjustment, x.TranslationReserve))
      .ToListAsync(ct);

    return CommandResult<TranslationBridgePage>.Ok(new TranslationBridgePage(
      items, totalCount, page, pageSize,
      MoneyPolicy.Normalize(totalTranslated), MoneyPolicy.Normalize(totalReserve)));
  }

  /// <summary>Paged intercompany matches for one scope with their unmatched
  /// amounts and required explanations. Currency and timing differences are shown,
  /// never offset or plugged.</summary>
  public static async Task<CommandResult<IntercompanyExceptionsPage>> GetIntercompanyExceptionsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId,
    bool unresolvedOnly = true, int page = 1, int pageSize = 100, CancellationToken ct = default)
  {
    if (page < 1 || pageSize is < 1 or > 500)
      return CommandResult<IntercompanyExceptionsPage>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A valid page number and size (1-500) are required.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<IntercompanyExceptionsPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, ReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<IntercompanyExceptionsPage>.Fail(auth.ErrorCode!, auth.Message!);

    var rows = db.IntercompanyMatches.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id);
    if (unresolvedOnly)
      rows = rows.Where(x => x.Status != AccountingWorkflowStates.Approved ||
        (x.Difference != 0m && string.IsNullOrEmpty(x.DifferenceReason)));
    var totalCount = await rows.CountAsync(ct);
    var totalDifference = totalCount > 0 ? await rows.SumAsync(x => x.Difference, ct) : 0m;
    var items = await rows
      .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
      .Skip((page - 1) * pageSize).Take(pageSize)
      .Select(x => new IntercompanyExceptionRow(
        x.Id, x.SellerClientId, x.BuyerClientId, x.AccountNature, x.MatchMode,
        x.PeriodCode, x.Currency, x.TransactionReference,
        x.SellerAmount, x.BuyerAmount, x.MatchedAmount, x.Difference,
        x.DifferenceReason, x.Status, x.OutsidePerimeterReview, x.EvidenceReference))
      .ToListAsync(ct);

    return CommandResult<IntercompanyExceptionsPage>.Ok(new IntercompanyExceptionsPage(
      items, totalCount, page, pageSize, MoneyPolicy.Normalize(totalDifference)));
  }

  /// <summary>Per-member component readiness for one scope: required approved
  /// members, their pinned component state and explicit compatibility mismatches.
  /// Reports missing inputs without exposing unauthorized package content.</summary>
  public static async Task<CommandResult<ComponentReadinessReport>> GetComponentReadinessAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId,
    CancellationToken ct = default)
  {
    var scope = await db.ConsolidationScopeVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<ComponentReadinessReport>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, ReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ComponentReadinessReport>.Fail(auth.ErrorCode!, auth.Message!);

    var members = await db.ClientGroupMemberships.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
        x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null)
      .OrderBy(x => x.ClientId)
      .ToListAsync(ct);
    var components = await db.ConsolidationComponents.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id)
      .ToListAsync(ct);
    var memberNames = await db.PracticeClients.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && members.Select(m => m.ClientId).Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);

    var memberRows = new List<ComponentReadinessRow>(members.Count);
    foreach (var member in members)
    {
      var component = components.FirstOrDefault(x => x.ClientId == member.ClientId);
      string? mismatch = null;
      var state = component is null ? "NONE" : component.Status;
      if (component is null)
        mismatch = "No component has been pinned for this required member.";
      else if (component.Status != AccountingWorkflowStates.Approved)
        mismatch = "The pinned component is not independently approved.";
      else if (!string.Equals(component.Currency, scope.ReportingCurrency, StringComparison.Ordinal) &&
        scope.Method == ConsolidationCalculator.RestrictedMethod)
        mismatch = "The restricted profile accepts only same-currency component packages.";

      memberRows.Add(new ComponentReadinessRow(
        member.ClientId, memberNames.GetValueOrDefault(member.ClientId, "Scoped member"),
        state, component?.Id, component?.Currency ?? string.Empty,
        component?.PeriodBasis ?? string.Empty, component?.TaxonomyVersion ?? string.Empty,
        component?.MappingVersion ?? string.Empty,
        CurrencyCompatible: mismatch is null,
        mismatch));
    }

    return CommandResult<ComponentReadinessReport>.Ok(new ComponentReadinessReport(
      scope.GroupId, scope.Id, scope.ReportingCurrency, scope.Method, scope.Status, memberRows));
  }
}
