namespace AuditSphereOps.Domain.Practice;

/// <summary>Append-only administrative evidence, separate from expiry and all role grants.</summary>
public sealed class StaffCertificationReview
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid CertificationId { get; set; }
  public long Revision { get; set; }
  public Guid RequestId { get; set; }
  public string RequestDigest { get; set; } = string.Empty;
  public Guid SubjectUserId { get; set; }
  public string SourceName { get; set; } = string.Empty;
  public string? SourceIssuer { get; set; }
  public DateOnly? SourceExpiresOn { get; set; }
  public DateTimeOffset SourceRecordedAt { get; set; }
  public Guid SourceRecordedByUserId { get; set; }
  public string SourceDigest { get; set; } = string.Empty;
  public string Decision { get; set; } = string.Empty;
  public Guid ReviewedByUserId { get; set; }
  public DateTimeOffset ReviewedAt { get; set; }
  public string EvidenceReference { get; set; } = string.Empty;
  public string Reason { get; set; } = string.Empty;

  public StaffCertification Snapshot() => new()
  {
    Id = CertificationId, FirmId = FirmId, UserId = SubjectUserId, Name = SourceName,
    Issuer = SourceIssuer, ExpiresOn = SourceExpiresOn, RecordedAt = SourceRecordedAt,
    RecordedByUserId = SourceRecordedByUserId
  };
  public StaffCertificationVerificationEvidence Evidence() => new(Id, FirmId, CertificationId, SubjectUserId,
    SourceDigest, Decision, ReviewedByUserId, ReviewedAt, EvidenceReference, Reason);
}
