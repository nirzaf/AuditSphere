using System.Globalization;
using System.Text;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Domain.Accounting;

public sealed record ClientAccountingDimensionSchemaValueDraft(
  Guid Id,
  string Code,
  string Name,
  bool IsActive,
  DateOnly EffectiveFrom,
  DateOnly? EffectiveTo);

public sealed record ClientAccountingDimensionSchemaDefinitionDraft(
  Guid Id,
  string DimensionType,
  string Code,
  string Name,
  bool IsRequired,
  IReadOnlyList<ClientAccountingDimensionSchemaValueDraft> Values);

/// <summary>Input to create one immutable schema revision. IDs are assigned by the owning Application operation.</summary>
public sealed record ClientAccountingDimensionSchemaDraft(
  Guid FirmId,
  Guid ClientId,
  Guid SchemaId,
  Guid RevisionId,
  long Revision,
  IReadOnlyList<ClientAccountingDimensionSchemaDefinitionDraft> Definitions,
  Guid? BaseRevisionId = null);

public sealed record ClientAccountingDimensionSchemaIssue(string Code, string Path);

public sealed record ClientAccountingDimensionSchemaBuildResult(
  ClientAccountingDimensionSchemaRevision? Value,
  IReadOnlyList<ClientAccountingDimensionSchemaIssue> Issues)
{
  public bool IsValid => Value is not null && Issues.Count == 0;
}

public sealed record ClientAccountingDimensionSchemaValue(
  Guid Id,
  string Code,
  string Name,
  bool IsActive,
  DateOnly EffectiveFrom,
  DateOnly? EffectiveTo);

public sealed record ClientAccountingDimensionSchemaDefinition(
  Guid Id,
  string DimensionType,
  string Code,
  string Name,
  bool IsRequired,
  IReadOnlyList<ClientAccountingDimensionSchemaValue> Values);

public sealed record ClientAccountingDimensionSelection(string DimensionType, string Code);

/// <summary>
/// Immutable content for one client-owned accounting-dimension schema revision.
/// This is content only; its owner applies the approved Module 20 lifecycle and publication decision.
/// </summary>
public sealed class ClientAccountingDimensionSchemaRevision
{
  public const int CanonicalDigestVersion = 1;
  public const int MaximumCodeLength = 100;
  public const int MaximumNameLength = 300;

  private ClientAccountingDimensionSchemaRevision(
    Guid firmId,
    Guid clientId,
    Guid schemaId,
    Guid revisionId,
    long revision,
    Guid? baseRevisionId,
    IReadOnlyList<ClientAccountingDimensionSchemaDefinition> definitions)
  {
    FirmId = firmId;
    ClientId = clientId;
    SchemaId = schemaId;
    RevisionId = revisionId;
    Revision = revision;
    BaseRevisionId = baseRevisionId;
    Definitions = definitions;
    CanonicalDigest = ComputeCanonicalDigest();
  }

  public Guid FirmId { get; }
  public Guid ClientId { get; }
  public Guid SchemaId { get; }
  public Guid RevisionId { get; }
  public long Revision { get; }
  public Guid? BaseRevisionId { get; }
  public IReadOnlyList<ClientAccountingDimensionSchemaDefinition> Definitions { get; }
  public string CanonicalDigest { get; }

