using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ConsolidationScopeSummaryDto(
  Guid Id,
  Guid GroupId,
  string GroupName,
  string GroupCode,
  int Version,
  long GroupRevision,
  Guid PeriodId,
  string ReportingCurrency,
  string Method,
  string Status,
  string OpeningBasis,
  bool IsAdvanced);

public sealed record ConsolidationComponentDto(
  Guid Id,
  Guid ClientId,
  string ClientLegalName,
  string SourceType,
  Guid? PackageId,
  Guid? ExternalComponentPackId,
  string PackageHash,
  string PeriodBasis,
  string TaxonomyVersion,
  string MappingVersion,
  string Currency,
  decimal OwnershipPercent,
  string ControlMethod,
  string Status,
  Guid SubmittedByUserId,
  DateTimeOffset SubmittedAt,
  Guid? ApprovedByUserId,
  DateTimeOffset? ApprovedAt);

public sealed record EligibleComponentPackageDto(
  Guid PackageId,
  Guid ClientId,
  string ClientLegalName,
  Guid EngagementId,
  string PeriodBasis,
  string TaxonomyVersion,
  string MappingVersion,
  string Currency,
  string CalculationHash);

public sealed record ConsolidationJournalLineDto(
  Guid Id,
  string TaxonomyCode,
  decimal Debit,
  decimal Credit,
  string Description,
  Guid? IntercompanyMatchId);

public sealed record ConsolidationJournalDto(
  Guid Id,
  string JournalNumber,
  string JournalType,
  string Currency,
  decimal TotalDebits,
  decimal TotalCreditsAbs,
  string EvidenceReference,
  string Status,
  string? ReturnReason,
  Guid CreatedByUserId,
  DateTimeOffset CreatedAt,
  Guid? ApprovedByUserId,
  DateTimeOffset? ApprovedAt,
  IReadOnlyList<ConsolidationJournalLineDto> Lines);

public sealed record ConsolidationRunSummaryDto(
  Guid Id,
  string EngineVersion,
  string RunHash,
  decimal SignedTotal,
  string Status,
  Guid CreatedByUserId,
  DateTimeOffset CreatedAt,
  Guid? ApprovedByUserId,
  DateTimeOffset? ApprovedAt);

public sealed record ConsolidationScopeWorkspaceDto(
  ConsolidationScopeSummaryDto Scope,
  bool CanPrepare,
  bool CanReview,
  Guid CurrentUserId,
  IReadOnlyList<ConsolidationComponentDto> Components,
  ConsolidationPage<EligibleComponentPackageDto> EligiblePackages,
  ConsolidationPage<ConsolidationJournalDto> Journals,
  ConsolidationPage<ConsolidationRunSummaryDto> Runs,
  ConsolidationReadResult<ConsolidationReport> LatestReport,
  ConsolidationReadResult<IReadOnlyList<ComponentReadinessRow>> ComponentReadiness,
  ConsolidationReadResult<IntercompanyExceptionsPage> IntercompanyExceptions);

/// <summary>
/// Authoritative detailed read model for a single consolidation perimeter version:
/// summary, components, eligible financial packages, elimination journals, run history,
/// member readiness, and latest approved report.
/// </summary>
public static class ConsolidationScopeWorkspaceQuery
{
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReviewRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private const int HistoryPageSize = 100;

