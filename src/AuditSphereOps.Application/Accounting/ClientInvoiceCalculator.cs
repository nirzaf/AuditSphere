namespace AuditSphereOps.Application.Accounting;

/// <summary>Explicit calculation inputs, not a jurisdiction approval or an issuance authorization.</summary>
public sealed record ClientInvoiceMoneyPolicy(string Currency, int DecimalPlaces, string MidpointRule,
  string ResidualTreatment, int MaximumResidualMinorUnits, string RoundingAccountCode);
public sealed record ClientInvoiceTaxInput(string Code, decimal Rate);
public sealed record ClientInvoiceLineInput(string Description, string AccountCode, decimal Quantity,
  decimal UnitPrice, decimal Discount, string TaxTreatment, IReadOnlyList<ClientInvoiceTaxInput> Taxes);
public sealed record ClientInvoiceTaxAmount(string Code, decimal Rate, decimal TaxableBase, decimal Amount);
public sealed record ClientInvoiceLineCalculation(int LineNumber, string Description, string AccountCode,
  decimal Quantity, decimal UnitPrice, decimal Discount, string TaxTreatment, decimal Net,
  IReadOnlyList<ClientInvoiceTaxAmount> Taxes, decimal Tax, decimal Gross, decimal RoundingAdjustment);
public sealed record ClientInvoiceCalculation(bool Valid, string EngineVersion, string DocumentKind,
  string TransactionCurrency, string FunctionalCurrency, string CurrencyBasis, ClientInvoiceMoneyPolicy? Policy,
  IReadOnlyList<ClientInvoiceLineCalculation> Lines, decimal Net, decimal Tax, decimal Gross,
  decimal RoundingAdjustment, string? Error, int? ErrorLineNumber);

/// <summary>
/// Pure shared invoice/credit arithmetic. No clocks, IDs, storage, tax-law inference or FX fallback.
/// A valid result is an unposted calculation; workflow authority and approved profiles are checked separately.
/// </summary>
public static class ClientInvoiceCalculator
{
  public const string EngineVersion = "native-invoice-line-net-v1";
  public const decimal MaximumStoredAmount = ClientNativeAmountRules.MaximumStoredAmount;
  public const decimal MaximumQuantity = 1_000_000_000m;
  public static bool ValidStoredAmount(decimal value) => ClientNativeAmountRules.ValidStoredAmount(value);
  private static bool Code(string? value, int bound) => !string.IsNullOrWhiteSpace(value) && value.Length <= bound && value == value.Trim();