  /// <summary>
  /// Validates and copies draft content. It performs no ID generation, persistence, clock reads or publication.
  /// Dimension-code uniqueness follows the existing GL import lookup, which is case-insensitive.
  /// </summary>
  public static ClientAccountingDimensionSchemaBuildResult Create(ClientAccountingDimensionSchemaDraft draft)
  {
    var issues = new List<ClientAccountingDimensionSchemaIssue>();
    if (draft is null)
    {
      Add(issues, "dimension-schema.required", "schema");
      return BuildFailure(issues);
    }

    if (draft.FirmId == Guid.Empty) Add(issues, "dimension-schema.scope.required", "firmId");
    if (draft.ClientId == Guid.Empty) Add(issues, "dimension-schema.scope.required", "clientId");
    if (draft.SchemaId == Guid.Empty) Add(issues, "dimension-schema.identity.required", "schemaId");
    if (draft.RevisionId == Guid.Empty) Add(issues, "dimension-schema.revision-identity.required", "revisionId");
    if (draft.Revision < 1) Add(issues, "dimension-schema.revision.invalid", "revision");
    else if ((draft.Revision == 1) == draft.BaseRevisionId.HasValue)
      Add(issues, "dimension-schema.base-revision.invalid", "baseRevisionId");
    if (draft.Definitions is null)
    {
      Add(issues, "dimension-schema.definitions.required", "definitions");
      return BuildFailure(issues);
    }

    var definitions = new List<ClientAccountingDimensionSchemaDefinition>();
    var definitionIds = new HashSet<Guid>();
    var valueIds = new HashSet<Guid>();
    var dimensionTypes = new HashSet<string>(StringComparer.Ordinal);
    var definitionCodesByType = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

    for (var definitionIndex = 0; definitionIndex < draft.Definitions.Count; definitionIndex++)
    {
      var sourceDefinition = draft.Definitions[definitionIndex];
      var definitionPath = $"definitions[{definitionIndex}]";
      if (sourceDefinition is null)
      {
        Add(issues, "dimension-schema.definition.required", definitionPath);
        continue;
      }

      if (sourceDefinition.Id == Guid.Empty)
        Add(issues, "dimension-schema.definition.identity.required", $"{definitionPath}.id");
      else if (!definitionIds.Add(sourceDefinition.Id))
        Add(issues, "dimension-schema.definition.identity.duplicate", $"{definitionPath}.id");

      var dimensionType = NormalizeDimensionType(sourceDefinition.DimensionType);
      if (!AccountingDimensionTypes.All.Contains(dimensionType))
        Add(issues, "dimension-schema.definition.type.unsupported", $"{definitionPath}.dimensionType");
      else if (!dimensionTypes.Add(dimensionType))
        Add(issues, "dimension-schema.definition.type.duplicate", $"{definitionPath}.dimensionType");

      var definitionCode = sourceDefinition.Code?.Trim() ?? string.Empty;
      var definitionName = sourceDefinition.Name?.Trim() ?? string.Empty;
      if (definitionCode.Length == 0)
        Add(issues, "dimension-schema.definition.code.required", $"{definitionPath}.code");
      else if (definitionCode.Length > MaximumCodeLength)
        Add(issues, "dimension-schema.definition.code.too-long", $"{definitionPath}.code");
      else
      {
        if (!definitionCodesByType.TryGetValue(dimensionType, out var definitionCodes))
          definitionCodesByType[dimensionType] = definitionCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!definitionCodes.Add(definitionCode))
          Add(issues, "dimension-schema.definition.code.duplicate", $"{definitionPath}.code");
      }
      if (definitionName.Length == 0)
        Add(issues, "dimension-schema.definition.name.required", $"{definitionPath}.name");
      else if (definitionName.Length > MaximumNameLength)
        Add(issues, "dimension-schema.definition.name.too-long", $"{definitionPath}.name");

      if (sourceDefinition.Values is null)
      {
        Add(issues, "dimension-schema.values.required", $"{definitionPath}.values");
        continue;
      }

      var values = new List<ClientAccountingDimensionSchemaValue>();
      var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      for (var valueIndex = 0; valueIndex < sourceDefinition.Values.Count; valueIndex++)
      {
        var sourceValue = sourceDefinition.Values[valueIndex];
        var valuePath = $"{definitionPath}.values[{valueIndex}]";
        if (sourceValue is null)
        {
          Add(issues, "dimension-schema.value.required", valuePath);
          continue;
        }

        if (sourceValue.Id == Guid.Empty)
          Add(issues, "dimension-schema.value.identity.required", $"{valuePath}.id");
        else if (!valueIds.Add(sourceValue.Id))
          Add(issues, "dimension-schema.value.identity.duplicate", $"{valuePath}.id");

        var code = sourceValue.Code?.Trim() ?? string.Empty;
        var name = sourceValue.Name?.Trim() ?? string.Empty;
        if (code.Length == 0)
          Add(issues, "dimension-schema.value.code.required", $"{valuePath}.code");
        else
        {
          if (code.Length > MaximumCodeLength)
            Add(issues, "dimension-schema.value.code.too-long", $"{valuePath}.code");
          if (!codes.Add(code))
            Add(issues, "dimension-schema.value.code.duplicate", $"{valuePath}.code");
        }

        if (name.Length == 0)
          Add(issues, "dimension-schema.value.name.required", $"{valuePath}.name");
        else if (name.Length > MaximumNameLength)
          Add(issues, "dimension-schema.value.name.too-long", $"{valuePath}.name");

        if (!HasValidRange(sourceValue.EffectiveFrom, sourceValue.EffectiveTo))
          Add(issues, "dimension-schema.value.effective-range.invalid", valuePath);

        values.Add(new ClientAccountingDimensionSchemaValue(
          sourceValue.Id, code, name, sourceValue.IsActive, sourceValue.EffectiveFrom, sourceValue.EffectiveTo));
      }

      if (sourceDefinition.IsRequired && sourceDefinition.Values.Count == 0)
        Add(issues, "dimension-schema.definition.required-values-missing", $"{definitionPath}.values");

      var orderedValues = values
        .OrderBy(x => x.Id.ToString("N"), StringComparer.Ordinal)
        .ToArray();
      definitions.Add(new ClientAccountingDimensionSchemaDefinition(
        sourceDefinition.Id,
        dimensionType,
        definitionCode,
        definitionName,
        sourceDefinition.IsRequired,
        Array.AsReadOnly(orderedValues)));
    }

