namespace AuditSphereOps.Application.Audit;

public sealed record ConfirmationBookedSourcePin(string Kind, Guid ReconciliationId, string ExpectedSnapshotSha256);
public sealed record ConfirmationBankSourceOption(Guid ReconciliationId, string SourceAsOfDate, string EntityIdentifier,
  string Currency, string? BookedAmount, string? SnapshotSha256, Guid LedgerScheduleId, Guid StatementScheduleId,
  string InputGeneration, bool Eligible, string? Blocker);
public sealed record ConfirmationBankSourcePage(Guid EngagementId, int Page, int PageSize, int Total,
  IReadOnlyList<ConfirmationBankSourceOption> Sources);
public sealed record ConfirmationBookedSourceView(string Kind, Guid ReconciliationId, string? SourceAsOfDate,
  string? BookedAmount, string? Currency, string SnapshotSha256, string Currentness,
  Guid LedgerScheduleId, Guid StatementScheduleId, string? LedgerSourceHash, string? StatementSourceHash, string? Blocker);

// Stable v1 shapes deliberately enumerate known source facts. No invented source revision,
// schedule reviewer, bank-to-respondent mapping or uploaded-file identity is recorded.
internal sealed record ConfirmationBankSourceSnapshot(string Version, ConfirmationBankSnapshot Bank,
  ConfirmationBankScheduleSnapshot LedgerSchedule, ConfirmationBankScheduleSnapshot StatementSchedule,
  ConfirmationBankBatchSnapshot? LedgerBatch, ConfirmationBankBatchSnapshot? StatementBatch,
  string LedgerRowsSha256, string StatementRowsSha256, IReadOnlyList<ConfirmationBankItemSnapshot> Items);
internal sealed record ConfirmationBankSnapshot(Guid Id, Guid FirmId, Guid ClientId, Guid EngagementId,
  Guid? ProcedureId, Guid LedgerScheduleId, Guid StatementScheduleId, DateOnly AsOfDate, string Currency,
  string LedgerBalance, string StatementBalance, string TimingItemTotal, string ProposedCorrectionTotal,
  string Residual, long InputGeneration, string Status, string? Conclusion, Guid CreatedByUserId,
  DateTimeOffset CreatedAt, Guid ReviewedByUserId, DateTimeOffset ReviewedAt);
internal sealed record ConfirmationBankScheduleSnapshot(Guid Id, Guid FirmId, Guid ClientId, Guid EngagementId,
  string ScheduleType, string EntityIdentifier, string SourceReceiptReference, DateTimeOffset? AsOfDate,
  DateOnly? PeriodStart, DateOnly? PeriodEnd, string Currency, string SignConvention, string SourceHash,
  Guid? SourceImportBatchId, Guid? AcceptedSourceDecisionId, string AcceptedSourceHash, int RowCount,
  string SignedControlTotal, string GlControlTotal, string Residual, long InputGeneration,
  string CompletenessDecision, string Status, Guid CreatedByUserId, DateTimeOffset CreatedAt);
internal sealed record ConfirmationBankBatchSnapshot(Guid Id, Guid FirmId, Guid ClientId, Guid EngagementId,
  Guid PeriodId, Guid? BookId, string SourceKind, string OriginKind, string ProfileVersion, string ParserVersion,
  string RawFileSha256Hex, string NormalizedDatasetDigest, string LegalEntityKey, string Currency,
  int RowCount, int ExpectedChunkCount, int ExpectedTransactionCount, int ExpectedLineCount,
  int AcceptedChunkCount, int AcceptedTransactionCount, int AcceptedLineCount, string Status,
  string ReceiptReference, Guid CreatedByUserId, DateTimeOffset CreatedAt);
internal sealed record ConfirmationBankItemSnapshot(Guid Id, Guid? SourceScheduleId, Guid? ProposedJournalId,
  string StableItemId, string ItemType, string SignedAmount, string Currency, string Description,
  string SourceReference, string EvidenceReference, DateTimeOffset CreatedAt);
internal sealed record ConfirmationBankScheduleRowSnapshot(Guid Id, string StableRowId, int SourceLineNumber,
  string AccountCode, string Description, string SignedAmount, string Currency, DateOnly? TransactionDate,
  DateOnly? PostingDate, DateOnly? DeliveryDate, DateOnly? ServiceDate, string OriginalValuesJson,
  DateTimeOffset CreatedAt);
