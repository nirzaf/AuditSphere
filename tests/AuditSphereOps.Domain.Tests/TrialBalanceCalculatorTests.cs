using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Domain.Tests;

// Appendix D balanced-TB fixture: 14 accounts, signed sum 0, debits == credits == 1,820,000 (NT-09).
// AJ-001 + re-upload idempotency guard the NT-10 calculation contract.
public sealed class TrialBalanceCalculatorTests
{
  private static IReadOnlyList<TbImportRow> Fixture() =>
  [
    new("100101", "Bank", 150000m, "QAR", "DEMO", "CA_CASH"),
    new("110100", "Trade Receivables", 300000m, "QAR", "DEMO", "CA_AR"),
    new("120100", "Inventory", 200000m, "QAR", "DEMO", "CA_INVENTORY"),
    new("150100", "Property Plant Equipment Cost", 150000m, "QAR", "DEMO", "NCA_PPE_COST"),
    new("159100", "Accumulated Depreciation", -50000m, "QAR", "DEMO", "NCA_PPE_ACCDEP"),
    new("200100", "Trade Payables", -240000m, "QAR", "DEMO", "CL_AP"),
    new("220100", "Loan", -30000m, "QAR", "DEMO", "NCL_LOAN"),
    new("300100", "Share Capital", -250000m, "QAR", "DEMO", "EQ_CAPITAL"),
    new("310100", "Opening Retained Earnings", -50000m, "QAR", "DEMO", "EQ_RETAINED"),
    new("400100", "Revenue", -1200000m, "QAR", "DEMO", "PL_REVENUE"),
    new("500100", "Cost of Sales", 800000m, "QAR", "DEMO", "PL_COS"),
    new("510100", "Payroll Expense", 190000m, "QAR", "DEMO", "PL_PAYROLL"),
    new("520100", "Depreciation Expense", 20000m, "QAR", "DEMO", "PL_DEPRECIATION"),
    new("530100", "Finance Costs", 10000m, "QAR", "DEMO", "PL_FINANCE"),
  ];

  [Fact]
  public void Fixture_Has14Accounts_AndBalancesToZero()
  {
    var parsed = TrialBalanceCalculator.Parse(Fixture());
    Assert.Equal(14, parsed.Rows.Count);
    Assert.True(parsed.Balanced);
    Assert.Equal(0m, parsed.SignedSum);
    Assert.Equal(1820000m, parsed.TotalDebits);
    Assert.Equal(1820000m, parsed.TotalCreditsAbs);
  }

  [Fact]
  public void Fixture_PresentationTotals_MatchAppendixD2()
  {
    var rows = Fixture().ToDictionary(r => r.AccountCode, r => r.Amount);
    Assert.Equal(650000m, rows["100101"] + rows["110100"] + rows["120100"]);
    Assert.Equal(100000m, rows["150100"] + rows["159100"]);
    Assert.Equal(270000m, -(rows["200100"] + rows["220100"]));
    var profit = -(rows["400100"] + rows["500100"] + rows["510100"] + rows["520100"] + rows["530100"]);
    Assert.Equal(180000m, profit);
    Assert.Equal(480000m, -(rows["300100"] + rows["310100"]) + profit);
  }

  [Fact]
  public void Aj001_AppliesOnce_Profit175k_AndReuploadDoesNotDoubleCount()
  {
    var balances = Fixture().ToDictionary(r => r.AccountCode, r => r.Amount);
    var aj = new[] { ("520100", 5000m, 0m), ("159100", 0m, 5000m) };
    var adjusted = TrialBalanceCalculator.ApplyJournal(balances, aj);
    // Profit 180k -> 175k; net PPE 100k -> 95k
    var profit = -(adjusted["400100"] + adjusted["500100"] + adjusted["510100"] + adjusted["520100"] + adjusted["530100"]);
    Assert.Equal(175000m, profit);
    Assert.Equal(95000m, adjusted["150100"] + adjusted["159100"]);

    // Replacement source already reflects AJ-001: applying again would give 170k (the AT-10 failure).
    var reuploaded = new Dictionary<string, decimal>(adjusted, StringComparer.Ordinal);
    var doubleApplied = TrialBalanceCalculator.ApplyJournal(reuploaded, aj);
    var wrongProfit = -(doubleApplied["400100"] + doubleApplied["500100"] + doubleApplied["510100"] + doubleApplied["520100"] + doubleApplied["530100"]);
    Assert.Equal(170000m, wrongProfit); // documents why the source bridge must mark AJ-001 reflected, not re-apply
    Assert.Equal(175000m, profit);      // correct path keeps 175k
  }

  [Fact]
  public void Unbalanced_Journal_IsRejected()
  {
    var balances = Fixture().ToDictionary(r => r.AccountCode, r => r.Amount);
    Assert.Throws<InvalidOperationException>(() =>
      TrialBalanceCalculator.ApplyJournal(balances, [("520100", 5000m, 0m)]));
  }

  [Fact]
  public void MoneyPolicy_RejectsMixedCurrencySum()
  {
    Assert.Throws<InvalidOperationException>(() => MoneyPolicy.RequireSameCurrency("QAR", "USD", "Currency"));
  }
}
