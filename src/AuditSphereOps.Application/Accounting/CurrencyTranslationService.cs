using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ExchangeRateSetRequest(string Code, string Source, DateOnly? EffectiveFrom = null,
  DateOnly? EffectiveTo = null, int Version = 1);
public sealed record ExchangeRateInput(string FromCurrency, string ToCurrency, DateOnly RateDate, string RateType, decimal Rate, string Direction);
public sealed record TranslationPolicyRequest(string Code, string FunctionalCurrency, string PresentationCurrency,
  string ClosingRateRule, string AverageRateRule, string HistoricalRateRule);

public static class ExchangeRateDirections
{
  public const string Direct = "DIRECT";
}

public static class CurrencyTranslationCalculator
{
  public static decimal Translate(decimal amount, string fromCurrency, string toCurrency, decimal rate)
  {
    fromCurrency = fromCurrency.Trim().ToUpperInvariant();
    toCurrency = toCurrency.Trim().ToUpperInvariant();
    if (fromCurrency.Length != 3 || toCurrency.Length != 3 || rate <= 0m ||
        (fromCurrency == toCurrency && rate != 1m))
      throw new InvalidOperationException("Currency translation needs valid currencies and an approved positive rate.");
    return MoneyPolicy.Normalize(amount * rate);
  }
}

