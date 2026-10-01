namespace AuditSphereOps.Domain.Completion;

/// <summary>Exact client-uploaded PDF scan of one generated representation letter. Append-only.</summary>
public sealed class SignedRepresentationLetter
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid DeliverableId { get; set; }
  public string DeliverableSha256 { get; set; } = "";
  public string ContentSha256 { get; set; } = "";
  public byte[] Content { get; set; } = [];
  public string ManagementSignatory { get; set; } = "";
  public Guid UploadedByUserId { get; set; }
  public DateTimeOffset UploadedAt { get; set; }
}

/// <summary>A Partner's human verification of management's signature on the exact scan, not automated signature recognition.</summary>
public sealed class RepresentationLetterVerification
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid SignedLetterId { get; set; }
  public Guid VerifiedByUserId { get; set; }
  public string Reason { get; set; } = "";
  public DateTimeOffset VerifiedAt { get; set; }
}

/// <summary>Versioned, approved visual firm seal. Does not assert certificate-backed signing.</summary>
public sealed class FirmSealSpecimen
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public long Version { get; set; }
  public byte[] PngContent { get; set; } = [];
  public string Sha256 { get; set; } = "";
  public Guid RegisteredByUserId { get; set; }
  public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>Immutable five-part assembly referencing a real released financial package and a posted balance invoice.</summary>
public sealed class CommercialDeliverableBundle
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  public Guid EngagementId { get; set; }
  public Guid SignedReportId { get; set; }
  public Guid FinancialPackageReleaseId { get; set; }
  public Guid FinancialPackageArtifactId { get; set; }
  public Guid ManagementLetterId { get; set; }
  public Guid SignedRepresentationLetterId { get; set; }
  public Guid BalanceInvoiceId { get; set; }
  public string SourceDigest { get; set; } = "";
  public string ManifestJson { get; set; } = "{}";
  public string ManifestSha256 { get; set; } = "";
  public byte[] Content { get; set; } = [];
  public string ContentSha256 { get; set; } = "";
  public Guid AssembledByUserId { get; set; }
  public DateTimeOffset AssembledAt { get; set; }
}
