namespace AuditSphereOps.Domain.Accounting;

/// <summary>
/// Persistence aggregate for one immutable schema snapshot. DRAFT members are editable; after
/// publication the revision and its children are append-only. Status strings map the Module 20
/// logical lifecycle onto existing accounting wire states: DRAFT, SUBMITTED (validated), APPROVED
/// (published).
/// </summary>
public sealed class ClientAccountingDimensionSchemaRevisionRecord
{
  private ClientAccountingDimensionSchemaRevisionRecord() { }

  public Guid Id { get; private set; } // Exact SchemaRevisionId consumed by future context pins.
  public Guid FirmId { get; private set; }
  public Guid ClientId { get; private set; }
  public Guid SchemaId { get; private set; } // Stable lineage identity across successor revisions.
  public long Revision { get; private set; }
  public Guid? BaseRevisionId { get; private set; }
  public string Status { get; private set; } = AccountingWorkflowStates.Draft;
  public int CanonicalDigestVersion { get; private set; } = ClientAccountingDimensionSchemaRevision.CanonicalDigestVersion;
  public string CanonicalDigest { get; private set; } = string.Empty;
  public bool ImpactAcknowledged { get; private set; }
  public Guid CreatedByUserId { get; private set; }
  public DateTimeOffset CreatedAt { get; private set; }
  public DateTimeOffset? ValidatedAt { get; private set; }
  public Guid? PublishedByUserId { get; private set; }
  public DateTimeOffset? PublishedAt { get; private set; }
  public List<ClientAccountingDimensionDefinitionRevisionRecord> Definitions { get; private set; } = [];

  public static ClientAccountingDimensionSchemaRevisionRecord FromSnapshot(
    ClientAccountingDimensionSchemaRevision snapshot,
    Guid createdByUserId,
    DateTimeOffset createdAt) => new()
    {
      Id = snapshot.RevisionId,
      FirmId = snapshot.FirmId,
      ClientId = snapshot.ClientId,
      SchemaId = snapshot.SchemaId,
      Revision = snapshot.Revision,
      BaseRevisionId = snapshot.BaseRevisionId,
      CanonicalDigestVersion = ClientAccountingDimensionSchemaRevision.CanonicalDigestVersion,
      CanonicalDigest = snapshot.CanonicalDigest,
      CreatedByUserId = createdByUserId,
      CreatedAt = createdAt,
      Definitions = snapshot.Definitions.Select(definition => new ClientAccountingDimensionDefinitionRevisionRecord
      {
        Id = definition.Id,
        FirmId = snapshot.FirmId,
        ClientId = snapshot.ClientId,
        SchemaRevisionId = snapshot.RevisionId,
        DimensionType = definition.DimensionType,
        Code = definition.Code,
        CodeKey = definition.Code.ToUpperInvariant(),
        Name = definition.Name,
        IsRequired = definition.IsRequired,
        Values = definition.Values.Select(value => new ClientAccountingDimensionValueRevisionRecord
        {
          Id = value.Id,
          FirmId = snapshot.FirmId,
          ClientId = snapshot.ClientId,
          SchemaRevisionId = snapshot.RevisionId,
          DefinitionId = definition.Id,
          Code = value.Code,
          CodeKey = value.Code.ToUpperInvariant(),
          Name = value.Name,
          IsActive = value.IsActive,
          EffectiveFrom = value.EffectiveFrom,
          EffectiveTo = value.EffectiveTo
        }).ToList()
      }).ToList()
    };

