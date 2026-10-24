namespace AuditSphereOps.Domain.Accounting;

public static class SourceImportOrigins
{
  public const string External = "EXTERNAL";
  public const string NativeLedger = "NATIVE_LEDGER";
}

/// <summary>Immutable generated GL provenance, anchored to an independently accepted native TB source.</summary>
public sealed class NativeGeneralLedgerSource
{
  public Guid ImportBatchId { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid TrialBalanceDatasetId { get; set; }
  public string NativeTrialBalanceSourceHash { get; set; } = string.Empty;
  public string NativeTrialBalanceManifestHash { get; set; } = string.Empty;
  public Guid RequestId { get; set; }
  public Guid ActorId { get; set; }
  public long ActorEpoch { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  public string SourceIdentityHash { get; set; } = string.Empty;
  public string ManifestHash { get; set; } = string.Empty;
  public string ManifestJson { get; set; } = string.Empty;
  public string GeneratedRowsHash { get; set; } = string.Empty;
  public string GeneratedRowsJson { get; set; } = string.Empty;
  public int TransactionCount { get; set; }
  public int LineCount { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
