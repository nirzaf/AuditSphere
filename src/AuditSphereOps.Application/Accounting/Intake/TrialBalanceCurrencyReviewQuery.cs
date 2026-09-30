using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record AppliedRate(string FromCurrency, string ToCurrency, string RateType, DateOnly RateDate, decimal Rate, string Direction, string Source, int SetVersion);

public sealed record CurrencyReviewLine(string AccountCode, string AccountName, decimal SourceAmount, decimal TranslatedAmount,
  decimal? PriorTranslatedAmount, decimal? Variance, decimal? VariancePercent, bool Highlighted, string? HighlightReason);

public sealed record CurrencyReviewView(Guid DatasetId, string SourceCurrency, string PresentationCurrency, DateOnly PeriodEnd,
  AppliedRate? CurrentRate, Guid? PriorDatasetId, AppliedRate? PriorRate, decimal ThresholdPercent, decimal ThresholdAmount,
  IReadOnlyList<CurrencyReviewLine> Lines);

/// <summary>
/// Upload-stage currency review: the trial balance converted at the approved closing rate for the period end (rate
/// source, date, direction and version shown), compared with the prior period's dataset converted the same way, with
/// variances above both a percentage and an absolute threshold highlighted. A missing or unapproved rate fails closed.
/// </summary>
public static class TrialBalanceCurrencyReviewQuery
{
  public static async Task<CommandResult<CurrencyReviewView>> GetAsync(IClientAccountingDbContext db, ActorContext actor, Guid datasetId,
    string presentationCurrency, decimal thresholdPercent = 10m, decimal thresholdAmount = 0m, CancellationToken ct = default)
  {
    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == datasetId && x.FirmId == actor.FirmId, ct);
    if (dataset is null) return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, dataset.ClientId, dataset.EngagementId,
      ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator", "Senior", "Staff"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<CurrencyReviewView>.Fail(auth.ErrorCode!, auth.Message!);
    var target = (presentationCurrency ?? string.Empty).Trim().ToUpperInvariant();
    if (target.Length != 3 || !target.All(char.IsLetter) || thresholdPercent < 0 || thresholdAmount < 0)
      return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a presentation currency and non-negative thresholds.");
    var period = dataset.PeriodId is null ? null : await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dataset.PeriodId, ct);
    if (period is null) return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.GateBlocked, "The dataset is not bound to a reporting period.");

    var rate = await RateAsync(db, actor.FirmId, dataset.Currency, target, period.EndDate, ct);
    if (rate is null)
      return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.GateBlocked,
        $"No approved closing rate {dataset.Currency}→{target} exists for {period.EndDate:yyyy-MM-dd}; conversion is refused rather than defaulted.");
    var current = await BalancesAsync(db, datasetId, ct);

    TrialBalanceDataset? prior = null;
    AppliedRate? priorRate = null;
    Dictionary<string, (string Name, decimal Amount)> priorBalances = [];
    if (period.PriorPeriodId is { } priorPeriodId)
    {
      prior = await db.TrialBalanceDatasets.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == dataset.ClientId && x.PeriodId == priorPeriodId &&
        x.ImportState == TrialBalanceImportStates.Sealed).OrderByDescending(x => x.ImportedAt).FirstOrDefaultAsync(ct);
      if (prior is not null)
      {
        var priorPeriod = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.Id == priorPeriodId, ct);
        priorRate = await RateAsync(db, actor.FirmId, prior.Currency, target, priorPeriod.EndDate, ct);
        if (priorRate is null)
          return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.GateBlocked,
            $"No approved closing rate {prior.Currency}→{target} exists for the prior period end {priorPeriod.EndDate:yyyy-MM-dd}.");
        priorBalances = await BalancesAsync(db, prior.Id, ct);
      }
    }
    var lines = current.Keys.Union(priorBalances.Keys).OrderBy(x => x, StringComparer.Ordinal).Select(code =>
    {
      var has = current.TryGetValue(code, out var now);
      var translated = has ? MoneyPolicy.Normalize(Math.Round(now.Amount * rate.Rate, 2, MidpointRounding.ToEven)) : 0m;
      decimal? priorTranslated = priorRate is not null && priorBalances.TryGetValue(code, out var was)
        ? MoneyPolicy.Normalize(Math.Round(was.Amount * priorRate.Rate, 2, MidpointRounding.ToEven)) : null;
      decimal? variance = prior is null ? null : translated - (priorTranslated ?? 0m);
      decimal? percent = priorTranslated is { } p && p != 0m ? Math.Round(variance!.Value / Math.Abs(p) * 100m, 1) : null;
      string? reason = prior is null ? null
        : !has ? "Account not in the current period"
        : priorTranslated is null ? "New account this period"
        : Math.Abs(variance!.Value) >= thresholdAmount && (percent is null || Math.Abs(percent.Value) >= thresholdPercent) && variance != 0m
          ? $"Moved {percent:0.#}% ({variance:N2} {target})" : null;
      return new CurrencyReviewLine(code, has ? now.Name : priorBalances[code].Name, has ? now.Amount : 0m, translated, priorTranslated, variance, percent, reason is not null, reason);
    }).ToList();
    return CommandResult<CurrencyReviewView>.Ok(new(datasetId, dataset.Currency, target, period.EndDate, rate, prior?.Id, priorRate, thresholdPercent, thresholdAmount, lines));
  }

  private static async Task<Dictionary<string, (string Name, decimal Amount)>> BalancesAsync(IAuditSphereDbContext db, Guid datasetId, CancellationToken ct) =>
    (await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == datasetId)
      .GroupBy(x => x.AccountCode).Select(g => new { g.Key, Name = g.Min(x => x.AccountName) ?? "", Amount = g.Sum(x => x.Amount) }).ToListAsync(ct))
    .ToDictionary(x => x.Key, x => (x.Name, x.Amount), StringComparer.Ordinal);

  private static async Task<AppliedRate?> RateAsync(IClientAccountingDbContext db, Guid firmId, string from, string to, DateOnly date, CancellationToken ct)
  {
    if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return new(from, to, "IDENTITY", date, 1m, ExchangeRateDirections.Direct, "Same currency", 0);
    var candidate = await db.ExchangeRates.AsNoTracking()
      .Join(db.ExchangeRateSetVersions.AsNoTracking(), r => r.RateSetVersionId, s => s.Id, (r, s) => new { r, s })
      .Where(x => x.r.FirmId == firmId && x.s.Status == AccountingWorkflowStates.Approved && x.r.FromCurrency == from && x.r.ToCurrency == to &&
        x.r.RateType == TranslationRatePurposes.Closing && x.r.RateDate == date && x.r.Direction == ExchangeRateDirections.Direct && x.r.Rate > 0)
      .OrderByDescending(x => x.s.ApprovedAt).ThenByDescending(x => x.s.Version).FirstOrDefaultAsync(ct);
    return candidate is null ? null : new(from, to, candidate.r.RateType, candidate.r.RateDate, candidate.r.Rate, candidate.r.Direction, candidate.s.Source, candidate.s.Version);
  }
}
