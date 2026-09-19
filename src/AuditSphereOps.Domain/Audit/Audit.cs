// Audit: risks, procedures, populations, samples, workpapers, findings, misstatements (§§19–23, 27.2).
// Every row carries the (firm, client, engagement) scope triple; the DbContext binds it to composite
// foreign keys so a cross-scope link is rejected by PostgreSQL, not only by application code (§42.3).
namespace AuditSphereOps.Domain.Audit;

public sealed class MaterialityAssessment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public string BenchmarkSource { get; set; } = string.Empty;
  public string BenchmarkVersion { get; set; } = string.Empty;
  public string Rationale { get; set; } = string.Empty;
  public decimal BenchmarkAmount { get; set; }
  public decimal RateApplied { get; set; }
  public decimal OverallMateriality { get; set; }
  public decimal PerformanceMateriality { get; set; }
  public decimal ClearlyTrivialThreshold { get; set; }
  public string? QualitativeConsiderations { get; set; }
  public string Status { get; set; } = MaterialityStatuses.Draft;
  public DateTimeOffset CreatedAt { get; set; }
}

public static class MaterialityStatuses
{
  public const string Draft = "DRAFT";
  public const string Approved = "APPROVED";
}

public sealed class AuditRisk
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public string AccountArea { get; set; } = string.Empty;
  public string Assertion { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public string Drivers { get; set; } = string.Empty;
  public string Severity { get; set; } = RiskSeverities.Normal;
  public string SignificanceDecision { get; set; } = string.Empty;
  public string? ControlsConsidered { get; set; }
  public string ResponseDescription { get; set; } = string.Empty;
  public string Status { get; set; } = RiskStatuses.Identified;
  public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Risk classification derived from the significance decision (§19.3); never a free-text field.</summary>
public static class RiskSeverities
{
  public const string Normal = "Normal";
  public const string Significant = "Significant";

  /// <summary>Maps the §19.3 significance decision onto the stored classification.</summary>
  public static string ForDecision(string significanceDecision) =>
    string.Equals(significanceDecision, SignificanceDecisions.Significant, StringComparison.Ordinal)
      ? Significant : Normal;
}

public static class SignificanceDecisions
{
  public const string Significant = "SIGNIFICANT";
  public const string Normal = "NORMAL";
}

public static class RiskStatuses
{
  public const string Identified = "IDENTIFIED";
  public const string Assessed = "ASSESSED";
  public const string Responded = "RESPONDED";
}

public sealed class PopulationVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public string Purpose { get; set; } = string.Empty;
  public string Assertion { get; set; } = string.Empty;
  public string SourceReceiptReference { get; set; } = string.Empty;
  public string ExtractionParameters { get; set; } = string.Empty;
  public int RowCount { get; set; }
  public decimal MonetaryControlTotal { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string? Exclusions { get; set; }
  public string Status { get; set; } = PopulationStatuses.PendingApproval;
  public DateTimeOffset CreatedAt { get; set; }
}

public static class PopulationStatuses
{
  public const string PendingApproval = "PENDING_APPROVAL";
  public const string Approved = "APPROVED";
  public const string Rejected = "REJECTED";
}

public sealed class AuditProcedure
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid RiskId { get; set; }
  public string Title { get; set; } = string.Empty;
  public string Status { get; set; } = "Planned";
  public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Workpaper
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  /// <summary>Planned procedure this workpaper executes; null when the work is not procedure-driven (§42.4).</summary>
  public Guid? ProcedureId { get; set; }
  public Guid ActorId { get; set; }
  public string Index { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;
  public string Objective { get; set; } = string.Empty;
  public string TemplateVersion { get; set; } = string.Empty;
  public string Procedure { get; set; } = string.Empty;
  public string? WorkPerformed { get; set; }
  public string? Conclusion { get; set; }
  public long Revision { get; set; } = 1;
  public string Status { get; set; } = WorkpaperStatuses.Working;
  public DateTimeOffset? SubmittedAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class WorkpaperStatuses
{
  public const string Working = "WORKING";
  public const string SubmittedSnapshot = "SUBMITTED_SNAPSHOT";
}

/// <summary>Immutable submitted content; review and approval always target a frozen submission (§21.1, §22).</summary>
public sealed class WorkpaperSubmission
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid WorkpaperId { get; set; }
  public Guid ActorId { get; set; }
  public long Revision { get; set; }
  public string WorkPerformed { get; set; } = string.Empty;
  public string Conclusion { get; set; } = string.Empty;
  public DateTimeOffset SubmittedAt { get; set; }
}

public sealed class Finding
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid ActorId { get; set; }
  public string FindingType { get; set; } = string.Empty;
  public string ImpactDescription { get; set; } = string.Empty;
  public bool Corrected { get; set; }
  public decimal? MonetaryAmount { get; set; }
  public string? ManagementResponse { get; set; }
  public string Status { get; set; } = FindingStatuses.Open;
  public DateTimeOffset CreatedAt { get; set; }
}

public static class FindingStatuses
{
  public const string Open = "OPEN";
  public const string Corrected = "CORRECTED";
  public const string Evaluated = "EVALUATED";
}
