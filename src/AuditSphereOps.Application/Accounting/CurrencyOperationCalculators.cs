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
  IReadOnlyList<TranslatedLine> Lines);

/// <summary>
/// Per-line foreign-operation translation. Each line's rate purpose is resolved from the
/// policy rules by taxonomy selector, so assets and liabilities use the closing rate while
/// income and expenses use the transaction or representative average rate and equity uses
/// the historical rate. The platform never converts every statement line at one closing
/// rate, and a purpose that has no rate supplied fails closed.
/// </summary>
public static class LineTranslationCalculator
{
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
    return sectionPurposes.TryGetValue(NormalizeSection(line.Section), out var purpose)
      ? purpose
      : throw new InvalidOperationException(
        $"No rate purpose is defined for line '{line.SourceLineId}' in section '{line.Section}'.");
  }

  public static LineTranslationBridge Translate(
    IReadOnlyList<TranslationLineInput> lines,
    IReadOnlyDictionary<string, string> selectorPurposes,
    IReadOnlyDictionary<string, string> sectionPurposes,
    IReadOnlyDictionary<string, decimal> ratesByPurpose,
    string presentationCurrency)
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

    // The cumulative translation reserve is the difference between equity translated at
    // historical rates and the same equity translated at the closing rate. It is a
    // calculated result with a rate bridge, never a balancing plug.
    var equityLines = translated.Where(x => IsEquity(x.Section)).ToList();
    var equityHistorical = MoneyPolicy.Normalize(equityLines.Sum(x => x.TranslatedAmount));
    var closingRate = string.Equals(functional, currency, StringComparison.Ordinal) ? 1m
      : ratesByPurpose.TryGetValue(TranslationRatePurposes.Closing, out var closing) ? closing
        : throw new InvalidOperationException("The closing rate is required to calculate the translation reserve bridge.");
    var equityAtClosing = MoneyPolicy.Normalize(equityLines.Sum(x =>
      CurrencyTranslationCalculator.Translate(x.FunctionalAmount, functional, currency, closingRate)));
    var reserveMovement = MoneyPolicy.Normalize(equityAtClosing - equityHistorical);

    var linesWithReserve = new List<TranslatedLine>(translated);
    if (reserveMovement != 0m)
      linesWithReserve.Add(new TranslatedLine(
        "CTA_RESERVE", "CTA", AccountingDefaults.CumulativeTranslationReserveSection, 0m,
        TranslationRatePurposes.Closing, closingRate, reserveMovement));

    return new LineTranslationBridge(
      functional, currency,
      MoneyPolicy.Normalize(lines.Sum(x => x.FunctionalAmount)),
      MoneyPolicy.Normalize(linesWithReserve.Sum(x => x.TranslatedAmount)),
      equityHistorical, equityAtClosing, reserveMovement, reserveMovement,
      ReserveExplained: reserveMovement == 0m || equityLines.Count > 0,
      linesWithReserve);
  }

  private static bool Matches(string selector, string value) =>
    !string.IsNullOrWhiteSpace(value) &&
    (string.Equals(selector, value.Trim(), StringComparison.OrdinalIgnoreCase) ||
     (selector.EndsWith('*') && value.Trim().StartsWith(selector[..^1], StringComparison.OrdinalIgnoreCase)));

  private static string NormalizeSection(string section) => (section ?? string.Empty).Trim().ToUpperInvariant();

  private static bool IsEquity(string section) => NormalizeSection(section) is
    "EQUITY" or "CAPITAL" or "RESERVES" or "RETAINED_EARNINGS" or "OCI" or "CHANGES_IN_EQUITY";
}
