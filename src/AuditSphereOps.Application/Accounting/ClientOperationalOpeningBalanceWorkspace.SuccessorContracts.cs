using System.Text.Json.Serialization;
using AuditSphereOps.Domain.Accounting;

namespace AuditSphereOps.Application.Accounting;

public sealed record NativeSuccessorOpeningRequest(Guid RequestId, Guid ClientId, Guid EngagementId, Guid PeriodId,
  long ExpectedPeriodRevision, Guid PriorPeriodId, long ExpectedPriorPeriodRevision, Guid NativeTrialBalanceDatasetId,
  string ExpectedSourceIdentityHash, string EvidenceReference, string? ReviewBasis = null, bool Reviewed = false);
public sealed record NativeSuccessorOpeningLine(Guid AccountId, string AccountCode, string AccountName, string Debit, string Credit);
public sealed record NativeSuccessorControlProof(string Role, Guid ConfigurationId, Guid DecisionId, Guid AccountId,
  string ClosingDebit, string ClosingCredit);
public sealed record NativeSuccessorOpeningPreview(string ReviewBasis, string SourceIdentityHash,
  string ProspectiveOpeningManifestHash, Guid PeriodId, string PeriodRevision, Guid PriorPeriodId,
  string PriorPeriodRevision, string AsOfDate, string Currency, string Basis, string BookScope,
  IReadOnlyList<NativeSuccessorOpeningLine> Lines, IReadOnlyList<NativeSuccessorControlProof> Controls,
  string PopulationHash, bool Ready, IReadOnlyList<string> Blockers,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NativeSuccessorCarriedPopulationView? CarriedPopulation = null);
public sealed record NativeSuccessorOpeningApprovalRequest(Guid RequestId, Guid ClientId, Guid EngagementId,
  Guid OpeningId, long ExpectedPeriodRevision, string ExpectedOpeningManifestHash, string ExpectedSuccessorProofHash, bool Reviewed);
public sealed record NativeSuccessorOpeningView(ClientOperationalOpeningBalanceView Opening, string SourceIdentityHash,
  string ProofHash, string ProofJson, bool IsCurrent, string? CurrentnessBlocker);

public static partial class ClientOperationalOpeningBalanceWorkspace
{
  internal sealed record SuccessorFacts(Guid TargetPeriodId, long TargetRevision, Guid PriorPeriodId, long PriorRevision,
    DateOnly PriorStart, DateOnly PriorEnd, Guid ClosedBy, DateTimeOffset ClosedAt, string CloseReason,
    string Basis, string Currency, Guid ChartId, string SourceManifestHash, string SourceRowsHash,
    Guid AcceptanceId, string AcceptanceHash, string ProfileHash, string MandateHash, string PopulationHash,
    IReadOnlyList<NativeSuccessorOpeningLine> Lines, IReadOnlyList<NativeSuccessorControlProof> Controls,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SuccessorCarriedPopulation? CarriedPopulation = null);
  internal sealed record SuccessorProof(string Version, Guid OpeningId, string OpeningManifestHash,
    Guid FirmId, Guid ClientId, Guid EngagementId, Guid PriorPeriodId, long PriorPeriodRevision,
    Guid SourceDatasetId, string SourceIdentityHash, SuccessorFacts Facts);
}