  public static ClientInvoiceCalculation Calculate(string documentKind, string transactionCurrency, string functionalCurrency,
    ClientInvoiceMoneyPolicy? policy, IReadOnlyList<ClientInvoiceLineInput>? lines)
  {
    ClientInvoiceCalculation Fail(string message, int? line = null) => new(false, EngineVersion, documentKind,
      transactionCurrency, functionalCurrency, transactionCurrency == functionalCurrency ? "SAME_CURRENCY" : "UNSUPPORTED_FX", policy, [], 0m, 0m, 0m, 0m, message, line);
    bool Currency(string? value) => value is { Length: 3 } && value.All(c => c is >= 'A' and <= 'Z');
    if (policy is null || !Currency(transactionCurrency) || !Currency(functionalCurrency) || policy.Currency != transactionCurrency ||
        transactionCurrency != functionalCurrency)
      return Fail("An explicit matching functional-currency policy is required; foreign-currency documents are not yet supported.");
    if (documentKind is not ("INVOICE" or "CREDIT" or "PURCHASE_INVOICE" or "PURCHASE_CREDIT") || policy.DecimalPlaces is < 0 or > 6 ||
        policy.MidpointRule is not ("TO_EVEN" or "AWAY_FROM_ZERO") ||
        policy.ResidualTreatment is not ("REJECT" or "EXPLICIT_ACCOUNT") || policy.MaximumResidualMinorUnits is < 0 or > 3 ||
        (policy.ResidualTreatment == "REJECT" && (policy.MaximumResidualMinorUnits != 0 || policy.RoundingAccountCode != "")) ||
        (policy.ResidualTreatment == "EXPLICIT_ACCOUNT" && (policy.MaximumResidualMinorUnits == 0 || !Code(policy.RoundingAccountCode, 100))))
      return Fail("Choose declared precision, supported midpoint rounding and bounded explicit residual treatment.");
    if (lines is null || lines.Count is < 1 or > 100) return Fail("An invoice or credit requires 1 to 100 descriptive lines.");
    var midpoint = policy.MidpointRule == "TO_EVEN" ? MidpointRounding.ToEven : MidpointRounding.AwayFromZero;
    decimal Round(decimal value) => decimal.Round(value, policy.DecimalPlaces, midpoint);
    decimal minor = 1m; for (var i = 0; i < policy.DecimalPlaces; i++) minor /= 10m;
    var result = new List<ClientInvoiceLineCalculation>(lines.Count);
    decimal netTotal = 0m, taxTotal = 0m, grossTotal = 0m, residualTotal = 0m;
    for (var index = 0; index < lines.Count; index++)
    {
      var number = index + 1; var line = lines[index];
      if (line is null || !Code(line.Description, 2000) || !Code(line.AccountCode, 100) ||
          line.Quantity <= 0m || line.Quantity > MaximumQuantity || decimal.Round(line.Quantity, 6) != line.Quantity ||
          !ValidStoredAmount(line.UnitPrice) || !ValidStoredAmount(line.Discount) ||
          line.TaxTreatment is not ("NONE" or "EXCLUSIVE" or "INCLUSIVE") || line.Taxes is null || line.Taxes.Count > 5 ||
          (line.TaxTreatment == "NONE" ? line.Taxes.Count != 0 : line.Taxes.Count == 0))
        return Fail("Provide bounded descriptions/accounts, positive six-decimal quantity and explicit supported tax treatment.", number);
      var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase); decimal rateTotal = 0m;
      foreach (var tax in line.Taxes)
      {
        if (tax is null || !Code(tax.Code, 50) || !codes.Add(tax.Code) || tax.Rate is < 0m or > 1m || decimal.Round(tax.Rate, 6) != tax.Rate)
          return Fail("Tax components require distinct explicit codes and bounded rates; zero-rated treatment remains explicitly coded.", number);
        rateTotal += tax.Rate;
      }
      if (rateTotal > 1m) return Fail("Combined tax rates above 100 percent and compound-tax methods are unsupported.", number);
      try
      {
        var raw = checked(line.Quantity * line.UnitPrice - line.Discount);
        if (raw < 0m || raw > MaximumStoredAmount) return Fail("Discount exceeds line value or line value exceeds native storage bounds.", number);
        var charge = Round(raw);
        var net = line.TaxTreatment == "INCLUSIVE" ? Round(charge / (1m + rateTotal)) : charge;
        // Declared v1 basis: individually rounded components calculated on rounded line net.
        var taxes = line.Taxes.Select(t => new ClientInvoiceTaxAmount(t.Code, t.Rate, net, Round(net * t.Rate))).ToArray();
        var taxAmount = taxes.Sum(t => t.Amount);
        var gross = line.TaxTreatment == "INCLUSIVE" ? charge : checked(net + taxAmount);
        var residual = gross - net - taxAmount;
        if (Math.Abs(residual) > minor * policy.MaximumResidualMinorUnits || (residual != 0m && policy.ResidualTreatment != "EXPLICIT_ACCOUNT"))
          return Fail("Inclusive rounding residual requires a supported bounded explicit adjustment account policy.", number);
        if (!ValidStoredAmount(net) || !ValidStoredAmount(taxAmount) || !ValidStoredAmount(gross)) return Fail("Calculated line amounts exceed native storage bounds.", number);
        result.Add(new(number, line.Description, line.AccountCode, line.Quantity, line.UnitPrice, line.Discount,
          line.TaxTreatment, net, taxes, taxAmount, gross, residual));
        netTotal += net; taxTotal += taxAmount; grossTotal += gross; residualTotal += residual;
      }
      catch (OverflowException) { return Fail("Calculation overflow; reduce the declared quantity, price or tax components.", number); }
    }
    if (!ValidStoredAmount(netTotal) || !ValidStoredAmount(taxTotal) || !ValidStoredAmount(grossTotal)) return Fail("Document totals exceed native storage bounds.");
    if (Math.Abs(residualTotal) > minor * policy.MaximumResidualMinorUnits)
      return Fail("Document rounding residual exceeds the declared bounded adjustment; no hidden balancing amount is permitted.");
    return new(true, EngineVersion, documentKind, transactionCurrency, functionalCurrency, "SAME_CURRENCY", policy,
      result, netTotal, taxTotal, grossTotal, residualTotal, null, null);
  }
}
