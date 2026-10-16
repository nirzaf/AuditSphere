using AuditSphereOps.Domain.Accounting;
using System.Text.Json.Serialization;

namespace AuditSphereOps.Application.Accounting;

public static class PeriodMaintenanceActions
{
  public const string RollForward = "ROLL_FORWARD";
  public const string CreateRestatement = "CREATE_RESTATEMENT";
  public const string ApproveRestatement = "APPROVE_RESTATEMENT";
}

public sealed record PeriodMaintenanceRequest(Guid RequestId, string ReviewBasis, string Action,
  RollForwardPeriodRequest? RollForward = null, CreatePeriodRestatementRequest? Restatement = null,
  Guid? RestatementId = null, bool Reviewed = false, string? ExpectedRequestHash = null);
/// <summary>Persisted submitted restatement evidence included in the exact reviewed state digest.</summary>
public sealed record PeriodMaintenanceApprovalDetail(Guid RestatementId, Guid ClientId, Guid EngagementId, Guid PeriodId,
  string PeriodCode, DateOnly PeriodStart, DateOnly PeriodEnd, string Currency,
  Guid OriginalPackageId, string OriginalPackageHash, Guid RevisedPackageId, string RevisedPackageHash,
  string RevisedBasis, string ChangeType, string AffectedPeriods, string Reason, string EvidenceReference,
  string Status, Guid CreatedByUserId, DateTimeOffset CreatedAt);
public sealed record PeriodMaintenanceState(Guid ClientId, string Action, Guid PeriodId, Guid? RestatementId,
  string ReviewBasis, bool CanProceed, IReadOnlyList<string> Blockers, [property: JsonIgnore] string StateJson, [property: JsonIgnore] PeriodMaintenanceApprovalDetail? ApprovalDetail = null);
public sealed record PeriodMaintenancePreview(Guid RequestId, string RequestHash, PeriodMaintenanceState State, PeriodMaintenanceRequest CanonicalInput, string Summary, PeriodMaintenanceApprovalDetail? ApprovalDetail = null);
public sealed record PeriodMaintenanceResult(Guid PeriodId, Guid? RestatementId, string Status, string SnapshotJson);
public sealed record PeriodMaintenanceReceiptView(Guid Id, Guid ClientId, Guid ActorId, [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long ActorEpoch,
  Guid RequestId, string RequestHash, string Action, string ReviewBasis, DateTimeOffset CreatedAt, PeriodMaintenanceResult Result);
public sealed record PeriodMaintenanceLookup(bool Found, PeriodMaintenanceReceiptView? Receipt);
public sealed record PeriodMaintenanceContext(IReadOnlyList<MaintenanceClosedPeriod> Periods,
  IReadOnlyList<MaintenanceRestatementRow> Restatements);
