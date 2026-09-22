using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Domain.Accounting;
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

  public static bool TryValidateScheduleInput(string method, string json, out string error)
  {
    error = string.Empty;
    try
    {
      using var document = JsonDocument.Parse(json);
      if (document.RootElement.ValueKind != JsonValueKind.Object)
      {
        error = "The method input must be a JSON object.";
        return false;
      }

      var root = document.RootElement;
      switch (method.Trim().ToUpperInvariant())
      {
        case AdvancedConsolidationMethods.ForeignCurrencyReserve:
          if (!TryDecimal(root, "openingNetAssets", out var openingNetAssets) ||
              !TryDecimal(root, "closingNetAssets", out var closingNetAssets) ||
              !TryDecimal(root, "currentProfit", out var currentProfit) ||
              !TryDecimal(root, "openingRate", out var openingRate) ||
              !TryDecimal(root, "closingRate", out var closingRate) ||
              !TryDecimal(root, "averageRate", out var averageRate) ||
              !TryDecimal(root, "openingTranslationReserve", out var openingReserve) ||
              !TryString(root, "functionalCurrency", out var functionalCurrency) ||
              !TryString(root, "presentationCurrency", out var presentationCurrency))
          {
            error = "Foreign-currency reserve input requires net assets, profit, opening reserve, opening/closing/average rates and both currencies.";
            return false;
          }
          ForeignOperationTranslationCalculator.Translate(openingNetAssets, closingNetAssets, currentProfit,
            openingRate, closingRate, averageRate, openingReserve, functionalCurrency, presentationCurrency);
          return true;

        case AdvancedConsolidationMethods.AcquisitionNci:
          if (!TryDate(root, "acquisitionDate", out var acquisitionDate) ||
              !TryDate(root, "controlDate", out var controlDate) ||
              !TryDecimal(root, "consideration", out var consideration) ||
              !TryDecimal(root, "nciAtAcquisition", out var nciAtAcquisition) ||
              !TryDecimal(root, "fairValueNetAssets", out var fairValueNetAssets) ||
              !TryDecimal(root, "openingReserves", out var openingReserves) ||
              !TryDecimal(root, "fairValueAdjustments", out var fairValueAdjustments) ||
              !TryDecimal(root, "nciOpening", out var nciOpening) ||
              !TryDecimal(root, "nciProfit", out var nciProfit) ||
              !TryDecimal(root, "nciOci", out var nciOci) ||
              !TryDecimal(root, "nciDistributions", out var nciDistributions))
          {
            error = "Acquisition/NCI input requires acquisition and control dates, consideration, fair-value inputs, reserves and the complete NCI rollforward.";
            return false;
          }
          CalculateAcquisition(new AcquisitionAccountingInput(acquisitionDate, controlDate, consideration, nciAtAcquisition,
            fairValueNetAssets, openingReserves, fairValueAdjustments));
          RollForwardNci(nciOpening, nciProfit, nciOci, nciDistributions);
          return true;

        case AdvancedConsolidationMethods.OwnershipChange:
          if (!TryDate(root, "effectiveDate", out var effectiveDate) ||
              !TryDecimal(root, "previousOwnershipPercent", out var previousOwnershipPercent) ||
              !TryDecimal(root, "newOwnershipPercent", out var newOwnershipPercent) ||
              !TryDecimal(root, "consideration", out var ownershipConsideration) ||
              !TryDecimal(root, "fairValueRetainedInterest", out var fairValueRetainedInterest) ||
              !TryDecimal(root, "carryingNetAssets", out var carryingNetAssets) ||
              !TryDecimal(root, "carryingNci", out var carryingNci) ||
              !TryBool(root, "controlLost", out var controlLost))
          {
            error = "Ownership-change input requires an effective date, both ownership percentages, consideration, retained interest, carrying values and control status.";
            return false;
          }
          CalculateOwnershipChange(new OwnershipChangeInput(effectiveDate, previousOwnershipPercent, newOwnershipPercent,
            ownershipConsideration, fairValueRetainedInterest, carryingNetAssets, carryingNci, controlLost));
          return true;

        case AdvancedConsolidationMethods.NestedGroup:
          if (!root.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Array ||
              components.GetArrayLength() == 0)
          {
            error = "Nested-group input requires at least one source-scope component.";
            return false;
          }
          var nestedComponents = new List<NestedConsolidationComponent>();
          foreach (var component in components.EnumerateArray())
          {
            if (component.ValueKind != JsonValueKind.Object ||
                !TryString(component, "economicEntityKey", out var economicEntityKey) ||
                !TryGuid(component, "sourceScopeVersionId", out var sourceScopeVersionId) ||
                !TryBool(component, "includedDirectly", out var includedDirectly))
            {
              error = "Every nested-group component needs an entity key, source scope identifier and direct-inclusion flag.";
              return false;
            }
            nestedComponents.Add(new NestedConsolidationComponent(economicEntityKey, sourceScopeVersionId, includedDirectly));
          }
          EnsureNoNestedDoubleCount(nestedComponents);
          return true;

        case AdvancedConsolidationMethods.AssetTransferElimination:
          if (!TryDecimal(root, "unrealizedProfit", out var unrealizedProfit) ||
              !TryDecimal(root, "postTransferDepreciation", out var postTransferDepreciation) ||
              !TryDecimal(root, "taxRate", out var taxRate))
          {
            error = "Asset-transfer input requires unrealized profit, post-transfer depreciation and tax rate.";
            return false;
          }
          CalculateAssetTransferElimination(unrealizedProfit, postTransferDepreciation, taxRate);
          return true;

        default:
          error = "The advanced consolidation method is not supported.";
          return false;
      }
    }
    catch (JsonException)
    {
      error = "The method input is not valid JSON.";
      return false;
    }
    catch (InvalidOperationException ex)
    {
      error = ex.Message;
      return false;
    }
  }

  private static bool TryDecimal(JsonElement root, string name, out decimal value)
  {
    value = 0m;
    return root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value);
  }

  private static bool TryDate(JsonElement root, string name, out DateOnly value)
  {
    value = default;
    return root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
      DateOnly.TryParse(property.GetString() ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
  }

  private static bool TryString(JsonElement root, string name, out string value)
  {
    value = string.Empty;
    if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
      return false;
    var text = property.GetString();
    if (string.IsNullOrWhiteSpace(text))
      return false;
    value = text;
    return true;
  }

  private static bool TryGuid(JsonElement root, string name, out Guid value)
  {
    value = Guid.Empty;
    return root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
      Guid.TryParse(property.GetString() ?? string.Empty, out value);
  }

  private static bool TryBool(JsonElement root, string name, out bool value)
  {
    value = false;
    if (!root.TryGetProperty(name, out var property) || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
      return false;
    value = property.GetBoolean();
    return true;
  }

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
