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
  IReadOnlyList<EligibleComponentPackageDto> EligiblePackages,
  IReadOnlyList<ConsolidationJournalDto> Journals,
  IReadOnlyList<ConsolidationRunSummaryDto> Runs,
  ConsolidationReport? LatestReport,
  IReadOnlyList<ComponentReadinessRow> ComponentReadiness,
  IReadOnlyList<IntercompanyExceptionRow> IntercompanyExceptions);

/// <summary>
/// Authoritative detailed read model for a single consolidation perimeter version:
/// summary, components, eligible financial packages, elimination journals, run history,
/// member readiness, and latest approved report.
/// </summary>
public static class ConsolidationScopeWorkspaceQuery
{
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReviewRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<ConsolidationScopeWorkspaceDto>> GetScopeWorkspaceAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeId, CancellationToken ct = default)
  {
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

    var clientIds = components.Select(x => x.ClientId).Distinct().ToList();
    var memberClientIds = await db.ClientGroupMemberships.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
        x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null)
      .Select(x => x.ClientId)
      .ToListAsync(ct);
    clientIds.AddRange(memberClientIds);
    var distinctClientIds = clientIds.Distinct().ToArray();

    var clientNames = await db.PracticeClients.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && distinctClientIds.Contains(x.Id))
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
    var eligiblePackages = new List<EligibleComponentPackageDto>();
    if (scope.Status == AccountingWorkflowStates.Draft && canPrepare)
    {
      var packages = await db.FinancialPackages.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && memberClientIds.Contains(x.ClientId) &&
          x.Status == AccountingPackageStates.PackageValidated)
        .OrderByDescending(x => x.CreatedAt)
        .ToListAsync(ct);

      foreach (var pkg in packages)
      {
        eligiblePackages.Add(new EligibleComponentPackageDto(
          pkg.Id,
          pkg.ClientId,
          clientNames.GetValueOrDefault(pkg.ClientId, "Unknown entity"),
          pkg.EngagementId,
          pkg.Basis ?? $"{pkg.PeriodStart}..{pkg.PeriodEnd}",
          pkg.TaxonomyVersion,
          pkg.MappingVersionId.ToString("D"),
          pkg.Currency,
          pkg.CalculationHash));
      }
    }

    // Elimination journals
    var journals = await db.ConsolidationJournals.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id)
      .OrderByDescending(x => x.CreatedAt)
      .ToListAsync(ct);

    var journalIds = journals.Select(x => x.Id).ToArray();
    var lines = await db.ConsolidationJournalLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && journalIds.Contains(x.ConsolidationJournalId))
      .OrderBy(x => x.Id)
      .ToListAsync(ct);

    var linesByJournal = lines.GroupBy(x => x.ConsolidationJournalId)
      .ToDictionary(g => g.Key, g => g.Select(l => new ConsolidationJournalLineDto(
        l.Id, l.TaxonomyCode, l.Debit, l.Credit, l.Description, l.IntercompanyMatchId)).ToList());

    var journalDtos = journals.Select(j => new ConsolidationJournalDto(
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
      linesByJournal.GetValueOrDefault(j.Id, []))).ToList();

    // Runs
    var runs = await db.ConsolidationRuns.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id)
      .OrderByDescending(x => x.CreatedAt)
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

    // Latest Report
    var latestReportResult = await ConsolidationService.GetLatestReportAsync(db, actor, scope.Id, ct);
    var latestReport = latestReportResult.Succeeded ? latestReportResult.Value : null;

    // Component Readiness
    var readinessResult = await ConsolidationQuery.GetComponentReadinessAsync(db, actor, scope.Id, ct);
    var readiness = readinessResult.Succeeded ? readinessResult.Value!.Members : [];

    // Intercompany Exceptions
    var intercompanyResult = await ConsolidationQuery.GetIntercompanyExceptionsAsync(db, actor, scope.Id, unresolvedOnly: false, ct: ct);
    var exceptions = intercompanyResult.Succeeded ? intercompanyResult.Value!.Items : [];

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
      runs,
      latestReport,
      readiness,
      exceptions));
  }
}
