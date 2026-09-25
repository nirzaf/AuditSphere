using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;

namespace AuditSphereOps.Domain.Tests;

/// <summary>M26 per-line foreign-operation translation: rate purposes per line, a
/// calculated translation reserve with a rate bridge, and fail-closed missing rates.</summary>
public sealed class LineTranslationTests
{
  private static readonly Dictionary<string, string> SectionPurposes = new(StringComparer.Ordinal)
  {
    ["ASSETS"] = TranslationRatePurposes.Closing,
    ["LIABILITIES"] = TranslationRatePurposes.Closing,
    ["INCOME"] = TranslationRatePurposes.Average,
    ["EXPENSE"] = TranslationRatePurposes.Average,
    ["EQUITY"] = TranslationRatePurposes.Historical
  };

  private static readonly Dictionary<string, decimal> Rates = new(StringComparer.Ordinal)
  {
    [TranslationRatePurposes.Closing] = 4.0m,
    [TranslationRatePurposes.Average] = 3.6m,
    [TranslationRatePurposes.Historical] = 3.5m
  };

  private static readonly List<TranslationLineInput> Lines =
  [
    new("L1", "CASH", "ASSETS", 100m, "USD"),
    new("L2", "CAPITAL", "EQUITY", -80m, "USD"),
    new("L3", "REVENUE", "INCOME", -40m, "USD"),
    new("L4", "EXPENSE", "EXPENSE", 20m, "USD")
  ];

  [Fact(DisplayName = "Each line is translated at its own rate purpose, never one closing rate")]
  public void LinesUseTheirOwnRatePurpose()
  {
    var bridge = LineTranslationCalculator.Translate(Lines, new Dictionary<string, string>(), SectionPurposes, Rates, "QAR");
    Assert.Equal("USD", bridge.FunctionalCurrency);
    Assert.Equal("QAR", bridge.PresentationCurrency);
    var cash = bridge.Lines.Single(x => x.SourceLineId == "L1");
    Assert.Equal(TranslationRatePurposes.Closing, cash.RatePurpose);
    Assert.Equal(4.0m, cash.RateApplied);
    Assert.Equal(400m, cash.TranslatedAmount);
    var revenue = bridge.Lines.Single(x => x.SourceLineId == "L3");
    Assert.Equal(TranslationRatePurposes.Average, revenue.RatePurpose);
    Assert.Equal(3.6m, revenue.RateApplied);
    Assert.Equal(-144m, revenue.TranslatedAmount);
    var capital = bridge.Lines.Single(x => x.SourceLineId == "L2");
    Assert.Equal(TranslationRatePurposes.Historical, capital.RatePurpose);
    Assert.Equal(3.5m, capital.RateApplied);
    Assert.Equal(-280m, capital.TranslatedAmount);
    var expense = bridge.Lines.Single(x => x.SourceLineId == "L4");
    Assert.Equal(72m, expense.TranslatedAmount);

    // A single closing rate over every line would give a different, prohibited result.
    Assert.NotEqual(4.0m, revenue.RateApplied);
  }

  [Fact(DisplayName = "The translation reserve is a calculated bridge, not a balancing plug")]
  public void TranslationReserveIsCalculatedFromTheRateBridge()
  {
    var bridge = LineTranslationCalculator.Translate(Lines, new Dictionary<string, string>(), SectionPurposes, Rates, "QAR");
    // Equity at the historical rate is -280; at the closing rate it would be -320.
    Assert.Equal(-280m, bridge.EquityAtHistoricalRate);
    Assert.Equal(-320m, bridge.EquityAtClosingRate);
    Assert.Equal(-40m, bridge.TranslationReserveMovement);
    Assert.Equal(-40m, bridge.ClosingTranslationReserve);
    Assert.True(bridge.ReserveExplained);
    var reserve = bridge.Lines.Single(x => x.SourceLineId == "CTA_RESERVE");
    Assert.Equal(AccountingDefaults.CumulativeTranslationReserveSection, reserve.Section);
    Assert.Equal(-40m, reserve.TranslatedAmount);
    // The bridge sum equals the translated total including the reserve.
    Assert.Equal(bridge.Lines.Sum(x => x.TranslatedAmount), bridge.TranslatedTotal);
  }

