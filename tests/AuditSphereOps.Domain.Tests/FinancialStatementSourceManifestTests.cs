using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;

namespace AuditSphereOps.Domain.Tests;

public sealed class FinancialStatementSourceManifestTests
{
  [Theory]
  [InlineData("cash-source")]
  [InlineData("cash-sign")]
  [InlineData("cash-date")]
  [InlineData("cash-classification")]
  [InlineData("cash-currency")]
  [InlineData("fx-source")]
  [InlineData("fx-sign")]
  [InlineData("equity-source")]
  [InlineData("equity-sign")]
  [InlineData("equity-scope")]
  [InlineData("opening-source")]
  [InlineData("closing-source")]
  [InlineData("cash-population")]
  [InlineData("supplementary-hash")]
  [InlineData("notes")]
  public void ScheduleSourceManifest_ChangesForExactProvenanceAndSignedAmounts(string change)
  {
    var package = new FinancialPackage { Id = Guid.NewGuid(), Currency = "QAR",
      CashAccountCodes = ["1000"], OpeningCashEvidenceReferences = ["opening"],
      ClosingCashEvidenceReferences = ["closing"], SupplementaryHash = "original" };
    var cash = new FinancialPackageCashFlowLine { Id = Guid.NewGuid(), FinancialPackageId = package.Id,
      Amount = -25m, Currency = "QAR", SourceReferences = ["source"], MovementDate = new DateOnly(2025, 12, 31),
      ClassificationRationale = "Reviewed investing classification" };
    var fx = new FinancialPackageFxEffect { Id = Guid.NewGuid(), FinancialPackageId = package.Id,
      Amount = 2m, EvidenceReference = "fx-source" };
    var equity = new FinancialPackageEquityLine { Id = Guid.NewGuid(), FinancialPackageId = package.Id,
      ClosingAmount = -100m, EvidenceReference = "equity-source" };
    var notes = "notes";
    string Hash() => FinancialStatementService.ComputePresentationSourceManifestHash([package], [cash], [fx], [equity], notes);
    var before = Hash();
    switch (change)
    {
      case "cash-source": cash.SourceReferences = ["replacement"]; break;
      case "cash-sign": cash.Amount = 25m; break;
      case "cash-date": cash.MovementDate = cash.MovementDate!.Value.AddDays(-1); break;
      case "cash-classification": cash.ClassificationRationale = "Changed classification"; break;
      case "cash-currency": cash.Currency = "USD"; break;
      case "fx-source": fx.EvidenceReference = "replacement"; break;
      case "fx-sign": fx.Amount = -2m; break;
      case "equity-source": equity.EvidenceReference = "replacement"; break;
      case "equity-sign": equity.ClosingAmount = 100m; break;
      case "equity-scope": equity.EngagementId = Guid.NewGuid(); break;
      case "opening-source": package.OpeningCashEvidenceReferences = ["replacement"]; break;
      case "closing-source": package.ClosingCashEvidenceReferences = ["replacement"]; break;
      case "cash-population": package.CashAccountCodes = ["1001"]; break;
      case "supplementary-hash": package.SupplementaryHash = "replacement"; break;
      case "notes": notes = "replacement"; break;
    }
    Assert.NotEqual(before, Hash());
  }

  [Fact]
  public void ScheduleSourceManifest_IsIndependentOfQueryAndReferenceOrdering()
  {
    var packages = new[] { new FinancialPackage { Id = Guid.NewGuid() }, new FinancialPackage { Id = Guid.NewGuid() } };
    var cash = new[] {
      new FinancialPackageCashFlowLine { Id = Guid.NewGuid(), FinancialPackageId = packages[0].Id, SourceReferences = ["b", "a"] },
      new FinancialPackageCashFlowLine { Id = Guid.NewGuid(), FinancialPackageId = packages[1].Id } };
    var before = FinancialStatementService.ComputePresentationSourceManifestHash(packages, cash, [], [], "notes");
    cash[0].SourceReferences = ["a", "b"];
    Assert.Equal(before, FinancialStatementService.ComputePresentationSourceManifestHash(
      packages.Reverse().ToArray(), cash.Reverse().ToArray(), [], [], "notes"));
  }
}