  public static async Task<CommandResult<ConsolidationScopeWorkspaceDto>> GetScopeWorkspaceAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeId, CancellationToken ct = default)
    => await GetScopeWorkspaceAsync(db, actor, scopeId, 1, 1, 1, ct);

  public static async Task<CommandResult<ConsolidationScopeWorkspaceDto>> GetScopeWorkspaceAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeId,
    int eligiblePackagesPage, int journalsPage, int runsPage, CancellationToken ct = default)
  {
    if (!IsValidPage(eligiblePackagesPage) || !IsValidPage(journalsPage) || !IsValidPage(runsPage))
      return CommandResult<ConsolidationScopeWorkspaceDto>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "Valid positive history page numbers are required.");

    var scope = await db.ConsolidationScopeVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == scopeId, ct);
    if (scope is null)
      return CommandResult<ConsolidationScopeWorkspaceDto>.Fail(ErrorCodes.ScopeDenied,
        "The requested consolidation scope is not available in the current firm and group grant.");

    var auth = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, ReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ConsolidationScopeWorkspaceDto>.Fail(auth.ErrorCode!, auth.Message!);

    var group = await db.ClientGroups.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == scope.GroupId, ct);
    if (group is null)
      return CommandResult<ConsolidationScopeWorkspaceDto>.Fail(ErrorCodes.ScopeDenied, "Group not found.");

    var canReview = (await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, ReviewRoles, ct)).Succeeded;
    var canPrepare = auth.Succeeded;

    // Components
    var components = await db.ConsolidationComponents.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id)
      .OrderBy(x => x.SubmittedAt)
      .ToListAsync(ct);

    var componentClientIds = components.Select(x => x.ClientId).Distinct().ToArray();

    var clientNames = await db.PracticeClients.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && componentClientIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);

    var componentDtos = components.Select(c => new ConsolidationComponentDto(
      c.Id,
      c.ClientId,
      clientNames.GetValueOrDefault(c.ClientId, "Unknown entity"),
      c.SourceType,
      c.PackageId,
      c.ExternalComponentPackId,
      c.PackageHash,
      c.PeriodBasis,
      c.TaxonomyVersion,
      c.MappingVersion,
      c.Currency,
      c.OwnershipPercent,
      c.ControlMethod,
      c.Status,
      c.SubmittedByUserId,
      c.SubmittedAt,
      c.ApprovedByUserId,
      c.ApprovedAt)).ToList();

    // Eligible packages for draft scope
    var eligiblePackages = new ConsolidationPage<EligibleComponentPackageDto>([], 0, eligiblePackagesPage, HistoryPageSize);
    if (scope.Status == AccountingWorkflowStates.Draft && canPrepare)
    {
      var packagesQuery = db.FinancialPackages.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.Status == AccountingPackageStates.PackageValidated &&
          db.ClientGroupMemberships.Any(m => m.FirmId == actor.FirmId && m.GroupId == scope.GroupId &&
            m.ClientId == x.ClientId && m.Status == AccountingWorkflowStates.Approved && m.EffectiveTo == null));
      var totalCount = await packagesQuery.CountAsync(ct);
      var packages = await packagesQuery
        .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        .Skip(PageOffset(eligiblePackagesPage)).Take(HistoryPageSize)
        .ToListAsync(ct);
      var packageClientIds = packages.Select(x => x.ClientId).Distinct().ToArray();
      var packageClientNames = await db.PracticeClients.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && packageClientIds.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);
      eligiblePackages = new ConsolidationPage<EligibleComponentPackageDto>(packages.Select(pkg =>
        new EligibleComponentPackageDto(
          pkg.Id,
          pkg.ClientId,
          packageClientNames.GetValueOrDefault(pkg.ClientId, "Unknown entity"),
          pkg.EngagementId,
          pkg.Basis ?? $"{pkg.PeriodStart}..{pkg.PeriodEnd}",
          pkg.TaxonomyVersion,
          pkg.MappingVersionId.ToString("D"),
          pkg.Currency,
          pkg.CalculationHash)).ToArray(), totalCount, eligiblePackagesPage, HistoryPageSize);
    }

    // Elimination journals
    var journalsQuery = db.ConsolidationJournals.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id);
    var journalCount = await journalsQuery.CountAsync(ct);
    var journals = await journalsQuery
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Skip(PageOffset(journalsPage)).Take(HistoryPageSize)
      .ToListAsync(ct);

    var journalIds = journals.Select(x => x.Id).ToArray();
    List<ConsolidationJournalLine> lines = journalIds.Length == 0 ? [] : await db.ConsolidationJournalLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && journalIds.Contains(x.ConsolidationJournalId))
      .OrderBy(x => x.Id)
      .ToListAsync(ct);

    var linesByJournal = lines.GroupBy(x => x.ConsolidationJournalId)
      .ToDictionary(g => g.Key, g => g.Select(l => new ConsolidationJournalLineDto(
        l.Id, l.TaxonomyCode, l.Debit, l.Credit, l.Description, l.IntercompanyMatchId)).ToList());

    var journalDtos = new ConsolidationPage<ConsolidationJournalDto>(journals.Select(j => new ConsolidationJournalDto(
      j.Id,
      j.JournalNumber,
      j.JournalType,
      j.Currency,
      j.TotalDebits,
      j.TotalCreditsAbs,
      j.EvidenceReference,
      j.Status,
      j.ReturnReason,
      j.CreatedByUserId,
      j.CreatedAt,
      j.ApprovedByUserId,
      j.ApprovedAt,
      linesByJournal.GetValueOrDefault(j.Id, []))).ToArray(), journalCount, journalsPage, HistoryPageSize);

    // Runs
    var runsQuery = db.ConsolidationRuns.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id);
    var runCount = await runsQuery.CountAsync(ct);
    var runs = await runsQuery
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Skip(PageOffset(runsPage)).Take(HistoryPageSize)
      .Select(r => new ConsolidationRunSummaryDto(
        r.Id,
        r.EngineVersion,
        r.RunHash,
        r.SignedTotal,
        r.Status,
        r.CreatedByUserId,
        r.CreatedAt,
        r.ApprovedByUserId,
        r.ApprovedAt))
      .ToListAsync(ct);
    var runPage = new ConsolidationPage<ConsolidationRunSummaryDto>(runs, runCount, runsPage, HistoryPageSize);

    // Latest Report
    var latestReportResult = await ConsolidationService.GetLatestReportAsync(db, actor, scope.Id, ct);
    var latestReport = ReadResult(latestReportResult);

    // Component Readiness
    var readinessResult = await ConsolidationQuery.GetComponentReadinessAsync(db, actor, scope.Id, ct);
    var readiness = ReadResult(readinessResult.Succeeded
      ? CommandResult<IReadOnlyList<ComponentReadinessRow>>.Ok(readinessResult.Value!.Members)
      : CommandResult<IReadOnlyList<ComponentReadinessRow>>.Fail(readinessResult.ErrorCode!, readinessResult.Message!));

    // Intercompany Exceptions
    var intercompanyResult = await ConsolidationQuery.GetIntercompanyExceptionsAsync(
      db, actor, scope.Id, unresolvedOnly: false, page: 1, pageSize: HistoryPageSize, ct: ct);
    var exceptions = ReadResult(intercompanyResult);

    var isAdvanced = AdvancedConsolidationMethods.All.Contains(scope.Method);

    var summary = new ConsolidationScopeSummaryDto(
      scope.Id,
      scope.GroupId,
      group.Name,
      group.Code,
      scope.Version,
      scope.GroupRevision,
      scope.PeriodId,
      scope.ReportingCurrency,
      scope.Method,
      scope.Status,
      scope.OpeningBasis,
      isAdvanced);

    return CommandResult<ConsolidationScopeWorkspaceDto>.Ok(new ConsolidationScopeWorkspaceDto(
      summary,
      canPrepare,
      canReview,
      actor.UserId,
      componentDtos,
      eligiblePackages,
      journalDtos,
      runPage,
      latestReport,
      readiness,
      exceptions));
  }

  private static bool IsValidPage(int page) => page > 0 && (long)(page - 1) * HistoryPageSize <= int.MaxValue;

  private static int PageOffset(int page) => checked((page - 1) * HistoryPageSize);

  private static ConsolidationReadResult<T> ReadResult<T>(CommandResult<T> result) => result.Succeeded
    ? new ConsolidationReadResult<T>(true, result.Value, null, null)
    : new ConsolidationReadResult<T>(false, default, result.ErrorCode, result.Message);
}
