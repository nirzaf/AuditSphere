using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Accounting;

public sealed record MonetaryRemeasurementResult(
  decimal OriginalAmount,
  decimal RemeasuredAmount,
  string RateBasis,
  decimal ForeignExchangeAdjustment,
  decimal RoundingAdjustment);

public sealed record ForeignOperationTranslationResult(
  decimal ClosingNetAssetsTranslated,
  decimal OpeningNetAssetsTranslated,
  decimal CurrentProfitTranslated,
  decimal TranslationReserveMovement,
  decimal ClosingTranslationReserve,
  decimal RoundingAdjustment);

public static class CurrencyRemeasurementCalculator
{
  public static MonetaryRemeasurementResult Remeasure(
    decimal amount, string fromCurrency, string toCurrency, bool monetary,
    decimal closingRate, decimal historicalRate, decimal priorCarryingAmountInFunctionalCurrency)
  {
    var rate = monetary ? closingRate : historicalRate;
    if (rate <= 0m)
      throw new InvalidOperationException("Remeasurement requires a positive rate for the selected item classification.");
    var exact = amount * rate;
    var remeasured = CurrencyTranslationCalculator.Translate(amount, fromCurrency, toCurrency, rate);
    return new MonetaryRemeasurementResult(
      MoneyPolicy.Normalize(amount), remeasured, monetary ? "CLOSING" : "HISTORICAL",
      monetary ? MoneyPolicy.Normalize(remeasured - priorCarryingAmountInFunctionalCurrency) : 0m,
      MoneyPolicy.Normalize(remeasured - exact));
  }
}

public static class ForeignOperationTranslationCalculator
{
  public static ForeignOperationTranslationResult Translate(
    decimal openingNetAssets, decimal closingNetAssets, decimal currentProfit,
    decimal openingRate, decimal closingRate, decimal averageRate,
    decimal openingTranslationReserve, string functionalCurrency, string presentationCurrency)
  {
    if (openingRate <= 0m || closingRate <= 0m || averageRate <= 0m)
      throw new InvalidOperationException("Foreign-operation translation requires positive opening, closing and average rates.");

    var openingTranslated = CurrencyTranslationCalculator.Translate(
      openingNetAssets, functionalCurrency, presentationCurrency, openingRate);
    var closingTranslated = CurrencyTranslationCalculator.Translate(
      closingNetAssets, functionalCurrency, presentationCurrency, closingRate);
    var profitTranslated = CurrencyTranslationCalculator.Translate(
      currentProfit, functionalCurrency, presentationCurrency, averageRate);
    var movement = MoneyPolicy.Normalize(closingTranslated - openingTranslated - profitTranslated);
    var closingReserve = MoneyPolicy.Normalize(openingTranslationReserve + movement);
    var exactClosing = closingNetAssets * closingRate;
    return new ForeignOperationTranslationResult(
      closingTranslated, openingTranslated, profitTranslated, movement, closingReserve,
      MoneyPolicy.Normalize(closingTranslated - exactClosing));
  }
}

public static class DisplayCurrencyConversionCalculator
{
  public static decimal Convert(decimal amount, string fromCurrency, string toCurrency, decimal rate) =>
    CurrencyTranslationCalculator.Translate(amount, fromCurrency, toCurrency, rate);
}

public static class TranslationRatePurposes
{
  /// <summary>Assets and liabilities: the closing rate at the reporting date.</summary>
  public const string Closing = "CLOSING";
  /// <summary>Income and expenses: the transaction-date rate.</summary>
  public const string Transaction = "TRANSACTION";
  /// <summary>Income and expenses: a documented representative average.</summary>
  public const string Average = "AVERAGE";
  /// <summary>Equity: the historical rate at contribution.</summary>
  public const string Historical = "HISTORICAL";
  /// <summary>Carry the opening translated balance forward without retranslation.</summary>
  public const string CarryForward = "CARRY_FORWARD";
  public static readonly string[] All = [Closing, Transaction, Average, Historical, CarryForward];
}

public sealed record TranslationLineInput(
  string SourceLineId, string TaxonomyCode, string Section, decimal FunctionalAmount, string FunctionalCurrency);

/// <summary>
/// One line's translation: the purpose applied, the rate used and the translated amount.
/// The rate type actually consumed is reported so the bridge is reproducible from evidence.
/// </summary>
public sealed record TranslatedLine(
  string SourceLineId, string TaxonomyCode, string Section,
  decimal FunctionalAmount, string RatePurpose, decimal RateApplied, decimal TranslatedAmount);

