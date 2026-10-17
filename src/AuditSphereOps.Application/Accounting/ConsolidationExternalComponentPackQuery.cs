using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ExternalComponentPackLineView(
  Guid Id, string TaxonomyCode, decimal Amount, string Currency, string SourceLineReference);

public sealed record ExternalComponentPackSummary(
  Guid Id, Guid ClientId, string? ClientLegalName, Guid EngagementId, Guid? PriorPackId, int Version,
  string PeriodStart, string PeriodEnd, string Framework, string ReportingCurrency, string PeriodBasis,
  string TaxonomyVersion, string MappingVersion, string SourceReference, string RawSourceHash,
  string NormalizedSourceDigest, string PackDigest, decimal DeclaredSignedTotal, decimal ReconciledSignedTotal,
  string ReconciliationStatus, string ReconciliationReference, Guid? ReconciledByUserId, DateTimeOffset? ReconciledAt,
  string CompatibilityBridgeStatus, string CompatibilityBridgeReference, Guid? CompatibilityBridgeApprovedByUserId,
  DateTimeOffset? CompatibilityBridgeApprovedAt, string Status, string ReturnReason, Guid SubmittedByUserId,
  DateTimeOffset SubmittedAt, Guid? ReturnedByUserId, DateTimeOffset? ReturnedAt,
  Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt,
  bool CanReconcile, bool CanReturn, bool CanApprove, bool CanPin, IReadOnlyList<string> CurrentBlockers);

public sealed record ExternalComponentPackClientOption(Guid ClientId, string LegalName, DateOnly MembershipEffectiveFrom);
public sealed record ExternalComponentPackEngagementOption(
  Guid EngagementId, Guid ClientId, string ServiceRoute, string PeriodStart, string PeriodEnd, string Status);

public sealed record ExternalComponentPackPage(
  Guid ScopeVersionId, Guid GroupId, long PinnedGroupRevision, long CurrentGroupRevision,
  string ScopeStatus, string ReportingCurrency, string Method, Guid CurrentUserId,
  bool CanSubmitPack, bool CanReviewPacks, bool CanPinApprovedPacks,
  IReadOnlyList<ExternalComponentPackSummary> Items, int TotalCount, int Page, int PageSize,
  IReadOnlyList<ExternalComponentPackClientOption> EligibleClients, int EligibleClientTotalCount,
  int EligibleClientPage, int EligibleClientPageSize, string ClientSearch,
  ExternalComponentPackClientOption? ResolvedClient,
  IReadOnlyList<ExternalComponentPackEngagementOption> EligibleEngagements, int EligibleEngagementTotalCount,
  int EligibleEngagementPage, int EligibleEngagementPageSize, string EngagementSearch,
  ExternalComponentPackEngagementOption? ResolvedEngagement);

public sealed record ExternalComponentPackDetail(
  Guid ScopeVersionId, Guid GroupId, long PinnedGroupRevision, long CurrentGroupRevision,
  string ScopeStatus, Guid CurrentUserId, ExternalComponentPackSummary Pack,
  IReadOnlyList<ExternalComponentPackLineView> Lines, int TotalLineCount,
  int LinePage, int LinePageSize, bool HasPreviousLines, bool HasNextLines);

/// <summary>Bounded, group-authorized read views for immutable external component source packs.</summary>
public static class ConsolidationExternalComponentPackQuery
{
  public const int MaximumPageSize = 100;
  public const int MaximumPackLines = 500;
  public const int MaximumOptionPageSize = 100;
  public const int MaximumLinePageSize = 100;

  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ActiveSubmissionStates = [ExternalComponentPackStates.Submitted, ExternalComponentPackStates.Resubmitted];

  private sealed record Context(
    ConsolidationScopeVersion Scope, long CurrentGroupRevision, bool CanPrepare, bool CanReview);

