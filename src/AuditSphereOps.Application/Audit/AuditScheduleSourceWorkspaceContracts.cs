namespace AuditSphereOps.Application.Audit;

public sealed record AuditScheduleSourceFields(
  Guid PbcAcceptanceReceiptId, Guid AcceptedSourceDecisionId, DateOnly SourceDate,
  AuditScheduleCsvColumns Columns, IReadOnlyList<string> SelectedControlAccountCodes);

public sealed record AuditScheduleCaptureRequest(
  Guid RequestId, AuditScheduleSourceFields Fields, string ExpectedManifestHash,
  long ExpectedInputGeneration, long ExpectedPeriodRevision, string ExpectedRequestHash, bool Reviewed);

public sealed record AuditScheduleSourceAccountView(
  string AccountCode, string ListingAmount, string TrialBalanceAmount, string Difference,
  string ListingRowCount, string TrialBalanceRowCount);

public sealed record AuditScheduleCapturePreview(
  Guid RequestId, string RequestHash, string ManifestHash, string InputGeneration, string PeriodRevision,
  AuditScheduleSourceFields CanonicalInput, string FileName, string RawFileSha256, string RowCount,
  string Currency, string LegalEntityKey, DateOnly SourceDate, string SignedTotal, string ClosingControl,
  string Residual, bool HasCompleteListingCoverage, bool AllAccountsReconciled,
  IReadOnlyList<AuditScheduleSourceAccountView> Accounts);

/// <summary>The original command outcome. Its status is historical, not current source eligibility.</summary>
public sealed record AuditScheduleSourceReceipt(
  string Version, string Action, Guid ScheduleId, Guid? ReviewId, Guid FirmId, Guid ClientId,
  Guid EngagementId, Guid ActorUserId, string ActorEpoch, Guid RequestId, string RequestHash,
  string ManifestHash, string InputGeneration, string RowCount, string SignedTotal,
  string ClosingControl, string Residual, string Status, DateTimeOffset CreatedAt);

public sealed record AuditScheduleSourceReceiptView(AuditScheduleSourceReceipt Receipt, string ReceiptHash);

/// <summary>Not found is not proof that an earlier in-flight request cannot still commit.</summary>
public sealed record AuditScheduleSourceReceiptLookup(bool Found, AuditScheduleSourceReceiptView? Receipt);

public sealed record AuditScheduleSourceReviewFields(string Action, string Reason, string EvidenceReference);

public sealed record AuditScheduleSourceReviewRequest(Guid RequestId, Guid ScheduleId,
  string ExpectedCaptureHash, long ExpectedInputGeneration, string ExpectedReviewBasis,
  AuditScheduleSourceReviewFields Fields, string ExpectedRequestHash, bool Reviewed);

public sealed record AuditScheduleSourceReviewPreview(Guid RequestId, Guid ScheduleId,
  string CaptureHash, string InputGeneration, string ReviewBasis, string RequestHash,
  AuditScheduleSourceReviewFields CanonicalInput, bool SourceCurrent, bool CanApprove,
  string? SourceFailureCode, string Status, Guid? ExistingReviewId);
