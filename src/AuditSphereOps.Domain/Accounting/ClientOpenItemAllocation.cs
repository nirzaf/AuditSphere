namespace AuditSphereOps.Domain.Accounting;

/// <summary>Immutable provenance for a manually recorded client receipt or supplier payment.</summary>
public sealed class ClientManualSettlementOrigin
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid JournalId { get; set; }
  public Guid CounterpartyId { get; set; }
  public string SourceKind { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public string Reference { get; set; } = string.Empty;
  public string EvidenceReference { get; set; } = string.Empty;
  public Guid CommandId { get; set; }
  public string IntentHash { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable client-scoped application or reversal of an existing ledger-backed open-item balance.</summary>
public sealed class ClientOpenItemAllocationSubmission
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public string SourceKind { get; set; } = string.Empty;
  public Guid SourceItemId { get; set; }
  public string Disposition { get; set; } = string.Empty;
  public Guid CounterpartyId { get; set; }
  public string Currency { get; set; } = string.Empty;
  public decimal SourceAmount { get; set; }
  public string SourceHash { get; set; } = string.Empty;
  public string Reference { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public Guid CommandId { get; set; }
  public string IntentHash { get; set; } = string.Empty;
  public string PreviewDigest { get; set; } = string.Empty;
  public string ManifestJson { get; set; } = string.Empty;
  public string ManifestHash { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Exact customer or supplier invoice targets for one approved allocation request.</summary>
public sealed class ClientOpenItemAllocationLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid SubmissionId { get; set; }
  public int LineNumber { get; set; }
  public string TargetKind { get; set; } = string.Empty;
  public Guid TargetOpenItemId { get; set; }
  public decimal Amount { get; set; }
  public Guid? ReversesAllocationLineId { get; set; }
}

/// <summary>Append-only independent decision on the exact open-item allocation or unallocation.</summary>
public sealed class ClientOpenItemAllocationDecision
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid SubmissionId { get; set; }
  public Guid CommandId { get; set; }
  public string IntentHash { get; set; } = string.Empty;
  public string Decision { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public string PreviewDigest { get; set; } = string.Empty;
  public string ReviewContextJson { get; set; } = string.Empty;
  public Guid ActorUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
