using AuditSphereOps.Domain.Audit;

namespace AuditSphereOps.Domain.Tests;

public sealed class OpeningBalanceReviewCurrentnessTests
{
  [Fact]
  public void LegacyReviewerMetadataDoesNotConferCurrentIndependentReview()
  {
    var row = new OpeningBalanceVerification { Revision = 2, RecordedByUserId = Guid.NewGuid(),
      ReviewedByUserId = Guid.NewGuid(), ReviewedAt = DateTimeOffset.UtcNow, Conclusion = OpeningBalanceVerificationConclusions.Agreed };
    Assert.False(OpeningBalanceReviewCurrentness.IsCurrent(row));
    row.ReviewedBasisRevision = 1;
    Assert.True(OpeningBalanceReviewCurrentness.IsCurrent(row));
    row.Revision = 3;
    Assert.False(OpeningBalanceReviewCurrentness.IsCurrent(row));
  }
}
