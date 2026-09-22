using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Accounting;

public sealed record AdvancedStatementLine(string Code, decimal ComparativeAmount, decimal CurrentAmount);

public sealed record AdvancedExecutionCalculation(
  string ComparativeStatementJson,
  string CurrentStatementJson,
  decimal ComparativeSignedTotal,
  decimal CurrentSignedTotal,
  string OutputManifest,
  string OutputDigest);

/// <summary>
/// Verifies the statement rollforward that accompanies an approved advanced-method schedule.
/// It never invents missing comparative/current lines: a schedule without complete balanced
/// statements remains a local review blocker.
/// </summary>
public static class AdvancedConsolidationExecutionCalculator
{
  public const string EngineVersion = "auditsphere.advanced-consolidation-engine.v1";

  public static bool TryCalculate(
    string method, string inputSnapshotJson, out AdvancedExecutionCalculation? calculation, out string error)
  {
    calculation = null;
    error = string.Empty;
    try
    {
      using var document = JsonDocument.Parse(inputSnapshotJson);
      var root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object)
      {
        error = "The advanced input snapshot must be a JSON object.";
        return false;
      }

      var normalizedMethod = method.Trim().ToUpperInvariant();
      if (!TryReadStatements(root, out var lines, out error) || !ValidateMethodInputs(normalizedMethod, root, lines, out var methodManifest, out error))
        return false;

      var comparative = lines.Select(x => new { code = x.Code, amount = x.ComparativeAmount })
        .OrderBy(x => x.code, StringComparer.Ordinal).ToArray();
      var current = lines.Select(x => new { code = x.Code, amount = x.CurrentAmount })
        .OrderBy(x => x.code, StringComparer.Ordinal).ToArray();
      var comparativeJson = JsonSerializer.Serialize(comparative);
      var currentJson = JsonSerializer.Serialize(current);
      var outputManifest = string.Join('\n', EngineVersion, normalizedMethod, methodManifest,
        Hashing.Sha256Hex(comparativeJson), Hashing.Sha256Hex(currentJson));
      calculation = new AdvancedExecutionCalculation(
        comparativeJson, currentJson,
        MoneyPolicy.Normalize(lines.Sum(x => x.ComparativeAmount)),
        MoneyPolicy.Normalize(lines.Sum(x => x.CurrentAmount)),
        outputManifest, Hashing.Sha256Hex(outputManifest));
      return true;
    }
    catch (JsonException)
    {
      error = "The advanced input snapshot is not valid JSON.";
      return false;
    }
    catch (InvalidOperationException ex)
    {
      error = ex.Message;
      return false;
    }
  }

  private static bool TryReadStatements(JsonElement root, out IReadOnlyList<AdvancedStatementLine> lines, out string error)
  {
    lines = [];
    error = string.Empty;
    var parsed = new Dictionary<string, AdvancedStatementLine>(StringComparer.OrdinalIgnoreCase);
    if (root.TryGetProperty("statementLines", out var combined))
    {
      if (combined.ValueKind != JsonValueKind.Array || combined.GetArrayLength() == 0)
      {
        error = "statementLines must be a non-empty array.";
        return false;
      }
      foreach (var line in combined.EnumerateArray())
      {
        if (!TryString(line, "code", out var code) || !TryDecimal(line, "comparativeAmount", out var comparative) ||
            !TryDecimal(line, "currentAmount", out var current) || !parsed.TryAdd(code, new AdvancedStatementLine(
              code, MoneyPolicy.Normalize(comparative), MoneyPolicy.Normalize(current))))
        {
          error = "Every statementLines row needs a unique code, comparativeAmount and currentAmount.";
          return false;
        }
      }
    }
    else
    {
      var comparativeCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var currentCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      if (!TryReadStatement(root, "comparativeStatement", parsed, comparativeCodes, true, ref error) ||
          !TryReadStatement(root, "currentStatement", parsed, currentCodes, false, ref error) || parsed.Count == 0)
        return false;
      if (!comparativeCodes.SetEquals(currentCodes))
      {
        error = "Comparative and current advanced statements must contain the same line codes.";
        return false;
      }
    }

    lines = parsed.Values.OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
    var comparativeTotal = MoneyPolicy.Normalize(lines.Sum(x => x.ComparativeAmount));
    var currentTotal = MoneyPolicy.Normalize(lines.Sum(x => x.CurrentAmount));
    if (comparativeTotal != 0m || currentTotal != 0m)
    {
      error = "Comparative and current advanced statements must each balance to zero.";
      return false;
    }
    return true;
  }

  private static bool TryReadStatement(JsonElement root, string propertyName,
    IDictionary<string, AdvancedStatementLine> lines, ISet<string> codes, bool comparative, ref string error)
  {
    if (!root.TryGetProperty(propertyName, out var statement) || statement.ValueKind != JsonValueKind.Array || statement.GetArrayLength() == 0)
    {
      error = $"{propertyName} must be a non-empty array.";
      return false;
    }
    foreach (var line in statement.EnumerateArray())
    {
      if (!TryString(line, "code", out var code) || !TryDecimal(line, "amount", out var amount) || !codes.Add(code))
      {
        error = $"Every {propertyName} row needs a unique code and amount.";
        return false;
      }
      if (lines.TryGetValue(code, out var existing))
      {
        lines[code] = comparative
          ? existing with { ComparativeAmount = MoneyPolicy.Normalize(amount) }
          : existing with { CurrentAmount = MoneyPolicy.Normalize(amount) };
      }
      else
      {
        lines.Add(code, comparative
          ? new AdvancedStatementLine(code, MoneyPolicy.Normalize(amount), 0m)
          : new AdvancedStatementLine(code, 0m, MoneyPolicy.Normalize(amount)));
      }
    }
    return true;
  }

  private static bool ValidateMethodInputs(string method, JsonElement root,
    IReadOnlyList<AdvancedStatementLine> lines, out string manifest, out string error)
  {
    manifest = string.Empty;
    error = string.Empty;
    switch (method)
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
          error = "Foreign-currency execution requires the complete opening, closing, profit, rate and currency inputs.";
          return false;
        }
        var translation = ForeignOperationTranslationCalculator.Translate(openingNetAssets, closingNetAssets, currentProfit,
          openingRate, closingRate, averageRate, openingReserve, functionalCurrency, presentationCurrency);
        if (!TryRequireLine(lines, "TRANSLATION_RESERVE", -openingReserve, -translation.ClosingTranslationReserve, out error))
          return false;
        manifest = string.Join('|', method, translation.OpeningNetAssetsTranslated.ToString("0.000000", CultureInfo.InvariantCulture),
          translation.ClosingNetAssetsTranslated.ToString("0.000000", CultureInfo.InvariantCulture),
          translation.CurrentProfitTranslated.ToString("0.000000", CultureInfo.InvariantCulture),
          translation.ClosingTranslationReserve.ToString("0.000000", CultureInfo.InvariantCulture),
          translation.RoundingAdjustment.ToString("0.000000", CultureInfo.InvariantCulture));
        return true;

      case AdvancedConsolidationMethods.AcquisitionNci:
        if (!TryDate(root, "acquisitionDate", out var acquisitionDate) || !TryDate(root, "controlDate", out var controlDate) ||
            !TryDecimal(root, "consideration", out var consideration) || !TryDecimal(root, "nciAtAcquisition", out var nciAtAcquisition) ||
            !TryDecimal(root, "fairValueNetAssets", out var fairValueNetAssets) || !TryDecimal(root, "openingReserves", out var openingReserves) ||
            !TryDecimal(root, "fairValueAdjustments", out var fairValueAdjustments) || !TryDecimal(root, "nciOpening", out var nciOpening) ||
            !TryDecimal(root, "nciProfit", out var nciProfit) || !TryDecimal(root, "nciOci", out var nciOci) ||
            !TryDecimal(root, "nciDistributions", out var nciDistributions))
        {
          error = "Acquisition/NCI execution requires acquisition, control, fair-value, reserve and NCI rollforward inputs.";
          return false;
        }
        var acquisition = AdvancedConsolidationCalculator.CalculateAcquisition(new AcquisitionAccountingInput(acquisitionDate,
          controlDate, consideration, nciAtAcquisition, fairValueNetAssets, openingReserves, fairValueAdjustments));
        var nci = AdvancedConsolidationCalculator.RollForwardNci(nciOpening, nciProfit, nciOci, nciDistributions);
        if (!TryRequireLine(lines, "NCI", -nciOpening, -nci.ClosingNci, out error))
          return false;
        var goodwillCode = acquisition.Goodwill > 0m ? "GOODWILL" : "BARGAIN_PURCHASE";
        var goodwillAmount = acquisition.Goodwill > 0m ? acquisition.Goodwill : acquisition.BargainPurchase;
        if (!TryRequireLine(lines, goodwillCode, 0m, goodwillAmount, out error))
          return false;
        manifest = string.Join('|', method, acquisitionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
          controlDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), acquisition.Goodwill.ToString("0.000000", CultureInfo.InvariantCulture),
          acquisition.BargainPurchase.ToString("0.000000", CultureInfo.InvariantCulture), nci.ClosingNci.ToString("0.000000", CultureInfo.InvariantCulture));
        return true;

      case AdvancedConsolidationMethods.OwnershipChange:
        if (!TryDate(root, "effectiveDate", out var effectiveDate) || !TryDecimal(root, "previousOwnershipPercent", out var previousOwnership) ||
            !TryDecimal(root, "newOwnershipPercent", out var newOwnership) || !TryDecimal(root, "consideration", out var ownershipConsideration) ||
            !TryDecimal(root, "fairValueRetainedInterest", out var retainedInterest) || !TryDecimal(root, "carryingNetAssets", out var carryingNetAssets) ||
            !TryDecimal(root, "carryingNci", out var carryingNci) || !TryBool(root, "controlLost", out var controlLost))
        {
          error = "Ownership-change execution requires the complete effective-date and carrying-value inputs.";
          return false;
        }
        var ownership = AdvancedConsolidationCalculator.CalculateOwnershipChange(new OwnershipChangeInput(effectiveDate,
          previousOwnership, newOwnership, ownershipConsideration, retainedInterest, carryingNetAssets, carryingNci, controlLost));
        if (!TryRequireLine(lines, "NCI_MOVEMENT", 0m, ownership.NciMovement, out error) ||
            !TryRequireLine(lines, "OWNERSHIP_CHANGE_GAIN_LOSS", 0m, ownership.DisposalGainOrLoss, out error))
          return false;
        manifest = string.Join('|', method, effectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
          ownership.NciMovement.ToString("0.000000", CultureInfo.InvariantCulture), ownership.DisposalGainOrLoss.ToString("0.000000", CultureInfo.InvariantCulture),
          ownership.ControlRetained.ToString(CultureInfo.InvariantCulture));
        return true;

      case AdvancedConsolidationMethods.NestedGroup:
        if (!root.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Array || components.GetArrayLength() == 0)
        {
          error = "Nested-group execution requires source scopes.";
          return false;
        }
        var nested = new List<NestedConsolidationComponent>();
        foreach (var component in components.EnumerateArray())
        {
          if (!TryString(component, "economicEntityKey", out var key) || !TryGuid(component, "sourceScopeVersionId", out var sourceScope) ||
              !TryBool(component, "includedDirectly", out var includedDirectly))
          {
            error = "Every nested-group source needs an entity key, source scope and direct-inclusion flag.";
            return false;
          }
          nested.Add(new NestedConsolidationComponent(key, sourceScope, includedDirectly));
        }
        AdvancedConsolidationCalculator.EnsureNoNestedDoubleCount(nested);
        manifest = string.Join('|', method, string.Join(',', nested.OrderBy(x => x.EconomicEntityKey, StringComparer.Ordinal)
          .Select(x => $"{x.EconomicEntityKey}:{x.SourceScopeVersionId:D}:{x.IncludedDirectly}")));
        return true;

      case AdvancedConsolidationMethods.AssetTransferElimination:
        if (!TryDecimal(root, "unrealizedProfit", out var unrealizedProfit) || !TryDecimal(root, "postTransferDepreciation", out var postTransferDepreciation) ||
            !TryDecimal(root, "taxRate", out var taxRate))
        {
          error = "Asset-transfer execution requires unrealized profit, depreciation and tax inputs.";
          return false;
        }
        var elimination = AdvancedConsolidationCalculator.CalculateAssetTransferElimination(unrealizedProfit, postTransferDepreciation, taxRate);
        if (!TryRequireLine(lines, "ASSET_TRANSFER_ELIMINATION", 0m, elimination.NetElimination, out error))
          return false;
        manifest = string.Join('|', method, elimination.UnrealizedProfitElimination.ToString("0.000000", CultureInfo.InvariantCulture),
          elimination.DepreciationAdjustment.ToString("0.000000", CultureInfo.InvariantCulture), elimination.RelatedTaxEffect.ToString("0.000000", CultureInfo.InvariantCulture),
          elimination.NetElimination.ToString("0.000000", CultureInfo.InvariantCulture));
        return true;

      default:
        error = "The advanced consolidation method is not supported.";
        return false;
    }
  }

  private static bool TryRequireLine(IReadOnlyList<AdvancedStatementLine> lines, string code,
    decimal comparative, decimal current, out string error)
  {
    var line = lines.SingleOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    if (line is null || line.ComparativeAmount != MoneyPolicy.Normalize(comparative) || line.CurrentAmount != MoneyPolicy.Normalize(current))
    {
      error = $"The statement rollforward must contain {code} with the method-calculated comparative and current amounts.";
      return false;
    }
    error = string.Empty;
    return true;
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
      DateOnly.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
  }

  private static bool TryString(JsonElement root, string name, out string value)
  {
    value = string.Empty;
    if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
      return false;
    value = property.GetString() ?? string.Empty;
    return !string.IsNullOrWhiteSpace(value);
  }

  private static bool TryGuid(JsonElement root, string name, out Guid value)
  {
    value = Guid.Empty;
    return root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
      Guid.TryParse(property.GetString(), out value);
  }

  private static bool TryBool(JsonElement root, string name, out bool value)
  {
    value = false;
    if (!root.TryGetProperty(name, out var property) || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
      return false;
    value = property.GetBoolean();
    return true;
  }
}
