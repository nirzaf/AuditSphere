using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;

namespace AuditSphereOps.Domain.Tests;

public sealed class QuotationCalculatorTests
{
  private static readonly Guid CardA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
  private static readonly Guid CardB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

  private static QuotationPricingInput Input(decimal complexity = 1m, decimal risk = 0m, decimal discount = 0m) => new("QAR",
    [new("Partner", "Audit", 10m, 1000m, CardA), new("Manager", "Audit", 20m, 750m, CardB)], complexity, risk, discount);

  [Fact]
  public void Fee_IsRateTimesHours_ThenComplexity_ThenRiskPremium_ThenDiscount()
  {
    var result = QuotationCalculator.Calculate(Input(complexity: 1.2m, risk: 10m, discount: 5m));
    Assert.Equal(25000m, result.BaseAmount);        // 10×1000 + 20×750
    Assert.Equal(5000m, result.ComplexityAmount);   // ×1.2 → 30,000
    Assert.Equal(3000m, result.RiskPremiumAmount);  // 10% of 30,000
    Assert.Equal(1650m, result.DiscountAmount);     // 5% of 33,000
    Assert.Equal(31350m, result.Fee);
    // The breakdown always reconciles to the fee.
    Assert.Equal(result.Fee, result.BaseAmount + result.ComplexityAmount + result.RiskPremiumAmount - result.DiscountAmount);
  }

  [Fact]
  public void SameInputs_GiveSameLinesFeeAndHash_RegardlessOfLineOrder()
  {
    var forward = Input(1.1m, 5m, 0m);
    var reversed = forward with { Lines = [.. forward.Lines.Reverse()] };
    Assert.Equal(QuotationCalculator.Calculate(forward).Fee, QuotationCalculator.Calculate(reversed).Fee);
    Assert.Equal(QuotationCalculator.InputHash(forward, false, null), QuotationCalculator.InputHash(reversed, false, null));
    Assert.NotEqual(QuotationCalculator.InputHash(forward, false, null), QuotationCalculator.InputHash(forward with { DiscountPercent = 1m }, false, null));
    Assert.NotEqual(QuotationCalculator.InputHash(forward, false, null), QuotationCalculator.InputHash(forward, true, "Bespoke payment plan"));
  }

  [Fact]
  public void EachStageRoundsToCurrencyPrecision_SoAmountsNeverCarryFractionsOfAMinorUnit()
  {
    var input = new QuotationPricingInput("QAR", [new("Staff", "Audit", 3.33m, 333.33m, CardA)], 1.333m, 7.77m, 3.33m);
    var result = QuotationCalculator.Calculate(input);
    foreach (var amount in new[] { result.BaseAmount, result.ComplexityAmount, result.RiskPremiumAmount, result.DiscountAmount, result.Fee })
      Assert.Equal(decimal.Round(amount, 2), amount);
  }

  [Theory]
  [InlineData(0.4, 0, 0)]      // complexity below the allowed range
  [InlineData(3.1, 0, 0)]      // complexity above the allowed range
  [InlineData(1.0, -1, 0)]     // negative premium
  [InlineData(1.0, 101, 0)]    // premium above 100%
  [InlineData(1.0, 0, -0.5)]   // negative discount
  [InlineData(1.0, 0, 100.5)]  // discount above 100%
  public void OutOfRangeFactorsFailClosed(double complexity, double risk, double discount)
  {
    var input = Input((decimal)complexity, (decimal)risk, (decimal)discount);
    Assert.NotNull(QuotationCalculator.Validate(input));
    Assert.Throws<ArgumentException>(() => QuotationCalculator.Calculate(input));
  }

  [Fact]
  public void MissingRate_DuplicateLine_BadHours_AndBadCurrencyAreRejectedNotDefaultedToZero()
  {
    Assert.Contains("No approved rate", QuotationCalculator.Validate(Input() with { Lines = [new("Partner", "Audit", 5m, 0m, Guid.Empty)] }));
    Assert.Contains("one line only", QuotationCalculator.Validate(Input() with { Lines = [new("Partner", "Audit", 5m, 100m, CardA), new("partner", "AUDIT", 1m, 100m, CardA)] }));
    Assert.NotNull(QuotationCalculator.Validate(Input() with { Lines = [new("Partner", "Audit", 0m, 100m, CardA)] }));
    Assert.NotNull(QuotationCalculator.Validate(Input() with { Lines = [new("Partner", "Audit", 1.234m, 100m, CardA)] }));
    Assert.NotNull(QuotationCalculator.Validate(Input() with { Lines = [] }));
    Assert.NotNull(QuotationCalculator.Validate(Input() with { Currency = "QA" }));
  }

  [Fact]
  public void ApprovalMatrix_DefaultsApplyWhenUnconfigured_AndConfiguredBandsPickTheHighestExceededBand()
  {
    Assert.Empty(CommercialApprovalMatrix.Required([], 10m, false)); // exactly the default threshold is not "over"
    var defaults = CommercialApprovalMatrix.Required([], 10.5m, true);
    Assert.Equal(["Partner", "Partner"], defaults.Select(x => x.Role));

    var manager = new CommercialApprovalRule { Id = Guid.NewGuid(), Kind = CommercialRuleKinds.DiscountOver, ThresholdPercent = 5m, RequiredRole = "Manager" };
    var partner = new CommercialApprovalRule { Id = Guid.NewGuid(), Kind = CommercialRuleKinds.DiscountOver, ThresholdPercent = 15m, RequiredRole = "Partner" };
    var terms = new CommercialApprovalRule { Id = Guid.NewGuid(), Kind = CommercialRuleKinds.NonStandardTerms, RequiredRole = "Administrator" };
    CommercialApprovalRule[] rules = [manager, partner, terms];

    Assert.Empty(CommercialApprovalMatrix.Required(rules, 5m, false));
    Assert.Equal("Manager", Assert.Single(CommercialApprovalMatrix.Required(rules, 8m, false)).Role);
    Assert.Equal("Partner", Assert.Single(CommercialApprovalMatrix.Required(rules, 20m, false)).Role);
    Assert.Equal(["Manager", "Administrator"], CommercialApprovalMatrix.Required(rules, 8m, true).Select(x => x.Role));
    // A deactivated rule no longer applies, and the unconfigured default is never silently skipped for terms.
    partner.Active = false;
    Assert.Equal("Manager", Assert.Single(CommercialApprovalMatrix.Required(rules, 20m, false)).Role);
  }
}
