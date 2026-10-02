using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record AppliedRate(string FromCurrency, string ToCurrency, string RateType, DateOnly RateDate, decimal Rate, string Direction, string Source, int SetVersion,
  Guid? RateSetId = null, Guid? ObservationId = null, string? SetCode = null, DateOnly? EffectiveFrom = null, DateOnly? EffectiveTo = null);
public sealed record CurrencyReviewLine(string AccountCode, string AccountName, decimal SourceAmount, decimal TranslatedAmount,
  decimal? PriorTranslatedAmount, decimal? Variance, decimal? VariancePercent, bool Highlighted, string? HighlightReason);
public sealed record CurrencyReviewView(Guid DatasetId, string SourceCurrency, string PresentationCurrency, DateOnly PeriodEnd,
  AppliedRate? CurrentRate, Guid? PriorDatasetId, AppliedRate? PriorRate, decimal ThresholdPercent, decimal ThresholdAmount,
  IReadOnlyList<CurrencyReviewLine> Lines, Guid ClientId, Guid EngagementId, Guid PeriodId, DateOnly? PriorPeriodEnd,
  string Method, string Rounding, string Revision);

/// <summary>Read-only upload closing-rate comparison. This is neither classification-based IAS 21 translation nor approval.</summary>
public static class TrialBalanceCurrencyReviewQuery
{
  private static readonly string[] Roles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator", "Senior", "Staff"];
  private static Task<CommandResult> Auth(IClientAccountingDbContext db, ActorContext actor, TrialBalanceDataset d, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, d.ClientId, d.EngagementId, Roles, InternalOnly: true), ct);
  private static string Digest(object value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
  public static async Task<CommandResult<CurrencyReviewView>> GetAsync(IClientAccountingDbContext db, ActorContext actor, Guid datasetId,
    string presentationCurrency, decimal thresholdPercent = 10m, decimal thresholdAmount = 0m, CancellationToken ct = default)
  {
    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == datasetId && x.FirmId == actor.FirmId, ct);
    if (dataset is null) return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await Auth(db, actor, dataset, ct);
    if (!auth.Succeeded) return CommandResult<CurrencyReviewView>.Fail(auth.ErrorCode!, auth.Message!);
    var target = (presentationCurrency ?? "").Trim().ToUpperInvariant();
    if (target.Length != 3 || target.Any(c => c is < 'A' or > 'Z') || thresholdPercent < 0 || thresholdAmount < 0)
      return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a three-letter presentation currency and non-negative thresholds.");
    if (dataset.ImportState != TrialBalanceImportStates.Sealed)
      return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.GateBlocked, "Finish sealing the source dataset before requesting currency review.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dataset.PeriodId && x.FirmId == actor.FirmId && x.ClientId == dataset.ClientId, ct);
    if (period is null) return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.GateBlocked, "The dataset is not bound to a reporting period.");
    var rate = await RateAsync(db, actor.FirmId, dataset.Currency, target, period.EndDate, ct);
    if (rate is null) return MissingRate(dataset.Currency, target, period.EndDate);
    var current = await BalancesAsync(db, dataset.Id, ct);
    if (current is null) return WindowExceeded();

    TrialBalanceDataset? prior = null;
    AppliedRate? priorRate = null;
    DateOnly? priorEnd = null;
    string? priorPeriodFingerprint = null;
    Dictionary<string, (string Name, decimal Amount)> priorBalances = [];
    if (period.PriorPeriodId is { } priorPeriodId)
    {
      // Filter before selecting/counting. A client-wide grant covers prior engagements; an engagement grant never widens.
      var now = DateTimeOffset.UtcNow;
      prior = await db.TrialBalanceDatasets.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == dataset.ClientId && x.PeriodId == priorPeriodId &&
        x.ImportState == TrialBalanceImportStates.Sealed && db.RoleGrants.Any(g => g.FirmId == actor.FirmId && g.UserId == actor.UserId && g.RevokedAt == null &&
          (g.ExpiresAt == null || g.ExpiresAt > now) && Roles.Contains(g.Role) &&
          ((g.ClientId == null && g.EngagementId == null) || (g.ClientId == x.ClientId && (g.EngagementId == null || g.EngagementId == x.EngagementId)))))
        .OrderByDescending(x => x.ImportedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
      if (prior is not null)
      {
        if (!(await Auth(db, actor, prior, ct)).Succeeded) return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        var priorPeriod = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == priorPeriodId && x.FirmId == actor.FirmId && x.ClientId == dataset.ClientId, ct);
        if (priorPeriod is null) return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.GateBlocked, "The prior source is not bound to an available reporting period.");
        priorPeriodFingerprint = Digest(priorPeriod);
        priorEnd = priorPeriod.EndDate;
        priorRate = await RateAsync(db, actor.FirmId, prior.Currency, target, priorPeriod.EndDate, ct);
        if (priorRate is null) return MissingRate(prior.Currency, target, priorPeriod.EndDate);
        var balances = await BalancesAsync(db, prior.Id, ct);
        if (balances is null) return WindowExceeded();
        priorBalances = balances;
      }
    }
    var codes = current.Keys.Union(priorBalances.Keys).OrderBy(x => x, StringComparer.Ordinal).Take(20001).ToList();
    if (codes.Count > 20000) return WindowExceeded();
    List<CurrencyReviewLine> lines;
    try
    {
      lines = codes.Select(code =>
      {
        var has = current.TryGetValue(code, out var value);
        var translated = has ? MoneyPolicy.Normalize(Math.Round(value.Amount * rate.Rate, 2, MidpointRounding.ToEven)) : 0m;
        decimal? priorTranslated = priorRate is not null && priorBalances.TryGetValue(code, out var was)
          ? MoneyPolicy.Normalize(Math.Round(was.Amount * priorRate.Rate, 2, MidpointRounding.ToEven)) : null;
        decimal? variance = prior is null ? null : translated - (priorTranslated ?? 0m);
        decimal? percent = priorTranslated is { } p && p != 0m ? Math.Round(variance!.Value / Math.Abs(p) * 100m, 1) : null;
        string? reason = prior is null ? null : !has ? "Account not in the current period" : priorTranslated is null ? "New account this period"
          : Math.Abs(variance!.Value) >= thresholdAmount && (percent is null || Math.Abs(percent.Value) >= thresholdPercent) && variance != 0m
            ? percent is null ? "Absolute movement exceeds the threshold; percentage is undefined for a zero prior balance" : "Movement exceeds both configured review thresholds" : null;
        return new CurrencyReviewLine(code, has ? value.Name : priorBalances[code].Name, has ? value.Amount : 0m, translated, priorTranslated, variance, percent, reason is not null, reason);
      }).ToList();
    }
    catch (OverflowException) { return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.GateBlocked, "Amounts exceed the supported currency calculation range; review the source and rate inputs."); }

    // Do not publish a result after authority or a selected immutable revision has changed during the read.
    auth = await Auth(db, actor, dataset, ct);
    if (!auth.Succeeded) return CommandResult<CurrencyReviewView>.Fail(auth.ErrorCode!, auth.Message!);
    if (prior is not null && !(await Auth(db, actor, prior, ct)).Succeeded) return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var currentSource = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == dataset.Id && x.FirmId == actor.FirmId, ct);
    var priorSource = prior is null ? null : await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == prior.Id && x.FirmId == actor.FirmId, ct);
    var currentPeriod = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.Id == period.Id && x.FirmId == actor.FirmId && x.ClientId == dataset.ClientId, ct);
    var currentPriorPeriod = prior is null ? null : await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == prior.PeriodId && x.FirmId == actor.FirmId && x.ClientId == dataset.ClientId, ct);
    if ((prior is not null && (currentPriorPeriod is null || priorPeriodFingerprint != Digest(currentPriorPeriod))) || Digest(period) != Digest(currentPeriod) || Digest(dataset) != Digest(currentSource) || (prior is not null && Digest(prior) != Digest(priorSource!)) ||
      rate != await RateAsync(db, actor.FirmId, dataset.Currency, target, period.EndDate, ct) ||
      (prior is not null && priorRate != await RateAsync(db, actor.FirmId, prior.Currency, target, priorEnd!.Value, ct)))
      return CommandResult<CurrencyReviewView>.Fail(ErrorCodes.StaleRevision, "Source or rate inputs changed. Request a fresh currency review.");
    var revision = Digest(new { actor.FirmId, actor.UserId, actor.SessionEpoch, dataset, period, prior, rate, priorRate, thresholdPercent, thresholdAmount, lines });
    return CommandResult<CurrencyReviewView>.Ok(new(datasetId, dataset.Currency, target, period.EndDate, rate, prior?.Id, priorRate, thresholdPercent, thresholdAmount, lines,
      dataset.ClientId, dataset.EngagementId, period.Id, priorEnd, "UPLOAD_CLOSING_COMPARISON", "Two decimal places; midpoint to even", revision));
  }
  private static CommandResult<CurrencyReviewView> MissingRate(string from, string to, DateOnly date) =>
    CommandResult<CurrencyReviewView>.Fail(ErrorCodes.GateBlocked, $"No valid approved DIRECT closing rate {from}→{to} is available for {date:yyyy-MM-dd}. Review the rate set, effective dates, direction and positive observation; conversion is refused.");
  private static CommandResult<CurrencyReviewView> WindowExceeded() => CommandResult<CurrencyReviewView>.Fail("window.exceeded", "The authorized source exceeds the interactive account window. Use a bounded source review.");
  private static async Task<Dictionary<string, (string Name, decimal Amount)>?> BalancesAsync(IAuditSphereDbContext db, Guid datasetId, CancellationToken ct)
  {
    var rows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == datasetId).GroupBy(x => x.AccountCode)
      .Select(g => new { g.Key, Name = g.Min(x => x.AccountName) ?? "", Amount = g.Sum(x => x.Amount) }).OrderBy(x => x.Key).Take(20001).ToListAsync(ct);
    return rows.Count > 20000 ? null : rows.ToDictionary(x => x.Key, x => (x.Name, x.Amount), StringComparer.Ordinal);
  }
  private static async Task<AppliedRate?> RateAsync(IClientAccountingDbContext db, Guid firmId, string from, string to, DateOnly date, CancellationToken ct)
  {
    if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return new(from, to, "IDENTITY", date, 1m, ExchangeRateDirections.Direct, "Same currency", 0);
    // Select the latest approved observation first: an invalid latest rate cannot silently fall back to an older positive rate.
    var candidate = await db.ExchangeRates.AsNoTracking().Join(db.ExchangeRateSetVersions.AsNoTracking(), r => r.RateSetVersionId, s => s.Id, (r, s) => new { r, s })
      .Where(x => x.r.FirmId == firmId && x.s.FirmId == firmId && x.s.Status == AccountingWorkflowStates.Approved && x.r.FromCurrency == from && x.r.ToCurrency == to &&
        x.r.RateType == TranslationRatePurposes.Closing && x.r.RateDate == date)
      .OrderByDescending(x => x.s.ApprovedAt).ThenByDescending(x => x.s.Version).ThenByDescending(x => x.s.Id).FirstOrDefaultAsync(ct);
    if (candidate is null || candidate.r.Rate <= 0 || candidate.r.Direction != ExchangeRateDirections.Direct ||
      candidate.s.EffectiveFrom > date || candidate.s.EffectiveTo < date) return null;
    return new(from, to, candidate.r.RateType, candidate.r.RateDate, candidate.r.Rate, candidate.r.Direction, candidate.s.Source, candidate.s.Version,
      candidate.s.Id, candidate.r.Id, candidate.s.Code, candidate.s.EffectiveFrom, candidate.s.EffectiveTo);
  }
}
