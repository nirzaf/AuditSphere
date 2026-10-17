using AuditSphereOps.Domain.Practice;

namespace AuditSphereOps.Domain.Tests;

public sealed class StaffCertificationEvidenceTests
{
  private static StaffCertification Source() => new()
  {
    Id = Guid.Parse("11111111-1111-4111-8111-111111111111"),
    FirmId = Guid.Parse("22222222-2222-4222-8222-222222222222"),
    UserId = Guid.Parse("33333333-3333-4333-8333-333333333333"),
    Name = "Synthetic qualification", Issuer = "Synthetic issuer", ExpiresOn = new DateOnly(2026, 12, 31),
    RecordedAt = DateTimeOffset.Parse("2026-10-01T08:00:00+00:00"),
    RecordedByUserId = Guid.Parse("44444444-4444-4444-8444-444444444444")
  };
  private static StaffCertificationVerificationEvidence Verified(StaffCertification source) => new(
    Guid.Parse("55555555-5555-4555-8555-555555555555"), source.FirmId, source.Id, source.UserId,
    StaffCertificationEvidence.SourceDigest(source), "VERIFIED", Guid.Parse("66666666-6666-4666-8666-666666666666"),
    source.RecordedAt.AddDays(1), "SYNTHETIC evidence reference", "Independently checked exact source");

  [Fact]
  public void OnlyIndependentCurrentExactSourceEvidenceApplies()
  {
    var source = Source(); var review = Verified(source); var date = new DateOnly(2026, 10, 18);
    Assert.True(StaffCertificationEvidence.IsApplicable(source, review, date));
    Assert.False(StaffCertificationEvidence.IsApplicable(source, null, date));
    foreach (var state in new[] { "REJECTED", "REVOKED", "UNVERIFIED" })
      Assert.False(StaffCertificationEvidence.IsApplicable(source, review with { Decision = state }, date));
    foreach (var reviewer in new[] { source.UserId, source.RecordedByUserId, Guid.Empty })
      Assert.False(StaffCertificationEvidence.IsApplicable(source, review with { ReviewedByUserId = reviewer }, date));
    Assert.False(StaffCertificationEvidence.IsApplicable(source, review with { FirmId = Guid.NewGuid() }, date));
    Assert.False(StaffCertificationEvidence.IsApplicable(source, review with { CertificationId = Guid.NewGuid() }, date));
    Assert.False(StaffCertificationEvidence.IsApplicable(source, review with { SubjectUserId = Guid.NewGuid() }, date));
    Assert.False(StaffCertificationEvidence.IsApplicable(source, review with { EvidenceReference = " " }, date));
    Assert.False(StaffCertificationEvidence.IsApplicable(source, review with { ReviewedAt = source.RecordedAt.AddDays(-1) }, date));
    Assert.False(StaffCertificationEvidence.IsApplicable(source, review with { ReviewedAt = source.RecordedAt.AddDays(30) }, date));
    Assert.True(StaffCertificationEvidence.IsApplicable(source, review, source.ExpiresOn!.Value));
    Assert.False(StaffCertificationEvidence.IsApplicable(source, review, source.ExpiresOn.Value.AddDays(1)));
    source.Name = "Changed qualification";
    Assert.False(StaffCertificationEvidence.IsApplicable(source, review, date));
  }

  [Fact]
  public void DigestRetainsExactSourceAndNormalizesOnlyTheRecordedInstant()
  {
    var source = Source(); var digest = StaffCertificationEvidence.SourceDigest(source);
    Assert.Equal("7a8acece32d5f01a47e68fb150a9119ffc03b62ff202dddba27d9d36a092fadd", digest);
    source.RecordedAt = source.RecordedAt.ToOffset(TimeSpan.FromHours(3));
    Assert.Equal(digest, StaffCertificationEvidence.SourceDigest(source));
    source.Issuer = "Different issuer";
    Assert.NotEqual(digest, StaffCertificationEvidence.SourceDigest(source));
    source = Source(); source.ExpiresOn = null;
    Assert.NotEqual(digest, StaffCertificationEvidence.SourceDigest(source));
    Assert.True(StaffCertificationEvidence.IsApplicable(source, Verified(source), new DateOnly(2026, 10, 18)));
  }
}
