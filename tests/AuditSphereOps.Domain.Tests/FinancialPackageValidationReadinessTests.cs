using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;

namespace AuditSphereOps.Domain.Tests;

public sealed class FinancialPackageValidationReadinessTests
{
  public static IEnumerable<object[]> RequiredCodes() => FinancialPackageValidationReadiness.RequiredCodes.Select(x => new object[] { x });

  [Theory]
  [MemberData(nameof(RequiredCodes))]
  public void EachMissingMandatoryResultFailsClosed(string code)
  {
    var firm = Guid.NewGuid(); var package = Guid.NewGuid();
    var rows = Complete(firm, package).Where(x => x.Code != code);
    var result = FinancialPackageValidationReadiness.Evaluate(firm, package, rows);
    Assert.False(result.Ready);
    Assert.Equal([code], result.Missing);
    Assert.Empty(result.Failed);
  }

  [Theory]
  [MemberData(nameof(RequiredCodes))]
  public void EachFailedMandatoryResultFailsClosed(string code)
  {
    var firm = Guid.NewGuid(); var package = Guid.NewGuid();
    var rows = Complete(firm, package);
    rows.Single(x => x.Code == code).Passed = false;
    var result = FinancialPackageValidationReadiness.Evaluate(firm, package, rows);
    Assert.False(result.Ready);
    Assert.Equal([code], result.Failed);
    Assert.Empty(result.Missing);
  }

  [Fact]
  public void ForeignResultsCannotCompleteThePopulationAndDuplicateSuccessIsAmbiguous()
  {
    var firm = Guid.NewGuid(); var package = Guid.NewGuid();
    Assert.False(FinancialPackageValidationReadiness.Evaluate(firm, package, Complete(Guid.NewGuid(), package)).Ready);
    Assert.False(FinancialPackageValidationReadiness.Evaluate(firm, package, Complete(firm, Guid.NewGuid())).Ready);
    var rows = Complete(firm, package);
    Assert.True(FinancialPackageValidationReadiness.Evaluate(firm, package, rows).Ready);
    rows.Add(new FinancialPackageValidation { FirmId = firm, FinancialPackageId = package, Code = rows[0].Code, Passed = true });
    var result = FinancialPackageValidationReadiness.Evaluate(firm, package, rows);
    Assert.False(result.Ready);
    Assert.Equal([rows[0].Code], result.Ambiguous);
  }

  private static List<FinancialPackageValidation> Complete(Guid firm, Guid package) =>
    FinancialPackageValidationReadiness.RequiredCodes.Select(x => new FinancialPackageValidation
    { FirmId = firm, FinancialPackageId = package, Code = x, Passed = true }).ToList();
}
