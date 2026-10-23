using AuditSphereOps.Domain.Accounting;

namespace AuditSphereOps.Application.Accounting;

/// <summary>
/// Module 20 draft contract plus backward-compatible technical references needed to edit and
/// publish a persisted draft. Null member IDs mean new members; non-null IDs must resolve from the
/// exact base revision or the exact draft being edited.
/// </summary>
public sealed record DimensionSchemaDraftDto(
  Guid ClientId,
  Guid? BaseRevisionRef,
  IReadOnlyList<DimensionSchemaDefinitionDraftDto> Definitions,
  Guid? DraftRevisionRef = null,
  bool ImpactAcknowledged = false,
  string? ExpectedCanonicalDigest = null);

public sealed record DimensionSchemaDefinitionDraftDto(
  string DimensionType,
  string Code,
  string Name,
  bool Required,
  IReadOnlyList<DimensionSchemaValueDraftDto> Values,
  Guid? DefinitionId = null);

public sealed record DimensionSchemaValueDraftDto(
  string Code,
  string Name,
  DateOnly EffectiveFrom,
  DateOnly? EffectiveTo,
  bool Active,
  Guid? ValueId = null);

public sealed record DimensionSchemaDraftReceiptDto(
  Guid DraftRevisionRef,
  Guid SchemaId,
  long Revision,
  int CanonicalDigestVersion,
  string CanonicalDigest,
  IReadOnlyList<DimensionSchemaDefinitionDraftDto> Definitions);

public sealed record DimensionSchemaPublicationReceiptDto(
  Guid SchemaRevisionRef,
  Guid SchemaId,
  long Revision,
  int CanonicalDigestVersion,
  string CanonicalDigest,
  string Status);

/// <summary>Exact published schema identity for downstream context resolution; never resolves to latest.</summary>
public sealed record DimensionSchemaRevisionRef(
  Guid SchemaId,
  Guid RevisionId,
  long Revision,
  int CanonicalDigestVersion,
  string CanonicalDigest);

/// <summary>Server-resolved owner scope and exact published schema identity.</summary>
public sealed record ResolvedDimensionSchemaRevisionDto(
  Guid FirmId,
  Guid ClientId,
  DimensionSchemaRevisionRef Reference);

/// <summary>
/// Application-owned ID assignment and mapping. No caller chooses a new identity: IDs are
/// generated here for null member IDs, and supplied IDs are accepted only from the resolved base
/// or draft. The Domain factory receives a complete, deterministic snapshot.
/// </summary>
internal static class DimensionSchemaDraftMapper
{
  public static int MaximumDefinitionsPerRequest => AccountingDimensionTypes.All.Count;

