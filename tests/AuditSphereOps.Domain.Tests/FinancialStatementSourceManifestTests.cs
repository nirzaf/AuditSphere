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
  [InlineData("cash-allocation-source")]
  [InlineData("cash-allocation-amount")]
  [InlineData("cash-allocation-scope")]
  [InlineData("cash-allocation-preparer")]
  [InlineData("fx-source")]
  [InlineData("fx-sign")]
  [InlineData("equity-source")]
  [InlineData("equity-sign")]
  [InlineData("equity-scope")]
  [InlineData("equity-transfer")]
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
    var allocation = new FinancialPackageCashFlowAllocation { Id = Guid.NewGuid(), FinancialPackageId = package.Id,
      CashFlowLineId = cash.Id, GeneralLedgerLineId = Guid.NewGuid(), GeneralLedgerTransactionId = Guid.NewGuid(),
      SourceImportBatchId = Guid.NewGuid(), SourceLineHash = "source-line-hash", SignedAmount = -25m, Currency = "QAR" };
    var notes = "notes";
    var transfers = new List<FinancialPackageEquityTransfer>();
    string Hash() => FinancialStatementService.ComputePresentationSourceManifestHash([package], [cash], [fx], [equity], [allocation], notes,
      equityTransfers: transfers);
    var before = Hash();
    switch (change)
    {
      case "cash-source": cash.SourceReferences = ["replacement"]; break;
      case "cash-sign": cash.Amount = 25m; break;
      case "cash-date": cash.MovementDate = cash.MovementDate!.Value.AddDays(-1); break;
      case "cash-classification": cash.ClassificationRationale = "Changed classification"; break;
      case "cash-currency": cash.Currency = "USD"; break;
      case "cash-allocation-source": allocation.SourceLineHash = "replacement"; break;
      case "cash-allocation-amount": allocation.SignedAmount = 24m; break;
      case "cash-allocation-scope": allocation.GeneralLedgerLineId = Guid.NewGuid(); break;
      case "cash-allocation-preparer": allocation.CreatedByUserId = Guid.NewGuid(); break;
      case "fx-source": fx.EvidenceReference = "replacement"; break;
      case "fx-sign": fx.Amount = -2m; break;
      case "equity-source": equity.EvidenceReference = "replacement"; break;
      case "equity-sign": equity.ClosingAmount = 100m; break;
      case "equity-scope": equity.EngagementId = Guid.NewGuid(); break;
      case "equity-transfer": transfers.Add(new FinancialPackageEquityTransfer { Id = Guid.NewGuid(), FinancialPackageId = package.Id,
        TransferReference = "TR-01", SourceLineCode = "RETAINED_EARNINGS", DestinationLineCode = "RESERVE", Amount = 5m,
        Currency = "QAR", EvidenceReference = "approved-transfer" }); break;
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
    var allocations = new[] { new FinancialPackageCashFlowAllocation { Id = Guid.NewGuid(), FinancialPackageId = packages[0].Id,
      CashFlowLineId = cash[0].Id, GeneralLedgerLineId = Guid.NewGuid(), SignedAmount = -1m } };
    var before = FinancialStatementService.ComputePresentationSourceManifestHash(packages, cash, [], [], allocations, "notes");
    cash[0].SourceReferences = ["a", "b"];
    Assert.Equal(before, FinancialStatementService.ComputePresentationSourceManifestHash(
      packages.Reverse().ToArray(), cash.Reverse().ToArray(), [], [], allocations, "notes"));
  }

  [Fact]
  public void IndependentCashFlowReview_EntersTheManifestOnlyWhenPresent()
  {
    var package = new FinancialPackage { Id = Guid.NewGuid() };
    var cash = new[] { new FinancialPackageCashFlowLine { Id = Guid.NewGuid(), FinancialPackageId = package.Id } };
    var baseHash = FinancialStatementService.ComputePresentationSourceManifestHash([package], cash, [], [], [], "notes");
    // No review: the identity is exactly the one it had before reviews were sealed.
    Assert.Equal(baseHash, FinancialStatementService.ComputePresentationSourceManifestHash([package], cash, [], [], [], "notes", []));
    var approval = new FinancialPackageCashFlowSourceReview { Id = Guid.NewGuid(), FinancialPackageId = package.Id, Decision = "APPROVED",
      SourceImportBatchId = Guid.NewGuid(), ReviewedByUserId = Guid.NewGuid(), ReviewedAt = DateTimeOffset.UtcNow };
    var approved = FinancialStatementService.ComputePresentationSourceManifestHash([package], cash, [], [], [], "notes", [approval]);
    Assert.NotEqual(baseHash, approved);
    var returned = new FinancialPackageCashFlowSourceReview { Id = approval.Id, FinancialPackageId = package.Id, Decision = "RETURNED",
      Reason = "Re-check the support.", SourceImportBatchId = approval.SourceImportBatchId, ReviewedByUserId = approval.ReviewedByUserId,
      ReviewedAt = approval.ReviewedAt };
    Assert.NotEqual(approved, FinancialStatementService.ComputePresentationSourceManifestHash([package], cash, [], [], [], "notes", [returned]));
  }
}
