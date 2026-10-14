namespace AuditSphereOps.Domain.Accounting;

/// <summary>
/// The exact formatted artifacts selected for one candidate statement presentation revision (CP-18). A composition seals the required formats
/// together: the identity and digest of each format's artifact, and one digest over the ordered manifest. It is append-only history.
/// </summary>
public sealed class FinancialStatementComposition
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid FinancialPackageId { get; set; }
  public Guid PresentationRevisionId { get; set; }
  public string PackageCalculationHash { get; set; } = string.Empty;
  public string ManifestDigest { get; set; } = string.Empty;
  public Guid PdfArtifactId { get; set; }
  public string PdfSha256Hex { get; set; } = string.Empty;
  public Guid WordArtifactId { get; set; }
  public string WordSha256Hex { get; set; } = string.Empty;
  public Guid ExcelArtifactId { get; set; }
  public string ExcelSha256Hex { get; set; } = string.Empty;
  public Guid SelectedByUserId { get; set; }
  public DateTimeOffset SelectedAt { get; set; }
}