  public static ClientAccountingDimensionSchemaBuildResult BuildSnapshot(
    Guid firmId,
    DimensionSchemaDraftDto request,
    Guid schemaId,
    Guid revisionId,
    long revision,
    ClientAccountingDimensionSchemaRevision? predecessor,
    ClientAccountingDimensionSchemaRevision? draftBeingEdited = null)
  {
    if (request is null)
      return Failure("dimension-schema.required", "schema");
    var inputGuard = ValidateResolvedLineage(
      firmId, request, schemaId, revisionId, revision, predecessor, draftBeingEdited);
    if (inputGuard is not null)
      return inputGuard;

    // IDs can only be resolved from the exact, owner-checked revision passed above.
    var identitySource = draftBeingEdited ?? predecessor;
    var issues = new List<ClientAccountingDimensionSchemaIssue>();
    if (request.Definitions is null)
      return Failure("dimension-schema.definitions.required", "definitions");
    if (request.Definitions.Count < 1 || request.Definitions.Count > MaximumDefinitionsPerRequest)
      return Failure("dimension-schema.definitions.count-invalid", "definitions");

    var definitions = new List<ClientAccountingDimensionSchemaDefinitionDraft>(request.Definitions.Count);
    foreach (var definition in request.Definitions)
    {
      if (definition is null)
      {
        issues.Add(new("dimension-schema.definition.required", "definitions"));
        continue;
      }

      var type = definition.DimensionType?.Trim().ToUpperInvariant() ?? string.Empty;
      var sourceDefinition = definition.DefinitionId is { } definitionId
        ? identitySource?.Definitions.SingleOrDefault(x => x.Id == definitionId)
        : null;
      if (definition.DefinitionId.HasValue && sourceDefinition is null)
        issues.Add(new("dimension-schema.definition.identity.not-in-base", $"definitions.{definition.DefinitionId.Value:D}"));
      if (sourceDefinition is not null && sourceDefinition.DimensionType != type)
        issues.Add(new("dimension-schema.definition.identity.type-changed", $"definitions.{sourceDefinition.Id:D}.dimensionType"));

      var values = new List<ClientAccountingDimensionSchemaValueDraft>(definition.Values?.Count ?? 0);
      if (definition.Values is null)
      {
        issues.Add(new("dimension-schema.values.required", $"definitions.{type}.values"));
        continue;
      }
      foreach (var value in definition.Values)
      {
        if (value is null)
        {
          issues.Add(new("dimension-schema.value.required", $"definitions.{type}.values"));
          continue;
        }

        var sourceValue = value.ValueId is { } valueId
          ? sourceDefinition?.Values.SingleOrDefault(x => x.Id == valueId)
          : null;
        if (value.ValueId.HasValue && sourceValue is null)
          issues.Add(new("dimension-schema.value.identity.not-in-definition", $"definitions.{type}.values.{value.ValueId.Value:D}"));

        values.Add(new ClientAccountingDimensionSchemaValueDraft(
          sourceValue?.Id ?? Guid.CreateVersion7(), value.Code, value.Name, value.Active,
          value.EffectiveFrom, value.EffectiveTo));
      }

      definitions.Add(new ClientAccountingDimensionSchemaDefinitionDraft(
        sourceDefinition?.Id ?? Guid.CreateVersion7(), type, definition.Code, definition.Name,
        definition.Required, Array.AsReadOnly(values.ToArray())));
    }

    if (issues.Count > 0)
      return new(null, Array.AsReadOnly(issues.ToArray()));

    var built = ClientAccountingDimensionSchemaRevision.Create(new ClientAccountingDimensionSchemaDraft(
      firmId, request.ClientId, schemaId, revisionId, revision,
      Array.AsReadOnly(definitions.ToArray()), request.BaseRevisionRef));
    if (!built.IsValid)
      return built;

    var successorIssues = new List<ClientAccountingDimensionSchemaIssue>();
    if (draftBeingEdited is not null)
      successorIssues.AddRange(built.Value!.ValidateDraftEditFrom(draftBeingEdited));
    if (predecessor is not null)
      successorIssues.AddRange(built.Value!.ValidateSuccessorFrom(predecessor));
    return successorIssues.Count == 0
      ? built
      : new(null, Array.AsReadOnly(successorIssues.ToArray()));
  }

  public static ClientAccountingDimensionSchemaBuildResult Rehydrate(
    ClientAccountingDimensionSchemaRevisionRecord record)
  {
    if (record is null)
      return Failure("dimension-schema.persistence.required", "revision");
    if (record.CanonicalDigestVersion != ClientAccountingDimensionSchemaRevision.CanonicalDigestVersion)
      return Failure("dimension-schema.digest.version.unsupported", "canonicalDigestVersion");

    var snapshot = ClientAccountingDimensionSchemaRevision.Create(new ClientAccountingDimensionSchemaDraft(
      record.FirmId,
      record.ClientId,
      record.SchemaId,
      record.Id,
      record.Revision,
      Array.AsReadOnly(record.Definitions.Select(definition => new ClientAccountingDimensionSchemaDefinitionDraft(
        definition.Id,
        definition.DimensionType,
        definition.Code,
        definition.Name,
        definition.IsRequired,
        Array.AsReadOnly(definition.Values.Select(value => new ClientAccountingDimensionSchemaValueDraft(
          value.Id,
          value.Code,
          value.Name,
          value.IsActive,
          value.EffectiveFrom,
          value.EffectiveTo)).ToArray()))).ToArray()),
      record.BaseRevisionId));
    if (!snapshot.IsValid)
      return snapshot;
    return string.Equals(snapshot.Value!.CanonicalDigest, record.CanonicalDigest, StringComparison.Ordinal)
      ? snapshot
      : Failure("dimension-schema.persistence.digest-mismatch", "canonicalDigest");
  }