    if (issues.Count > 0)
      return BuildFailure(issues);

    var orderedDefinitions = definitions
      .OrderBy(x => x.Id.ToString("N"), StringComparer.Ordinal)
      .ToArray();
    var revision = new ClientAccountingDimensionSchemaRevision(
      draft.FirmId,
      draft.ClientId,
      draft.SchemaId,
      draft.RevisionId,
      draft.Revision,
      draft.BaseRevisionId,
      Array.AsReadOnly(orderedDefinitions));
    return new ClientAccountingDimensionSchemaBuildResult(
      revision, Array.AsReadOnly(Array.Empty<ClientAccountingDimensionSchemaIssue>()));
  }

  /// <summary>
  /// Ensures a successor snapshot retains every stable definition/value identity from its exact
  /// published base. Existing members may be renamed, recoded, made optional, or deactivated, but
  /// are not silently dropped or moved to a different dimension type/definition.
  /// </summary>
  public IReadOnlyList<ClientAccountingDimensionSchemaIssue> ValidateSuccessorFrom(
    ClientAccountingDimensionSchemaRevision? baseRevision)
  {
    var issues = new List<ClientAccountingDimensionSchemaIssue>();
    if (baseRevision is null)
    {
      Add(issues, "dimension-schema.base.required", "baseRevision");
      return Array.AsReadOnly(issues.ToArray());
    }

    if (FirmId != baseRevision.FirmId || ClientId != baseRevision.ClientId || SchemaId != baseRevision.SchemaId)
      Add(issues, "dimension-schema.successor.scope-or-lineage-mismatch", "scope");
    if (RevisionId == baseRevision.RevisionId)
      Add(issues, "dimension-schema.successor.revision-identity-reused", "revisionId");
    if (BaseRevisionId != baseRevision.RevisionId)
      Add(issues, "dimension-schema.successor.base-mismatch", "baseRevisionId");
    if (baseRevision.Revision == long.MaxValue || Revision != baseRevision.Revision + 1)
      Add(issues, "dimension-schema.successor.revision-invalid", "revision");

    issues.AddRange(ValidateStableMembersFrom(baseRevision));
    return Array.AsReadOnly(issues.ToArray());
  }

  /// <summary>Ensures editing a saved draft cannot silently replace an already assigned member ID.</summary>
  public IReadOnlyList<ClientAccountingDimensionSchemaIssue> ValidateDraftEditFrom(
    ClientAccountingDimensionSchemaRevision? previousDraft)
  {
    var issues = new List<ClientAccountingDimensionSchemaIssue>();
    if (previousDraft is null)
    {
      Add(issues, "dimension-schema.draft-edit.base-required", "previousDraft");
      return Array.AsReadOnly(issues.ToArray());
    }

    if (FirmId != previousDraft.FirmId || ClientId != previousDraft.ClientId || SchemaId != previousDraft.SchemaId ||
        RevisionId != previousDraft.RevisionId || Revision != previousDraft.Revision || BaseRevisionId != previousDraft.BaseRevisionId)
      Add(issues, "dimension-schema.draft-edit.identity-mismatch", "draft");
    issues.AddRange(ValidateStableMembersFrom(previousDraft));
    return Array.AsReadOnly(issues.ToArray());
  }

  private IReadOnlyList<ClientAccountingDimensionSchemaIssue> ValidateStableMembersFrom(
    ClientAccountingDimensionSchemaRevision previous)
  {
    var issues = new List<ClientAccountingDimensionSchemaIssue>();

    var currentDefinitions = Definitions.ToDictionary(x => x.Id);
    var currentValueOwners = Definitions
      .SelectMany(definition => definition.Values.Select(value => (value.Id, DefinitionId: definition.Id)))
      .ToDictionary(x => x.Id, x => x.DefinitionId);
    var priorValueOwners = previous.Definitions
      .SelectMany(definition => definition.Values.Select(value => (value.Id, DefinitionId: definition.Id)))
      .ToDictionary(x => x.Id, x => x.DefinitionId);
    foreach (var priorDefinition in previous.Definitions)
    {
      if (!currentDefinitions.TryGetValue(priorDefinition.Id, out var currentDefinition))
      {
        Add(issues, "dimension-schema.successor.definition-identity-missing", $"definitions.{priorDefinition.Id:D}");
        foreach (var priorValue in priorDefinition.Values)
          if (!currentValueOwners.ContainsKey(priorValue.Id))
            Add(issues, "dimension-schema.successor.value-identity-missing", $"definitions.{priorDefinition.Id:D}.values.{priorValue.Id:D}");
        continue;
      }

      if (!string.Equals(currentDefinition.DimensionType, priorDefinition.DimensionType, StringComparison.Ordinal))
        Add(issues, "dimension-schema.successor.definition-type-changed", $"definitions.{priorDefinition.Id:D}.dimensionType");

      var currentValues = currentDefinition.Values.ToDictionary(x => x.Id);
      foreach (var priorValue in priorDefinition.Values)
      {
        if (!currentValues.ContainsKey(priorValue.Id))
          Add(issues, "dimension-schema.successor.value-identity-missing", $"definitions.{priorDefinition.Id:D}.values.{priorValue.Id:D}");
      }
    }

    foreach (var currentDefinition in Definitions)
    {
      foreach (var currentValue in currentDefinition.Values)
      {
        if (priorValueOwners.TryGetValue(currentValue.Id, out var priorDefinitionId) &&
            priorDefinitionId != currentDefinition.Id)
          Add(issues, "dimension-schema.successor.value-definition-changed", $"definitions.{currentDefinition.Id:D}.values.{currentValue.Id:D}");
      }
    }

    return Array.AsReadOnly(issues.ToArray());
  }

  /// <summary>Checks a line's dimension selections against this exact schema revision and posting period.</summary>
  public IReadOnlyList<ClientAccountingDimensionSchemaIssue> ValidateSelections(
    Guid firmId,
    Guid clientId,
    DateOnly periodStart,
    DateOnly periodEnd,
    DateOnly postingDate,
    IReadOnlyList<ClientAccountingDimensionSelection> selections)
  {
    var issues = new List<ClientAccountingDimensionSchemaIssue>();
    if (firmId != FirmId || clientId != ClientId)
      Add(issues, "dimension-schema.scope.mismatch", "scope");
    if (periodStart > periodEnd)
      Add(issues, "dimension-schema.period.invalid", "period");
    if (postingDate < periodStart || postingDate > periodEnd)
      Add(issues, "dimension-schema.posting-date.out-of-period", "postingDate");
    if (selections is null)
    {
      Add(issues, "dimension-schema.selections.required", "selections");
      return Array.AsReadOnly(issues.ToArray());
    }

    var selectedTypes = new HashSet<string>(StringComparer.Ordinal);
    foreach (var selection in selections)
    {
      if (selection is null)
      {
        Add(issues, "dimension-schema.selection.required", "selections");
        continue;
      }

      var dimensionType = NormalizeDimensionType(selection.DimensionType);
      if (!AccountingDimensionTypes.All.Contains(dimensionType))
      {
        Add(issues, "dimension-schema.selection.type.unknown", "selections.dimensionType");
        continue;
      }

      if (!selectedTypes.Add(dimensionType))
        Add(issues, "dimension-schema.selection.scope.duplicate", $"selections.{dimensionType}");

      var definition = Definitions.FirstOrDefault(x => x.DimensionType == dimensionType);
      if (definition is null)
      {
        Add(issues, "dimension-schema.selection.definition.unknown", $"selections.{dimensionType}");
        continue;
      }

      var code = selection.Code?.Trim() ?? string.Empty;
      if (code.Length == 0)
      {
        Add(issues, "dimension-schema.selection.value.required", $"selections.{dimensionType}.code");
        continue;
      }

      var value = definition.Values.FirstOrDefault(x =>
        string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
      if (value is null)
      {
        Add(issues, "dimension-schema.selection.value.unknown", $"selections.{dimensionType}.code");
        continue;
      }

      if (!value.IsActive)
        Add(issues, "dimension-schema.selection.value.inactive", $"selections.{dimensionType}.code");
      else if (!IsEffective(postingDate, value.EffectiveFrom, value.EffectiveTo))
        Add(issues, "dimension-schema.selection.out-of-period", $"selections.{dimensionType}.code");
    }

    foreach (var definition in Definitions)
    {
      if (definition.IsRequired && !selectedTypes.Contains(definition.DimensionType))
        Add(issues, "dimension-schema.selection.required", $"selections.{definition.DimensionType}");
    }

    return Array.AsReadOnly(issues.ToArray());
  }

  private string ComputeCanonicalDigest()
  {
    var canonical = new StringBuilder();
    AppendField(canonical, $"client-accounting-dimension-schema.v{CanonicalDigestVersion}");
    AppendField(canonical, FirmId.ToString("D"));
    AppendField(canonical, ClientId.ToString("D"));
    AppendField(canonical, SchemaId.ToString("D"));
    AppendField(canonical, RevisionId.ToString("D"));
    AppendField(canonical, BaseRevisionId?.ToString("D") ?? "<none>");
    AppendField(canonical, Revision.ToString(CultureInfo.InvariantCulture));
    AppendField(canonical, Definitions.Count.ToString(CultureInfo.InvariantCulture));

    foreach (var definition in Definitions)
    {
      AppendField(canonical, definition.Id.ToString("D"));
      AppendField(canonical, definition.DimensionType);
      AppendField(canonical, definition.Code);
      AppendField(canonical, definition.Name);
      AppendField(canonical, definition.IsRequired ? "1" : "0");
      AppendField(canonical, definition.Values.Count.ToString(CultureInfo.InvariantCulture));

      foreach (var value in definition.Values)
      {
        AppendField(canonical, value.Id.ToString("D"));
        AppendField(canonical, value.Code);
        AppendField(canonical, value.Name);
        AppendField(canonical, value.IsActive ? "1" : "0");
        AppendField(canonical, FormatDate(value.EffectiveFrom));
        AppendField(canonical, FormatOptionalDate(value.EffectiveTo));
      }
    }

    return Hashing.Sha256Hex(canonical.ToString());
  }

  private static ClientAccountingDimensionSchemaBuildResult BuildFailure(
    List<ClientAccountingDimensionSchemaIssue> issues) =>
    new(null, Array.AsReadOnly(issues.ToArray()));

  private static void Add(List<ClientAccountingDimensionSchemaIssue> issues, string code, string path) =>
    issues.Add(new ClientAccountingDimensionSchemaIssue(code, path));

  private static string NormalizeDimensionType(string? dimensionType) =>
    dimensionType?.Trim().ToUpperInvariant() ?? string.Empty;

  private static bool HasValidRange(DateOnly start, DateOnly? end) => !end.HasValue || end.Value >= start;

  // EffectiveFrom/EffectiveTo are inclusive, matching the existing DateOnly reporting-period bounds.
  private static bool IsEffective(DateOnly date, DateOnly start, DateOnly? end) =>
    date >= start && (!end.HasValue || date <= end.Value);

  private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

  private static string FormatOptionalDate(DateOnly? date) =>
    date.HasValue ? FormatDate(date.Value) : "<open>";

  // The prefix is a UTF-8 byte count (not a UTF-16 character count), so arbitrary names/codes
  // remain unambiguous without relying on JSON serializer settings.
  private static void AppendField(StringBuilder canonical, string value)
  {
    var utf8ByteLength = Encoding.UTF8.GetByteCount(value);
    canonical.Append(utf8ByteLength.ToString(CultureInfo.InvariantCulture));
    canonical.Append(':');
    canonical.Append(value);
    canonical.Append('\n');
  }
}