  [Fact(DisplayName = "A selector overrides the section default and a wildcard is honoured")]
  public void SelectorsOverrideSectionDefaults()
  {
    var selectors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["REVENUE"] = TranslationRatePurposes.Transaction,
      ["PREPAY*"] = TranslationRatePurposes.Historical
    };
    var rates = new Dictionary<string, decimal>(Rates) { [TranslationRatePurposes.Transaction] = 3.7m };
    var lines = new List<TranslationLineInput>(Lines)
    {
      new("L5", "PREPAYMENTS", "ASSETS", 10m, "USD")
    };
    var bridge = LineTranslationCalculator.Translate(lines, selectors, SectionPurposes, rates, "QAR");
    Assert.Equal(TranslationRatePurposes.Transaction, bridge.Lines.Single(x => x.SourceLineId == "L3").RatePurpose);
    Assert.Equal(3.7m, bridge.Lines.Single(x => x.SourceLineId == "L3").RateApplied);
    // The wildcard selector beats the ASSETS closing default.
    var prepay = bridge.Lines.Single(x => x.SourceLineId == "L5");
    Assert.Equal(TranslationRatePurposes.Historical, prepay.RatePurpose);
    Assert.Equal(35m, prepay.TranslatedAmount);
  }

  [Fact(DisplayName = "Missing, zero and unspecified rates fail closed")]
  public void MissingRatesFailClosed()
  {
    // No average rate supplied for an income line.
    var withoutAverage = new Dictionary<string, decimal>
    {
      [TranslationRatePurposes.Closing] = 4.0m,
      [TranslationRatePurposes.Historical] = 3.5m
    };
    Assert.Throws<InvalidOperationException>(() =>
      LineTranslationCalculator.Translate(Lines, new Dictionary<string, string>(), SectionPurposes, withoutAverage, "QAR"));

    // A zero or negative rate is refused.
    var nonPositive = new Dictionary<string, decimal>(Rates) { [TranslationRatePurposes.Average] = 0m };
    Assert.Throws<InvalidOperationException>(() =>
      LineTranslationCalculator.Translate(Lines, new Dictionary<string, string>(), SectionPurposes, nonPositive, "QAR"));

    // A line whose section has no purpose defined fails closed rather than defaulting.
    var unknownSection = new List<TranslationLineInput> { new("X", "OTHER", "SOMETHING_ELSE", 10m, "USD") };
    Assert.Throws<InvalidOperationException>(() =>
      LineTranslationCalculator.Translate(unknownSection, new Dictionary<string, string>(), SectionPurposes, Rates, "QAR"));

    // Mixed functional currencies are refused.
    var mixed = new List<TranslationLineInput> { new("A", "CASH", "ASSETS", 1m, "USD"), new("B", "CASH", "ASSETS", 1m, "EUR") };
    Assert.Throws<ArgumentException>(() =>
      LineTranslationCalculator.Translate(mixed, new Dictionary<string, string>(), SectionPurposes, Rates, "QAR"));
  }

  [Fact(DisplayName = "Same-currency translation is identity and needs no rate")]
  public void SameCurrencyNeedsNoRate()
  {
    var lines = new List<TranslationLineInput>
    {
      new("L1", "CASH", "ASSETS", 100m, "QAR"),
      new("L2", "CAPITAL", "EQUITY", -100m, "QAR")
    };
    var bridge = LineTranslationCalculator.Translate(lines, new Dictionary<string, string>(), SectionPurposes,
      new Dictionary<string, decimal>(), "QAR");
    Assert.Equal(100m, bridge.Lines.Single(x => x.SourceLineId == "L1").TranslatedAmount);
    Assert.Equal(-100m, bridge.Lines.Single(x => x.SourceLineId == "L2").TranslatedAmount);
    Assert.Equal(1m, bridge.Lines.Single(x => x.SourceLineId == "L1").RateApplied);
    Assert.Equal(0m, bridge.TranslationReserveMovement);
    // The pair nets to zero and no reserve line is added when the movement is zero.
    Assert.Equal(0m, bridge.TranslatedTotal);
    Assert.Equal(2, bridge.Lines.Count);
  }

  [Fact(DisplayName = "Equity-only movements produce an explained reserve without income lines")]
  public void EquityOnlyStillBridgesTheReserve()
  {
    var lines = new List<TranslationLineInput>
    {
      new("E1", "CAPITAL", "EQUITY", -100m, "USD"),
      new("A1", "CASH", "ASSETS", 100m, "USD")
    };
    var bridge = LineTranslationCalculator.Translate(lines, new Dictionary<string, string>(), SectionPurposes, Rates, "QAR");
    Assert.True(bridge.ReserveExplained);
    Assert.Equal(-350m, bridge.EquityAtHistoricalRate);
    Assert.Equal(-400m, bridge.EquityAtClosingRate);
    Assert.Equal(-50m, bridge.TranslationReserveMovement);
  }
}
