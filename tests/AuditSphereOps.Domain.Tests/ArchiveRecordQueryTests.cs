using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Domain.Tests;

public sealed class ArchiveRecordQueryTests
{
  [Theory]
  [InlineData(null)]
  [InlineData(0)]
  [InlineData(100)]
  public async Task UnbuiltArchiveHasAnEmptyPageEvenWhenAnOldCursorIsSupplied(int? afterOrdinal)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var archiveId = Guid.NewGuid();
    db.Archives.Add(new Archive
    {
      Id = archiveId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      ProfileId = "SYNTHETIC-UNBUILT", ProfileVersion = 1,
      Status = ArchiveStates.AssemblyInProgress, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var result = await AuditRecordQueries.ArchiveAsync(db, PbcSeed.Actor(f.Staff, "Staff"), archiveId, afterOrdinal);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(archiveId, result.Value!.Id);
    Assert.Null(result.Value.ManifestVersion);
    Assert.Empty(result.Value.Entries);
    Assert.Equal(0, result.Value.TotalEntryCount);
    Assert.Null(result.Value.NextOrdinal);
    Assert.Null(result.Value.SecondarySharePointCopy);
  }

  [Fact]
  public async Task ExportEligibilityIsSeparateFromReadAccessAndRequiresTheSafeReviewedPayload()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var archiveId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    var admin = PbcSeed.Actor(f.Admin, "Administrator");
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.RecordsProfiles.Add(new RecordsProfile
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ProfileCode = "SYNTHETIC-REVIEWED", Version = 1,
        RecordClass = "Synthetic reviewed archive", Jurisdiction = "TEST", ServiceRoute = "STATUTORY_AUDIT",
        RetentionTrigger = "REPORT_DATE", ProtectionMode = "LOCAL_ONLY", LabelId = "test-only",
        LegalHoldBehavior = "SUSPEND_DISPOSITION", AmendmentRoute = "CONTROLLED_AMENDMENT",
        DispositionOwner = "Records Custodian", BackupRequirements = "Synthetic fixture",
        CreatedByUserId = f.Admin.Id, Approved = true, ApprovedAt = now,
        ApprovedByUserId = f.Admin.Id, CreatedAt = now
      });
      seed.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ProfileId = "SYNTHETIC-REVIEWED", ProfileVersion = 1,
        Status = ArchiveStates.Issued, CreatedAt = now
      });
      await seed.SaveChangesAsync();
    }

    ArchiveManifestResult built;
    await using (var builder = new AuditSphereDbContext(pg.Options))
    {
      var result = await RecordsArchiveService.BuildManifestAsync(builder, admin, archiveId);
      Assert.True(result.Succeeded, result.Message);
      built = result.Value!;
    }
    await using (var reviewer = new AuditSphereDbContext(pg.Options))
    {
      var staffRead = await AuditRecordQueries.ArchiveAsync(reviewer, PbcSeed.Actor(f.Staff, "Staff"), archiveId);
      Assert.True(staffRead.Succeeded, staffRead.Message);
      Assert.False(staffRead.Value!.CanReview);
      var authorizedRead = await AuditRecordQueries.ArchiveAsync(reviewer, admin, archiveId);
      Assert.True(authorizedRead.Succeeded, authorizedRead.Message);
      Assert.True(authorizedRead.Value!.CanReview);
      Assert.Equal(built.ManifestId, authorizedRead.Value.ManifestId);
      Assert.Equal(built.ManifestVersion, authorizedRead.Value.ManifestVersion);
      Assert.Equal(built.Digest, authorizedRead.Value.ManifestDigest);
      var result = await RecordsArchiveService.ReviewManifestAsync(reviewer, admin, archiveId,
        new ArchiveManifestReviewRequest(built.ManifestId, built.ManifestVersion, built.Digest, Guid.NewGuid().ToString("N")));
      Assert.True(result.Succeeded, result.Message);
    }

    await using var db = new AuditSphereDbContext(pg.Options);
    var staff = await AuditRecordQueries.ArchiveAsync(db, PbcSeed.Actor(f.Staff, "Staff"), archiveId);
    Assert.True(staff.Succeeded, staff.Message);
    Assert.False(staff.Value!.CanExport);
    var custodian = await AuditRecordQueries.ArchiveAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), archiveId);
    Assert.True(custodian.Succeeded, custodian.Message);
    Assert.True(custodian.Value!.CanExport);
    Assert.False(custodian.Value.CanReview);
  }
}
