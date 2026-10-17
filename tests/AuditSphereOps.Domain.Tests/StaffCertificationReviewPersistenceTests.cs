using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class StaffCertificationReviewPersistenceTests
{
  [Fact]
  public async Task DecisionsRetainExactSourcesAndRejectMutationStaleHeadsAndDuplicateRequests()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var source = new StaffCertification { Id = Guid.NewGuid(), FirmId = seed.FirmId, UserId = seed.Staff.Id,
      Name = "Synthetic qualification", Issuer = "Synthetic issuer", ExpiresOn = new DateOnly(2027, 1, 1),
      RecordedByUserId = seed.Admin.Id, RecordedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") };
    db.StaffCertifications.Add(source); await db.SaveChangesAsync();
    StaffCertificationReview Review(long revision) => new()
    {
      Id = Guid.NewGuid(), FirmId = source.FirmId, CertificationId = source.Id, SubjectUserId = source.UserId,
      SourceName = source.Name, SourceIssuer = source.Issuer, SourceExpiresOn = source.ExpiresOn,
      SourceRecordedAt = source.RecordedAt, SourceRecordedByUserId = source.RecordedByUserId,
      SourceDigest = StaffCertificationEvidence.SourceDigest(source), Revision = revision,
      RequestId = Guid.NewGuid(), RequestDigest = new string('a', 64), Decision = "VERIFIED",
      ReviewedByUserId = seed.Reviewer.Id, ReviewedAt = DateTimeOffset.Parse("2026-10-18T08:00:00Z"),
      EvidenceReference = "SYNTHETIC retained evidence reference", Reason = "Independent exact-source check"
    };
    var first = Review(1); db.StaffCertificationReviews.Add(first); await db.SaveChangesAsync();
    Assert.True(StaffCertificationEvidence.IsApplicable(first.Snapshot(), first.Evidence(), new DateOnly(2026, 10, 18)));
    foreach (var sql in new[] { "UPDATE staff_certification_reviews SET reason='changed'", "DELETE FROM staff_certification_reviews" })
    {
      var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
      Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
    }
    var invalid = new[] { Review(3), Review(2), Review(2), Review(2) };
    invalid[1].SourceName = "Different source";
    invalid[2].ReviewedByUserId = source.UserId;
    invalid[3].ReviewedByUserId = source.RecordedByUserId;
    foreach (var row in invalid)
    {
      db.StaffCertificationReviews.Add(row);
      var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
      Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
      db.ChangeTracker.Clear();
    }
    var duplicateRequest = Review(2); duplicateRequest.RequestId = first.RequestId;
    db.StaffCertificationReviews.Add(duplicateRequest);
    var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
    db.ChangeTracker.Clear();
    var withdrawn = Review(2); withdrawn.Decision = "REVOKED"; withdrawn.EvidenceReference = ""; withdrawn.Reason = "Withdrawn exact decision";
    db.StaffCertificationReviews.Add(withdrawn); await db.SaveChangesAsync();
    var retained = await db.StaffCertificationReviews.AsNoTracking().OrderBy(x => x.Revision).ToListAsync();
    Assert.Equal(2, retained.Count);
    Assert.Equal(first.SourceDigest, retained[0].SourceDigest);
    Assert.Equal("Independent exact-source check", retained[0].Reason);
    Assert.False(StaffCertificationEvidence.IsApplicable(source, retained[^1].Evidence(), new DateOnly(2026, 10, 18)));
    // Destructive down/up rehearsal is confined to this disposable schema; original qualification rows survive.
    var migrator = db.GetService<IMigrator>();
    await migrator.MigrateAsync("20261017142359_AuditProcedureCommandEvidenceDigest");
    Assert.True(await db.StaffCertifications.AsNoTracking().AnyAsync(x => x.Id == source.Id));
    await migrator.MigrateAsync();
    Assert.Empty(await db.StaffCertificationReviews.AsNoTracking().ToListAsync());
  }
}
