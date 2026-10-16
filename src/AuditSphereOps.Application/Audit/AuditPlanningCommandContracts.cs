using System.Text.Json.Serialization;

namespace AuditSphereOps.Application.Audit;

public static class AuditPlanningActions
{
  public const string CalculateMateriality = "CALCULATE_MATERIALITY";
  public const string RoundMateriality = "ROUND_MATERIALITY";
  public const string ApproveMateriality = "APPROVE_MATERIALITY";
  public const string CreateRisk = "CREATE_RISK";
  public const string AssessRisk = "ASSESS_RISK";
  public const string AssignRiskOwner = "ASSIGN_RISK_OWNER";
  public const string ClearRisk = "CLEAR_RISK";
  public const string LinkProcedureRisk = "LINK_PROCEDURE_RISK";
}
public sealed record PlanningCalculateFields(string BenchmarkKind, string? DestinationCode, string RatePercent,
  string PerformancePercent, string TrivialPercent, string Rationale, string? QualitativeConsiderations = null);
public sealed record PlanningRoundFields(Guid AssessmentId, string PlanningMateriality, string TolerableError, string SadThreshold, string Rationale);
public sealed record PlanningApproveFields(Guid AssessmentId);
public sealed record PlanningCreateRiskFields(string Area, string Assertion, string Description, string Drivers,
  string SignificanceDecision, string Response, string? ControlsConsidered = null);
public sealed record PlanningAssessRiskFields(Guid RiskId, int Likelihood, int Magnitude, bool Fraud, bool CriticalEstimate, string Rationale);
public sealed record PlanningAssignRiskOwnerFields(Guid RiskId, Guid ExpectedAssessmentId, Guid OwnerUserId);
public sealed record PlanningClearRiskFields(Guid RiskId, Guid ExpectedAssessmentId, string Note);
public sealed record PlanningLinkRiskFields(Guid ProcedureId, Guid? RiskId, Guid? ExpectedRiskId);
/// <summary>Exactly one action-specific payload is permitted. Decimal values use invariant exact strings.</summary>
public sealed record AuditPlanningCommandFields(string Action, PlanningCalculateFields? Calculate = null,
  PlanningRoundFields? Round = null, PlanningApproveFields? Approve = null, PlanningCreateRiskFields? CreateRisk = null,
  PlanningAssessRiskFields? AssessRisk = null, PlanningAssignRiskOwnerFields? AssignRiskOwner = null,
  PlanningClearRiskFields? ClearRisk = null, PlanningLinkRiskFields? LinkProcedureRisk = null);
public sealed record AuditPlanningCommandRequest(Guid RequestId, string ReviewBasis, AuditPlanningCommandFields Fields,
  string? ExpectedRequestHash = null, bool Reviewed = false);
public sealed record PlanningBenchmark(string Kind, string? DestinationCode, string Label, string? Amount, int LineCount);
public sealed record PlanningSourceDetail(Guid MappingId, string MappingVersion, Guid DatasetId, string DatasetDigest,
  string Currency, IReadOnlyList<PlanningBenchmark> Benchmarks);
public sealed record PlanningMaterialityDetail(Guid AssessmentId, Guid? CalculationId, Guid? RoundingDecisionId,
  Guid PreparedByUserId, string State, string BenchmarkKind, string? DestinationCode, string Currency,
  string BenchmarkAmount, string RatePercent, string PerformancePercent, string TrivialPercent,
  string ComputedPlanningMateriality, string ComputedTolerableError, string ComputedSadThreshold,
  string PlanningMateriality, string TolerableError, string SadThreshold, string Rationale, string? QualitativeConsiderations,
  string? RoundingRationale, Guid? RoundedByUserId, Guid? MappingId, string? MappingVersion, Guid? DatasetId,
  string? DatasetDigest, string PolicyVersion, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt);
public sealed record PlanningRiskDetail(Guid RiskId, string Area, string Assertion, string Description, string Drivers,
  string SignificanceDecision, string Response, string? ControlsConsidered, Guid? AssessmentId, int? Likelihood, int? Magnitude,
  bool Fraud, bool CriticalEstimate, string? Band, string? AssessmentRationale, Guid? AssessedByUserId,
  Guid? ClearanceId, Guid? ClearedByUserId, string? ClearanceNote, Guid? OwnerAssignmentId, Guid? OwnerUserId, string? OwnerStaffingLevel);
public sealed record PlanningProcedureDetail(Guid ProcedureId, Guid? RiskId, string Status, string ResultRevision);
public sealed record PlanningOwnerOption(Guid UserId, string Name, string StaffingLevel);
public sealed record AuditPlanningCommandState(Guid EngagementId, Guid ClientId, string Action, string ReviewBasis,
  bool CanProceed, IReadOnlyList<string> Blockers, PlanningSourceDetail? Source, PlanningMaterialityDetail? Materiality,
  PlanningRiskDetail? Risk, PlanningProcedureDetail? Procedure, IReadOnlyList<PlanningOwnerOption> Owners,
  [property: JsonIgnore] string StateJson);
public sealed record PlanningMaterialityOutcome(string BenchmarkAmount, string PlanningMateriality, string TolerableError, string SadThreshold, string Currency);
public sealed record AuditPlanningCommandPreview(Guid RequestId, string RequestHash, string ReviewBasis,
  AuditPlanningCommandFields CanonicalInput, AuditPlanningCommandState State, string Summary,
  PlanningMaterialityOutcome? MaterialityOutcome, string? RiskBandOutcome);
public sealed record AuditPlanningCommandResult(Guid ResultId, PlanningMaterialityDetail? Materiality,
  PlanningRiskDetail? Risk, PlanningProcedureDetail? Procedure, string Summary);
public sealed record AuditPlanningCommandReceiptView(Guid Id, Guid ActorId, string ActorEpoch, Guid EngagementId, Guid ClientId,
  Guid RequestId, string RequestHash, string ReviewBasis, string Action, DateTimeOffset CreatedAt,
  AuditPlanningCommandPreview Preview, AuditPlanningCommandResult Result);
public sealed record AuditPlanningCommandLookup(bool Found, AuditPlanningCommandReceiptView? Receipt);
