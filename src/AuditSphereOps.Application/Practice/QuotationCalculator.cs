using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Practice;

/// <summary>One priced line: hours for a role/activity at the approved rate-card rate (rate is an input, never looked up here).</summary>
public sealed record QuotationLineInput(string Role, string Activity, decimal Hours, decimal RatePerHour, Guid RateCardVersionId);

public sealed record QuotationPricingInput(
  string Currency,
  IReadOnlyList<QuotationLineInput> Lines,
  decimal ComplexityFactor,
  decimal RiskPremiumPercent,
  decimal DiscountPercent);

public sealed record QuotationLineResult(string Role, string Activity, decimal Hours, decimal RatePerHour, decimal Amount, Guid RateCardVersionId);

public sealed record QuotationPricingResult(
  IReadOnlyList<QuotationLineResult> Lines,
  decimal BaseAmount,
  decimal ComplexityAmount,
  decimal RiskPremiumAmount,
  decimal DiscountAmount,
  decimal Fee);

/// <summary>
/// Deterministic fee model: sum(hours × rate) → complexity factor → risk premium → discount. Pure: no EF, clocks,
/// network or generated IDs. Each stage is rounded to currency precision so the breakdown always sums to the fee.
/// Out-of-range or missing inputs fail closed instead of defaulting to zero.
/// </summary>
public static class QuotationCalculator
{
  public const int CurrencyScale = 2;
  public const decimal MinimumComplexity = 0.5m;
  public const decimal MaximumComplexity = 3m;
  public const decimal MaximumPercent = 100m;
  public const int MaximumLines = 100;
  public const decimal MaximumHoursPerLine = 100_000m;

  /// <returns>An error message, or null when the input is valid.</returns>
  public static string? Validate(QuotationPricingInput input)
  {
    if (string.IsNullOrWhiteSpace(input.Currency) || input.Currency.Trim().Length != 3)
      return "A three-letter currency is required.";
    if (input.Lines is null || input.Lines.Count is < 1 or > MaximumLines)
      return $"A quotation needs 1 to {MaximumLines} hour lines.";
    foreach (var line in input.Lines)
    {
      if (string.IsNullOrWhiteSpace(line.Role) || string.IsNullOrWhiteSpace(line.Activity))
        return "Every line needs a role and an activity.";
      if (line.Hours <= 0 || line.Hours > MaximumHoursPerLine || decimal.Round(line.Hours, 2) != line.Hours)
        return "Hours must be positive, at most two decimals and within bounds.";
      if (line.RatePerHour <= 0)
        return $"No approved rate is available for {line.Role} / {line.Activity}; approve a rate card before quoting.";
    }
    var duplicate = input.Lines.GroupBy(x => (x.Role.Trim().ToUpperInvariant(), x.Activity.Trim().ToUpperInvariant())).FirstOrDefault(g => g.Count() > 1);
    if (duplicate is not null) return "Each role and activity may appear on one line only.";
    if (input.ComplexityFactor < MinimumComplexity || input.ComplexityFactor > MaximumComplexity)
      return $"The complexity factor must be between {MinimumComplexity} and {MaximumComplexity}.";
    if (input.RiskPremiumPercent < 0 || input.RiskPremiumPercent > MaximumPercent)
      return "The risk premium must be between 0 and 100 percent.";
    if (input.DiscountPercent < 0 || input.DiscountPercent > MaximumPercent)
      return "The discount must be between 0 and 100 percent.";
    return null;
  }

  public static QuotationPricingResult Calculate(QuotationPricingInput input)
  {
    var error = Validate(input);
    if (error is not null) throw new ArgumentException(error, nameof(input));

    var lines = input.Lines
      .OrderBy(x => x.Role, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Activity, StringComparer.OrdinalIgnoreCase)
      .Select(x => new QuotationLineResult(x.Role.Trim(), x.Activity.Trim(), x.Hours, x.RatePerHour,
        MoneyPolicy.Normalize(x.Hours * x.RatePerHour, CurrencyScale), x.RateCardVersionId))
      .ToList();
    var baseAmount = lines.Sum(x => x.Amount);
    var adjusted = MoneyPolicy.Normalize(baseAmount * input.ComplexityFactor, CurrencyScale);
    var complexityAmount = adjusted - baseAmount;
    var riskAmount = MoneyPolicy.Normalize(adjusted * input.RiskPremiumPercent / 100m, CurrencyScale);
    var preDiscount = adjusted + riskAmount;
    var discountAmount = MoneyPolicy.Normalize(preDiscount * input.DiscountPercent / 100m, CurrencyScale);
    var fee = preDiscount - discountAmount;
    return new QuotationPricingResult(lines, baseAmount, complexityAmount, riskAmount, discountAmount, fee);
  }

  /// <summary>Canonical hash of the priced inputs so an identical recalculation is recognized and a change is visible.</summary>
  public static string InputHash(QuotationPricingInput input, bool nonStandardTerms, string? note)
  {
    var canonical = string.Join("|",
      input.Currency.Trim().ToUpperInvariant(),
      string.Join(";", input.Lines.OrderBy(x => x.Role, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Activity, StringComparer.OrdinalIgnoreCase)
        .Select(x => string.Join(",", x.Role.Trim().ToUpperInvariant(), x.Activity.Trim().ToUpperInvariant(),
          x.Hours.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
          x.RatePerHour.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture), x.RateCardVersionId.ToString("D")))),
      input.ComplexityFactor.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
      input.RiskPremiumPercent.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
      input.DiscountPercent.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
      nonStandardTerms ? "NST" : "STD", (note ?? string.Empty).Trim());
    return Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(canonical));
  }
}