public sealed record LineTranslationBridge(
  string FunctionalCurrency, string PresentationCurrency,
  decimal FunctionalTotal, decimal TranslatedTotal,
  decimal EquityAtHistoricalRate, decimal EquityAtClosingRate,
  decimal TranslationReserveMovement, decimal ClosingTranslationReserve,
  bool ReserveExplained,
  IReadOnlyList<TranslatedLine> Lines,
  decimal ProfitAtAverageRate = 0m,
  decimal ProfitAtClosingRate = 0m,
  decimal OpeningTranslationReserve = 0m);

/// <summary>
/// Per-line foreign-operation translation. Each line's rate purpose is resolved from the
/// policy rules by taxonomy selector, so assets and liabilities use the closing rate while
/// income and expenses use the transaction or representative average rate and equity uses
/// the historical rate. The platform never converts every statement line at one closing
/// rate, and a purpose that has no rate supplied fails closed.
/// </summary>
public static class LineTranslationCalculator
{
  public static readonly IReadOnlyDictionary<string, string> DefaultSectionPurposes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
  {
    ["ASSETS"] = TranslationRatePurposes.Closing,
    ["ASSET"] = TranslationRatePurposes.Closing,
    ["CURRENT_ASSETS"] = TranslationRatePurposes.Closing,
    ["NON_CURRENT_ASSETS"] = TranslationRatePurposes.Closing,
    ["LIABILITIES"] = TranslationRatePurposes.Closing,
    ["LIABILITY"] = TranslationRatePurposes.Closing,
    ["CURRENT_LIABILITIES"] = TranslationRatePurposes.Closing,
    ["NON_CURRENT_LIABILITIES"] = TranslationRatePurposes.Closing,
    ["EQUITY"] = TranslationRatePurposes.Historical,
    ["CAPITAL"] = TranslationRatePurposes.Historical,
    ["RESERVES"] = TranslationRatePurposes.Historical,
    ["RETAINED_EARNINGS"] = TranslationRatePurposes.Historical,
    ["OCI"] = TranslationRatePurposes.Historical,
    ["CHANGES_IN_EQUITY"] = TranslationRatePurposes.Historical,
    ["CTA_RESERVE"] = TranslationRatePurposes.Closing,
    ["INCOME"] = TranslationRatePurposes.Average,
    ["REVENUE"] = TranslationRatePurposes.Average,
    ["EXPENSE"] = TranslationRatePurposes.Average,
    ["EXPENSES"] = TranslationRatePurposes.Average,
    ["PROFIT_LOSS"] = TranslationRatePurposes.Average,
    ["PL"] = TranslationRatePurposes.Average,
    ["COST_OF_SALES"] = TranslationRatePurposes.Average,
    ["OPERATING_EXPENSES"] = TranslationRatePurposes.Average,
    ["FINANCE_COSTS"] = TranslationRatePurposes.Average,
    ["TAX"] = TranslationRatePurposes.Average,
    ["TAXATION"] = TranslationRatePurposes.Average,
  };

  /// <summary>Resolves the rate purpose for a line: the first matching selector wins, and
  /// a section default applies when no selector matches.</summary>
  public static string ResolvePurpose(
    TranslationLineInput line,
    IReadOnlyDictionary<string, string> selectorPurposes,
    IReadOnlyDictionary<string, string> sectionPurposes)
  {
    ArgumentNullException.ThrowIfNull(line);
    foreach (var selector in selectorPurposes)
    {
      var key = selector.Key.Trim();
      if (key.Length == 0) continue;
      if (Matches(key, line.TaxonomyCode) || Matches(key, line.SourceLineId)) return selector.Value;
    }
    var normalizedSection = NormalizeSection(line.Section);
    if (sectionPurposes.TryGetValue(normalizedSection, out var purpose))
      return purpose;
    if (DefaultSectionPurposes.TryGetValue(normalizedSection, out var defaultPurpose))
      return defaultPurpose;
    throw new InvalidOperationException(
      $"No rate purpose is defined for line '{line.SourceLineId}' in section '{line.Section}'.");
  }

