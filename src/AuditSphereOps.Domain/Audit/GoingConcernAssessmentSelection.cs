namespace AuditSphereOps.Domain.Audit;

/// <summary>No governing-assessment designation policy exists. Never rank record-local revisions.</summary>
public sealed record GoingConcernAssessmentSelection(GoingConcernAssessment? Assessment, bool Ambiguous)
{
  public const string AmbiguousBlocker = "going-concern:ambiguous-assessment";
  public static GoingConcernAssessmentSelection Select(IReadOnlyList<GoingConcernAssessment> candidates) =>
    new(candidates.Count == 1 ? candidates[0] : null, candidates.Count > 1);
}
