using AuditSphereOps.Application.Accounting;

namespace AuditSphereOps.Domain.Tests;

public sealed class ClientInvoiceCalculatorTests
{
  private static ClientInvoiceMoneyPolicy Policy(int precision = 2, string midpoint = "AWAY_FROM_ZERO") => new("QAR", precision, midpoint, "REJECT", 0, "");
  private static ClientInvoiceLineInput Line(decimal price = 100m, string treatment = "NONE", params ClientInvoiceTaxInput[] taxes) => new("Consulting", "4000", 1m, price, 0m, treatment, taxes);
  private static ClientInvoiceCalculation Calculate(ClientInvoiceMoneyPolicy policy, params ClientInvoiceLineInput[] lines) => ClientInvoiceCalculator.Calculate("INVOICE", "QAR", "QAR", policy, lines);

  [Theory]
  [InlineData(0, "AWAY_FROM_ZERO", "2.5", "3")]
  [InlineData(0, "TO_EVEN", "2.5", "2")]
  [InlineData(2, "AWAY_FROM_ZERO", "1.005", "1.01")]
  [InlineData(2, "TO_EVEN", "1.005", "1.00")]
  [InlineData(3, "AWAY_FROM_ZERO", "1.0005", "1.001")]
  [InlineData(3, "TO_EVEN", "1.0005", "1.000")]
  public void UsesDeclaredZeroTwoAndThreeDecimalPolicies(int precision, string midpoint, string price, string expected)
  {
    var result = Calculate(Policy(precision, midpoint), Line(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));
    Assert.True(result.Valid, result.Error); Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result.Gross);
    Assert.Equal(result.Gross, result.Net); Assert.Equal(0m, result.Tax); Assert.Empty(Assert.Single(result.Lines).Taxes);
  }

  [Fact]
  public void MixedTaxInvoiceAndCreditUseTheSameEngineAndPreserveExactInputs()
  {
    ClientInvoiceLineInput[] lines = [Line(100m, "EXCLUSIVE", new ClientInvoiceTaxInput("STANDARD", .05m), new ClientInvoiceTaxInput("LEVY", .1m)), Line(115m, "INCLUSIVE", new ClientInvoiceTaxInput("STANDARD", .05m), new ClientInvoiceTaxInput("LEVY", .1m)), Line(10m) with { Quantity = 2.5m, Discount = 5m }];
    var invoice = Calculate(Policy(), lines);
    var credit = ClientInvoiceCalculator.Calculate("CREDIT", "QAR", "QAR", Policy(), lines);
    Assert.True(invoice.Valid, invoice.Error); Assert.True(credit.Valid, credit.Error);
    Assert.Equal(220m, invoice.Net); Assert.Equal(30m, invoice.Tax); Assert.Equal(250m, invoice.Gross);
    Assert.Equal(invoice.Net, credit.Net); Assert.Equal(invoice.Tax, credit.Tax); Assert.Equal(invoice.Gross, credit.Gross);
    Assert.Equal("CREDIT", credit.DocumentKind); Assert.Equal(ClientInvoiceCalculator.EngineVersion, credit.EngineVersion);
    Assert.Equal(2.5m, invoice.Lines[2].Quantity); Assert.Equal(10m, invoice.Lines[2].UnitPrice); Assert.Equal(5m, invoice.Lines[2].Discount);
    Assert.All(invoice.Lines.Take(2), l => { Assert.Equal(100m, l.Net); Assert.Equal(100m, l.Taxes[0].TaxableBase); Assert.Equal(5m, l.Taxes[0].Amount); Assert.Equal(10m, l.Taxes[1].Amount); });
    Assert.Equal(invoice.Gross, invoice.Net + invoice.Tax + invoice.RoundingAdjustment);
    Assert.Equal("SAME_CURRENCY", invoice.CurrencyBasis);
  }

  [Fact]
  public void ZeroRatedIsExplicitAndDifferentFromNoTax()
  {
    var noTax = Calculate(Policy(), Line());
    var zeroRate = Calculate(Policy(), Line(100m, "EXCLUSIVE", new ClientInvoiceTaxInput("ZERO_RATE", 0m)));
    Assert.True(noTax.Valid); Assert.True(zeroRate.Valid);
    Assert.Empty(noTax.Lines[0].Taxes); Assert.Equal("ZERO_RATE", Assert.Single(zeroRate.Lines[0].Taxes).Code);
    Assert.Equal(noTax.Gross, zeroRate.Gross);
  }

  [Fact]
  public void InclusiveResidualRequiresABoundedNamedAccountAndIsNeverHidden()
  {
    var line = Line(.03m, "INCLUSIVE", new ClientInvoiceTaxInput("STANDARD", .2m));
    Assert.False(Calculate(Policy(), line).Valid);
    var explicitPolicy = Policy() with { ResidualTreatment = "EXPLICIT_ACCOUNT", MaximumResidualMinorUnits = 1, RoundingAccountCode = "6999" };
    var result = Calculate(explicitPolicy, line);
    Assert.True(result.Valid, result.Error); Assert.Equal(.03m, result.Gross); Assert.Equal(.03m, result.Net); Assert.Equal(.01m, result.Tax);
    Assert.Equal(-.01m, result.RoundingAdjustment); Assert.Equal(-.01m, result.Lines[0].RoundingAdjustment);
    Assert.Equal("6999", result.Policy!.RoundingAccountCode); Assert.Equal(result.Gross, result.Net + result.Tax + result.RoundingAdjustment);
    Assert.False(Calculate(explicitPolicy with { RoundingAccountCode = "" }, line).Valid);
    Assert.False(Calculate(explicitPolicy, line, line).Valid);
    var even = Calculate(explicitPolicy with { MidpointRule = "TO_EVEN" }, line);
    Assert.True(even.Valid); Assert.Equal(.01m, even.RoundingAdjustment);
  }

  [Fact]
  public void RefusesUnsupportedFxPolicyAndTaxInputs()
  {
    var fx = ClientInvoiceCalculator.Calculate("INVOICE", "USD", "QAR", Policy() with { Currency = "USD" }, [Line()]);
    Assert.False(fx.Valid); Assert.Equal("UNSUPPORTED_FX", fx.CurrencyBasis); Assert.Empty(fx.Lines);
    foreach (var policy in new[] { Policy() with { DecimalPlaces = -1 }, Policy() with { DecimalPlaces = 7 }, Policy() with { MidpointRule = "GUESS" }, Policy() with { Currency = "USD" }, Policy() with { MaximumResidualMinorUnits = 1 }, Policy() with { ResidualTreatment = "SUSPENSE" } }) Assert.False(Calculate(policy, Line()).Valid);
    foreach (var line in new[] { Line(100m, "NONE", new ClientInvoiceTaxInput("UNEXPECTED", .05m)), Line(100m, "EXCLUSIVE"), Line(100m, "INCLUSIVE", new ClientInvoiceTaxInput("A", -.1m)), Line(100m, "EXCLUSIVE", new ClientInvoiceTaxInput("A", .5m), new ClientInvoiceTaxInput("a", .1m)), Line(100m, "EXCLUSIVE", new ClientInvoiceTaxInput("A", .6m), new ClientInvoiceTaxInput("B", .6m)), Line(100m, "UNKNOWN"), Line(100m, "EXCLUSIVE", new ClientInvoiceTaxInput("A", .0000001m)) })
    {
      var result = Calculate(Policy(), line); Assert.False(result.Valid); Assert.Equal(1, result.ErrorLineNumber); Assert.Empty(result.Lines);
    }
  }

  [Fact]
  public void BoundsQuantitiesStoragePrecisionAndDocumentTotals()
  {
    foreach (var line in new[] { Line() with { Quantity = 0m }, Line() with { Quantity = -1m }, Line() with { Quantity = .0000001m }, Line() with { Quantity = ClientInvoiceCalculator.MaximumQuantity + 1m }, Line() with { UnitPrice = .0000001m }, Line() with { UnitPrice = -1m }, Line() with { Discount = 101m }, Line() with { AccountCode = "" }, Line() with { Description = "" }, Line() with { Quantity = 1000000000m, UnitPrice = ClientInvoiceCalculator.MaximumStoredAmount } }) Assert.False(Calculate(Policy(), line).Valid);
    Assert.False(Calculate(Policy(), Enumerable.Repeat(Line(), 101).ToArray()).Valid);
    Assert.False(Calculate(Policy()).Valid);
    Assert.False(Calculate(Policy(), Line(ClientInvoiceCalculator.MaximumStoredAmount)).Valid);
    var max = Line(ClientInvoiceCalculator.MaximumStoredAmount);
    Assert.True(Calculate(Policy(6), max).Valid);
    Assert.False(Calculate(Policy(6), max, max).Valid);
    Assert.Equal(ClientOperationalJournalCalculator.MaximumLineAmount, ClientInvoiceCalculator.MaximumStoredAmount);
    foreach (var amount in new[] { 0m, 1.234567m, -.01m, .0000001m, ClientInvoiceCalculator.MaximumStoredAmount, ClientInvoiceCalculator.MaximumStoredAmount + .000001m })
      Assert.Equal(ClientInvoiceCalculator.ValidStoredAmount(amount), ClientOperationalJournalCalculator.ValidAmount(amount));
  }

  [Fact]
  public void InvalidMissingInputsFailWithoutAnInferredPolicyOrPartialResult()
  {
    Assert.False(ClientInvoiceCalculator.Calculate("INVOICE", "QAR", "QAR", null, [Line()]).Valid);
    Assert.False(ClientInvoiceCalculator.Calculate("INVOICE", "QAR", "QAR", Policy(), null).Valid);
    Assert.False(Calculate(Policy(), null!).Valid);
    Assert.False(Calculate(Policy(), Line() with { Taxes = null! }).Valid);
    Assert.False(Calculate(Policy(), Line(100m, "EXCLUSIVE", (ClientInvoiceTaxInput)null!)).Valid);
    Assert.False(ClientInvoiceCalculator.Calculate("GUESS", "QAR", "QAR", Policy(), [Line()]).Valid);
    Assert.False(ClientInvoiceCalculator.Calculate("INVOICE", "qar", "qar", Policy(), [Line()]).Valid);
    var partial = Calculate(Policy(), Line(), Line() with { Quantity = -1m });
    Assert.False(partial.Valid); Assert.Empty(partial.Lines); Assert.Equal(0m, partial.Gross); Assert.Equal(2, partial.ErrorLineNumber);
  }

  [Fact]
  public void SupportedPricesReconcileAtEveryDeclaredPrecisionWithoutMutatingInputs()
  {
    foreach (var precision in new[] { 0, 2, 3 })
    foreach (var midpoint in new[] { "TO_EVEN", "AWAY_FROM_ZERO" })
    foreach (var value in Enumerable.Range(1, 100))
    {
      var policy = Policy(precision, midpoint) with { ResidualTreatment = "EXPLICIT_ACCOUNT", MaximumResidualMinorUnits = 3, RoundingAccountCode = "6999" };
      var line = Line(value / 37m, "INCLUSIVE", new ClientInvoiceTaxInput("A", .05m), new ClientInvoiceTaxInput("B", .075m)) with { UnitPrice = decimal.Round(value / 37m, 6) };
      var originalPrice = line.UnitPrice;
      var result = Calculate(policy, line);
      Assert.True(result.Valid, result.Error);
      Assert.Equal(result.Gross, result.Net + result.Tax + result.RoundingAdjustment);
      Assert.Equal(result.Tax, result.Lines[0].Taxes.Sum(x => x.Amount));
      Assert.Equal(originalPrice, line.UnitPrice);
      var repeated = Calculate(policy, line);
      Assert.Equal(result.Net, repeated.Net); Assert.Equal(result.Tax, repeated.Tax); Assert.Equal(result.Gross, repeated.Gross); Assert.Equal(result.RoundingAdjustment, repeated.RoundingAdjustment);
    }
  }
}