  public static LineTranslationBridge Translate(
    IReadOnlyList<TranslationLineInput> lines,
    IReadOnlyDictionary<string, string> selectorPurposes,
    IReadOnlyDictionary<string, string> sectionPurposes,
    IReadOnlyDictionary<string, decimal> ratesByPurpose,
    string presentationCurrency,
    decimal openingTranslationReserve = 0m)
  {
    ArgumentNullException.ThrowIfNull(lines);
    if (lines.Count == 0)
      throw new ArgumentException("A line translation requires at least one line.", nameof(lines));
    if (string.IsNullOrWhiteSpace(presentationCurrency) || presentationCurrency.Trim().Length != 3)
      throw new ArgumentException("A three-letter presentation currency is required.", nameof(presentationCurrency));
    var currency = presentationCurrency.Trim().ToUpperInvariant();
    var functionalCurrencies = lines.Select(x => (x.FunctionalCurrency ?? string.Empty).Trim().ToUpperInvariant())
      .Distinct(StringComparer.Ordinal).ToArray();
    if (functionalCurrencies.Length != 1 || functionalCurrencies[0].Length != 3)
      throw new ArgumentException("All lines must share one functional currency.", nameof(lines));
    var functional = functionalCurrencies[0];

    var translated = new List<TranslatedLine>(lines.Count);
    foreach (var line in lines)
    {
      var purpose = ResolvePurpose(line, selectorPurposes, sectionPurposes);
      if (!TranslationRatePurposes.All.Contains(purpose))
        throw new InvalidOperationException($"Line '{line.SourceLineId}' resolves to unsupported rate purpose '{purpose}'.");
      // Same functional and presentation currency is identity, not a missing rate.
      var rate = string.Equals(functional, currency, StringComparison.Ordinal)
        ? 1m
        : ratesByPurpose.TryGetValue(purpose, out var supplied) ? supplied
          : throw new InvalidOperationException(
            $"The {purpose} rate is required to translate line '{line.SourceLineId}' from {functional} into {currency}.");
      if (rate <= 0m)
        throw new InvalidOperationException($"The {purpose} rate for line '{line.SourceLineId}' must be greater than zero.");
      translated.Add(new TranslatedLine(line.SourceLineId, line.TaxonomyCode, line.Section,
        MoneyPolicy.Normalize(line.FunctionalAmount), purpose, rate,
        CurrencyTranslationCalculator.Translate(line.FunctionalAmount, functional, currency, rate)));
    }

    // Under IAS 21, the cumulative translation reserve is calculated through the rate bridge:
    // 1. Difference on equity items translated at historical rates vs closing rate.
    // 2. Difference on income and expense items translated at average/transaction rates vs closing rate.
    // 3. Difference on any carried forward/other non-closing rate items.
    // It is a calculated, fully explained result from exchange rate movements, never a balancing plug.
    var closingRate = string.Equals(functional, currency, StringComparison.Ordinal) ? 1m
      : ratesByPurpose.TryGetValue(TranslationRatePurposes.Closing, out var closing) ? closing
        : throw new InvalidOperationException("The closing rate is required to calculate the translation reserve bridge.");

    var equityLines = translated.Where(x => IsEquity(x.Section)).ToList();
    var equityHistorical = MoneyPolicy.Normalize(equityLines.Sum(x => x.TranslatedAmount));
    var equityAtClosing = MoneyPolicy.Normalize(equityLines.Sum(x =>
      CurrencyTranslationCalculator.Translate(x.FunctionalAmount, functional, currency, closingRate)));
    var equityDifference = MoneyPolicy.Normalize(equityAtClosing - equityHistorical);

    var pnlLines = translated.Where(x => IsProfitOrLoss(x.Section)).ToList();
    var profitAtAverage = MoneyPolicy.Normalize(pnlLines.Sum(x => x.TranslatedAmount));
    var profitAtClosing = MoneyPolicy.Normalize(pnlLines.Sum(x =>
      CurrencyTranslationCalculator.Translate(x.FunctionalAmount, functional, currency, closingRate)));
    var profitDifference = MoneyPolicy.Normalize(profitAtClosing - profitAtAverage);

    var otherNonClosingLines = translated.Where(x => !IsEquity(x.Section) && !IsProfitOrLoss(x.Section)).ToList();
    var otherDifference = MoneyPolicy.Normalize(otherNonClosingLines.Sum(x =>
      CurrencyTranslationCalculator.Translate(x.FunctionalAmount, functional, currency, closingRate) - x.TranslatedAmount));

    var reserveMovement = MoneyPolicy.Normalize(equityDifference + profitDifference + otherDifference);
    var closingReserve = MoneyPolicy.Normalize(openingTranslationReserve + reserveMovement);

    var linesWithReserve = new List<TranslatedLine>(translated);
    if (closingReserve != 0m)
      linesWithReserve.Add(new TranslatedLine(
        "CTA_RESERVE", "CTA", AccountingDefaults.CumulativeTranslationReserveSection, 0m,
        TranslationRatePurposes.Closing, closingRate, closingReserve));

    return new LineTranslationBridge(
      functional, currency,
      MoneyPolicy.Normalize(lines.Sum(x => x.FunctionalAmount)),
      MoneyPolicy.Normalize(linesWithReserve.Sum(x => x.TranslatedAmount)),
      equityHistorical, equityAtClosing, reserveMovement, closingReserve,
      ReserveExplained: reserveMovement == 0m || equityLines.Count > 0 || pnlLines.Count > 0,
      linesWithReserve,
      profitAtAverage, profitAtClosing, openingTranslationReserve);
  }

