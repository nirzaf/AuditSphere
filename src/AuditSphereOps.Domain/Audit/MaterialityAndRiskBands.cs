// Materiality engine and risk-band routing (STE 2.4-01..03). The calculation record binds the benchmark to the exact
// approved mapping and sealed trial-balance digest it was derived from; band assessments store their rule inputs so
// the band is reproducible and a database check rejects a colour that does not follow from them.
namespace AuditSphereOps.Domain.Audit;

public static class MaterialityBenchmarks
{
  public const string Revenue = "REVENUE";
  public const string ProfitBeforeTax = "PROFIT_BEFORE_TAX";
  public const string TotalAssets = "TOTAL_ASSETS";
  public const string NetAssets = "NET_ASSETS";
  public const string TotalExpenses = "TOTAL_EXPENSES";
  /// <summary>One mapped financial-statement line item chosen by destination code.</summary>
  public const string MappedLine = "MAPPED_LINE";

  public static readonly string[] All = [Revenue, ProfitBeforeTax, TotalAssets, NetAssets, TotalExpenses, MappedLine];
}

/// <summary>Append-only inputs and results of one automatic Planning Materiality / Tolerable Error / SAD calculation.</summary>
public sealed class MaterialityCalculation
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid MaterialityAssessmentId { get; set; }
  public Guid MappingVersionId { get; set; }
  public long MappingVersionNumber { get; set; }
  public Guid DatasetId { get; set; }
  public string DatasetDigest { get; set; } = string.Empty;
  public string BenchmarkKind { get; set; } = MaterialityBenchmarks.Revenue;
  public string? DestinationCode { get; set; }
  public int SourceLineCount { get; set; }
  public decimal BenchmarkAmount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public decimal RatePercent { get; set; }
  public decimal PerformancePercent { get; set; }
  public decimal TrivialPercent { get; set; }
  public decimal PlanningMateriality { get; set; }
  public decimal TolerableError { get; set; }
  public decimal SadThreshold { get; set; }
  public string PolicyVersion { get; set; } = string.Empty;
  public string InputHash { get; set; } = string.Empty;
  public Guid CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}

public static class RiskBands
{
  public const string Green = "GREEN";
  public const string Amber = "AMBER";
  public const string Red = "RED";
}

/// <summary>Append-only band assessment; the newest one for a risk is current.</summary>
public sealed class RiskBandAssessment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid RiskId { get; set; }
  public int LikelihoodScore { get; set; }
  public int MagnitudeScore { get; set; }
  public bool Significant { get; set; }
  public bool FraudRisk { get; set; }
  public string Band { get; set; } = RiskBands.Green;
  public string RuleVersion { get; set; } = string.Empty;
  public string Rationale { get; set; } = string.Empty;
  public Guid AssessedByUserId { get; set; }
  public DateTimeOffset AssessedAt { get; set; }
}

/// <summary>Mandatory Partner review of one exact red band assessment.</summary>
public sealed class RiskPartnerClearance
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid RiskId { get; set; }
  public Guid RiskBandAssessmentId { get; set; }
  public Guid PartnerUserId { get; set; }
  public string Note { get; set; } = string.Empty;
  public DateTimeOffset ClearedAt { get; set; }
}

/// <summary>Who performs the response to a risk; the staffing level must meet the band's minimum.</summary>
public sealed class RiskOwnerAssignment
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid RiskId { get; set; }
  public Guid RiskBandAssessmentId { get; set; }
  public Guid OwnerUserId { get; set; }
  public string OwnerStaffingLevel { get; set; } = string.Empty;
  public Guid AssignedByUserId { get; set; }
  public DateTimeOffset AssignedAt { get; set; }
}