  /// <summary>Replaces only an unvalidated draft's content while retaining its exact identity and author.</summary>
  public bool ReplaceDraftSnapshot(ClientAccountingDimensionSchemaRevision snapshot, bool impactAcknowledged)
  {
    if (Status != AccountingWorkflowStates.Draft || snapshot is null ||
        snapshot.FirmId != FirmId || snapshot.ClientId != ClientId || snapshot.SchemaId != SchemaId ||
        snapshot.RevisionId != Id || snapshot.Revision != Revision || snapshot.BaseRevisionId != BaseRevisionId)
      return false;

    CanonicalDigestVersion = ClientAccountingDimensionSchemaRevision.CanonicalDigestVersion;
    CanonicalDigest = snapshot.CanonicalDigest;
    ImpactAcknowledged = impactAcknowledged;
    Definitions = snapshot.Definitions.Select(definition => new ClientAccountingDimensionDefinitionRevisionRecord
    {
      Id = definition.Id,
      FirmId = FirmId,
      ClientId = ClientId,
      SchemaRevisionId = Id,
      DimensionType = definition.DimensionType,
      Code = definition.Code,
      CodeKey = definition.Code.ToUpperInvariant(),
      Name = definition.Name,
      IsRequired = definition.IsRequired,
      Values = definition.Values.Select(value => new ClientAccountingDimensionValueRevisionRecord
      {
        Id = value.Id,
        FirmId = FirmId,
        ClientId = ClientId,
        SchemaRevisionId = Id,
        DefinitionId = definition.Id,
        Code = value.Code,
        CodeKey = value.Code.ToUpperInvariant(),
        Name = value.Name,
        IsActive = value.IsActive,
        EffectiveFrom = value.EffectiveFrom,
        EffectiveTo = value.EffectiveTo
      }).ToList()
    }).ToList();
    return true;
  }

  public void SetDraftImpactAcknowledgement(bool impactAcknowledged)
  {
    if (Status == AccountingWorkflowStates.Draft)
      ImpactAcknowledged = impactAcknowledged;
  }

  /// <summary>Module 20 Validated transition, persisted using existing SUBMITTED wire state.</summary>
  public bool MarkValidated(DateTimeOffset validatedAt)
  {
    if (Status != AccountingWorkflowStates.Draft || validatedAt == default)
      return false;
    Status = AccountingWorkflowStates.Submitted;
    ValidatedAt = validatedAt;
    return true;
  }

  /// <summary>Module 20 independently Published transition, persisted using existing APPROVED state.</summary>
  public bool TryPublish(
    Guid publisherUserId,
    DateTimeOffset publishedAt,
    bool impactAcknowledged)
  {
    if (Status != AccountingWorkflowStates.Submitted || publisherUserId == Guid.Empty ||
        publishedAt == default || publisherUserId == CreatedByUserId || !impactAcknowledged)
      return false;

    Status = AccountingWorkflowStates.Approved;
    PublishedByUserId = publisherUserId;
    PublishedAt = publishedAt;
    ImpactAcknowledged = true;
    return true;
  }
}

/// <summary>One definition snapshot; the same Id is repeated in successor revision rows.</summary>
public sealed class ClientAccountingDimensionDefinitionRevisionRecord
{
  public Guid Id { get; set; } // Stable DefinitionId, scoped by SchemaRevisionId for row identity.
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid SchemaRevisionId { get; set; }
  public string DimensionType { get; set; } = string.Empty;
  public string Code { get; set; } = string.Empty;
  public string CodeKey { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public bool IsRequired { get; set; }
  public ClientAccountingDimensionSchemaRevisionRecord? SchemaRevision { get; set; }
  public List<ClientAccountingDimensionValueRevisionRecord> Values { get; set; } = [];
}

/// <summary>One allowed value snapshot; the same Id is repeated in successor revision rows.</summary>
public sealed class ClientAccountingDimensionValueRevisionRecord
{
  public Guid Id { get; set; } // Stable ValueId, scoped by SchemaRevisionId for row identity.
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid SchemaRevisionId { get; set; }
  public Guid DefinitionId { get; set; }
  public string Code { get; set; } = string.Empty;
  public string CodeKey { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public bool IsActive { get; set; }
  public DateOnly EffectiveFrom { get; set; }
  public DateOnly? EffectiveTo { get; set; }
  public ClientAccountingDimensionDefinitionRevisionRecord? Definition { get; set; }
}
