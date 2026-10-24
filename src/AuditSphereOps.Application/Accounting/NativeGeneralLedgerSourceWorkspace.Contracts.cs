namespace AuditSphereOps.Application.Accounting;

public sealed record NativeGeneralLedgerSourceRequest(
  Guid RequestId, Guid ClientId, Guid EngagementId, Guid TrialBalanceDatasetId,
  string ExpectedTrialBalanceSourceHash, bool PeriodLedgerReviewed, string EvidenceReference,
  string? ReviewBasis = null, bool Reviewed = false);

/// <summary>The actual approved native opening; this is not an imported opening TB dataset.</summary>
public sealed record NativeGeneralLedgerOpeningSource(Guid OpeningId, string ManifestHash);

public sealed record NativeGeneralLedgerSourceLine(
  Guid JournalId, Guid LineId, Guid ClientAccountId, string JournalNumber, string PostingDate,
  string AccountCode, string Debit, string Credit, string OriginalCurrency, string OriginalAmount,
  string FunctionalAmount, string Currency, string SourceUser, string SourceSystem,
  bool? IsManual, bool? IsYearEnd);

public sealed record NativeGeneralLedgerSourcePreview(
  string ReviewBasis, Guid TrialBalanceDatasetId, Guid PeriodId, string AsOfDate,
  string Currency, string LegalEntityKey, string BookScope, string OriginKind,
  string SourceIdentityHash, string ManifestHash, string GeneratedRowsHash,
  int TransactionCount, int LineCount, string TotalDebit, string TotalCredit,
  string PostingSequence, NativeGeneralLedgerOpeningSource NativeOpening,
  IReadOnlyList<string> UnavailableMetadata, IReadOnlyList<NativeGeneralLedgerSourceLine> Rows);

public sealed record NativeGeneralLedgerSourceView(
  Guid ImportBatchId, Guid ClientId, Guid EngagementId, Guid TrialBalanceDatasetId,
  Guid PeriodId, string AsOfDate, string Currency, string LegalEntityKey,
  string BookScope, string OriginKind, string Status,
  string SourceIdentityHash, string ManifestHash, string GeneratedRowsHash,
  int TransactionCount, int LineCount, string TotalDebit, string TotalCredit,
  string PostingSequence, NativeGeneralLedgerOpeningSource NativeOpening,
  IReadOnlyList<string> UnavailableMetadata, bool IsCurrent, string? CurrentnessBlocker,
  Guid CapturedByUserId, DateTimeOffset CreatedAt);

public sealed record NativeGeneralLedgerSourceArtifact(
  Guid ImportBatchId, string FileName, string ContentType, string GeneratedRowsJson,
  string GeneratedRowsHash, string ManifestJson, string ManifestHash,
  string SourceIdentityHash, bool IsCurrent, string? CurrentnessBlocker);
