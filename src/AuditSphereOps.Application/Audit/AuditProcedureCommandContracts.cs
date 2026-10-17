namespace AuditSphereOps.Application.Audit;

public static class ProcedureCommandActions
{
  public const string SubmitResult = "SUBMIT_RESULT";
  public const string ReviewResult = "REVIEW_RESULT";
}

public sealed record ProcedureCommandContext(
  Guid RequestId,
  string Action,
  string RequestHash,
  long ActorEpoch,
  int ExpectedDefinitionRevision,
  long ExpectedResultRevision,
  bool Reviewed);

public sealed record ProcedureCommandBasis(
  Guid ProcedureId,
  string SourceProcedureId,
  string Title,
  string ApplicabilityStatus,
  string ProcedureStatus,
  int DefinitionRevision,
  long InputGeneration,
  long CurrentResultRevision,
  Guid? LatestResultId,
  long LatestResultRevision,
  string? LatestResultStatus,
  Guid? LatestReviewId,
  string? LatestReviewDecision,
  bool CanSubmit,
  bool CanReview,
  IReadOnlyList<string> Blockers);

public sealed record ProcedureCommandState(ProcedureCommandBasis Basis, string StateHash);

public static class ProcedureEvidenceSelectionKinds
{
  public const string StoredLink = "STORED_LINK";
  public const string PhysicalLink = "PHYSICAL_LINK";
  public const string ManualReference = "MANUAL_REFERENCE";
}

public sealed record ProcedureEvidenceSelection(
  string Kind,
  Guid? LinkId = null,
  Guid? PhysicalLinkId = null,
  string? ManualReference = null,
  string? Observation = null);

public sealed record ProcedureCommandRequest(
  Guid RequestId,
  string Action,
  int ExpectedDefinitionRevision,
  long ExpectedInputGeneration,
  long ExpectedResultRevision,
  Guid? ResultId,
  string? WorkPerformed,
  string? StructuredResultJson,
  IReadOnlyList<string>? EvidenceReferences,
  string? Conclusion,
  string? Decision,
  string? Comment,
  bool Reviewed,
  string? ExpectedRequestHash,
  IReadOnlyList<ProcedureEvidenceSelection>? EvidenceSelections = null);

public sealed record ProcedureCommandPreview(
  Guid RequestId,
  string RequestHash,
  string Action,
  ProcedureCommandBasis Basis,
  bool CanProceed,
  IReadOnlyList<string> Blockers);

public sealed record ProcedureCommandReceiptView(
  Guid Id,
  Guid ProcedureId,
  string SourceProcedureId,
  string Action,
  Guid ActorUserId,
  long ActorEpoch,
  Guid RequestId,
  string RequestHash,
  int DefinitionRevision,
  long InputGeneration,
  long ExpectedResultRevision,
  Guid? ResultId,
  Guid? WorkpaperId,
  Guid? ReviewId,
  long ResultRevision,
  string? ResultStatus,
  string? Decision,
  string? EvidenceDigest,
  DateTimeOffset CommittedAt);

public sealed record ProcedureCommandLookup(bool Found, ProcedureCommandReceiptView? Receipt);