public static class CurrencyTranslationService
{
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<Guid>> CreateRateSetAsync(
    IClientAccountingDbContext db, ActorContext actor, ExchangeRateSetRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Source) || request.Version < 1 ||
        request.EffectiveFrom is { } from && request.EffectiveTo is { } to && from > to)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An exchange-rate set needs a source, positive version and ordered effective range.");
    var auth = await FirmAuthAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (await db.ExchangeRateSetVersions.AnyAsync(x => x.FirmId == actor.FirmId && x.Code == request.Code.Trim(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The exchange-rate set code already exists.");
    var set = new ExchangeRateSetVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Code = request.Code.Trim(), Version = request.Version, Source = request.Source.Trim(),
      EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ExchangeRateSetVersions.Add(set);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(set.Id);
  }

  public static async Task<CommandResult> AddRateAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid rateSetId, ExchangeRateInput request,
    CancellationToken ct = default)
  {
    var set = await db.ExchangeRateSetVersions.SingleOrDefaultAsync(x => x.Id == rateSetId && x.FirmId == actor.FirmId, ct);
    if (set is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await FirmAuthAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    var from = request.FromCurrency.Trim().ToUpperInvariant();
    var to = request.ToCurrency.Trim().ToUpperInvariant();
    var direction = request.Direction.Trim().ToUpperInvariant();
    var rateType = request.RateType.Trim().ToUpperInvariant();
    if (set.Status != AccountingWorkflowStates.Draft || from.Length != 3 || to.Length != 3 || from == to || request.Rate <= 0m ||
        string.IsNullOrWhiteSpace(rateType) || direction != ExchangeRateDirections.Direct ||
        set.EffectiveFrom is { } effectiveFrom && request.RateDate < effectiveFrom ||
        set.EffectiveTo is { } effectiveTo && request.RateDate > effectiveTo)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Only positive DIRECT rate-set entries are supported by this profile.");
    if (await db.ExchangeRates.AnyAsync(x => x.FirmId == actor.FirmId && x.RateSetVersionId == set.Id &&
        x.FromCurrency == from && x.ToCurrency == to && x.RateDate == request.RateDate && x.RateType == rateType, ct))
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "This rate observation already exists in the rate set.");
    db.ExchangeRates.Add(new ExchangeRate
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, RateSetVersionId = set.Id, FromCurrency = from, ToCurrency = to,
      RateDate = request.RateDate, RateType = rateType, Rate = request.Rate,
      Direction = direction, CreatedAt = DateTimeOffset.UtcNow
    });
    if (set.EffectiveFrom is null || request.RateDate < set.EffectiveFrom)
      set.EffectiveFrom = request.RateDate;
    if (set.EffectiveTo is null || request.RateDate > set.EffectiveTo)
      set.EffectiveTo = request.RateDate;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ApproveRateSetAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid rateSetId,
    CancellationToken ct = default)
  {
    var set = await db.ExchangeRateSetVersions.SingleOrDefaultAsync(x => x.Id == rateSetId && x.FirmId == actor.FirmId, ct);
    if (set is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await FirmAuthAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (set.Status != AccountingWorkflowStates.Draft || set.CreatedByUserId == actor.UserId ||
        !await db.ExchangeRates.AnyAsync(x => x.FirmId == actor.FirmId && x.RateSetVersionId == set.Id, ct) ||
        set.EffectiveFrom is null || set.EffectiveTo is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "A separate reviewer can approve only a populated draft rate set.");
    set.Status = AccountingWorkflowStates.Approved;
    set.ApprovedByUserId = actor.UserId;
    set.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> CreatePolicyAsync(
    IClientAccountingDbContext db, ActorContext actor, TranslationPolicyRequest request,
    CancellationToken ct = default)
  {
    var functional = request.FunctionalCurrency.Trim().ToUpperInvariant();
    var presentation = request.PresentationCurrency.Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(request.Code) || functional.Length != 3 || presentation.Length != 3 ||
        string.IsNullOrWhiteSpace(request.ClosingRateRule) || string.IsNullOrWhiteSpace(request.AverageRateRule) ||
        string.IsNullOrWhiteSpace(request.HistoricalRateRule))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A translation policy needs explicit currency and rate rules.");
    var auth = await FirmAuthAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (await db.TranslationPolicyVersions.AnyAsync(x => x.FirmId == actor.FirmId && x.Code == request.Code.Trim(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The translation policy code already exists.");
    var policy = new TranslationPolicyVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Code = request.Code.Trim(), FunctionalCurrency = functional,
      PresentationCurrency = presentation, ClosingRateRule = request.ClosingRateRule.Trim(), AverageRateRule = request.AverageRateRule.Trim(),
      HistoricalRateRule = request.HistoricalRateRule.Trim(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.TranslationPolicyVersions.Add(policy);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(policy.Id);
  }

  public static async Task<CommandResult> ApprovePolicyAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid policyId,
    CancellationToken ct = default)
  {
    var policy = await db.TranslationPolicyVersions.SingleOrDefaultAsync(x => x.Id == policyId && x.FirmId == actor.FirmId, ct);
    if (policy is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await FirmAuthAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (policy.Status != AccountingWorkflowStates.Draft || policy.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "A separate reviewer must approve the translation policy.");
    policy.Status = AccountingWorkflowStates.Approved;
    policy.ApprovedByUserId = actor.UserId;
    policy.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> TranslateComponentAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid componentId, Guid rateSetId,
    Guid policyId, DateOnly rateDate, string rateType, CancellationToken ct = default)
  {
    var component = await db.ConsolidationComponents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == componentId && x.FirmId == actor.FirmId, ct);
    if (component is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == component.ScopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var groupGrant = await db.GroupAccessGrants.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
      x.UserId == actor.UserId && x.RevokedAt == null && PreparerRoles.Contains(x.Role), ct);
    if (!groupGrant)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Explicit group access is required.");
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    if (scope.Method != ConsolidationCalculator.ForeignOperationMethod || scope.Status != AccountingWorkflowStates.Draft ||
        component.Status != AccountingWorkflowStates.Approved || scope.ExchangeRateSetVersionId != rateSetId ||
        scope.TranslationPolicyVersionId != policyId || scope.TranslationRateDate != rateDate ||
        !string.Equals(scope.TranslationRateType, rateType.Trim().ToUpperInvariant(), StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The component is not ready for the pinned foreign-operation translation profile.");
    var set = await db.ExchangeRateSetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rateSetId && x.FirmId == actor.FirmId && x.Status == AccountingWorkflowStates.Approved, ct);
    var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == policyId && x.FirmId == actor.FirmId && x.Status == AccountingWorkflowStates.Approved, ct);
    if (set is null || policy is null || component.Currency != policy.FunctionalCurrency || scope.ReportingCurrency != policy.PresentationCurrency)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Approved policy, rate set and component/reporting currencies must agree.");
    var normalizedRateType = rateType.Trim().ToUpperInvariant();
    var rate = await db.ExchangeRates.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.RateSetVersionId == set.Id &&
        x.FromCurrency == component.Currency && x.ToCurrency == scope.ReportingCurrency && x.RateDate == rateDate && x.RateType == normalizedRateType &&
        x.Direction == ExchangeRateDirections.Direct)
      .Select(x => (decimal?)x.Rate).SingleOrDefaultAsync(ct) ?? 0m;
    if (rate <= 0m)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "No approved rate exists for the requested date and type.");
    var package = component.SourceType == ConsolidationComponentSources.InternalPackage && component.PackageId is { } packageId
      ? await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == packageId &&
          x.ClientId == component.ClientId && x.EngagementId == component.EngagementId && x.Status == AccountingPackageStates.PackageValidated, ct)
      : null;
    var externalPack = component.SourceType == ConsolidationComponentSources.ExternalPack && component.ExternalComponentPackId is { } externalPackId
      ? await db.ExternalComponentPacks.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == externalPackId &&
          x.GroupId == component.GroupId && x.ScopeVersionId == component.ScopeVersionId && x.Status == ExternalComponentPackStates.Approved &&
          x.ReconciliationStatus == ExternalComponentReconciliationStates.Reconciled, ct)
      : null;
    var externalLines = externalPack is null ? new List<ExternalComponentPackLine>() : await db.ExternalComponentPackLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.GroupId == component.GroupId && x.ScopeVersionId == component.ScopeVersionId && x.ExternalComponentPackId == externalPack.Id).ToListAsync(ct);
    if ((component.SourceType == ConsolidationComponentSources.InternalPackage &&
         (package is null || package.Currency != component.Currency || package.CalculationHash != component.PackageHash)) ||
        (component.SourceType == ConsolidationComponentSources.ExternalPack &&
         (externalPack is null || externalPack.ReportingCurrency != component.Currency || externalPack.PackDigest != component.PackageHash ||
          !ConsolidationService.ExternalPackLinesMatch(externalPack, externalLines))))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The component source changed; rebuild the translation input.");
    var sourceHash = component.PackageHash;
    var existing = await db.TranslationResults.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ComponentId == component.Id && x.RateSetVersionId == set.Id && x.TranslationPolicyVersionId == policy.Id, ct);
    if (existing is not null)
    {
      if (existing.SourcePackageHash == sourceHash && existing.RateDate == rateDate && existing.RateType == normalizedRateType &&
          existing.AppliedRate == rate)
        return CommandResult<Guid>.Ok(existing.Id);
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "A different translation input already exists for this component and policy version.");
    }
    var componentAmount = package is not null
      ? await db.FinancialPackageLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id)
        .SumAsync(x => x.Amount, ct)
      : externalLines.Sum(x => x.Amount);
    var translated = CurrencyTranslationCalculator.Translate(componentAmount, component.Currency, scope.ReportingCurrency, rate);
    var roundingAdjustment = MoneyPolicy.Normalize(translated - componentAmount * rate);
    var foreignExchangeAdjustment = MoneyPolicy.Normalize(translated - componentAmount - roundingAdjustment);
    var result = new TranslationResult
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = component.GroupId, ScopeVersionId = component.ScopeVersionId,
      ComponentId = component.Id, RateSetVersionId = set.Id, TranslationPolicyVersionId = policy.Id,
      SourcePackageHash = sourceHash, RateDate = rateDate, RateType = normalizedRateType, AppliedRate = rate,
      FromCurrency = component.Currency, ToCurrency = scope.ReportingCurrency, TranslatedAmount = translated,
      ForeignExchangeAdjustment = foreignExchangeAdjustment, RoundingAdjustment = roundingAdjustment,
      TranslationReserve = 0m, Status = AccountingWorkflowStates.Submitted, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.TranslationResults.Add(result);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(result.Id);
  }

  public static async Task<CommandResult> ApproveTranslationAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid translationResultId, CancellationToken ct = default)
  {
    var result = await db.TranslationResults.SingleOrDefaultAsync(x => x.Id == translationResultId && x.FirmId == actor.FirmId, ct);
    if (result is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ReviewerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return auth;
    if (result.Status != AccountingWorkflowStates.Submitted || result.CreatedByUserId == actor.UserId || result.RateDate is null ||
        result.AppliedRate is not > 0m || string.IsNullOrWhiteSpace(result.SourcePackageHash) || string.IsNullOrWhiteSpace(result.RateType))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Only a complete translation prepared by another user can be approved.");
    var component = await db.ConsolidationComponents.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.Id == result.ComponentId && x.GroupId == result.GroupId && x.ScopeVersionId == result.ScopeVersionId, ct);
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.Id == result.ScopeVersionId && x.GroupId == result.GroupId, ct);
    if (scope is not null && !await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    var package = component is { SourceType: ConsolidationComponentSources.InternalPackage, PackageId: { } packageId }
      ? await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
          x.Id == packageId && x.ClientId == component.ClientId && x.EngagementId == component.EngagementId, ct)
      : null;
    var externalPack = component is { SourceType: ConsolidationComponentSources.ExternalPack, ExternalComponentPackId: { } externalPackId }
      ? await db.ExternalComponentPacks.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
          x.Id == externalPackId && x.GroupId == result.GroupId && x.ScopeVersionId == result.ScopeVersionId, ct)
      : null;
    var externalLines = externalPack is null ? new List<ExternalComponentPackLine>() : await db.ExternalComponentPackLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.GroupId == result.GroupId && x.ScopeVersionId == result.ScopeVersionId && x.ExternalComponentPackId == externalPack.Id).ToListAsync(ct);
    var set = await db.ExchangeRateSetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.Id == result.RateSetVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
    var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.Id == result.TranslationPolicyVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
    var sourceIsCurrent = component is not null &&
      ((component.SourceType == ConsolidationComponentSources.InternalPackage && package is not null &&
        package.Status == AccountingPackageStates.PackageValidated && package.CalculationHash == result.SourcePackageHash &&
        package.Currency == result.FromCurrency) ||
       (component.SourceType == ConsolidationComponentSources.ExternalPack && externalPack is not null &&
        externalPack.Status == ExternalComponentPackStates.Approved && externalPack.ReconciliationStatus == ExternalComponentReconciliationStates.Reconciled &&
        externalPack.PackDigest == result.SourcePackageHash && externalPack.ReportingCurrency == result.FromCurrency &&
        ConsolidationService.ExternalPackLinesMatch(externalPack, externalLines)));
    if (component is null || scope is null || !sourceIsCurrent || set is null || policy is null || scope.Status != AccountingWorkflowStates.Draft ||
        component.Status != AccountingWorkflowStates.Approved ||
        scope.Method != ConsolidationCalculator.ForeignOperationMethod || scope.ExchangeRateSetVersionId != result.RateSetVersionId ||
        scope.TranslationPolicyVersionId != result.TranslationPolicyVersionId || scope.TranslationRateDate != result.RateDate ||
        scope.TranslationRateType != result.RateType || policy.FunctionalCurrency != result.FromCurrency ||
        policy.PresentationCurrency != result.ToCurrency)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The translation input is no longer the current approved scope input.");
    var rate = await db.ExchangeRates.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.RateSetVersionId == set.Id &&
      x.FromCurrency == result.FromCurrency && x.ToCurrency == result.ToCurrency && x.RateDate == result.RateDate && x.RateType == result.RateType &&
      x.Direction == ExchangeRateDirections.Direct)
      .Select(x => (decimal?)x.Rate).SingleOrDefaultAsync(ct);
    if (rate is null || rate.Value != result.AppliedRate.Value)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The approved rate set no longer contains the recorded rate.");
    var total = package is not null
      ? await db.FinancialPackageLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id)
        .SumAsync(x => x.Amount, ct)
      : externalLines.Sum(x => x.Amount);
    var expectedTranslated = CurrencyTranslationCalculator.Translate(total, result.FromCurrency, result.ToCurrency, result.AppliedRate.Value);
    var expectedRounding = MoneyPolicy.Normalize(expectedTranslated - total * result.AppliedRate.Value);
    var expectedForeignExchange = MoneyPolicy.Normalize(expectedTranslated - total - expectedRounding);
    if (expectedTranslated != result.TranslatedAmount || expectedRounding != result.RoundingAdjustment ||
        expectedForeignExchange != result.ForeignExchangeAdjustment)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The translation result no longer matches the package lines.");
    result.Status = AccountingWorkflowStates.Approved;
    result.ApprovedByUserId = actor.UserId;
    result.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult> FirmAuthAsync(
    IClientAccountingDbContext db, ActorContext actor, IReadOnlyList<string> roles, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles.ToArray(), InternalOnly: true), ct);
}