  private static ClientAccountingDimensionSchemaBuildResult Failure(string code, string path) =>
    new(null, Array.AsReadOnly(new[] { new ClientAccountingDimensionSchemaIssue(code, path) }));

  private static ClientAccountingDimensionSchemaBuildResult? ValidateResolvedLineage(
    Guid firmId,
    DimensionSchemaDraftDto request,
    Guid schemaId,
    Guid revisionId,
    long revision,
    ClientAccountingDimensionSchemaRevision? predecessor,
    ClientAccountingDimensionSchemaRevision? draftBeingEdited)
  {
    if (schemaId == Guid.Empty || revisionId == Guid.Empty || revision < 1)
      return Failure("dimension-schema.identity.required", "revision");

    if (predecessor is null)
    {
      if (request.BaseRevisionRef.HasValue)
        return Failure("dimension-schema.base.unresolved", "baseRevisionRef");
    }
    else
    {
      if (!request.BaseRevisionRef.HasValue || request.BaseRevisionRef.Value != predecessor.RevisionId)
        return Failure("dimension-schema.base.reference-mismatch", "baseRevisionRef");
      if (predecessor.FirmId != firmId || predecessor.ClientId != request.ClientId)
        return Failure("dimension-schema.base.scope-mismatch", "baseRevisionRef");
      if (predecessor.SchemaId != schemaId)
        return Failure("dimension-schema.base.schema-mismatch", "baseRevisionRef");
      if (predecessor.Revision < 1 || predecessor.Revision == long.MaxValue || revision != predecessor.Revision + 1)
        return Failure("dimension-schema.base.revision-mismatch", "revision");
      if (revisionId == predecessor.RevisionId)
        return Failure("dimension-schema.successor.identity-reused", "revisionId");
    }

    if (draftBeingEdited is null)
    {
      if (request.DraftRevisionRef.HasValue)
        return Failure("dimension-schema.draft.unresolved", "draftRevisionRef");
      return null;
    }

    if (!request.DraftRevisionRef.HasValue || request.DraftRevisionRef.Value != draftBeingEdited.RevisionId ||
        draftBeingEdited.FirmId != firmId || draftBeingEdited.ClientId != request.ClientId ||
        draftBeingEdited.SchemaId != schemaId || draftBeingEdited.RevisionId != revisionId ||
        draftBeingEdited.Revision != revision || draftBeingEdited.BaseRevisionId != request.BaseRevisionRef)
      return Failure("dimension-schema.draft.owner-or-lineage-mismatch", "draftRevisionRef");

    if (draftBeingEdited.BaseRevisionId is null)
    {
      if (predecessor is not null || request.BaseRevisionRef.HasValue || revision != 1)
        return Failure("dimension-schema.draft.base-mismatch", "baseRevisionRef");
    }
    else if (predecessor is null || predecessor.RevisionId != draftBeingEdited.BaseRevisionId.Value ||
        draftBeingEdited.SchemaId != predecessor.SchemaId || predecessor.Revision == long.MaxValue ||
        draftBeingEdited.Revision != predecessor.Revision + 1)
      return Failure("dimension-schema.draft.base-mismatch", "baseRevisionRef");

    return null;
  }
}

/*
Target service entry points in ClientAccountingService.Dimensions.cs:

  SaveDimensionSchemaDraftAsync(
    IClientAccountingDbContext db, ActorContext actor, DimensionSchemaDraftDto request, CancellationToken ct)

  PublishDimensionSchemaAsync(
    IClientAccountingDbContext db, ActorContext actor, DimensionSchemaDraftDto request,
    DecisionInputDto decision, CancellationToken ct)

DecisionInputDto's Decision/Rationale/exact-EvidenceRef shape is documented in the shared R2R contract;
T007 owns the scoped actor/EvidenceRef foundation. Do not define a competing decision or evidence-reference
type in this capability, or invent allowed decision values or role names.
*/
