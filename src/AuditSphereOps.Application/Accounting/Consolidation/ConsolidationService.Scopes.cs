using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ConsolidationService
{
  public static async Task<CommandResult<Guid>> CreateScopeAsync(
    IClientAccountingDbContext db, ActorContext actor, ConsolidationScopeRequest request,
    CancellationToken ct = default)
  {
    var currency = (string.IsNullOrWhiteSpace(request.ReportingCurrency) ? AccountingDefaults.DefaultCurrency : request.ReportingCurrency).Trim().ToUpperInvariant();
    var method = request.Method.Trim().ToUpperInvariant();
    var advancedMethod = AdvancedConsolidationMethods.All.Contains(method);
    if (request.GroupId == Guid.Empty || request.PeriodId == Guid.Empty || currency.Length != 3 ||
        currency.Any(c => c is < 'A' or > 'Z') ||
        (method is not (ConsolidationCalculator.RestrictedMethod or ConsolidationCalculator.ForeignOperationMethod) && !advancedMethod) ||
        string.IsNullOrWhiteSpace(request.OpeningBasis))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only the approved bounded consolidation profiles are enabled.");
    var auth = await GroupAuthAsync(db, actor, request.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var group = await db.ClientGroups.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == request.GroupId, ct);
    if (group is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var members = await db.ClientGroupMemberships.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.GroupId == request.GroupId &&
      x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null).ToListAsync(ct);
    if (members.Count == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved group membership is required before a perimeter can be created.");
    if (method is ConsolidationCalculator.ForeignOperationMethod or AdvancedConsolidationMethods.ForeignCurrencyReserve)
    {
      if (request.ExchangeRateSetVersionId is null || request.TranslationPolicyVersionId is null || request.TranslationRateDate is null ||
          string.IsNullOrWhiteSpace(request.TranslationRateType))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Foreign-currency consolidation requires an approved rate set, policy, date and rate type.");
      var rateSet = await db.ExchangeRateSetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.Id == request.ExchangeRateSetVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
      var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.Id == request.TranslationPolicyVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
      if (rateSet is null || policy is null || policy.PresentationCurrency != currency ||
          !TranslationPolicyRules.AllowsRateType(policy, request.TranslationRateType))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The selected approved foreign-currency policy and rate set do not match the reporting currency.");
    }
    var openingRunHash = string.Empty;
    var openingTranslationManifestHash = string.Empty;
    var openingTranslationReserve = 0m;
    var recurringEliminationManifest = string.Empty;
    if (request.PriorScopeVersionId is { } priorScopeId)
    {
      var priorScope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.Id == priorScopeId && x.GroupId == request.GroupId, ct);
      if (priorScope is null || priorScope.Status != AccountingWorkflowStates.Approved)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A group roll-forward requires an approved prior consolidation scope.");
      var priorRun = await db.ConsolidationRuns.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.GroupId == request.GroupId && x.ScopeVersionId == priorScope.Id && x.Status == AccountingWorkflowStates.Approved)
        .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
      if (priorRun is null)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A group roll-forward requires an approved prior consolidation run.");
      openingRunHash = priorRun.RunHash;

      var priorTranslations = await db.TranslationResults.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.GroupId == request.GroupId && x.ScopeVersionId == priorScope.Id && x.Status == AccountingWorkflowStates.Approved)
        .OrderBy(x => x.Id).ToListAsync(ct);
      openingTranslationReserve = MoneyPolicy.Normalize(priorTranslations.Sum(x => x.TranslationReserve));
      openingTranslationManifestHash = Hashing.Sha256Hex(string.Join('\n', priorTranslations.Select(x => string.Join('|',
        x.ComponentId.ToString("D"), x.RateSetVersionId.ToString("D"), x.TranslationPolicyVersionId.ToString("D"),
        x.SourcePackageHash ?? string.Empty, x.RateDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        x.RateType, x.AppliedRate?.ToString("0.000000", CultureInfo.InvariantCulture) ?? string.Empty,
        x.FromCurrency, x.ToCurrency, x.TranslatedAmount.ToString("0.000000", CultureInfo.InvariantCulture),
        x.CalculationVersion, x.TranslationReserve.ToString("0.000000", CultureInfo.InvariantCulture)))));

      var priorJournals = await db.ConsolidationJournals.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.GroupId == request.GroupId && x.ScopeVersionId == priorScope.Id && x.Status == AccountingWorkflowStates.Approved)
        .OrderBy(x => x.Id).ToListAsync(ct);
      var priorJournalIds = priorJournals.Select(x => x.Id).ToArray();
      var priorJournalLines = await db.ConsolidationJournalLines.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && priorJournalIds.Contains(x.ConsolidationJournalId)).OrderBy(x => x.Id).ToListAsync(ct);
      recurringEliminationManifest = Hashing.Sha256Hex(string.Join('\n',
        priorJournals.Select(x => string.Join('|', x.Id.ToString("D"), x.JournalNumber, x.JournalType,
          x.Currency, x.TotalDebits.ToString("0.000000", CultureInfo.InvariantCulture),
          x.TotalCreditsAbs.ToString("0.000000", CultureInfo.InvariantCulture), x.EvidenceReference))
        .Concat(priorJournalLines.Select(x => string.Join('|', x.Id.ToString("D"), x.ConsolidationJournalId.ToString("D"),
          x.IntercompanyMatchId?.ToString("D") ?? string.Empty, x.TaxonomyCode,
          x.Debit.ToString("0.000000", CultureInfo.InvariantCulture), x.Credit.ToString("0.000000", CultureInfo.InvariantCulture),
          x.Currency, x.Description)))));
    }
    var version = (await db.ConsolidationScopeVersions.Where(x => x.FirmId == actor.FirmId && x.GroupId == request.GroupId && x.PeriodId == request.PeriodId)
      .Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
    var scope = new ConsolidationScopeVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = request.GroupId, PeriodId = request.PeriodId,
      GroupRevision = group.Revision,
      PriorScopeVersionId = request.PriorScopeVersionId, OpeningRunHash = openingRunHash,
      OpeningTranslationManifestHash = openingTranslationManifestHash, OpeningTranslationReserve = openingTranslationReserve,
      RecurringEliminationManifest = recurringEliminationManifest,
      Version = version, ReportingCurrency = currency, Method = method,
      OpeningBasis = request.OpeningBasis.Trim(), ExchangeRateSetVersionId = request.ExchangeRateSetVersionId,
      TranslationPolicyVersionId = request.TranslationPolicyVersionId, TranslationRateDate = request.TranslationRateDate,
      TranslationRateType = request.TranslationRateType.Trim().ToUpperInvariant(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ConsolidationScopeVersions.Add(scope);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(scope.Id);
  }

  public static async Task<CommandResult> ApproveScopeAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId,
    CancellationToken ct = default)
  {
    var scope = await db.ConsolidationScopeVersions.SingleOrDefaultAsync(x => x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (scope.Status != AccountingWorkflowStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft perimeter can be approved.");
    var currentGroupRevision = await db.ClientGroups.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == scope.GroupId)
      .Select(x => (long?)x.Revision).SingleOrDefaultAsync(ct);
    if (currentGroupRevision is null || currentGroupRevision.Value != scope.GroupRevision)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    var ownershipEdges = await db.OwnershipInterestVersions.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id &&
      x.Status == AccountingWorkflowStates.Approved).Select(x => new OwnershipEdge(x.ParentClientId, x.ChildClientId,
      x.EffectiveFrom, x.EffectiveTo)).ToListAsync(ct);
    var advancedMethod = AdvancedConsolidationMethods.All.Contains(scope.Method);
    if (HasOwnershipCycle(ownershipEdges))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The consolidation perimeter contains a circular ownership hierarchy.");
    if (ownershipEdges.Count > 0 && !advancedMethod)
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "Ownership hierarchy data is recorded but intermediate and nested consolidation is not enabled for this calculation profile.");
    if (!await HasMethodOwnerAcceptanceAsync(db, actor.FirmId, scope.GroupId, scope.Method, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "An independently accepted group capability profile is required before perimeter approval.");
    var memberships = await db.ClientGroupMemberships.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
      x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null).Select(x => x.ClientId).ToListAsync(ct);
    var components = await db.ConsolidationComponents.Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id).ToListAsync(ct);
    if (memberships.Count == 0 || components.Count != memberships.Distinct().Count() ||
        memberships.Any(x => components.All(c => c.ClientId != x)) || components.Any(x => x.Status != AccountingWorkflowStates.Approved ||
          (!advancedMethod && (x.OwnershipPercent != 100m || x.ControlMethod != "CONTROLLED"))))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Every approved perimeter member needs one approved compatible component package.");
    if (scope.Method == ConsolidationCalculator.RestrictedMethod && components.Any(x => x.Currency != scope.ReportingCurrency))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The restricted profile requires same-currency component packages.");
    if (scope.Method is ConsolidationCalculator.ForeignOperationMethod or AdvancedConsolidationMethods.ForeignCurrencyReserve)
    {
      if (scope.ExchangeRateSetVersionId is null || scope.TranslationPolicyVersionId is null || scope.TranslationRateDate is null ||
          string.IsNullOrWhiteSpace(scope.TranslationRateType))
        return CommandResult.Fail(ErrorCodes.GateBlocked, "The foreign-currency scope is missing its pinned translation inputs.");
      var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.Id == scope.TranslationPolicyVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
      if (policy is null)
        return CommandResult.Fail(ErrorCodes.GateBlocked, "The pinned translation policy is not approved.");
      if (scope.Method == ConsolidationCalculator.ForeignOperationMethod)
      {
        var translations = await db.TranslationResults.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
          x.ScopeVersionId == scope.Id && x.RateSetVersionId == scope.ExchangeRateSetVersionId &&
          x.TranslationPolicyVersionId == scope.TranslationPolicyVersionId && x.RateDate == scope.TranslationRateDate &&
          x.RateType == scope.TranslationRateType && x.Status == AccountingWorkflowStates.Approved &&
          x.CalculationVersion == TranslationCalculationVersions.ComponentTranslationV2).ToListAsync(ct);
        if (components.Any(x => (x.Currency != scope.ReportingCurrency &&
            (x.Currency != policy.FunctionalCurrency || translations.All(t => t.ComponentId != x.Id || t.SourcePackageHash != x.PackageHash))) ||
          (x.Currency == scope.ReportingCurrency && x.Currency != policy.PresentationCurrency)))
          return CommandResult.Fail(ErrorCodes.GateBlocked, "Every foreign component needs the pinned approved translation result.");
      }
    }
    scope.Status = AccountingWorkflowStates.Approved;
    scope.ApprovedByUserId = actor.UserId;
    scope.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }
}
