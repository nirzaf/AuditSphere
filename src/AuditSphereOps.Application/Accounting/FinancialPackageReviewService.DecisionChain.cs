using System.Text;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Accounting;

/// <summary>
/// Latest-effective formal decisions (CP-19, F-04). The formal order is management, then accounting, then Partner. A stage's approval is
/// effective only while it is that stage's latest decision on the exact package version and composition, and it was recorded after the
/// predecessor stage's effective approval. A later return or rejection therefore defeats an earlier approval for new progression, and a
/// fresh management approval after a return requires accounting and Partner to approve again. Order is the database-assigned recording
/// sequence, never wall-clock time alone.
/// </summary>
public static partial class FinancialPackageReviewService
{
  internal static readonly string[] FormalStageOrder =
  [
    FinancialPackageReviewStages.ManagementApproval,
    FinancialPackageReviewStages.AccountingReview,
    FinancialPackageReviewStages.PartnerApproval,
  ];

  /// <summary>Decisions in the order they were recorded.</summary>
  internal static IReadOnlyList<FinancialPackageReviewDecision> InRecordedOrder(IEnumerable<FinancialPackageReviewDecision> decisions) =>
    decisions.OrderBy(x => x.Sequence).ThenBy(x => x.DecidedAt).ThenBy(x => x.Id).ToList();

  /// <summary>
  /// The effective approval per formal stage for one decision chain (one package version and one composition identity). A stage maps to
  /// null when its latest decision is not an approval, or when its approval predates the predecessor's effective approval.
  /// </summary>
  internal static IReadOnlyDictionary<string, FinancialPackageReviewDecision?> EffectiveApprovals(
    IEnumerable<FinancialPackageReviewDecision> chain)
  {
    var ordered = InRecordedOrder(chain);
    var position = ordered.Select((decision, index) => (decision, index)).ToDictionary(x => x.decision.Id, x => x.index);
    var result = new Dictionary<string, FinancialPackageReviewDecision?>(StringComparer.Ordinal);
    FinancialPackageReviewDecision? predecessor = null;
    for (var i = 0; i < FormalStageOrder.Length; i++)
    {
      var stage = FormalStageOrder[i];
      var latest = ordered.LastOrDefault(x => x.Stage == stage);
      var effective = latest is not null && latest.Decision == FinancialPackageReviewDecisions.Approved &&
        (i == 0 || predecessor is not null && position[latest.Id] > position[predecessor.Id])
          ? latest
          : null;
      result[stage] = effective;
      predecessor = effective;
    }
    return result;
  }

  /// <summary>
  /// True when this exact decision is already the current word at its stage, so recording it again would only be a redundant copy: a
  /// repeated return or rejection of the stage's latest return or rejection, or a repeated approval while the stage's latest approval is
  /// still effective. A redundant approval is refused rather than recorded, because recording it would reorder the chain and silently
  /// defeat the later stages' approvals. An approval that a later return at an earlier stage defeated may be recorded again.
  /// </summary>
  internal static bool IsCurrentDuplicate(IEnumerable<FinancialPackageReviewDecision> chain, string stage, string decision)
  {
    var decisions = InRecordedOrder(chain);
    var latestAtStage = decisions.LastOrDefault(x => x.Stage == stage);
    if (latestAtStage is null || latestAtStage.Decision != decision) return false;
    return decision != FinancialPackageReviewDecisions.Approved || EffectiveApprovals(decisions)[stage]?.Id == latestAtStage.Id;
  }

  private static string ReviewRequestHash(string stage, string decision, string evidenceMode, string evidenceReference, string comment,
    Guid? presentationRevisionId, Guid? compositionId, string? compositionDigest, Guid? expectedLatestDecisionId)
  {
    var canonical = string.Join('\u001f', "financial-package-review-intent.v1", stage, decision, evidenceMode, evidenceReference, comment,
      presentationRevisionId?.ToString("D") ?? "", compositionId?.ToString("D") ?? "", compositionDigest ?? "",
      expectedLatestDecisionId?.ToString("D") ?? "");
    return Hashing.Sha256Hex(Encoding.UTF8.GetBytes(canonical));
  }
}
