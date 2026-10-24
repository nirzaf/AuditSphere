using AuditSphereOps.Domain.Audit;

namespace AuditSphereOps.Domain.Tests;

public sealed class AuditScheduleControlReconciliationTests
{
  [Fact]
  public void MatchingGrandTotalDoesNotHideOffsettingAccountErrorsOrOmittedSelectedAccounts()
  {
    var result = AuditScheduleControlReconciliation.Reconcile(["0020", "0010"],
      [new("0010", 200m)], [new("0010", 100m), new("0020", 100m)]);
    Assert.Equal(new[] { "0010", "0020" }, result.Select(x => x.AccountCode));
    Assert.Equal(0m, result.Sum(x => x.Difference));
    Assert.Equal(new[] { 100m, -100m }, result.Select(x => x.Difference));
    Assert.Equal(0, result[1].ListingRowCount);
    Assert.All(result, x => Assert.NotEqual(0m, x.Difference));
  }

  [Fact]
  public void ExactSignedClosingRowsTieOutWithoutNettingOtherAccountsOrLosingLeadingZeros()
  {
    var result = AuditScheduleControlReconciliation.Reconcile(["0010", "0020"],
      [new("0010", 120.123456m), new("0010", -20m), new("0020", 0m)],
      [new("0010", 90m), new("0010", 10.123456m), new("0020", 0m), new("10", 999m)]);
    Assert.All(result, x => Assert.Equal(0m, x.Difference));
    Assert.Equal(100.123456m, result[0].ListingAmount);
    Assert.Equal(2, result[0].ListingRowCount);
    Assert.Equal(2, result[0].TrialBalanceRowCount);
    Assert.Equal(1, result[1].TrialBalanceRowCount);
    Assert.Equal(result, AuditScheduleControlReconciliation.Reconcile(["0020", "0010"],
      [new("0020", 0m), new("0010", -20m), new("0010", 120.123456m)],
      [new("0020", 0m), new("0010", 10.123456m), new("0010", 90m)]));
  }

  [Theory]
  [InlineData("duplicate")]
  [InlineData("missing")]
  [InlineData("outside")]
  [InlineData("precision")]
  [InlineData("sum-bound")]
  public void InvalidScopeOrUnrepresentableAmountsAreRefused(string scenario)
  {
    string[] selected = scenario == "duplicate" ? ["0010", "0010"] : ["0010"];
    AuditScheduleAccountAmount[] listing = scenario switch
    {
      "outside" => [new("10", 1m)],
      "precision" => [new("0010", 0.0000001m)],
      "sum-bound" => [new("0010", 9_999_999_999_999.999999m), new("0010", 0.000001m)],
      _ => [new("0010", 1m)]
    };
    AuditScheduleAccountAmount[] controls = scenario == "missing" ? [new("0020", 1m)] : [new("0010", 1m)];
    Assert.Throws<ArgumentException>(() => AuditScheduleControlReconciliation.Reconcile(selected, listing, controls));
  }
}
