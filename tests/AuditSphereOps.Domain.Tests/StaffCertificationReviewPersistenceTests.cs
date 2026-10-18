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
  public async Task PublicationRequiresIndependentAuthorityAndExactSourceHeadAndRetryIntent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var source = new StaffCertification { Id = Guid.NewGuid(), FirmId = seed.FirmId, UserId = seed.Staff.Id,
      Name = "Synthetic qualification", RecordedByUserId = seed.Reviewer.Id, RecordedAt = DateTimeOffset.UtcNow.AddDays(-1) };
    db.StaffCertifications.Add(source);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Reviewer, "Manager", clientId: seed.ClientId, engagementId: seed.EngagementId));
    await db.SaveChangesAsync();
    // Hash the persisted timestamp representation (PostgreSQL stores timestamptz at microsecond precision).
    await db.Entry(source).ReloadAsync();
    Assert.False((await AuditSphereOps.Application.Practice.ResourcePlanningService.GetCertificationReviewAsync(db,
      PbcSeed.Actor(seed.Reviewer, "Manager"), source.Id)).Succeeded);
    var intent = new AuditSphereOps.Application.Practice.ReviewStaffCertificationRequest(source.Id, Guid.NewGuid(),
      StaffCertificationEvidence.SourceDigest(source), 0, "VERIFIED", "Synthetic evidence", "Independent review");
    var actor = PbcSeed.Actor(seed.Admin, "Administrator");
    var first = await AuditSphereOps.Application.Practice.ResourcePlanningService.ReviewCertificationAsync(db, actor, intent);
    Assert.True(first.Succeeded, first.Message);
    var workspace = await AuditSphereOps.Application.Practice.ResourcePlanningService.GetCertificationReviewAsync(db, actor, source.Id);
    Assert.True(workspace.Succeeded, workspace.Message);
    Assert.True(workspace.Value!.VerifiedForCurrentSource); Assert.True(workspace.Value.CanReview);
    Assert.Equal(1, workspace.Value.Revision); Assert.Equal(intent.SourceDigest, workspace.Value.SourceDigest);
    var retry = await AuditSphereOps.Application.Practice.ResourcePlanningService.ReviewCertificationAsync(db, actor, intent);
    Assert.True(retry.Succeeded, retry.Message); Assert.Equal(first.Value, retry.Value);
    Assert.False((await AuditSphereOps.Application.Practice.ResourcePlanningService.ReviewCertificationAsync(db, actor,
      intent with { Reason = "Changed intent" })).Succeeded);
    Assert.False((await AuditSphereOps.Application.Practice.ResourcePlanningService.ReviewCertificationAsync(db, actor,
      intent with { RequestId = Guid.NewGuid() })).Succeeded);
    Assert.False((await AuditSphereOps.Application.Practice.ResourcePlanningService.ReviewCertificationAsync(db,
      PbcSeed.Actor(seed.Reviewer, "Reviewer"), intent with { RequestId = Guid.NewGuid(), ExpectedRevision = 1 })).Succeeded);
    var withdrawn = await AuditSphereOps.Application.Practice.ResourcePlanningService.ReviewCertificationAsync(db, actor,
      intent with { RequestId = Guid.NewGuid(), ExpectedRevision = 1, Decision = "REVOKED", EvidenceReference = "", Reason = "Withdrawn" });
    Assert.True(withdrawn.Succeeded, withdrawn.Message);
    var rows = await db.StaffCertificationReviews.AsNoTracking().OrderBy(x => x.Revision).ToListAsync();
    Assert.Equal(2, rows.Count); Assert.Equal("VERIFIED", rows[0].Decision); Assert.Equal("REVOKED", rows[1].Decision);
    workspace = await AuditSphereOps.Application.Practice.ResourcePlanningService.GetCertificationReviewAsync(db, actor, source.Id);
    Assert.True(workspace.Succeeded, workspace.Message); Assert.False(workspace.Value!.VerifiedForCurrentSource);
    Assert.Equal("REVOKED", workspace.Value.LatestReview!.Decision);
    Assert.False((await AuditSphereOps.Application.Practice.ResourcePlanningService.GetCertificationReviewAsync(db,
      PbcSeed.Actor(seed.Client, "ClientUser"), source.Id)).Succeeded);

  }

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
