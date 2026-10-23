using System.Data;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
  bool IsAdvanced,
  Guid? SupersedesScopeVersionId,
  Guid? SupersedesComponentId);

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
  DateTimeOffset? ApprovedAt,
  bool CanApprove,
  string? ApprovalBlocker,
  string CurrentnessState,
  string? CurrentnessBlockerCode)
{
  // Retained only between the repeatable-read projection and its final recheck; it is not part of the published API contract.
  internal string? CurrentnessEvidenceHash { get; init; }
}

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

public sealed record ConsolidationPageInfo(int TotalCount, int Page, int PageSize);

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
  ConsolidationReadResult<IntercompanyExceptionsPage> IntercompanyExceptions,
  ConsolidationPageInfo ComponentPaging,
  ConsolidationPageInfo? ReadinessPaging,
  ConsolidationComponentDto? ResolvedComponent,
  bool CanApproveScope,
  string? ScopeApprovalBlocker);

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
    => await GetScopeWorkspaceAsync(db, actor, scopeId, eligiblePackagesPage, journalsPage, runsPage, 1, 1, null, ct);

  public static async Task<CommandResult<ConsolidationScopeWorkspaceDto>> GetScopeWorkspaceAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeId,
    int eligiblePackagesPage, int journalsPage, int runsPage, int componentsPage, int readinessPage,
    Guid? selectedComponentId, CancellationToken ct = default)
  {
    if (!IsValidPage(eligiblePackagesPage) || !IsValidPage(journalsPage) || !IsValidPage(runsPage) ||
        !IsValidPage(componentsPage) || !IsValidPage(readinessPage))
      return CommandResult<ConsolidationScopeWorkspaceDto>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "Valid positive history page numbers are required.");
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<ConsolidationScopeWorkspaceDto>.Fail(ErrorCodes.GateBlocked,
        "The consolidation workspace requires an independent read snapshot.");

    CommandResult<ConsolidationScopeWorkspaceDto> result;
    ConsolidationScopeVersion snapshotScope;
    ClientGroup snapshotGroup;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      result = await ReadWithinSnapshotAsync(db, actor, scopeId, eligiblePackagesPage, journalsPage, runsPage,
        componentsPage, readinessPage, selectedComponentId, ct);
      if (!result.Succeeded)
        return result;

      snapshotScope = await db.ConsolidationScopeVersions.AsNoTracking()
        .SingleAsync(x => x.FirmId == actor.FirmId && x.Id == scopeId, ct);
      snapshotGroup = await db.ClientGroups.AsNoTracking()
        .SingleAsync(x => x.FirmId == actor.FirmId && x.Id == snapshotScope.GroupId, ct);
      await snapshot.CommitAsync(ct);
    }

    var currentReviewAuth = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, snapshotScope.GroupId, ReviewRoles, ct);
    var currentScope = await db.ConsolidationScopeVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == scopeId, ct);
    var currentGroup = await db.ClientGroups.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == snapshotScope.GroupId, ct);
    if (currentScope is null || currentGroup is null ||
        !SameScopeContext(snapshotScope, currentScope) || !SameGroupContext(snapshotGroup, currentGroup))
      return CommandResult<ConsolidationScopeWorkspaceDto>.Fail(ErrorCodes.GenerationStale,
        "The scope or group changed while this workspace was read. Reload the current pages.");

    var currentReadAuth = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, snapshotScope.GroupId, ReadRoles, ct);
    if (!currentReadAuth.Succeeded)
      return CommandResult<ConsolidationScopeWorkspaceDto>.Fail(currentReadAuth.ErrorCode!, currentReadAuth.Message!);

    var workspace = result.Value!;
    var canPrepareAtReturn = workspace.CanPrepare && currentReadAuth.Succeeded;
    var canReviewAtReturn = workspace.CanReview && currentReviewAuth.Succeeded;
    const string reviewerRequired = "Current consolidation reviewer authorization is required.";
    var componentIds = workspace.Components.Select(x => x.Id)
      .Concat(workspace.ResolvedComponent is null ? [] : [workspace.ResolvedComponent.Id])
      .Distinct().ToArray();
    IReadOnlyDictionary<Guid, CommandResult> currentComponentReadiness = new Dictionary<Guid, CommandResult>();
    var currentComponents = componentIds.Length == 0
      ? new List<ConsolidationComponent>()
      : await db.ConsolidationComponents.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.GroupId == snapshotScope.GroupId &&
          x.ScopeVersionId == scopeId && componentIds.Contains(x.Id))
        .ToListAsync(ct);
    if (canReviewAtReturn && componentIds.Length > 0)
    {
      currentComponentReadiness = await ConsolidationService.GetComponentApprovalReadinessAsync(
        db, actor, currentScope, currentComponents, ct);
    }
    var currentComponentCurrentness = await ConsolidationService.GetComponentCurrentnessAsync(
      db, actor, currentScope, currentComponents, ct);

    ConsolidationComponentDto ComponentAtReturn(ConsolidationComponentDto component)
    {
      var currentnessAtReturn = component;
      if (currentComponentCurrentness.TryGetValue(component.Id, out var finalPerimeter) &&
          finalPerimeter.BlockerCode == "PERIMETER_STALE")
        currentnessAtReturn = component with
        {
          CurrentnessState = ConsolidationComponentCurrentnessStates.Stale,
          CurrentnessBlockerCode = finalPerimeter.BlockerCode,
          CurrentnessEvidenceHash = null
        };
      else if (component.CurrentnessState == ConsolidationComponentCurrentnessStates.Current)
      {
        var currentRow = currentComponents.SingleOrDefault(x => x.Id == component.Id);
        if (currentRow is null)
          currentnessAtReturn = component with
          {
            CurrentnessState = ConsolidationComponentCurrentnessStates.Stale,
            CurrentnessBlockerCode = "COMPONENT_CHANGED",
            CurrentnessEvidenceHash = null
          };
        else if (!SameComponentPin(component, currentRow))
          currentnessAtReturn = component with
          {
            CurrentnessState = ConsolidationComponentCurrentnessStates.Stale,
            CurrentnessBlockerCode = "COMPONENT_PIN_CHANGED",
            CurrentnessEvidenceHash = null
          };
        else if (!currentComponentCurrentness.TryGetValue(component.Id, out var finalCurrentness))
          currentnessAtReturn = component with
          {
            CurrentnessState = ConsolidationComponentCurrentnessStates.Stale,
            CurrentnessBlockerCode = "CURRENTNESS_RECHECK_UNAVAILABLE",
            CurrentnessEvidenceHash = null
          };
        else if (finalCurrentness.State != ConsolidationComponentCurrentnessStates.Current)
          currentnessAtReturn = component with
          {
            CurrentnessState = finalCurrentness.State,
            CurrentnessBlockerCode = finalCurrentness.BlockerCode,
            CurrentnessEvidenceHash = null
          };
        else if (!string.Equals(component.CurrentnessEvidenceHash,
          CurrentnessEvidenceHash(currentRow, finalCurrentness.EvidenceHash), StringComparison.Ordinal))
          currentnessAtReturn = component with
          {
            CurrentnessState = ConsolidationComponentCurrentnessStates.Stale,
            CurrentnessBlockerCode = "SOURCE_EVIDENCE_CHANGED",
            CurrentnessEvidenceHash = null
          };
      }

      if (!canReviewAtReturn)
        return currentnessAtReturn with { CanApprove = false, ApprovalBlocker = reviewerRequired };

      if (!currentComponentReadiness.TryGetValue(component.Id, out var readiness))
        return component.CanApprove
          ? currentnessAtReturn with { CanApprove = false, ApprovalBlocker = "Current component approval readiness could not be established; reload the current scope." }
          : currentnessAtReturn;

      if (!readiness.Succeeded)
      {
        var blocker = readiness.ErrorCode == ErrorCodes.ScopeDenied
          ? "Current source authorization does not permit component approval."
          : readiness.Message ?? "Current component approval readiness could not be established; reload the current scope.";
        return currentnessAtReturn with { CanApprove = false, ApprovalBlocker = blocker };
      }

      // Current readiness may only retain a capability already established by the snapshot.
      return component.CanApprove ? currentnessAtReturn with { CanApprove = true, ApprovalBlocker = null } : currentnessAtReturn;
    }

    var componentsAtReturn = workspace.Components.Select(ComponentAtReturn).ToArray();
    var resolvedComponentAtReturn = workspace.ResolvedComponent is null
      ? null
      : ComponentAtReturn(workspace.ResolvedComponent);

    var canApproveScopeAtReturn = false;
    var scopeApprovalBlocker = workspace.ScopeApprovalBlocker;
    if (!canReviewAtReturn)
      scopeApprovalBlocker = reviewerRequired;
    else if (workspace.CanApproveScope)
    {
      var scopeReadiness = await ConsolidationService.GetScopeApprovalReadinessAsync(
        db, actor, currentGroup, currentScope, ct);
      canApproveScopeAtReturn = scopeReadiness.Succeeded;
      scopeApprovalBlocker = scopeReadiness.Succeeded ? null
        : scopeReadiness.ErrorCode == ErrorCodes.ScopeDenied ? reviewerRequired
        : scopeReadiness.Message ?? "Current scope approval readiness could not be established; reload the current scope.";
    }

    return CommandResult<ConsolidationScopeWorkspaceDto>.Ok(workspace with
    {
      CanPrepare = canPrepareAtReturn,
      CanReview = canReviewAtReturn,
      Components = componentsAtReturn,
      ResolvedComponent = resolvedComponentAtReturn,
      CanApproveScope = workspace.CanApproveScope && canApproveScopeAtReturn,
      ScopeApprovalBlocker = scopeApprovalBlocker
    });
  }

  private static async Task<CommandResult<ConsolidationScopeWorkspaceDto>> ReadWithinSnapshotAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeId,
    int eligiblePackagesPage, int journalsPage, int runsPage, int componentsPage, int readinessPage,
    Guid? selectedComponentId, CancellationToken ct)
  {
    if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.RepeatableRead)
      return CommandResult<ConsolidationScopeWorkspaceDto>.Fail(ErrorCodes.GateBlocked,
        "The consolidation projection requires its owned RepeatableRead snapshot.");

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
    var componentsQuery = db.ConsolidationComponents.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id);
    var componentCount = await componentsQuery.CountAsync(ct);
    var components = await componentsQuery.OrderBy(x => x.SubmittedAt).ThenBy(x => x.Id)
      .Skip(PageOffset(componentsPage)).Take(HistoryPageSize).ToListAsync(ct);
    var selectedComponent = selectedComponentId.HasValue
      ? await componentsQuery.SingleOrDefaultAsync(x => x.Id == selectedComponentId.Value, ct) : null;
    var currentnessComponents = components.ToList();
    if (selectedComponent is not null && currentnessComponents.All(x => x.Id != selectedComponent.Id))
      currentnessComponents.Add(selectedComponent);
    var componentCurrentness = await ConsolidationService.GetComponentCurrentnessAsync(
      db, actor, scope, currentnessComponents, ct);
    var componentClientIds = components.Select(x => x.ClientId)
      .Concat(selectedComponent is null ? [] : new[] { selectedComponent.ClientId }).Distinct().ToArray();

    var clientNames = await db.PracticeClients.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && componentClientIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);

    // Eligible packages for draft scope
    var eligiblePackages = new ConsolidationPage<EligibleComponentPackageDto>([], 0, eligiblePackagesPage, HistoryPageSize);
    if (scope.Status == AccountingWorkflowStates.Draft && canPrepare)
    {
      var packagesQuery = db.FinancialPackages.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.Status == AccountingPackageStates.PackageValidated &&
          ConsolidationScopeGuards.ApprovedMembershipsOverlappingPeriod(
            db, actor.FirmId, scope.GroupId, scope.PeriodId).Any(m => m.ClientId == x.ClientId));
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
    var readinessResult = await ConsolidationQuery.GetComponentReadinessAsync(
      db, actor, scope.Id, readinessPage, HistoryPageSize, ct);
    var readiness = ReadResult(readinessResult.Succeeded
      ? CommandResult<IReadOnlyList<ComponentReadinessRow>>.Ok(readinessResult.Value!.Items)
      : CommandResult<IReadOnlyList<ComponentReadinessRow>>.Fail(readinessResult.ErrorCode!, readinessResult.Message!));
    var readinessPaging = readinessResult.Succeeded
      ? new ConsolidationPageInfo(readinessResult.Value!.TotalCount, readinessPage, HistoryPageSize) : null;

    // Intercompany Exceptions
    var intercompanyResult = await ConsolidationQuery.GetIntercompanyExceptionsAsync(
      db, actor, scope.Id, unresolvedOnly: false, page: 1, pageSize: HistoryPageSize, ct: ct);
    var exceptions = ReadResult(intercompanyResult);

    IReadOnlyDictionary<Guid, CommandResult> componentApprovalReadiness = new Dictionary<Guid, CommandResult>();
    if (canReview)
    {
      var approvalComponents = components.ToList();
      if (selectedComponent is not null && approvalComponents.All(x => x.Id != selectedComponent.Id))
        approvalComponents.Add(selectedComponent);
      componentApprovalReadiness = await ConsolidationService.GetComponentApprovalReadinessAsync(
        db, actor, scope, approvalComponents, ct);
    }
    var scopeApprovalReadiness = canReview
      ? await ConsolidationService.GetScopeApprovalReadinessAsync(db, actor, group, scope, ct)
      : null;

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
      isAdvanced,
      scope.SupersedesScopeVersionId,
      scope.SupersedesComponentId);

    var canApproveScope = canReview && scopeApprovalReadiness?.Succeeded == true;
    var scopeApprovalBlocker = canApproveScope ? null
      : !canReview ? "Current consolidation reviewer authorization is required."
      : scopeApprovalReadiness?.ErrorCode == ErrorCodes.ScopeDenied ? "Current consolidation reviewer authorization is required."
      : scopeApprovalReadiness?.Message ?? "Scope approval readiness could not be established; reload the current scope.";

    ConsolidationComponentDto ComponentDto(ConsolidationComponent c)
    {
      CommandResult? readiness = null;
      if (canReview && componentApprovalReadiness.TryGetValue(c.Id, out var currentReadiness))
        readiness = currentReadiness;
      var canApprove = readiness?.Succeeded == true;
      var blocker = canApprove ? null
        : !canReview ? "Current consolidation reviewer authorization is required."
        : readiness is null ? "Component approval readiness could not be established; reload the current scope."
        : readiness.ErrorCode == ErrorCodes.ScopeDenied ? "Current source authorization does not permit component approval."
        : readiness.Message ?? "Component approval readiness could not be established; reload the current scope.";
      var currentness = componentCurrentness.GetValueOrDefault(c.Id,
        new ConsolidationComponentCurrentness(ConsolidationComponentCurrentnessStates.Unavailable,
          "CURRENTNESS_UNAVAILABLE", null));

      return new ConsolidationComponentDto(
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
        c.ApprovedAt,
        canApprove,
        blocker,
        currentness.State,
        currentness.BlockerCode)
      {
        CurrentnessEvidenceHash = CurrentnessEvidenceHash(c, currentness.EvidenceHash)
      };
    }
    var componentDtos = components.Select(ComponentDto).ToList();

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
      exceptions,
      new ConsolidationPageInfo(componentCount, componentsPage, HistoryPageSize),
      readinessPaging,
      selectedComponent is null ? null : ComponentDto(selectedComponent),
      canApproveScope,
      scopeApprovalBlocker));
  }

  private static bool IsValidPage(int page) => page > 0 && (long)(page - 1) * HistoryPageSize <= int.MaxValue;

  private static bool SameScopeContext(ConsolidationScopeVersion left, ConsolidationScopeVersion right) =>
    left.Id == right.Id && left.FirmId == right.FirmId && left.GroupId == right.GroupId &&
    left.GroupRevision == right.GroupRevision && left.PeriodId == right.PeriodId &&
    left.PriorScopeVersionId == right.PriorScopeVersionId &&
    left.SupersedesScopeVersionId == right.SupersedesScopeVersionId &&
    left.SupersedesComponentId == right.SupersedesComponentId && left.OpeningRunHash == right.OpeningRunHash &&
    left.OpeningTranslationManifestHash == right.OpeningTranslationManifestHash &&
    left.OpeningTranslationReserve == right.OpeningTranslationReserve &&
    left.RecurringEliminationManifest == right.RecurringEliminationManifest && left.Version == right.Version &&
    left.ReportingCurrency == right.ReportingCurrency && left.Method == right.Method && left.Status == right.Status &&
    left.OpeningBasis == right.OpeningBasis && left.ExchangeRateSetVersionId == right.ExchangeRateSetVersionId &&
    left.TranslationPolicyVersionId == right.TranslationPolicyVersionId && left.TranslationRateDate == right.TranslationRateDate &&
    left.TranslationRateType == right.TranslationRateType && left.CreatedByUserId == right.CreatedByUserId &&
    left.ApprovedByUserId == right.ApprovedByUserId && left.ApprovedAt == right.ApprovedAt && left.CreatedAt == right.CreatedAt;

  private static bool SameGroupContext(ClientGroup left, ClientGroup right) =>
    left.Id == right.Id && left.FirmId == right.FirmId && left.Code == right.Code && left.Name == right.Name &&
    left.Status == right.Status && left.Revision == right.Revision && left.CreatedByUserId == right.CreatedByUserId &&
    left.CreatedAt == right.CreatedAt;

  private static bool SameComponentPin(ConsolidationComponentDto snapshot, ConsolidationComponent current) =>
    snapshot.Id == current.Id && snapshot.ClientId == current.ClientId && snapshot.SourceType == current.SourceType &&
    snapshot.PackageId == current.PackageId && snapshot.ExternalComponentPackId == current.ExternalComponentPackId &&
    snapshot.PackageHash == current.PackageHash && snapshot.PeriodBasis == current.PeriodBasis &&
    snapshot.TaxonomyVersion == current.TaxonomyVersion && snapshot.MappingVersion == current.MappingVersion &&
    snapshot.Currency == current.Currency && snapshot.OwnershipPercent == current.OwnershipPercent &&
    snapshot.ControlMethod == current.ControlMethod;

  private static string? CurrentnessEvidenceHash(ConsolidationComponent component, string? sourceEvidenceHash) =>
    sourceEvidenceHash is null ? null : Hashing.Sha256Hex(string.Join('\u001f',
      component.Id.ToString("D"), component.Status, component.SubmittedByUserId.ToString("D"),
      component.SubmittedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
      component.ApprovedByUserId?.ToString("D") ?? string.Empty,
      component.ApprovedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
      component.PackageId?.ToString("D") ?? string.Empty,
      component.ExternalComponentPackId?.ToString("D") ?? string.Empty,
      component.PackageHash, sourceEvidenceHash));

  private static int PageOffset(int page) => checked((page - 1) * HistoryPageSize);

  private static ConsolidationReadResult<T> ReadResult<T>(CommandResult<T> result) => result.Succeeded
    ? new ConsolidationReadResult<T>(true, result.Value, null, null)
    : new ConsolidationReadResult<T>(false, default, result.ErrorCode, result.Message);
}
