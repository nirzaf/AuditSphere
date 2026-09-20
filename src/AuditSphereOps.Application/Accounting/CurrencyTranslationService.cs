using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ExchangeRateSetRequest(string Code, string Source);
public sealed record ExchangeRateInput(string FromCurrency, string ToCurrency, DateOnly RateDate, string RateType, decimal Rate, string Direction);
public sealed record TranslationPolicyRequest(string Code, string FunctionalCurrency, string PresentationCurrency,
  string ClosingRateRule, string AverageRateRule, string HistoricalRateRule);

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
  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<Guid>> CreateRateSetAsync(
    IClientAccountingDbContext db, ActorContext actor, ExchangeRateSetRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Source))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An exchange-rate set needs a source and version code.");
    var auth = await FirmAuthAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (await db.ExchangeRateSetVersions.AnyAsync(x => x.FirmId == actor.FirmId && x.Code == request.Code.Trim(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The exchange-rate set code already exists.");
    var set = new ExchangeRateSetVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Code = request.Code.Trim(), Source = request.Source.Trim(),
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
    if (set.Status != AccountingWorkflowStates.Draft || from.Length != 3 || to.Length != 3 || from == to || request.Rate <= 0m ||
        string.IsNullOrWhiteSpace(request.RateType) || string.IsNullOrWhiteSpace(request.Direction))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Only valid draft rate-set entries can be added.");
    db.ExchangeRates.Add(new ExchangeRate
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, RateSetVersionId = set.Id, FromCurrency = from, ToCurrency = to,
      RateDate = request.RateDate, RateType = request.RateType.Trim().ToUpperInvariant(), Rate = request.Rate,
      Direction = request.Direction.Trim().ToUpperInvariant(), CreatedAt = DateTimeOffset.UtcNow
    });
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
        !await db.ExchangeRates.AnyAsync(x => x.FirmId == actor.FirmId && x.RateSetVersionId == set.Id, ct))
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
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleAsync(x => x.Id == component.ScopeVersionId && x.FirmId == actor.FirmId, ct);
    var groupGrant = await db.GroupAccessGrants.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
      x.UserId == actor.UserId && x.RevokedAt == null && ReviewerRoles.Contains(x.Role), ct);
    if (!groupGrant)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Explicit group access is required.");
    var set = await db.ExchangeRateSetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rateSetId && x.FirmId == actor.FirmId && x.Status == AccountingWorkflowStates.Approved, ct);
    var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == policyId && x.FirmId == actor.FirmId && x.Status == AccountingWorkflowStates.Approved, ct);
    if (set is null || policy is null || component.Currency != policy.FunctionalCurrency || scope.ReportingCurrency != policy.PresentationCurrency)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Approved policy, rate set and component/reporting currencies must agree.");
    var rate = component.Currency == scope.ReportingCurrency
      ? 1m
      : await db.ExchangeRates.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.RateSetVersionId == set.Id &&
          x.FromCurrency == component.Currency && x.ToCurrency == scope.ReportingCurrency && x.RateDate == rateDate && x.RateType == rateType.Trim().ToUpperInvariant())
        .Select(x => (decimal?)x.Rate).SingleOrDefaultAsync(ct) ?? 0m;
    if (rate <= 0m)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "No approved rate exists for the requested date and type.");
    var componentAmount = await db.FinancialPackageLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == component.PackageId)
      .SumAsync(x => x.Amount, ct);
    var translated = CurrencyTranslationCalculator.Translate(componentAmount, component.Currency, scope.ReportingCurrency, rate);
    var result = new TranslationResult
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = component.GroupId, ScopeVersionId = component.ScopeVersionId,
      ComponentId = component.Id, RateSetVersionId = set.Id, TranslationPolicyVersionId = policy.Id,
      FromCurrency = component.Currency, ToCurrency = scope.ReportingCurrency, TranslatedAmount = translated,
      TranslationReserve = 0m, Status = AccountingWorkflowStates.Approved, CreatedAt = DateTimeOffset.UtcNow
    };
    db.TranslationResults.Add(result);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(result.Id);
  }

  private static async Task<CommandResult> FirmAuthAsync(
    IClientAccountingDbContext db, ActorContext actor, IReadOnlyList<string> roles, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles.ToArray(), InternalOnly: true), ct);
}
