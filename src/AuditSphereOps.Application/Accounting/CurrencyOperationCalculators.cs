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
    decimal closingRate, decimal historicalRate)
  {
    if (closingRate <= 0m || historicalRate <= 0m)
      throw new InvalidOperationException("Remeasurement requires positive closing and historical rates.");
    var rate = monetary ? closingRate : historicalRate;
    var exact = amount * rate;
    var remeasured = CurrencyTranslationCalculator.Translate(amount, fromCurrency, toCurrency, rate);
    return new MonetaryRemeasurementResult(
      MoneyPolicy.Normalize(amount), remeasured, monetary ? "CLOSING" : "HISTORICAL",
      MoneyPolicy.Normalize(remeasured - amount), MoneyPolicy.Normalize(remeasured - exact));
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
