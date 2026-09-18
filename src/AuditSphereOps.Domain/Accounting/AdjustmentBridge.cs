// Adjustment source bridge (§17.4): a unique (base, journal) key stops one exact
// application being inserted twice, but it does NOT stop reapplying a journal to a
// replacement TB that already includes it. These records close that gap: a reviewer
// verifies per-base reflection, and a plan applies only NOT_REFLECTED journals.
// UNKNOWN/PARTIALLY_REFLECTED blocks finalization; REFLECTED contributes zero.
namespace AuditSphereOps.Domain.Accounting;

public static class ReflectionStates
{
  public const string Unknown = "UNKNOWN";
  public const string NotReflected = "NOT_REFLECTED";
  public const string Reflected = "REFLECTED";
  public const string PartiallyReflected = "PARTIALLY_REFLECTED";
  public const string NotApplicable = "NOT_APPLICABLE";
}

/// <summary>Reviewer verdict for one logical journal revision against one base dataset.
/// Absence of a row means UNKNOWN and blocks plan finalization.</summary>
public sealed class JournalSourceReconciliation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid BaseDatasetId { get; set; }
  public string LogicalJournalNumber { get; set; } = string.Empty;
  public long JournalRevision { get; set; } = 1;
  public string State { get; set; } = ReflectionStates.Unknown;
  public string Evidence { get; set; } = string.Empty; // source posting IDs / line bridge
  public Guid? ReviewedByUserId { get; set; }
  public DateTimeOffset? ReviewedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable selection of base + approved journal revisions + reflection
/// decisions. Finalization snapshots the reflection state; a later change makes the
/// plan stale and unusable for release.</summary>
public sealed class AdjustmentPlan
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid BaseDatasetId { get; set; }
  public string Status { get; set; } = "Draft"; // Draft|Finalized
  public string? ResultHash { get; set; }
  public decimal AppliedDebits { get; set; }
  public decimal AppliedCreditsAbs { get; set; }
  public int AppliedJournalCount { get; set; }
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AdjustmentPlanLine
{
  public Guid Id { get; set; }
  public Guid PlanId { get; set; }
  public string LogicalJournalNumber { get; set; } = string.Empty;
  public long JournalRevision { get; set; } = 1;
  public string Layer { get; set; } = "REPORTING";
  public string ReflectionState { get; set; } = ReflectionStates.Unknown;
}
