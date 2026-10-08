using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Domain.Tests;

public sealed class ContractContributionCalculatorTests
{
  [Fact]
  public void UsesContractFeeAndLifetimeApprovedTime_WithoutSubstitutingActualStaffCost()
  {
    var contribution = ContractContributionCalculator.Compute(10000m, "QAR",
      [new(120, 500m, "QAR"), new(60, 200m, "QAR")]);
    Assert.NotNull(contribution);
    Assert.Equal(1200m, contribution.LifetimeStandardValue);
    Assert.Equal(8800m, contribution.FeeLessStandardValue);
  }

  [Theory]
  [InlineData(null, "QAR")]
  [InlineData(500, null)]
  [InlineData(500, "USD")]
  [InlineData(-1, "QAR")]
  public void MissingRateOrDifferentCurrency_IsUnavailable_NotZero(int? rate, string? currency) =>
    Assert.Null(ContractContributionCalculator.Compute(10000m, "QAR", [new(60, (decimal?)rate, currency)]));
}
