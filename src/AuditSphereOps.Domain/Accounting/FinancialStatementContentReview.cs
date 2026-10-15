namespace AuditSphereOps.Domain.Accounting;

/// <summary>
/// An independent reviewer's decision on the statement content of one exact draft presentation (CP-13/14, F-03): its disclosure
/// responses and applicability, numeric note schedules and equity movements with their evidence references. Append-only. Approval records
/// a human professional judgement on the exact snapshot; it does not resolve preparer-entered evidence to accepted ledger records, and the
/// candidate presentation labels equity evidence as reviewed manual evidence rather than ledger-derived.
/// </summary>
public sealed class FinancialStatementContentReview
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  /// <summary>The unapproved draft presentation whose content was reviewed.</summary>
  public Guid PresentationRevisionId { get; set; }
  /// <summary>The draft snapshot the reviewer examined. A different snapshot is different content.</summary>
  public string SnapshotSha256Hex { get; set; } = string.Empty;
  public string Decision { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;
  public Guid ReviewedByUserId { get; set; }
  public DateTimeOffset ReviewedAt { get; set; }
  /// <summary>The caller's original intent; a retry with the same identity and payload returns this row.</summary>
  public Guid RequestId { get; set; }
  public string RequestHash { get; set; } = string.Empty;
  /// <summary>The latest content decision on the same draft the reviewer saw, or null when this was the first.</summary>
  public Guid? PreviousReviewId { get; set; }
  /// <summary>Database-assigned order of recording. The latest decision is ordered by this value, never by the application clock.</summary>
  public long Sequence { get; set; }
}

public static class FinancialStatementContentReviewDecisions
{
  public const string Approved = "APPROVED";
  public const string Returned = "RETURNED";
}
