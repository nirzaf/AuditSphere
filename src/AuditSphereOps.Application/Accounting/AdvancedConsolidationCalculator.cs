using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Accounting;

public sealed record AcquisitionAccountingInput(
  DateOnly AcquisitionDate,
  DateOnly ControlDate,
  decimal Consideration,
  decimal NciAtAcquisition,
  decimal FairValueNetAssets,
  decimal OpeningReserves,
  decimal FairValueAdjustments = 0m);

public sealed record AcquisitionAccountingResult(
  decimal Goodwill,
  decimal BargainPurchase,
  decimal OpeningReserves,
  decimal FairValueAdjustedNetAssets,
  string Method);

public sealed record NciRollforwardResult(
  decimal OpeningNci,
  decimal Profit,
  decimal Oci,
  decimal Distributions,
  decimal ClosingNci);

public sealed record OwnershipChangeInput(
  DateOnly EffectiveDate,
  decimal PreviousOwnershipPercent,
  decimal NewOwnershipPercent,
  decimal Consideration,
  decimal FairValueRetainedInterest,
  decimal CarryingNetAssets,
  decimal CarryingNci,
  bool ControlLost);

public sealed record OwnershipChangeResult(
  decimal NciMovement,
  decimal DisposalGainOrLoss,
  bool ControlRetained,
  string Method);

public sealed record NestedConsolidationComponent(string EconomicEntityKey, Guid SourceScopeVersionId, bool IncludedDirectly);

public sealed record AssetTransferEliminationResult(
  decimal UnrealizedProfitElimination,
  decimal DepreciationAdjustment,
  decimal RelatedTaxEffect,
  decimal NetElimination);

public static class AdvancedConsolidationCalculator
{
  public const string AcquisitionNciMethod = "ACQUISITION_NCI_V1";
  public const string OwnershipChangeMethod = "OWNERSHIP_CHANGE_V1";
  public const string NestedGroupMethod = "NESTED_GROUP_V1";
  public const string AssetTransferMethod = "ASSET_TRANSFER_ELIMINATION_V1";

  public static AcquisitionAccountingResult CalculateAcquisition(AcquisitionAccountingInput input)
  {
    if (input.ControlDate < input.AcquisitionDate || input.Consideration < 0m || input.NciAtAcquisition < 0m)
      throw new InvalidOperationException("Acquisition and control dates and consideration must be explicit and ordered.");
    var fairValueAdjustedNetAssets = MoneyPolicy.Normalize(input.FairValueNetAssets + input.FairValueAdjustments);
    var goodwillOrBargain = MoneyPolicy.Normalize(input.Consideration + input.NciAtAcquisition - fairValueAdjustedNetAssets);
    return new AcquisitionAccountingResult(
      Math.Max(0m, goodwillOrBargain), Math.Max(0m, -goodwillOrBargain),
      MoneyPolicy.Normalize(input.OpeningReserves), fairValueAdjustedNetAssets, AcquisitionNciMethod);
  }

  public static NciRollforwardResult RollForwardNci(
    decimal openingNci, decimal profit, decimal oci, decimal distributions)
  {
    if (openingNci < 0m || distributions < 0m)
      throw new InvalidOperationException("NCI opening and distributions must be non-negative.");
    return new NciRollforwardResult(
      MoneyPolicy.Normalize(openingNci), MoneyPolicy.Normalize(profit), MoneyPolicy.Normalize(oci),
      MoneyPolicy.Normalize(distributions), MoneyPolicy.Normalize(openingNci + profit + oci - distributions));
  }

  public static OwnershipChangeResult CalculateOwnershipChange(OwnershipChangeInput input)
  {
    if (input.EffectiveDate == default || input.PreviousOwnershipPercent is < 0m or > 100m ||
        input.NewOwnershipPercent is < 0m or > 100m || input.Consideration < 0m ||
        input.FairValueRetainedInterest < 0m || input.CarryingNetAssets < 0m || input.CarryingNci < 0m ||
        (input.ControlLost && input.NewOwnershipPercent >= input.PreviousOwnershipPercent))
      throw new InvalidOperationException("An ownership change needs valid effective ownership and consideration inputs.");
    var controlRetained = !input.ControlLost;
    var nciMovement = controlRetained
      ? MoneyPolicy.Normalize((input.PreviousOwnershipPercent - input.NewOwnershipPercent) / 100m * input.CarryingNetAssets)
      : MoneyPolicy.Normalize(-input.CarryingNci);
    var disposalGainOrLoss = controlRetained
      ? 0m
      : MoneyPolicy.Normalize(input.Consideration + input.FairValueRetainedInterest - input.CarryingNetAssets - input.CarryingNci);
    return new OwnershipChangeResult(nciMovement, disposalGainOrLoss, controlRetained, OwnershipChangeMethod);
  }

  public static void EnsureNoNestedDoubleCount(IReadOnlyCollection<NestedConsolidationComponent> components)
  {
    if (components.Count == 0 || components.Any(x => string.IsNullOrWhiteSpace(x.EconomicEntityKey) || x.SourceScopeVersionId == Guid.Empty) ||
        components.GroupBy(x => x.EconomicEntityKey.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1) ||
        components.GroupBy(x => x.SourceScopeVersionId).Any(x => x.Count() > 1))
      throw new InvalidOperationException("Nested consolidation inputs contain a duplicated economic entity or source scope.");
  }

  public static AssetTransferEliminationResult CalculateAssetTransferElimination(
    decimal unrealizedProfit, decimal postTransferDepreciation, decimal taxRate)
  {
    if (unrealizedProfit < 0m || postTransferDepreciation < 0m || taxRate is < 0m or > 1m)
      throw new InvalidOperationException("Asset-transfer elimination needs non-negative profit/depreciation and a tax rate from 0 to 1.");
    var profitElimination = MoneyPolicy.Normalize(unrealizedProfit);
    var depreciationAdjustment = MoneyPolicy.Normalize(postTransferDepreciation);
    var taxEffect = MoneyPolicy.Normalize((profitElimination - depreciationAdjustment) * taxRate);
    return new AssetTransferEliminationResult(profitElimination, depreciationAdjustment, taxEffect,
      MoneyPolicy.Normalize(-profitElimination + depreciationAdjustment + taxEffect));
  }
}
