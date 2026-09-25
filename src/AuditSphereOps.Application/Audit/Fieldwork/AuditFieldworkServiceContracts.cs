using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record ScheduleRowInput(
  string StableRowId,
  int SourceLineNumber,
  string AccountCode,
  string Description,
  decimal SignedAmount,
  string Currency,
  DateOnly? TransactionDate,
  DateOnly? PostingDate,
  DateOnly? DeliveryDate,
  DateOnly? ServiceDate,
  string OriginalValuesJson);

public sealed record CreateScheduleRequest(
  Guid EngagementId,
  string ScheduleType,
  string EntityIdentifier,
  string SourceReceiptReference,
  DateTimeOffset? AsOfDate,
  DateOnly? PeriodStart,
  DateOnly? PeriodEnd,
  string Currency,
  string SignConvention,
  string SourceHash,
  decimal GlControlTotal,
  IReadOnlyList<ScheduleRowInput> Rows,
  Guid? SourceImportBatchId = null);

public sealed record ScheduleValue(Guid ScheduleId, string Status, int RowCount, decimal SignedControlTotal, decimal Residual);
public sealed record ReviewScheduleRequest(Guid ScheduleId, string CompletenessDecision, bool Approve);
public sealed record BankReconciliationItemInput(
  string StableItemId, string ItemType, decimal SignedAmount, string Description,
  string SourceReference, string EvidenceReference, Guid? SourceScheduleId = null, Guid? ProposedJournalId = null);
public sealed record CreateBankReconciliationRequest(
  Guid EngagementId, Guid? ProcedureId, Guid LedgerScheduleId, Guid StatementScheduleId,
  DateOnly AsOfDate, IReadOnlyList<BankReconciliationItemInput> Items);
public sealed record BankReconciliationValue(
  Guid BankReconciliationId, string Status, string Currency, decimal LedgerBalance,
  decimal StatementBalance, decimal TimingItemTotal, decimal ProposedCorrectionTotal, decimal Residual);
public sealed record ReviewBankReconciliationRequest(Guid BankReconciliationId, string Decision, string Conclusion);

public sealed record SelectionItemInput(string StableRowId, decimal SignedAmount, string Currency, string InclusionReason, Guid? ScheduleRowId = null);
public sealed record CreateSelectionRequest(
  Guid EngagementId,
  Guid ProcedureId,
  Guid? ScheduleId,
  Guid? PopulationVersionId,
  string Method,
  string Rationale,
  IReadOnlyList<SelectionItemInput> Items);
public sealed record SelectionValue(Guid SelectionId, int SelectedCount, decimal SelectedSignedTotal, string Status);
public sealed record ReviewSelectionRequest(Guid SelectionId, string Decision, string? Comment);

public sealed record RecordItemTestRequest(
  Guid SelectionItemId,
  string WorkPerformed,
  IReadOnlyList<string> EvidenceReferences,
  string Result,
  decimal? ExceptionAmount,
  string? ContradictoryEvidence,
  string? FollowUp);
public sealed record ItemTestValue(Guid AuditItemTestId, long Revision, string Result);
public sealed record ReviewItemTestRequest(Guid AuditItemTestId, string Decision, string? Comment);

public sealed record CreateConfirmationRequest(
  Guid EngagementId,
  Guid? ProcedureId,
  string AreaCode,
  string SourceRecordId,
  decimal BookedAmount,
  string Currency,
  DateOnly ConfirmationDate,
  string Respondent,
  string ContactValidationSource);
public sealed record ConfirmationValue(Guid ConfirmationCaseId, string Status);
public sealed record RecordConfirmationDispatchRequest(Guid ConfirmationCaseId, string DispatchReference);
public sealed record RecordConfirmationResponseRequest(
  Guid ConfirmationCaseId,
  string Origin,
  string Channel,
  string ResponseReference,
  decimal? ConfirmedAmount,
  string AuthenticityAssessment,
  string Decision);
public sealed record ReviewConfirmationResponseRequest(Guid ConfirmationResponseId);
public sealed record RecordAlternativeProcedureRequest(
  Guid ConfirmationCaseId,
  string Purpose,
  IReadOnlyList<string> EvidenceReferences,
  string Conclusion);
public sealed record ReviewAlternativeProcedureRequest(Guid AlternativeProcedureId, string? Comment);
public sealed record CloseConfirmationRequest(Guid ConfirmationCaseId, string Conclusion);

public sealed record RecordAreaAssessmentRequest(
  Guid EngagementId,
  Guid? ProcedureId,
  string AreaCode,
  string AssessmentKind,
  string MethodologyReference,
  string InputSnapshotJson,
  decimal? BookedAmount,
  decimal? AuditedAmount,
  decimal? ResidualAmount,
  decimal? VariancePercent,
  string? Currency,
  DateOnly? PeriodStart,
  DateOnly? PeriodEnd,
  IReadOnlyList<string> EvidenceReferences,
  string Conclusion);
public sealed record AreaAssessmentValue(Guid AuditAreaAssessmentId, string Status);
public sealed record ReviewAreaAssessmentRequest(Guid AuditAreaAssessmentId, string Decision, string? Comment);

public sealed record RecordDifferenceRequest(
  Guid EngagementId,
  Guid? ProcedureId,
  string AccountArea,
  string DifferenceType,
  string Description,
  decimal Amount,
  string Currency,
  string MaterialityReference = "",
  string QualitativeConcerns = "");
public sealed record DifferenceValue(Guid AuditDifferenceId, string Status);
public sealed record AuditDifferenceSummary(
  string Currency, int DifferenceCount, decimal GrossAmount, decimal SignedNetAmount,
  decimal UnadjustedGrossAmount, decimal UnadjustedSignedNetAmount,
  decimal CorrectedGrossAmount, decimal CorrectedSignedNetAmount);
public sealed record EvaluateDifferenceRequest(Guid AuditDifferenceId, bool Corrected, string Evaluation, string? ManagementResponse, string? CorrectionReference);
public sealed record LinkDifferenceToJournalRequest(
  Guid AuditDifferenceId,
  Guid JournalId,
  long JournalRevision,
  Guid SourceReflectionReconciliationId,
  Guid VerifiedAdjustedSnapshotId,
  string CorrectionState = AuditDifferenceCorrectionStates.Proposed);
public sealed record SetDifferenceCorrectionStateRequest(
  Guid AuditDifferenceId, string CorrectionState, string? Reason = null);

public sealed record AuditCompletionEvaluation(
  bool Ready,
  int ProcedureCount,
  int ApplicableProcedureCount,
  int ReviewedProcedureCount,
  IReadOnlyList<string> Blockers);

/// <summary>
/// Shared fieldwork commands for source control, selections, confirmations, area assessments and
/// differences. Account areas stay distinct through AreaCode/AssessmentKind while using one guarded
/// persistence and review path.
/// </summary>