  private static async Task<CommandResult<Context>> LoadContextAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeId, CancellationToken ct)
  {
    var scope = await db.ConsolidationScopeVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == scopeId, ct);
    if (scope is null)
      return CommandResult<Context>.Fail(ErrorCodes.ScopeDenied, "The consolidation scope is unavailable in the current group grant.");

    var read = await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, ReadRoles, ct);
    if (!read.Succeeded)
      return CommandResult<Context>.Fail(read.ErrorCode!, read.Message!);

    var currentRevision = await db.ClientGroups.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == scope.GroupId)
      .Select(x => (long?)x.Revision).SingleOrDefaultAsync(ct);
    if (currentRevision is null)
      return CommandResult<Context>.Fail(ErrorCodes.ScopeDenied, "The consolidation group is unavailable.");

    var canPrepare = (await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, PreparerRoles, ct)).Succeeded;
    var canReview = (await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, ReviewerRoles, ct)).Succeeded;
    return CommandResult<Context>.Ok(new Context(scope, currentRevision.Value, canPrepare, canReview));
  }

  public static async Task<CommandResult<ExternalComponentPackPage>> GetPageAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeId,
    int page = 1, int pageSize = 25,
    int clientPage = 1, int clientPageSize = 25, string? clientSearch = null,
    int engagementPage = 1, int engagementPageSize = 25, string? engagementSearch = null,
    Guid? selectedClientId = null, Guid? selectedEngagementId = null,
    CancellationToken ct = default)
  {
    if (page is < 1 or > 1_000_000 || pageSize is < 1 or > MaximumPageSize ||
        clientPage is < 1 or > 1_000_000 || clientPageSize is < 1 or > MaximumOptionPageSize ||
        engagementPage is < 1 or > 1_000_000 || engagementPageSize is < 1 or > MaximumOptionPageSize ||
        clientSearch is { Length: > 100 } || engagementSearch is { Length: > 100 })
      return CommandResult<ExternalComponentPackPage>.Fail(ErrorCodes.Accounting.MappingInvalid,
        $"Valid pack, client and engagement pages (page sizes up to {MaximumOptionPageSize}) and bounded searches are required.");

    var loaded = await LoadContextAsync(db, actor, scopeId, ct);
    if (!loaded.Succeeded)
      return CommandResult<ExternalComponentPackPage>.Fail(loaded.ErrorCode!, loaded.Message!);
    var context = loaded.Value!;
    var scope = context.Scope;
    var isCurrent = context.CurrentGroupRevision == scope.GroupRevision;
    var canSubmit = context.CanPrepare && isCurrent && scope.Status == AccountingWorkflowStates.Draft;
    var canReview = context.CanReview && isCurrent;

    var packs = db.ExternalComponentPacks.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id);
    var totalCount = await packs.CountAsync(ct);
    var pageRows = await packs.OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.Id)
      .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
    var pageClientIds = pageRows.Select(x => x.ClientId).Distinct().ToArray();
    var existingComponentClients = pageClientIds.Length == 0 ? new HashSet<Guid>() : await db.ConsolidationComponents.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && pageClientIds.Contains(x.ClientId))
      .Select(x => x.ClientId).Distinct().ToHashSetAsync(ct);
    var clientNames = pageClientIds.Length == 0 ? new Dictionary<Guid, string>() : await db.PracticeClients.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && pageClientIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);
    var allowedCurrencies = await AllowedComponentCurrenciesAsync(db, actor.FirmId, scope, ct);
    var items = pageRows.Select(pack => ToSummary(
      pack, clientNames.GetValueOrDefault(pack.ClientId), scope, context.CurrentGroupRevision,
      actor.UserId, context.CanPrepare, context.CanReview, existingComponentClients.Contains(pack.ClientId), allowedCurrencies)).ToArray();

    var memberQuery = db.ClientGroupMemberships.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
        x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null);
    var eligibleClientQuery = db.PracticeClients.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && memberQuery.Any(m => m.ClientId == x.Id));
    var normalizedClientSearch = (clientSearch ?? string.Empty).Trim();
    if (normalizedClientSearch.Length > 0)
    {
      var foldedClientSearch = normalizedClientSearch.ToLowerInvariant();
      eligibleClientQuery = eligibleClientQuery.Where(x => x.LegalName.ToLower().Contains(foldedClientSearch));
    }
    var eligibleClientTotalCount = await eligibleClientQuery.CountAsync(ct);
    var clientRows = await eligibleClientQuery.OrderBy(x => x.Id)
      .Skip((clientPage - 1) * clientPageSize).Take(clientPageSize)
      .Select(x => new { ClientId = x.Id, x.LegalName }).ToListAsync(ct);
    var clientIdsOnPage = clientRows.Select(x => x.ClientId).ToArray();
    var membershipDates = clientIdsOnPage.Length == 0 ? new Dictionary<Guid, DateOnly>() : await memberQuery
      .Where(x => clientIdsOnPage.Contains(x.ClientId)).GroupBy(x => x.ClientId)
      .Select(g => new { ClientId = g.Key, EffectiveFrom = g.Min(x => x.EffectiveFrom) })
      .ToDictionaryAsync(x => x.ClientId, x => x.EffectiveFrom, ct);
    var eligibleClients = clientRows.Select(x => new ExternalComponentPackClientOption(
      x.ClientId, x.LegalName, membershipDates[x.ClientId])).ToArray();

    ExternalComponentPackClientOption? resolvedClient = null;
    if (selectedClientId is { } requestedClientId && requestedClientId != Guid.Empty)
    {
      var selectedClient = await db.PracticeClients.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.Id == requestedClientId &&
          memberQuery.Any(m => m.ClientId == x.Id))
        .Select(x => new { ClientId = x.Id, x.LegalName }).SingleOrDefaultAsync(ct);
      if (selectedClient is not null)
      {
        var effectiveFrom = await memberQuery.Where(x => x.ClientId == requestedClientId)
          .MinAsync(x => (DateOnly?)x.EffectiveFrom, ct);
        if (effectiveFrom is { } date)
          resolvedClient = new ExternalComponentPackClientOption(selectedClient.ClientId, selectedClient.LegalName, date);
      }
    }

    var engagementQuery = db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && memberQuery.Any(m => m.ClientId == x.PracticeClientId));
    if (selectedClientId is { } eligibleClientFilter && eligibleClientFilter != Guid.Empty)
      engagementQuery = engagementQuery.Where(x => x.PracticeClientId == eligibleClientFilter);
    var normalizedEngagementSearch = (engagementSearch ?? string.Empty).Trim();
    if (normalizedEngagementSearch.Length > 0)
    {
      var foldedEngagementSearch = normalizedEngagementSearch.ToLowerInvariant();
      engagementQuery = engagementQuery.Where(x => x.ServiceRoute.ToLower().Contains(foldedEngagementSearch) ||
        x.Status.ToLower().Contains(foldedEngagementSearch));
    }
    var eligibleEngagementTotalCount = await engagementQuery.CountAsync(ct);
    var eligibleEngagements = await engagementQuery.OrderBy(x => x.PracticeClientId)
      .ThenByDescending(x => x.PeriodEnd).ThenBy(x => x.Id)
      .Skip((engagementPage - 1) * engagementPageSize).Take(engagementPageSize)
      .Select(x => new ExternalComponentPackEngagementOption(
        x.Id, x.PracticeClientId, x.ServiceRoute, x.PeriodStart, x.PeriodEnd, x.Status)).ToListAsync(ct);

    ExternalComponentPackEngagementOption? resolvedEngagement = null;
    if (selectedEngagementId is { } requestedEngagementId && requestedEngagementId != Guid.Empty)
    {
      var selectedEngagement = db.Engagements.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.Id == requestedEngagementId &&
          memberQuery.Any(m => m.ClientId == x.PracticeClientId));
      if (selectedClientId is { } selectedClientFilter && selectedClientFilter != Guid.Empty)
        selectedEngagement = selectedEngagement.Where(x => x.PracticeClientId == selectedClientFilter);
      resolvedEngagement = await selectedEngagement.Select(x => new ExternalComponentPackEngagementOption(
        x.Id, x.PracticeClientId, x.ServiceRoute, x.PeriodStart, x.PeriodEnd, x.Status)).SingleOrDefaultAsync(ct);
    }

    return CommandResult<ExternalComponentPackPage>.Ok(new ExternalComponentPackPage(
      scope.Id, scope.GroupId, scope.GroupRevision, context.CurrentGroupRevision,
      scope.Status, scope.ReportingCurrency, scope.Method, actor.UserId,
      canSubmit, canReview, canSubmit,
      items, totalCount, page, pageSize,
      eligibleClients, eligibleClientTotalCount, clientPage, clientPageSize, normalizedClientSearch, resolvedClient,
      eligibleEngagements, eligibleEngagementTotalCount, engagementPage, engagementPageSize,
      normalizedEngagementSearch, resolvedEngagement));
  }

  public static async Task<CommandResult<ExternalComponentPackDetail>> GetPackAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeId, Guid packId,
    int linePage = 1, int linePageSize = MaximumLinePageSize, CancellationToken ct = default)
  {
    if (linePage is < 1 or > MaximumPackLines || linePageSize is < 1 or > MaximumLinePageSize)
      return CommandResult<ExternalComponentPackDetail>.Fail(ErrorCodes.Accounting.MappingInvalid,
        $"A valid retained-line page is required (page size 1-{MaximumLinePageSize}).");
    var loaded = await LoadContextAsync(db, actor, scopeId, ct);
    if (!loaded.Succeeded)
      return CommandResult<ExternalComponentPackDetail>.Fail(loaded.ErrorCode!, loaded.Message!);
    var context = loaded.Value!;
    var scope = context.Scope;
    var pack = await db.ExternalComponentPacks.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id && x.Id == packId, ct);
    if (pack is null)
      return CommandResult<ExternalComponentPackDetail>.Fail(ErrorCodes.ScopeDenied, "The external component pack is unavailable in this scope.");

    var clientName = await db.PracticeClients.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == pack.ClientId)
      .Select(x => (string?)x.LegalName).SingleOrDefaultAsync(ct);
    var existingComponent = await db.ConsolidationComponents.AsNoTracking()
      .AnyAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.ClientId == pack.ClientId, ct);
    var allowedCurrencies = await AllowedComponentCurrenciesAsync(db, actor.FirmId, scope, ct);
    var summary = ToSummary(pack, clientName, scope, context.CurrentGroupRevision,
      actor.UserId, context.CanPrepare, context.CanReview, existingComponent, allowedCurrencies);
    var linesQuery = db.ExternalComponentPackLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id && x.ExternalComponentPackId == pack.Id);
    var lineCount = await linesQuery.CountAsync(ct);
    var lineOffset = (linePage - 1) * linePageSize;
    var lines = await linesQuery.OrderBy(x => x.SourceLineReference).ThenBy(x => x.TaxonomyCode).ThenBy(x => x.Id)
      .Skip(lineOffset).Take(linePageSize).Select(x => new ExternalComponentPackLineView(
        x.Id, x.TaxonomyCode, x.Amount, x.Currency, x.SourceLineReference)).ToListAsync(ct);

    return CommandResult<ExternalComponentPackDetail>.Ok(new ExternalComponentPackDetail(
      scope.Id, scope.GroupId, scope.GroupRevision, context.CurrentGroupRevision,
      scope.Status, actor.UserId, summary, lines,
      lineCount, linePage, linePageSize, lineOffset > 0 && lineOffset < lineCount,
      lineCount > lineOffset + lines.Count));
  }

  private static ExternalComponentPackSummary ToSummary(
    ExternalComponentPack pack, string? clientName, ConsolidationScopeVersion scope, long currentGroupRevision,
    Guid currentUserId, bool hasPreparerGrant, bool hasReviewerGrant, bool componentAlreadySubmitted,
    IReadOnlySet<string> allowedCurrencies)
  {
    var currentRevision = scope.GroupRevision == currentGroupRevision;
    var canReviewActivePack = hasReviewerGrant && currentRevision && scope.Status == AccountingWorkflowStates.Draft &&
      ActiveSubmissionStates.Contains(pack.Status, StringComparer.Ordinal) &&
      pack.SubmittedByUserId != currentUserId;
    var canReconcile = canReviewActivePack && pack.ReconciliationStatus == ExternalComponentReconciliationStates.Pending;
    var canReturn = canReviewActivePack;
    var canApprove = canReviewActivePack && pack.ReconciliationStatus == ExternalComponentReconciliationStates.Reconciled &&
      (pack.CompatibilityBridgeStatus != ExternalComponentBridgeStates.Pending || !string.IsNullOrWhiteSpace(pack.CompatibilityBridgeReference));
    var canPin = hasPreparerGrant && currentRevision && scope.Status == AccountingWorkflowStates.Draft &&
      pack.Status == ExternalComponentPackStates.Approved &&
      pack.ReconciliationStatus == ExternalComponentReconciliationStates.Reconciled && !componentAlreadySubmitted &&
      SupportsCurrency(allowedCurrencies, pack.ReportingCurrency);

    var blockers = new List<string>();
    if (!currentRevision) blockers.Add("The group perimeter changed; create a new scope version before continuing.");
    if (scope.Status != AccountingWorkflowStates.Draft) blockers.Add("The scope is no longer a draft accepting component packs.");
    if (pack.SubmittedByUserId == currentUserId && ActiveSubmissionStates.Contains(pack.Status, StringComparer.Ordinal))
      blockers.Add("A user other than the submitter must reconcile, return or approve this pack.");
    if (pack.Status == ExternalComponentPackStates.Returned)
      blockers.Add("This retained version was returned; resubmit it as a new linked version before review.");
    else if (pack.Status == ExternalComponentPackStates.Approved)
      blockers.Add("This pack is approved; pin it only if no component is already submitted for the entity.");
    else if (!ActiveSubmissionStates.Contains(pack.Status, StringComparer.Ordinal))
      blockers.Add("The pack is not in an active submitted state.");
    if (pack.ReconciliationStatus != ExternalComponentReconciliationStates.Reconciled)
      blockers.Add("Independent reconciliation evidence is required before approval or pinning.");
    if (pack.CompatibilityBridgeStatus == ExternalComponentBridgeStates.Pending &&
        string.IsNullOrWhiteSpace(pack.CompatibilityBridgeReference))
      blockers.Add("A documented compatibility bridge is required for the changed period or basis.");
    if (componentAlreadySubmitted)
      blockers.Add("A component is already submitted for this legal entity in this scope.");
    if (pack.Status == ExternalComponentPackStates.Approved && !SupportsCurrency(allowedCurrencies, pack.ReportingCurrency))
      blockers.Add("The pack currency is outside the approved scope translation inputs.");

    return new ExternalComponentPackSummary(
      pack.Id, pack.ClientId, clientName, pack.EngagementId, pack.PriorPackId, pack.Version,
      pack.PeriodStart, pack.PeriodEnd, pack.Framework, pack.ReportingCurrency, pack.PeriodBasis,
      pack.TaxonomyVersion, pack.MappingVersion, pack.SourceReference, pack.RawSourceHash,
      pack.NormalizedSourceDigest, pack.PackDigest, pack.DeclaredSignedTotal, pack.ReconciledSignedTotal,
      pack.ReconciliationStatus, pack.ReconciliationReference, pack.ReconciledByUserId, pack.ReconciledAt,
      pack.CompatibilityBridgeStatus, pack.CompatibilityBridgeReference, pack.CompatibilityBridgeApprovedByUserId,
      pack.CompatibilityBridgeApprovedAt, pack.Status, pack.ReturnReason, pack.SubmittedByUserId,
      pack.SubmittedAt, pack.ReturnedByUserId, pack.ReturnedAt, pack.ApprovedByUserId, pack.ApprovedAt,
      canReconcile, canReturn, canApprove, canPin, blockers);
  }

  private static async Task<IReadOnlySet<string>> AllowedComponentCurrenciesAsync(
    IClientAccountingDbContext db, Guid firmId, ConsolidationScopeVersion scope, CancellationToken ct)
  {
    if (scope.Method == ConsolidationCalculator.RestrictedMethod)
      return new HashSet<string>([scope.ReportingCurrency], StringComparer.Ordinal);
    if (scope.Method == ConsolidationCalculator.ForeignOperationMethod && scope.TranslationPolicyVersionId is { } policyId)
    {
      var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == firmId && x.Id == policyId && x.Status == AccountingWorkflowStates.Approved, ct);
      return policy is null ? new HashSet<string>(StringComparer.Ordinal) :
        new HashSet<string>([policy.FunctionalCurrency, policy.PresentationCurrency], StringComparer.Ordinal);
    }
    return new HashSet<string>(StringComparer.Ordinal) { "*" };
  }

  private static bool SupportsCurrency(IReadOnlySet<string> allowedCurrencies, string currency) =>
    allowedCurrencies.Contains("*") || allowedCurrencies.Contains(currency);
}
