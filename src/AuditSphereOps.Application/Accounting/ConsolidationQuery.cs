using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ConsolidationPage<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public sealed record ConsolidationReadResult<T>(bool Available, T? Value, string? ErrorCode, string? Message);

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
  decimal SellerAmount, decimal BuyerAmount, decimal MatchedAmount, decimal Difference, decimal UnmatchedResidual,
  string DifferenceReason, string Status, bool OutsidePerimeterReview, string EvidenceReference,
  string SellerTaxonomyCode, string BuyerTaxonomyCode, string MatchGroupReference,
  Guid CreatedByUserId, DateTimeOffset CreatedAt, Guid? ReviewedByUserId, DateTimeOffset? ReviewedAt);

public sealed record IntercompanyExceptionsPage(
  Guid ScopeVersionId, Guid GroupId, long PinnedGroupRevision, long CurrentGroupRevision,
  IReadOnlyList<IntercompanyExceptionRow> Items, int TotalCount, int Page, int PageSize,
  decimal TotalUnmatchedDifference, decimal TotalGrossUnmatchedExposure);

public sealed record IntercompanyMatchMemberOption(Guid ClientId, string LegalName, bool HasApprovedComponent);

public sealed record IntercompanyMatchOptions(
  Guid ScopeVersionId, Guid GroupId, long PinnedGroupRevision, long CurrentGroupRevision,
  string ReportingCurrency, DateOnly? PeriodStartDate, DateOnly? PeriodEndDate, string? MembershipBlocker,
  IReadOnlyList<IntercompanyMatchMemberOption> Members, bool MembersTruncated,
  IReadOnlyList<string> TaxonomyCodes, bool TaxonomyCodesTruncated);

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
    var pageOffset = ((long)page - 1) * pageSize;
    if (page < 1 || pageSize is < 1 or > 500 || pageOffset > int.MaxValue)
      return CommandResult<IntercompanyExceptionsPage>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A valid page number and size (1-500) are required.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<IntercompanyExceptionsPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, ReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<IntercompanyExceptionsPage>.Fail(auth.ErrorCode!, auth.Message!);
    var currentGroupRevision = await db.ClientGroups.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == scope.GroupId)
      .Select(x => (long?)x.Revision).SingleOrDefaultAsync(ct);
    if (currentGroupRevision is null)
      return CommandResult<IntercompanyExceptionsPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var rows = db.IntercompanyMatches.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id);
    if (unresolvedOnly)
      rows = rows.Where(x => x.Status != AccountingWorkflowStates.Approved ||
        ((x.Difference != 0m || Math.Abs(x.SellerAmount) > x.MatchedAmount || Math.Abs(x.BuyerAmount) > x.MatchedAmount) &&
          x.DifferenceReason.Trim() == string.Empty));
    var totalCount = await rows.CountAsync(ct);
    var totalDifference = totalCount > 0 ? await rows.SumAsync(x => x.Difference, ct) : 0m;
    var totalGrossUnmatchedExposure = totalCount > 0 ? await rows.SumAsync(x =>
      (Math.Abs(x.SellerAmount) > x.MatchedAmount ? Math.Abs(x.SellerAmount) - x.MatchedAmount : 0m) +
      (Math.Abs(x.BuyerAmount) > x.MatchedAmount ? Math.Abs(x.BuyerAmount) - x.MatchedAmount : 0m), ct) : 0m;
    var items = await rows
      .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
      .Skip((int)pageOffset).Take(pageSize)
      .Select(x => new IntercompanyExceptionRow(
        x.Id, x.SellerClientId, x.BuyerClientId, x.AccountNature, x.MatchMode,
        x.PeriodCode, x.Currency, x.TransactionReference,
        x.SellerAmount, x.BuyerAmount, x.MatchedAmount, x.Difference,
        (Math.Abs(x.SellerAmount) > x.MatchedAmount ? Math.Abs(x.SellerAmount) - x.MatchedAmount : 0m) +
        (Math.Abs(x.BuyerAmount) > x.MatchedAmount ? Math.Abs(x.BuyerAmount) - x.MatchedAmount : 0m),
        x.DifferenceReason, x.Status, x.OutsidePerimeterReview, x.EvidenceReference,
        x.SellerTaxonomyCode, x.BuyerTaxonomyCode, x.MatchGroupReference,
        x.CreatedByUserId, x.CreatedAt, x.ReviewedByUserId, x.ReviewedAt))
      .ToListAsync(ct);

    return CommandResult<IntercompanyExceptionsPage>.Ok(new IntercompanyExceptionsPage(
      scope.Id, scope.GroupId, scope.GroupRevision, currentGroupRevision.Value,
      items, totalCount, page, pageSize, MoneyPolicy.Normalize(totalDifference),
      MoneyPolicy.Normalize(totalGrossUnmatchedExposure)));
  }

  /// <summary>Bounded member and source-taxonomy options for intercompany matching.
  /// Membership is included when its effective interval overlaps the scope reporting period;
  /// eligibility is never inferred from today's date.</summary>
  public static async Task<CommandResult<IntercompanyMatchOptions>> GetIntercompanyMatchOptionsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId,
    CancellationToken ct = default)
  {
    var scope = await db.ConsolidationScopeVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<IntercompanyMatchOptions>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, ReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<IntercompanyMatchOptions>.Fail(auth.ErrorCode!, auth.Message!);
    var currentGroupRevision = await db.ClientGroups.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == scope.GroupId)
      .Select(x => (long?)x.Revision).SingleOrDefaultAsync(ct);
    if (currentGroupRevision is null)
      return CommandResult<IntercompanyMatchOptions>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var period = await db.ClientReportingPeriods.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == scope.PeriodId)
      .Select(x => new { x.StartDate, x.EndDate })
      .SingleOrDefaultAsync(ct);
    var hasValidPeriod = period is not null && period.StartDate != default && period.EndDate != default &&
      period.StartDate <= period.EndDate;
    var periodStart = hasValidPeriod ? period!.StartDate : DateOnly.MinValue;
    var periodEnd = hasValidPeriod ? period!.EndDate : DateOnly.MinValue;
    var periodStartDate = hasValidPeriod ? periodStart : (DateOnly?)null;
    var periodEndDate = hasValidPeriod ? periodEnd : (DateOnly?)null;
    var membershipBlocker = !hasValidPeriod
      ? "The scope reporting period date is missing or invalid; member eligibility cannot be confirmed."
      : null;
    List<Guid> memberIds = hasValidPeriod
      ? await db.ClientGroupMemberships.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
          x.Status == AccountingWorkflowStates.Approved && x.EffectiveFrom <= periodEnd &&
          (x.EffectiveTo == null || x.EffectiveTo >= periodStart))
        .OrderBy(x => x.ClientId).Select(x => x.ClientId).Take(501).ToListAsync(ct)
      : [];
    var memberIdsShown = memberIds.Take(500).ToArray();
    var components = await db.ConsolidationComponents.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id &&
        x.Status == AccountingWorkflowStates.Approved)
      .OrderBy(x => x.ClientId)
      .Select(x => new { x.ClientId, x.PackageId, x.ExternalComponentPackId })
      .Take(501).ToListAsync(ct);
    // Membership options must not infer a missing component from a separately
    // bounded taxonomy sample. Read exact existence for the displayed members.
    var approvedComponentClientIds = await db.ConsolidationComponents.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id &&
        x.Status == AccountingWorkflowStates.Approved && memberIdsShown.Contains(x.ClientId))
      .Select(x => x.ClientId).ToHashSetAsync(ct);
    var packageIds = components.Where(x => x.PackageId.HasValue).Select(x => x.PackageId!.Value).Distinct().ToArray();
    var externalPackIds = components.Where(x => x.ExternalComponentPackId.HasValue)
      .Select(x => x.ExternalComponentPackId!.Value).Distinct().ToArray();

    List<string> packageTaxonomyCodes = packageIds.Length == 0 ? [] : await db.FinancialPackageLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.FinancialPackageId) && x.DestinationCode != string.Empty)
      .Select(x => x.DestinationCode).Distinct().OrderBy(x => x).Take(501).ToListAsync(ct);
    List<string> externalTaxonomyCodes = externalPackIds.Length == 0 ? [] : await db.ExternalComponentPackLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id &&
        externalPackIds.Contains(x.ExternalComponentPackId) && x.TaxonomyCode != string.Empty)
      .Select(x => x.TaxonomyCode).Distinct().OrderBy(x => x).Take(501).ToListAsync(ct);
    var allTaxonomyCodes = packageTaxonomyCodes.Concat(externalTaxonomyCodes)
      .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).Take(501).ToList();
    var legalNames = await db.PracticeClients.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && memberIdsShown.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);
    var members = memberIdsShown.Select(clientId => new IntercompanyMatchMemberOption(
      clientId, legalNames.GetValueOrDefault(clientId, "Unknown entity"), approvedComponentClientIds.Contains(clientId))).ToArray();

    return CommandResult<IntercompanyMatchOptions>.Ok(new IntercompanyMatchOptions(
      scope.Id, scope.GroupId, scope.GroupRevision, currentGroupRevision.Value, scope.ReportingCurrency,
      periodStartDate, periodEndDate, membershipBlocker, members, memberIds.Count > 500,
      allTaxonomyCodes.Take(500).ToArray(), memberIds.Count > 500 || components.Count > 500 ||
        packageTaxonomyCodes.Count > 500 || externalTaxonomyCodes.Count > 500 || allTaxonomyCodes.Count > 500));
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