  private static bool Matches(string selector, string value) =>
    !string.IsNullOrWhiteSpace(value) &&
    (string.Equals(selector, value.Trim(), StringComparison.OrdinalIgnoreCase) ||
     (selector.EndsWith('*') && value.Trim().StartsWith(selector[..^1], StringComparison.OrdinalIgnoreCase)));

  private static string NormalizeSection(string section) => (section ?? string.Empty).Trim().ToUpperInvariant();

  public static bool IsEquity(string section) => NormalizeSection(section) is
    "EQUITY" or "CAPITAL" or "RESERVES" or "RETAINED_EARNINGS" or "OCI" or "CHANGES_IN_EQUITY";

  public static bool IsProfitOrLoss(string section) => NormalizeSection(section) is
    "INCOME" or "EXPENSE" or "REVENUE" or "PROFIT_LOSS" or "PL" or "COST_OF_SALES" or
    "OPERATING_EXPENSES" or "FINANCE_COSTS" or "TAX" or "TAXATION";

  public static string InferSection(string taxonomyCodeOrAccount)
  {
    if (string.IsNullOrWhiteSpace(taxonomyCodeOrAccount)) return "ASSETS";
    var normalized = NormalizeSection(taxonomyCodeOrAccount);
    if (IsEquity(normalized)) return "EQUITY";
    if (IsProfitOrLoss(normalized)) return "EXPENSE";
    if (normalized.StartsWith("CASH", StringComparison.Ordinal) ||
        normalized.StartsWith("BANK", StringComparison.Ordinal) ||
        normalized.StartsWith("RECEIV", StringComparison.Ordinal) ||
        normalized.StartsWith("INVENT", StringComparison.Ordinal) ||
        normalized.StartsWith("PREPAY", StringComparison.Ordinal) ||
        normalized.StartsWith("ASSET", StringComparison.Ordinal) ||
        normalized.StartsWith("PPE", StringComparison.Ordinal) ||
        normalized.StartsWith("DEPOSIT", StringComparison.Ordinal))
      return "ASSETS";
    if (normalized.StartsWith("PAYAB", StringComparison.Ordinal) ||
        normalized.StartsWith("ACCRU", StringComparison.Ordinal) ||
        normalized.StartsWith("LOAN", StringComparison.Ordinal) ||
        normalized.StartsWith("BORROW", StringComparison.Ordinal) ||
        normalized.StartsWith("LIABIL", StringComparison.Ordinal) ||
        normalized.StartsWith("PROVIS", StringComparison.Ordinal))
      return "LIABILITIES";
    if (normalized.StartsWith("CAPITAL", StringComparison.Ordinal) ||
        normalized.StartsWith("SHARE", StringComparison.Ordinal) ||
        normalized.StartsWith("EQUITY", StringComparison.Ordinal) ||
        normalized.StartsWith("RETAIN", StringComparison.Ordinal) ||
        normalized.StartsWith("RESERV", StringComparison.Ordinal) ||
        normalized.StartsWith("CTA", StringComparison.Ordinal))
      return "EQUITY";
    if (normalized.StartsWith("REV", StringComparison.Ordinal) ||
        normalized.StartsWith("SALE", StringComparison.Ordinal) ||
        normalized.StartsWith("INCOME", StringComparison.Ordinal) ||
        normalized.StartsWith("TURNOVER", StringComparison.Ordinal))
      return "INCOME";
    if (normalized.StartsWith("EXP", StringComparison.Ordinal) ||
        normalized.StartsWith("COST", StringComparison.Ordinal) ||
        normalized.StartsWith("COS", StringComparison.Ordinal) ||
        normalized.StartsWith("COG", StringComparison.Ordinal) ||
        normalized.StartsWith("SALARY", StringComparison.Ordinal) ||
        normalized.StartsWith("WAGE", StringComparison.Ordinal) ||
        normalized.StartsWith("DEPREC", StringComparison.Ordinal) ||
        normalized.StartsWith("AMORT", StringComparison.Ordinal) ||
        normalized.StartsWith("INTEREST", StringComparison.Ordinal) ||
        normalized.StartsWith("TAX", StringComparison.Ordinal) ||
        normalized.StartsWith("FEE", StringComparison.Ordinal) ||
        normalized.StartsWith("RENT", StringComparison.Ordinal))
      return "EXPENSE";
    return "ASSETS";
  }
}
